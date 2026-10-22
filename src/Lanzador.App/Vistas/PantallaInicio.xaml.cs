using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Lanzador.App.Entrada;
using Lanzador.App.Modelos;
using Lanzador.App.Tema;
using Lanzador.Core;
using Lanzador.App.Audio;

namespace Lanzador.App.Vistas;

/// <summary>Una carátula dentro de una fila de Inicio (el mismo juego puede estar en dos filas).</summary>
public sealed class CaratulaVM(JuegoVM juego) : Observable
{
    private bool _seleccionado;

    public JuegoVM Juego { get; } = juego;
    public bool Seleccionado { get => _seleccionado; set => Fijar(ref _seleccionado, value); }
}

/// <summary>
/// Inicio, como en Big Picture: el juego con foco arriba en grande (HeroHeader) y debajo filas de carátulas
/// (Continuar jugando, Favoritos, Todos los juegos). El foco se queda en la segunda posición y la fila se desliza.
/// </summary>
public partial class PantallaInicio : UserControl, ISeccion
{
    private const double AnchoCaratula = 264 + 24; // tile-shelf + space-6
    private const double AltoFila = 376;            // la fila siguiente asoma justo encima de la barra de botones

    private sealed class Fila(string titulo, string? detalle, List<CaratulaVM> caratulas)
    {
        public string Titulo { get; } = titulo;
        public string? Detalle { get; } = detalle;
        public List<CaratulaVM> Caratulas { get; } = caratulas;
        public int Indice { get; set; }
        public FrameworkElement Vista { get; set; } = null!;
        public TranslateTransform Pista { get; } = new();
    }

    private readonly List<Fila> _filas = new();
    private readonly DispatcherTimer _esperaFondo = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private string _firma = "";
    private int _fila;

    public PantallaInicio()
    {
        InitializeComponent();
        _esperaFondo.Tick += async (_, _) =>
        {
            _esperaFondo.Stop();
            await ActualizarFondoAsync();
        };
        Loaded += (_, _) =>
        {
            Catalogo.AlCambiar += AlCambiarCatalogo;
            AlCambiarCatalogo();
        };
        Unloaded += (_, _) => Catalogo.AlCambiar -= AlCambiarCatalogo;
    }

    public Seccion Seccion => Seccion.Inicio;

    public IReadOnlyList<Ayuda> Ayudas { get; } =
    [
        new("A", "Jugar", "Enter"),
        new("X", "Trucos", "T"),
        new("Y", "Más opciones", "O"),
        new("☰", "Menú", "M"),
    ];

    private Fila? FilaActual => _fila >= 0 && _fila < _filas.Count ? _filas[_fila] : null;

    private JuegoVM? Actual => FilaActual is { Caratulas.Count: > 0 } f ? f.Caratulas[f.Indice].Juego : null;

    public void AlMostrar()
    {
        ActualizarHeroe(false);
        _esperaFondo.Stop();
        _esperaFondo.Start();
    }

    public void Ejecutar(Accion accion)
    {
        switch (accion)
        {
            case Accion.Izquierda: Sonidos.Moverse(MoverEnFila(-1)); break;
            case Accion.Derecha: Sonidos.Moverse(MoverEnFila(1)); break;
            case Accion.Arriba: Sonidos.Moverse(CambiarFila(_fila - 1)); break;
            case Accion.Abajo: Sonidos.Moverse(CambiarFila(_fila + 1)); break;
            case Accion.Aceptar: if (Actual != null) Menus.Jugar(Actual); break;
            case Accion.Trucos: if (Actual != null) Menus.AbrirTrucos(Actual); break;
            case Accion.Opciones: if (Actual != null) Menus.AbrirJuego(Actual); break;
            case Accion.Menu: MainWindow.Actual.AbrirMenu(Menus.Principal()); break;
        }
    }

    public void SeleccionarPorNombre(string texto)
    {
        for (var f = _filas.Count - 1; f >= 0; f--)
        {
            var i = _filas[f].Caratulas.FindIndex(c => c.Juego.Nombre.Contains(texto, StringComparison.CurrentCultureIgnoreCase));
            if (i < 0)
                continue;
            CambiarFila(f);
            Enfocar(i, true);
            return;
        }
    }

    // ───── Filas ─────

    private void AlCambiarCatalogo()
    {
        TextoMensaje.Text = Catalogo.Mensaje ?? "";
        TextoMensaje.Visibility = Catalogo.Mensaje != null ? Visibility.Visible : Visibility.Collapsed;
        Heroe.Visibility = Estado.Juegos.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        var recientes = Catalogo.Recientes(12);
        var favoritos = Catalogo.Favoritos();
        var definicion = new List<(string Titulo, string? Detalle, List<JuegoVM> Juegos)>();
        if (recientes.Count > 0)
            definicion.Add(("Continuar jugando", null, recientes));
        if (favoritos.Count > 0)
            definicion.Add(("Favoritos", favoritos.Count.ToString(), favoritos));
        if (Estado.Juegos.Count > 0)
            definicion.Add(("Todos los juegos", Estado.Juegos.Count.ToString(), Estado.Juegos));

        // Si las filas no cambiaron (p. ej. llegó la compatibilidad), solo se refresca el juego con foco.
        var firma = string.Join("|", definicion.Select(d => d.Titulo + ":" + string.Join(",", d.Juegos.Select(j => j.Juego.Clave))));
        if (firma == _firma)
        {
            ActualizarHeroe(false);
            return;
        }
        _firma = firma;

        var tituloAnterior = FilaActual?.Titulo;
        var juegoAnterior = Actual?.Juego.Clave;
        _filas.Clear();
        ListaFilas.Children.Clear();
        foreach (var (titulo, detalle, juegos) in definicion)
        {
            var fila = new Fila(titulo, detalle, juegos.Select(j => new CaratulaVM(j)).ToList());
            fila.Vista = CrearVistaFila(fila);
            _filas.Add(fila);
            ListaFilas.Children.Add(fila.Vista);
        }
        if (_filas.Count == 0)
        {
            _fila = 0;
            return;
        }

        // Al volver de jugar, el juego queda primero en «Continuar jugando»: el foco va a él.
        var filaDestino = Math.Max(0, _filas.FindIndex(f => f.Titulo == tituloAnterior));
        _fila = -1;
        CambiarFila(filaDestino, animar: false);
        var indice = juegoAnterior == null ? -1 : _filas[filaDestino].Caratulas.FindIndex(c => c.Juego.Juego.Clave == juegoAnterior);
        if (indice < 0 && _filas[filaDestino].Titulo == "Continuar jugando")
            indice = 0;
        Enfocar(Math.Max(0, indice), false);
    }

    private FrameworkElement CrearVistaFila(Fila fila)
    {
        var cabecera = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(64, 0, 0, 0) };
        cabecera.Children.Add(new TextBlock { Text = fila.Titulo, Style = (Style)FindResource("title") });
        if (fila.Detalle != null)
            cabecera.Children.Add(new TextBlock
            {
                Text = fila.Detalle,
                Style = (Style)FindResource("caption"),
                Margin = new Thickness(16, 0, 0, 3),
                VerticalAlignment = VerticalAlignment.Bottom,
            });

        var pista = new ItemsControl
        {
            ItemsSource = fila.Caratulas,
            ItemTemplate = (DataTemplate)FindResource("PlantillaCaratula"),
            ItemsPanel = (ItemsPanelTemplate)FindResource("PanelHorizontal"),
            Focusable = false,
            Margin = new Thickness(64, 26, 0, 0),
            RenderTransform = fila.Pista,
        };

        var vista = new StackPanel { Height = AltoFila };
        vista.Children.Add(cabecera);
        vista.Children.Add(pista);
        return vista;
    }

    private bool MoverEnFila(int paso)
    {
        if (FilaActual is not { } fila)
            return false;
        var destino = fila.Indice + paso;
        if (destino < 0 || destino >= fila.Caratulas.Count)
            return false; // borde de la fila
        Enfocar(destino, true);
        return true;
    }

    private bool CambiarFila(int destino, bool animar = true)
    {
        if (destino < 0 || destino >= _filas.Count || destino == _fila)
            return false;
        var anterior = FilaActual;
        if (anterior is { Caratulas.Count: > 0 })
            anterior.Caratulas[anterior.Indice].Seleccionado = false;
        _fila = destino;
        var fila = _filas[destino];

        // Mantiene la columna a la vista: la carátula de la nueva fila más cercana a la posición del foco anterior.
        if (anterior != null && animar)
        {
            var columnaVisible = Math.Min(anterior.Indice, 1);
            var primeraVisible = Math.Max(0, fila.Indice - 1);
            fila.Indice = Math.Clamp(primeraVisible + columnaVisible, 0, fila.Caratulas.Count - 1);
        }

        var duracion = MainWindow.Duracion("dur-slide");
        var curva = MainWindow.Curva("ease-out");
        var y = -destino * AltoFila;
        if (animar)
            PosicionFilas.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(y, duracion) { EasingFunction = curva });
        else
        {
            PosicionFilas.BeginAnimation(TranslateTransform.YProperty, null);
            PosicionFilas.Y = y;
        }
        // Las filas que quedan arriba se desvanecen bajo el juego destacado.
        for (var i = 0; i < _filas.Count; i++)
        {
            var opacidad = i < destino ? 0 : 1;
            if (animar)
                _filas[i].Vista.BeginAnimation(OpacityProperty, new DoubleAnimation(opacidad, duracion) { EasingFunction = curva });
            else
            {
                _filas[i].Vista.BeginAnimation(OpacityProperty, null);
                _filas[i].Vista.Opacity = opacidad;
            }
        }
        Enfocar(fila.Indice, animar);
        return true;
    }

    private void Enfocar(int indice, bool animar)
    {
        if (FilaActual is not { Caratulas.Count: > 0 } fila)
            return;
        indice = Math.Clamp(indice, 0, fila.Caratulas.Count - 1);
        foreach (var c in fila.Caratulas)
            if (c.Seleccionado && !ReferenceEquals(c, fila.Caratulas[indice]))
                c.Seleccionado = false;
        var cambio = fila.Indice != indice || !fila.Caratulas[indice].Seleccionado;
        fila.Indice = indice;
        fila.Caratulas[indice].Seleccionado = true;

        // El foco se queda en la segunda posición; la fila se desliza (dur-slide).
        var x = -Math.Max(0, indice - 1) * AnchoCaratula;
        if (animar)
            fila.Pista.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(x, MainWindow.Duracion("dur-slide")) { EasingFunction = MainWindow.Curva("ease-out") });
        else
        {
            fila.Pista.BeginAnimation(TranslateTransform.XProperty, null);
            fila.Pista.X = x;
        }

        if (cambio)
        {
            ActualizarHeroe(animar);
            _esperaFondo.Stop();
            _esperaFondo.Start();
        }
    }

    // ───── Juego destacado ─────

    private void ActualizarHeroe(bool animar)
    {
        var juego = Actual;
        PanelChips.Children.Clear();
        if (juego == null)
        {
            TextoNombre.Text = "";
            TextoDetalle.Text = "";
            return;
        }
        var preferencias = Estado.Config.Juegos.GetValueOrDefault(juego.Juego.Clave);
        var preferido = Estado.Config.Emulador(preferencias?.EmuladorPreferido);
        var ultimo = Estado.Config.Emulador(preferencias?.UltimoEmulador);

        TextoNombre.Text = juego.Nombre;
        var detalle = new List<string>();
        if ((preferido ?? ultimo) is { } habitual)
            detalle.Add(habitual.Nombre);
        detalle.Add(preferencias?.UltimaVez is { } cuando ? "Jugado " + Hace(cuando) : "Sin jugar todavía");
        TextoDetalle.Text = string.Join(" · ", detalle).ToUpper(CultureInfo.CurrentCulture);
        TextoJugar.Text = "Jugar"; // A pregunta con qué emulador (el preferido primero)

        if (preferido != null)
            PanelChips.Children.Add(Chips.Crear($"Preferido: {preferido.Nombre}", Tono.Neutro, estrella: true));
        var calificados = 0;
        foreach (var emulador in Estado.EmuladoresDisponibles)
        {
            var c = preferencias?.CalificacionDe(emulador.Id) ?? Calificacion.SinProbar;
            if (c == Calificacion.SinProbar)
                continue;
            calificados++;
            PanelChips.Children.Add(Chips.Crear($"{emulador.Nombre}: {Estado.TextoCalificacion(c)}", Chips.DeCalificacion(c)));
        }
        if (juego.Compat is { } compat)
            PanelChips.Children.Add(Chips.Crear($"Ryubing: {compat.EstadoEs}", Chips.DeCompatibilidad(compat.Nivel)));
        if (juego.CompatCitron is { } citron)
            PanelChips.Children.Add(Chips.Crear($"Citron: {citron.EstadoEs}", Chips.DeCompatibilidad(citron.Nivel)));
        if (juego.CompatYuzu is { } yuzu)
            PanelChips.Children.Add(Chips.Crear($"Eden y Suyu (yuzu 2024): {yuzu.EstadoEs}", Chips.DeCompatibilidad(yuzu.Nivel)));
        if (calificados == 0)
            PanelChips.Children.Add(Chips.Crear("Sin calificar", Tono.Neutro));
        if (preferencias?.Favorito == true)
            PanelChips.Children.Add(Chips.Crear("En favoritos", Tono.Info));

        if (animar)
        {
            // Cambio suave del texto (dur-focus): entra desde un poco a la derecha.
            var duracion = MainWindow.Duracion("dur-focus");
            var curva = MainWindow.Curva("ease-out");
            Heroe.BeginAnimation(OpacityProperty, new DoubleAnimation(0.35, 1, duracion) { EasingFunction = curva });
            PosicionHeroe.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(12, 0, duracion) { EasingFunction = curva });
        }
    }

    private async Task ActualizarFondoAsync()
    {
        var juego = Actual;
        if (juego == null || !juego.Juego.TieneTitleId)
        {
            MainWindow.Actual.FijarFondo(null);
            return;
        }
        var ruta = await Imagenes.FondoAsync(juego.Juego.TitleId, CancellationToken.None);
        if (juego == Actual && ReferenceEquals(MainWindow.Actual.Contenido.Content, this))
            MainWindow.Actual.FijarFondo(ruta);
    }

    /// <summary>"hoy", "ayer", "hace 3 días", "hace 2 semanas" o la fecha.</summary>
    private static string Hace(DateTimeOffset cuando)
    {
        var dias = (DateTime.Today - cuando.LocalDateTime.Date).Days;
        return dias switch
        {
            <= 0 => "hoy",
            1 => "ayer",
            < 14 => $"hace {dias} días",
            < 60 => $"hace {dias / 7} semanas",
            _ => "el " + cuando.LocalDateTime.ToString("d 'de' MMMM", CultureInfo.GetCultureInfo("es-ES")),
        };
    }

    // ───── Ratón ─────

    private void AlPulsarCaratula(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not CaratulaVM caratula)
            return;
        var f = _filas.FindIndex(x => x.Caratulas.Contains(caratula));
        if (f < 0)
            return;
        CambiarFila(f);
        Enfocar(_filas[f].Caratulas.IndexOf(caratula), true);
        if (e.ClickCount == 2)
            Menus.Jugar(caratula.Juego);
    }

    private void AlPulsarJugar(object sender, MouseButtonEventArgs e)
    {
        if (Actual != null)
            Menus.Jugar(Actual);
    }
}
