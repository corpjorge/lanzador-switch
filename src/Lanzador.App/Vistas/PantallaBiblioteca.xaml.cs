using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Lanzador.App.Audio;
using Lanzador.App.Entrada;
using Lanzador.App.Modelos;
using Lanzador.Core;

namespace Lanzador.App.Vistas;

/// <summary>Todos los juegos en cuadrícula (tile-grid 200), con filtros que se recorren con Y.</summary>
public partial class PantallaBiblioteca : UserControl, ISeccion
{
    private const double AnchoCelda = 200 + 24;
    private const double AltoCelda = 200 + 12 + 22 + 36;

    private enum Filtro { Todos, Favoritos, Jugados, JugablesRyubing, SinCalificar }

    private List<JuegoVM> _visibles = new();
    private Filtro _filtro = Filtro.Todos;
    private int _indice = -1;

    public PantallaBiblioteca()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            Catalogo.AlCambiar += AlCambiarCatalogo;
            AlCambiarCatalogo();
        };
        Unloaded += (_, _) => Catalogo.AlCambiar -= AlCambiarCatalogo;
    }

    public Seccion Seccion => Seccion.Biblioteca;

    public IReadOnlyList<Ayuda> Ayudas { get; } =
    [
        new("A", "Abrir", "Enter"),
        new("X", "Trucos", "T"),
        new("Y", "Filtrar", "O"),
        new("☰", "Menú", "M"),
    ];

    private JuegoVM? Actual => _indice >= 0 && _indice < _visibles.Count ? _visibles[_indice] : null;

    private int Columnas => Math.Max(1, (int)((Zona.ActualWidth - 128 + 24) / AnchoCelda));

    public void AlMostrar() => MainWindow.Actual.FijarFondo(null);

    public void Ejecutar(Accion accion)
    {
        var antes = _indice;
        var columnas = Columnas;
        switch (accion)
        {
            case Accion.Izquierda: if (_indice % columnas > 0) Seleccionar(_indice - 1); break;
            case Accion.Derecha: if (_indice % columnas < columnas - 1 && _indice + 1 < _visibles.Count) Seleccionar(_indice + 1); break;
            case Accion.Arriba: if (_indice - columnas >= 0) Seleccionar(_indice - columnas); break;
            case Accion.Abajo:
                if (_indice + columnas < _visibles.Count)
                    Seleccionar(_indice + columnas);
                else if (_indice / columnas < (_visibles.Count - 1) / columnas)
                    Seleccionar(_visibles.Count - 1);
                break;
            case Accion.Aceptar: if (Actual != null) Menus.AbrirJuego(Actual); return;
            case Accion.Trucos: if (Actual != null) Menus.AbrirTrucos(Actual); return;
            case Accion.Opciones: CambiarFiltro(); return;
            case Accion.Menu: MainWindow.Actual.AbrirMenu(Menus.Principal()); return;
            default: return;
        }
        Sonidos.Moverse(_indice != antes);
    }

    public void SeleccionarPorNombre(string texto)
    {
        var indice = _visibles.FindIndex(j => j.Nombre.Contains(texto, StringComparison.CurrentCultureIgnoreCase));
        if (indice >= 0)
            Seleccionar(indice);
    }

    private void AlCambiarCatalogo() => Filtrar(Actual?.Juego.Clave);

    // ───── Filtros ─────

    private static string NombreFiltro(Filtro filtro) => filtro switch
    {
        Filtro.Favoritos => "Favoritos",
        Filtro.Jugados => "Jugados",
        Filtro.JugablesRyubing => "Jugables en Ryubing",
        Filtro.SinCalificar => "Sin calificar",
        _ => "Todos",
    };

    private static bool Cumple(JuegoVM juego, Filtro filtro)
    {
        var preferencias = Estado.Config.Juegos.GetValueOrDefault(juego.Juego.Clave);
        return filtro switch
        {
            Filtro.Favoritos => preferencias?.Favorito == true,
            Filtro.Jugados => preferencias?.UltimaVez != null,
            Filtro.JugablesRyubing => juego.Compat is { Nivel: >= 4 },
            Filtro.SinCalificar => preferencias == null || preferencias.Calificaciones.Values.All(c => c == Calificacion.SinProbar),
            _ => true,
        };
    }

    private void CambiarFiltro()
    {
        _filtro = (Filtro)(((int)_filtro + 1) % Enum.GetValues<Filtro>().Length);
        Sonidos.Tocar(Sonido.Encender);
        Filtrar(Actual?.Juego.Clave);
    }

    private void Filtrar(string? claveAnterior)
    {
        foreach (var juego in _visibles)
            juego.Seleccionado = false;
        _visibles = Estado.Juegos.Where(j => Cumple(j, _filtro)).ToList();
        Cuadricula.ItemsSource = _visibles;
        PintarFiltros();

        TextoMensaje.Text = Catalogo.Mensaje
                            ?? (_visibles.Count == 0 ? $"No hay juegos en «{NombreFiltro(_filtro)}». Pulsa Y para cambiar de filtro." : "");
        TextoMensaje.Visibility = TextoMensaje.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        _indice = -1;
        if (_visibles.Count > 0)
            Seleccionar(Math.Max(0, _visibles.FindIndex(j => j.Juego.Clave == claveAnterior)), animar: false);
    }

    private void PintarFiltros()
    {
        ListaFiltros.Children.Clear();
        foreach (var filtro in Enum.GetValues<Filtro>())
        {
            var activo = filtro == _filtro;
            var texto = NombreFiltro(filtro) + (activo ? $" · {_visibles.Count}" : "");
            var chip = new Border
            {
                Padding = new Thickness(activo ? 17 : 20, activo ? 5 : 8, activo ? 17 : 20, activo ? 5 : 8),
                Margin = new Thickness(0, 0, 12, 0),
                Background = (Brush)FindResource(activo ? "surface-200" : "surface-100"),
                BorderBrush = (Brush)FindResource("focus"),
                BorderThickness = new Thickness(activo ? 3 : 0),
                Cursor = Cursors.Hand,
                Child = new TextBlock
                {
                    Text = texto,
                    FontFamily = (FontFamily)FindResource("font-text"),
                    FontSize = 20,
                    Foreground = (Brush)FindResource(activo ? "ink-100" : "ink-200"),
                },
            };
            Tema.Forma.SetPastilla(chip, true);
            chip.MouseLeftButtonUp += (_, _) =>
            {
                _filtro = filtro;
                Filtrar(Actual?.Juego.Clave);
            };
            ListaFiltros.Children.Add(chip);
        }
    }

    // ───── Foco y desplazamiento ─────

    private void Seleccionar(int indice, bool animar = true)
    {
        if (_visibles.Count == 0)
            return;
        indice = Math.Clamp(indice, 0, _visibles.Count - 1);
        if (indice == _indice)
            return;
        if (Actual != null)
            Actual.Seleccionado = false;
        _indice = indice;
        Actual!.Seleccionado = true;
        Desplazar(animar);
    }

    /// <summary>La fila con foco queda como segunda visible (la primera si es la de arriba), con un deslizamiento suave.</summary>
    private void Desplazar(bool animar)
    {
        var columnas = Columnas;
        var filasVisibles = Math.Max(1, (int)((Zona.ActualHeight - 28) / AltoCelda));
        var filas = (_visibles.Count + columnas - 1) / columnas;
        var fila = _indice / columnas;
        var primera = Math.Clamp(fila - 1, 0, Math.Max(0, filas - filasVisibles));
        if (fila - primera >= filasVisibles)
            primera = fila - filasVisibles + 1;
        var y = -primera * AltoCelda;
        if (animar)
            PosicionCuadricula.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(y, MainWindow.Duracion("dur-slide")) { EasingFunction = MainWindow.Curva("ease-out") });
        else
        {
            PosicionCuadricula.BeginAnimation(TranslateTransform.YProperty, null);
            PosicionCuadricula.Y = y;
        }
    }

    private void AlCambiarTamanoZona(object sender, SizeChangedEventArgs e)
    {
        Cuadricula.Width = Math.Max(AnchoCelda, Zona.ActualWidth - 128 + 24);
        if (_indice >= 0)
            Desplazar(false);
    }

    private void AlPulsarJuego(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not JuegoVM juego)
            return;
        Seleccionar(_visibles.IndexOf(juego));
        if (e.ClickCount == 2)
            Menus.AbrirJuego(juego);
    }
}
