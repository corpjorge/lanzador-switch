using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Lanzador.App.Entrada;
using Lanzador.App.Modelos;
using Lanzador.App.Tema;
using Lanzador.Core;
using Lanzador.Core.Trucos;
using Lanzador.App.Audio;

namespace Lanzador.App.Vistas;

/// <summary>
/// Página de un juego: carátula grande, con qué emulador jugar (LB/RB), sus trucos, tu calificación,
/// la versión que usa cada emulador y el archivo. Y lo marca como favorito.
/// </summary>
public partial class PantallaJuego : UserControl, IPantalla
{
    private enum Foco { Jugar, Trucos, Calificacion, Archivo }

    private readonly JuegoVM _juego;
    private readonly List<EmuladorConfig> _emuladores;
    private CatalogoTrucos? _catalogo;
    private bool _catalogoFallo;
    private int _emulador;
    private Foco _foco = Foco.Jugar;
    private int _consulta;

    public PantallaJuego(JuegoVM juego, string origen)
    {
        InitializeComponent();
        _juego = juego;
        TextoOrigen.Text = origen;
        TextoMiga.Text = juego.Nombre;
        TextoNombre.Text = juego.Nombre;
        TextoIniciales.Text = juego.Iniciales;
        TextoId.Text = juego.Juego.TieneTitleId ? juego.Juego.TitleId : "El nombre del archivo no trae el ID del juego";
        TextoArchivo.Text = juego.Juego.RutaArchivo;
        TextoArchivo.ToolTip = juego.Juego.RutaArchivo;
        TextoVolver.Text = $"Para volver al lanzador durante el juego, mantén 1,5 s {Mando.TextoComboTodos}."
                           + (MainWindow.TeclaTrucos is { } tecla ? $" {tecla} muestra tus trucos activos." : "");

        _emuladores = Estado.EmuladoresDisponibles.ToList();
        var preferencias = Estado.Config.Juegos.GetValueOrDefault(juego.Juego.Clave);
        var inicial = preferencias?.EmuladorPreferido ?? preferencias?.UltimoEmulador;
        _emulador = Math.Max(0, _emuladores.FindIndex(e => e.Id == inicial));

        Loaded += async (_, _) =>
        {
            await CargarCaratulaAsync();
            await CargarTrucosAsync();
        };
        MarcarFoco();
    }

    public IReadOnlyList<Ayuda> Ayudas => _foco switch
    {
        Foco.Jugar =>
        [
            new("A", Emulador is { } e ? $"Jugar con {e.Nombre}" : "Jugar", "Enter"),
            new("X", "Trucos", "T"),
            new("Y", Favorito ? "Quitar de favoritos" : "Favorito", "O"),
            new("LB RB", "Emulador", "Q / E"),
            new("☰", "Más", "M"),
            new("B", "Volver", "Esc"),
        ],
        _ =>
        [
            new("A", _foco == Foco.Archivo ? "Abrir la carpeta" : "Abrir", "Enter"),
            new("Y", Favorito ? "Quitar de favoritos" : "Favorito", "O"),
            new("LB RB", "Emulador", "Q / E"),
            new("☰", "Más", "M"),
            new("B", "Volver", "Esc"),
        ],
    };

    private EmuladorConfig? Emulador => _emulador >= 0 && _emulador < _emuladores.Count ? _emuladores[_emulador] : null;

    private PreferenciasJuego Preferencias => Estado.Config.PreferenciasDe(_juego.Juego.Clave);

    private bool Favorito => Estado.Config.Juegos.GetValueOrDefault(_juego.Juego.Clave)?.Favorito == true;

    public void AlMostrar()
    {
        ActualizarTodo();
        if (_juego.Juego.TieneTitleId)
            _ = PonerFondoAsync();
    }

    public void Ejecutar(Accion accion)
    {
        switch (accion)
        {
            case Accion.Arriba: Sonidos.Moverse(MoverFoco(-1)); break;
            case Accion.Abajo: Sonidos.Moverse(MoverFoco(1)); break;
            case Accion.AnteriorPestana: Sonidos.Moverse(CambiarEmulador(-1)); break;
            case Accion.SiguientePestana: Sonidos.Moverse(CambiarEmulador(1)); break;
            case Accion.Aceptar: Activar(_foco); break;
            case Accion.Trucos: Activar(Foco.Trucos); break;
            case Accion.Opciones: CambiarFavorito(); break;
            case Accion.Menu: MainWindow.Actual.AbrirMenu(Menus.OpcionesJuego(_juego)); break;
            case Accion.Atras: MainWindow.Actual.Volver(); break;
        }
    }

    // ───── Acciones ─────

    private void Activar(Foco foco)
    {
        switch (foco)
        {
            case Foco.Jugar:
                if (Emulador is { } emulador)
                    _ = MainWindow.Actual.JugarAsync(_juego, emulador);
                else
                    MainWindow.Actual.Aviso("No hay emuladores configurados. Revisa Configuración (botón ☰).", true);
                break;
            case Foco.Trucos:
                if (_catalogoFallo)
                    _ = CargarTrucosAsync();
                else
                    Menus.AbrirTrucos(_juego, Emulador?.Id);
                break;
            case Foco.Calificacion:
                if (Emulador is { } aCalificar)
                    MainWindow.Actual.AbrirMenu(Menus.Calificar(_juego, aCalificar));
                break;
            case Foco.Archivo:
                Menus.Abrir("explorer.exe", $"/select,\"{_juego.Juego.RutaArchivo}\"");
                break;
        }
    }

    private void CambiarFavorito()
    {
        var preferencias = Preferencias;
        preferencias.Favorito = !preferencias.Favorito;
        Estado.GuardarConfig();
        Catalogo.AvisarCambio();
        MainWindow.Actual.Aviso(preferencias.Favorito ? $"Listo: {_juego.Nombre} está en favoritos." : $"Listo: {_juego.Nombre} ya no está en favoritos.");
        ActualizarChips();
        MainWindow.Actual.ActualizarAyudas();
    }

    private bool CambiarEmulador(int paso)
    {
        var destino = _emulador + paso;
        if (destino < 0 || destino >= _emuladores.Count)
            return false;
        _emulador = destino;
        ActualizarTodo();
        MainWindow.Actual.ActualizarAyudas();
        return true;
    }

    private bool MoverFoco(int paso)
    {
        var destino = (int)_foco + paso;
        if (destino < 0 || destino > (int)Foco.Archivo)
            return false;
        _foco = (Foco)destino;
        MarcarFoco();
        MainWindow.Actual.ActualizarAyudas();
        return true;
    }

    private void MarcarFoco()
    {
        var jugar = _foco == Foco.Jugar;
        BrilloJugar.Visibility = jugar ? Visibility.Visible : Visibility.Collapsed;
        CajaJugar.BorderBrush = jugar ? (Brush)FindResource("focus") : Brushes.Transparent;
        foreach (var (fila, foco) in new[] { (FilaTrucos, Foco.Trucos), (FilaCalificacion, Foco.Calificacion), (FilaArchivo, Foco.Archivo) })
        {
            var activa = foco == _foco;
            fila.Background = activa ? (Brush)FindResource("surface-200") : (Brush)FindResource("surface-glass-strong");
            fila.BorderBrush = activa ? (Brush)FindResource("focus") : Brushes.Transparent;
            fila.Effect = activa ? (System.Windows.Media.Effects.Effect)FindResource("shadow-focus") : null;
        }
    }

    // ───── Datos ─────

    private void ActualizarTodo()
    {
        ActualizarChips();
        ActualizarSelector();
        ActualizarCalificacion();
        ActualizarVersionYTrucos();
    }

    private void ActualizarChips()
    {
        PanelChips.Children.Clear();
        var preferencias = Estado.Config.Juegos.GetValueOrDefault(_juego.Juego.Clave);
        if (Estado.Config.Emulador(preferencias?.EmuladorPreferido) is { } preferido)
            PanelChips.Children.Add(Chips.Crear($"Preferido: {preferido.Nombre}", Tono.Neutro, estrella: true));
        foreach (var emulador in _emuladores)
        {
            var c = preferencias?.CalificacionDe(emulador.Id) ?? Calificacion.SinProbar;
            if (c != Calificacion.SinProbar)
                PanelChips.Children.Add(Chips.Crear($"{emulador.Nombre}: {Estado.TextoCalificacion(c)}", Chips.DeCalificacion(c)));
        }
        if (_juego.Compat is { } compat)
            PanelChips.Children.Add(Chips.Crear("Lista de Ryubing: " + compat.EstadoEs + (compat.Etiquetas.Length > 0 ? $" · {compat.EtiquetasEs}" : ""),
                Chips.DeCompatibilidad(compat.Nivel)));
        if (_juego.CompatCitron is { } citron)
            PanelChips.Children.Add(Chips.Crear($"Lista de Citron: {citron.EstadoEs}", Chips.DeCompatibilidad(citron.Nivel)));
        if (_juego.CompatYuzu is { } yuzu)
            PanelChips.Children.Add(Chips.Crear($"Lista de yuzu (2024, base de Eden y Suyu): {yuzu.EstadoEs}", Chips.DeCompatibilidad(yuzu.Nivel)));
        if (preferencias?.Favorito == true)
            PanelChips.Children.Add(Chips.Crear("En favoritos", Tono.Info));
        if (preferencias?.UltimaVez is { } cuando)
            PanelChips.Children.Add(Chips.Crear($"Última vez: {cuando.LocalDateTime:dd/MM/yyyy HH:mm}", Tono.Neutro));
    }

    private void ActualizarSelector()
    {
        ListaEmuladores.Children.Clear();
        var preferido = Estado.Config.Juegos.GetValueOrDefault(_juego.Juego.Clave)?.EmuladorPreferido;
        if (_emuladores.Count == 0)
        {
            ListaEmuladores.Children.Add(new TextBlock { Text = "Sin emuladores", Style = (Style)FindResource("body"), Margin = new Thickness(16, 0, 16, 0) });
            TextoJugar.Text = "Jugar";
            return;
        }
        for (var i = 0; i < _emuladores.Count; i++)
        {
            var emulador = _emuladores[i];
            var activo = i == _emulador;
            var texto = new TextBlock
            {
                FontFamily = (FontFamily)FindResource("font-text"),
                FontSize = 22,
                FontWeight = activo ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = (Brush)FindResource(activo ? "ink-100" : "ink-200"),
                VerticalAlignment = VerticalAlignment.Center,
            };
            texto.Inlines.Add(new Run(emulador.Nombre));
            if (emulador.Id == preferido)
                texto.Inlines.Add(new Run(" ★") { Foreground = (Brush)FindResource("star-400") });
            var contenido = new StackPanel { Orientation = Orientation.Horizontal };
            if (IconosEmulador.De(emulador) is { } icono)
            {
                var imagen = new Image { Source = icono, Width = 26, Height = 26, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
                RenderOptions.SetBitmapScalingMode(imagen, BitmapScalingMode.HighQuality);
                contenido.Children.Add(imagen);
            }
            contenido.Children.Add(texto);
            var pastilla = new Border
            {
                Padding = new Thickness(activo ? 14 : 12, 6, activo ? 20 : 16, 6),
                Background = activo ? (Brush)FindResource("surface-200") : Brushes.Transparent,
                Child = contenido,
                Cursor = Cursors.Hand,
            };
            Forma.SetPastilla(pastilla, true);
            var indice = i;
            pastilla.MouseLeftButtonUp += (_, _) =>
            {
                _emulador = indice;
                ActualizarTodo();
                MainWindow.Actual.ActualizarAyudas();
            };
            ListaEmuladores.Children.Add(pastilla);
        }
        TextoJugar.Text = $"Jugar con {_emuladores[_emulador].Nombre}";
    }

    private void ActualizarCalificacion()
    {
        var preferencias = Estado.Config.Juegos.GetValueOrDefault(_juego.Juego.Clave);
        TextoCalificacion.Text = _emuladores.Count == 0 ? "—"
            : Agrupar(_emuladores.Select(e => (e, Estado.TextoCalificacion(preferencias?.CalificacionDe(e.Id) ?? Calificacion.SinProbar))));
    }

    /// <summary>Versión que usa cada emulador y, para el elegido, su Build ID y cuántos trucos tiene.</summary>
    private void ActualizarVersionYTrucos()
    {
        if (Emulador is not { } elegido)
        {
            TextoVersion.Text = "—";
            TextoTrucos.Text = "No hay emuladores configurados";
            TextoBuildId.Text = "";
            return;
        }
        var versiones = _emuladores.Select(e => (Emulador: e, Version: Detectar(e))).ToList();
        TextoVersion.Text = Agrupar(versiones.Select(v => (v.Emulador, VersionDetectada.Texto(v.Version.Version))));
        var version = versiones.First(v => v.Emulador == elegido).Version;
        var buildId = _catalogo?.BuildDeVersion(version.Version);
        TextoBuildId.Text = buildId ?? "";
        TextoBuildId.ToolTip = buildId != null ? "Build ID de esa versión" : null;

        if (!_juego.Juego.TieneTitleId)
        {
            TextoTrucos.Text = "Sin el ID del juego no puedo buscar sus trucos";
            return;
        }
        if (_catalogo == null)
        {
            TextoTrucos.Text = _catalogoFallo ? "No se pudo consultar los trucos (A para reintentar)" : "Buscando trucos…";
            return;
        }
        var textoVersion = VersionDetectada.Texto(version.Version);
        if (buildId == null)
        {
            TextoTrucos.Text = $"No conozco el Build ID de la {textoVersion}";
            return;
        }
        List<TrucoInstalado> instalados;
        try
        {
            instalados = GestoresTrucos.Para(elegido).Leer(_juego.Juego.TitleId, buildId);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TextoTrucos.Text = $"No se pudieron leer los trucos de {elegido.Nombre}";
            return;
        }
        var disponibles = _catalogo.Para(buildId).Count;
        var activos = instalados.Count(t => t.Activo);
        TextoTrucos.Text = instalados.Count == 0
            ? $"Ninguno instalado en {elegido.Nombre} ({textoVersion}) · {disponibles} disponibles"
            : $"{activos} activos de {instalados.Count} en {elegido.Nombre} ({textoVersion}) · {disponibles} disponibles";
    }

    /// <summary>"Ryujinx: v4 · Eden, Citron Neo y Suyu: v0": junta los emuladores con el mismo valor, el elegido primero.</summary>
    private string Agrupar(IEnumerable<(EmuladorConfig Emulador, string Valor)> valores)
    {
        var grupos = valores
            .GroupBy(v => v.Valor)
            .OrderByDescending(g => g.Any(v => v.Emulador == Emulador))
            .Select(g =>
            {
                var nombres = g.Select(v => v.Emulador.Nombre).ToList();
                var lista = nombres.Count == 1 ? nombres[0] : string.Join(", ", nombres.SkipLast(1)) + " y " + nombres[^1];
                return $"{lista}: {g.Key}";
            });
        return string.Join(" · ", grupos);
    }

    private VersionDetectada Detectar(EmuladorConfig emulador)
    {
        try
        {
            return DetectorVersion.Detectar(_juego.Juego, emulador, _catalogo);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new VersionDetectada(0, true, ex.Message);
        }
    }

    private async Task CargarTrucosAsync()
    {
        if (!_juego.Juego.TieneTitleId || _catalogo != null)
            return;
        var consulta = ++_consulta;
        _catalogoFallo = false;
        try
        {
            var catalogo = await FuentesTrucos.CargarAsync(_juego.Juego.TitleId, false, CancellationToken.None);
            if (consulta != _consulta)
                return;
            _catalogo = catalogo;
        }
        catch (Exception ex) when (ex is IOException or System.Net.Http.HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            _catalogoFallo = true;
        }
        ActualizarVersionYTrucos();
    }

    private async Task CargarCaratulaAsync()
    {
        if (!_juego.Juego.TieneTitleId)
            return;
        var ruta = await Imagenes.IconoGrandeAsync(_juego.Juego.TitleId, CancellationToken.None);
        var imagen = ruta == null ? null : await Task.Run(() => Imagen.Cargar(ruta, 720));
        if (imagen == null)
            return;
        ImagenCaratula.Source = imagen;
        TextoIniciales.Visibility = Visibility.Collapsed;
        ImagenCaratula.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, MainWindow.Duracion("dur-fade")));
    }

    private async Task PonerFondoAsync()
    {
        var ruta = await Imagenes.FondoAsync(_juego.Juego.TitleId, CancellationToken.None);
        if (ReferenceEquals(MainWindow.Actual.Contenido.Content, this))
            MainWindow.Actual.FijarFondo(ruta);
    }

    // ───── Ratón ─────

    private void AlPulsarJugar(object sender, MouseButtonEventArgs e)
    {
        _foco = Foco.Jugar;
        MarcarFoco();
        Activar(Foco.Jugar);
    }

    private void AlPulsarFila(object sender, MouseButtonEventArgs e)
    {
        var foco = sender == FilaTrucos ? Foco.Trucos : sender == FilaCalificacion ? Foco.Calificacion : Foco.Archivo;
        _foco = foco;
        MarcarFoco();
        MainWindow.Actual.ActualizarAyudas();
        Activar(foco);
    }
}
