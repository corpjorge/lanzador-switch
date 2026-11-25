using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Lanzador.App.Audio;
using Lanzador.App.Entrada;
using Lanzador.App.Modelos;
using Lanzador.Core;
using Lanzador.Core.Trucos;

namespace Lanzador.App.Vistas;

public partial class PantallaContenidoJuego : UserControl, IPantalla
{
    private sealed record Fila(EntradaContenidoJuego Entrada)
    {
        public string Nombre => Entrada.Nombre;
        public string Estado => Entrada.Estado;
        public bool Convertible => Entrada.Convertible;
        public string Referencia => string.Join(" · ", new[] { Entrada.Detalle, Entrada.Ruta }.OfType<string>());
    }

    private readonly Juego _juego;
    private readonly string _nombreJuego;
    private readonly IReadOnlyList<EmuladorConfig> _emuladores;
    private readonly Action<int> _alCambiarEmulador;
    private readonly HashSet<string> _convertidos = new(StringComparer.OrdinalIgnoreCase);
    private Task<IReadOnlyList<PaqueteJuego>>? _paquetes;
    private IReadOnlyList<EntradaContenidoJuego> _filas = [];
    private int _emulador;
    private int _consulta;
    private bool _buscando;
    private bool _error;
    private TipoContenidoJuego _pestana;

    public PantallaContenidoJuego(JuegoVM juego, IReadOnlyList<EmuladorConfig> emuladores, int emulador, Action<int> alCambiarEmulador)
    {
        InitializeComponent();
        _juego = juego.Juego;
        _nombreJuego = juego.Nombre;
        _emuladores = emuladores;
        _alCambiarEmulador = alCambiarEmulador;
        _emulador = emulador;
        TextoJuego.Text = juego.Nombre;
    }

    public IReadOnlyList<Ayuda> Ayudas =>
    [
        new("A", "Abrir / convertir", "Enter"),
        new("LB RB", "Emulador", "Q / E"),
        new("← →", "Tipo de contenido", "← / →"),
        new("X", "Convertir pendientes", "T"),
        new("B", "Volver", "Esc"),
    ];

    public void AlMostrar()
    {
        _paquetes = null;
        _ = CargarAsync();
    }

    public void Ejecutar(Accion accion)
    {
        switch (accion)
        {
            case Accion.Arriba: MoverFila(-1); break;
            case Accion.Abajo: MoverFila(1); break;
            case Accion.Izquierda: CambiarPestana(Math.Max(0, (int)_pestana - 1)); break;
            case Accion.Derecha: CambiarPestana(Math.Min((int)TipoContenidoJuego.Mod, (int)_pestana + 1)); break;
            case Accion.AnteriorPestana: CambiarEmulador(-1); break;
            case Accion.SiguientePestana: CambiarEmulador(1); break;
            case Accion.Aceptar: AbrirFila(ListaContenido.SelectedItem as Fila); break;
            case Accion.Trucos: AbrirConversion(); break;
            case Accion.Atras: MainWindow.Actual.Volver(); break;
        }
    }

    internal static string Resumen(IReadOnlyList<EntradaContenidoJuego> filas)
    {
        var partes = new List<string>();
        var actualizacion = filas.FirstOrDefault(f => f.Tipo == TipoContenidoJuego.Actualizacion &&
            (f.Activo || f.Estado is "Habilitada" or "Instalada" or "Incluida en el juego"));
        var dlc = filas.Count(f => f.Tipo == TipoContenidoJuego.Dlc && f.Activo);
        var mods = filas.Count(f => f.Tipo == TipoContenidoJuego.Mod);
        var pendientes = filas.Count(f => f.Convertible);
        if (pendientes > 0)
            partes.Add($"{pendientes} por convertir");
        if (actualizacion is { Ruta: { } ruta })
        {
            var (numero, texto) = EscanerBiblioteca.LeerVersion(Path.GetFileNameWithoutExtension(ruta));
            var version = texto ?? (numero is > 0 ? VersionDetectada.Texto(numero.Value) : null);
            partes.Add(version == null ? "Actualización" : $"Actualización {version}");
        }
        else if (actualizacion != null)
            partes.Add(actualizacion.Nombre);
        if (dlc > 0)
            partes.Add($"{dlc} DLC {(dlc == 1 ? "activo" : "activos")}");
        if (mods > 0)
            partes.Add(mods == 1 ? "1 mod" : $"{mods} mods");
        return partes.Count > 0 ? string.Join("  ·  ", partes) : filas.Count > 0 ? "Ver archivos disponibles" : "Sin contenido detectado";
    }

    private async Task CargarAsync()
    {
        var consulta = ++_consulta;
        var emulador = _emuladores.ElementAtOrDefault(_emulador);
        var carpetas = Estado.Config.CarpetasEscaneo;
        var emuladores = Estado.Config.Emuladores.ToArray();
        TextoEmulador.Text = emulador?.Nombre ?? "Sin emulador seleccionado";
        EmuladorAnterior.IsEnabled = _emulador > 0;
        EmuladorSiguiente.IsEnabled = _emulador + 1 < _emuladores.Count;
        _buscando = true;
        _error = false;
        Pintar();
        try
        {
            var paquetes = await (_paquetes ??= Task.Run(() => ContenidoJuego.Buscar(_juego, carpetas, emuladores)));
            var actuales = paquetes.Select(p => _convertidos.Contains(p.Ruta) ? p with { Convertido = true } : p).ToList();
            var filas = await Task.Run(() => ContenidoJuego.Leer(_juego, emulador, actuales));
            if (consulta != _consulta)
                return;
            _filas = filas;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException)
        {
            if (consulta != _consulta)
                return;
            _error = true;
            _filas = [];
        }
        _buscando = false;
        Pintar();
    }

    private void Pintar()
    {
        var pendientes = _filas.Count(f => f.Convertible);
        BotonLote.Visibility = _buscando || pendientes < 2 ? Visibility.Collapsed : Visibility.Visible;
        BotonLote.Content = $"Convertir pendientes de este juego ({pendientes})";
        var visibles = _buscando ? [] : _filas.Where(f => f.Tipo == _pestana).Select(f => new Fila(f)).ToList();
        var anterior = (ListaContenido.SelectedItem as Fila)?.Entrada;
        ListaContenido.ItemsSource = visibles;
        ListaContenido.SelectedIndex = visibles.Count == 0 ? -1 : Math.Max(0, visibles.FindIndex(f => f.Entrada == anterior));
        TextoVacio.Text = _buscando ? "Buscando contenido…"
            : _error ? "No se pudo leer el contenido de este juego."
            : !_juego.TieneTitleId ? "El nombre del archivo del juego no contiene un Title ID."
            : _pestana switch
            {
                TipoContenidoJuego.Actualizacion => "No se encontraron actualizaciones para este juego.",
                TipoContenidoJuego.Dlc => "No se encontraron DLC para este juego.",
                _ => "No se encontraron mods para este emulador.",
            };
        TextoVacio.Visibility = visibles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CambiarEmulador(int paso)
    {
        var destino = _emulador + paso;
        if (destino < 0 || destino >= _emuladores.Count)
        {
            Sonidos.Moverse(false);
            return;
        }
        Sonidos.Moverse(true);
        _emulador = destino;
        _alCambiarEmulador(destino);
        _ = CargarAsync();
    }

    private void CambiarPestana(int indice)
    {
        var pestana = (TipoContenidoJuego)indice;
        if (pestana == _pestana)
            return;
        _pestana = pestana;
        PestanaActualizaciones.IsChecked = pestana == TipoContenidoJuego.Actualizacion;
        PestanaDlc.IsChecked = pestana == TipoContenidoJuego.Dlc;
        PestanaMods.IsChecked = pestana == TipoContenidoJuego.Mod;
        Sonidos.Moverse(true);
        Pintar();
    }

    private void MoverFila(int paso)
    {
        var destino = ListaContenido.SelectedIndex + paso;
        var posible = destino >= 0 && destino < ListaContenido.Items.Count;
        Sonidos.Moverse(posible);
        if (!posible)
            return;
        ListaContenido.SelectedIndex = destino;
        ListaContenido.ScrollIntoView(ListaContenido.SelectedItem);
    }

    private void AbrirFila(Fila? fila)
    {
        if (fila?.Entrada is not { Ruta: { } ruta } entrada)
            return;
        if (entrada.Convertible)
            AbrirConversion(ruta);
        else if (Directory.Exists(ruta))
            Menus.Abrir("explorer.exe", $"\"{ruta}\"");
        else if (File.Exists(ruta))
            Menus.Abrir("explorer.exe", $"/select,\"{ruta}\"");
    }

    private void AbrirConversion(string? ruta = null)
    {
        if (_buscando)
            return;
        var archivos = _filas.Where(f => f.Convertible).Select(f => f.Ruta!).ToArray();
        if (archivos.Length == 0)
        {
            MainWindow.Actual.Aviso("No hay archivos para convertir de este juego.");
            return;
        }
        MainWindow.Actual.Mostrar(new PantallaConversion(ruta, archivos, _nombreJuego, confirmarLote: ruta == null,
            alConvertir: convertido => _convertidos.Add(convertido)));
    }

    private void AlPulsarPestana(object sender, RoutedEventArgs e) =>
        CambiarPestana(sender == PestanaActualizaciones ? 0 : sender == PestanaDlc ? 1 : 2);

    private void AlCambiarEmulador(object sender, RoutedEventArgs e) =>
        CambiarEmulador(sender == EmuladorAnterior ? -1 : 1);

    private void AlConvertirLote(object sender, RoutedEventArgs e) => AbrirConversion();

    private void AlConvertirArchivo(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is Fila fila)
            AbrirFila(fila);
    }

    private void AlAbrirFila(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject origen &&
            ItemsControl.ContainerFromElement(ListaContenido, origen) is ListBoxItem { DataContext: Fila fila })
            AbrirFila(fila);
    }
}
