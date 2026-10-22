using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Lanzador.App.Entrada;
using Lanzador.Core;
using Lanzador.Core.IA;
using Microsoft.Win32;
using Lanzador.App.Audio;

namespace Lanzador.App.Vistas;

public partial class PantallaConfiguracion : UserControl, IPantalla
{
    private readonly Configuracion _copia;
    private readonly ObservableCollection<string> _carpetas;

    public PantallaConfiguracion()
    {
        InitializeComponent();

        // Se edita una copia: Cancelar no deja cambios a medias.
        _copia = JsonSerializer.Deserialize<Configuracion>(JsonSerializer.Serialize(Estado.Config, GestorConfiguracion.OpcionesJson), GestorConfiguracion.OpcionesJson)!;
        _copia.Juegos = new Dictionary<string, PreferenciasJuego>(_copia.Juegos, StringComparer.OrdinalIgnoreCase);
        ListaEmuladores.ItemsSource = _copia.Emuladores;
        _carpetas = new ObservableCollection<string>(_copia.CarpetasJuegos);
        ListaCarpetas.ItemsSource = _carpetas;
        ComboMotor.SelectedItem = ComboMotor.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == _copia.MotorIA) ?? ComboMotor.Items[0];
        CampoModelo.Text = _copia.ModeloIA;
        CasillaPantallaCompleta.IsChecked = _copia.PantallaCompleta;
        CasillaContenidoEnLinea.IsChecked = _copia.ContenidoEnLinea;
        Elegir(ComboCanalEden, _copia.Actualizaciones.CanalEden);
        Elegir(ComboVarianteCitron, _copia.Actualizaciones.VarianteCitron);
        CasillaSonidos.IsChecked = _copia.SonidosActivados;
        for (var v = 10; v <= 100; v += 10)
            ComboVolumen.Items.Add(new ComboBoxItem { Content = $"{v} %", Tag = v });
        ComboVolumen.SelectedIndex = Math.Clamp((int)Math.Round(_copia.VolumenSonido / 10.0) - 1, 0, 9);
        CasillaReducirMovimiento.IsChecked = _copia.ReducirMovimiento;
        CasillaIntro.IsChecked = _copia.MostrarIntro;
        ComboTeclaTrucos.Items.Add(new ComboBoxItem { Content = "Desactivada", Tag = "" });
        for (var f = 1; f <= 12; f++)
            ComboTeclaTrucos.Items.Add(new ComboBoxItem { Content = $"F{f}" + (f == 7 ? " (recomendada)" : ""), Tag = $"F{f}" });
        ComboTeclaTrucos.SelectedItem = ComboTeclaTrucos.Items.Cast<ComboBoxItem>()
            .FirstOrDefault(i => string.Equals((string)i.Tag, _copia.TeclaTrucos, StringComparison.OrdinalIgnoreCase)) ?? ComboTeclaTrucos.Items[0];

        Loaded += (_, _) => Dispatcher.BeginInvoke(() => Formulario.MoveFocus(new TraversalRequest(FocusNavigationDirection.First)));
    }

    public IReadOnlyList<Ayuda> Ayudas { get; } =
    [
        new("A", "Pulsar / marcar", ""),
        new("☰", "Guardar", ""),
        new("B", "Salir sin guardar", "Esc"),
    ];

    public bool UsaTeclado => true;

    public void Ejecutar(Accion accion)
    {
        var enfocado = Keyboard.FocusedElement as FrameworkElement;
        switch (accion)
        {
            case Accion.Arriba: Sonidos.Tocar(Sonido.Paso); Mover(enfocado, FocusNavigationDirection.Previous); break;
            case Accion.Abajo: Sonidos.Tocar(Sonido.Paso); Mover(enfocado, FocusNavigationDirection.Next); break;
            case Accion.Izquierda: Cambiar(enfocado, -1); break;
            case Accion.Derecha: Cambiar(enfocado, 1); break;
            case Accion.Aceptar: Pulsar(enfocado); break;
            case Accion.Menu: AlGuardar(this, new RoutedEventArgs()); break;
            case Accion.Atras: AlCancelar(this, new RoutedEventArgs()); break;
        }
    }

    private void Mover(FrameworkElement? enfocado, FocusNavigationDirection direccion)
    {
        if (enfocado == null || !IsAncestorOf(enfocado))
            Formulario.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        else
            enfocado.MoveFocus(new TraversalRequest(direccion));
        (Keyboard.FocusedElement as FrameworkElement)?.BringIntoView();
    }

    private static void Cambiar(FrameworkElement? enfocado, int paso)
    {
        if (enfocado is ComboBox combo && combo.Items.Count > 0)
            combo.SelectedIndex = (combo.SelectedIndex + paso + combo.Items.Count) % combo.Items.Count;
        else if (enfocado is ListBoxItem item && ItemsControl.ItemsControlFromItemContainer(item) is ListBox lista && lista.Items.Count > 0)
            lista.SelectedIndex = Math.Clamp(lista.SelectedIndex + paso, 0, lista.Items.Count - 1);
    }

    private static void Pulsar(FrameworkElement? enfocado)
    {
        switch (enfocado)
        {
            case Button boton when new ButtonAutomationPeer(boton).GetPattern(PatternInterface.Invoke) is IInvokeProvider invocar:
                invocar.Invoke();
                break;
            case ToggleButton casilla:
                casilla.IsChecked = casilla.IsChecked != true;
                break;
            case ComboBox combo:
                Cambiar(combo, 1);
                break;
        }
    }

    private static EmuladorConfig? EmuladorDe(object sender) => (sender as FrameworkElement)?.DataContext as EmuladorConfig;

    private void AlExaminarExe(object sender, RoutedEventArgs e)
    {
        if (EmuladorDe(sender) is not { } emulador)
            return;
        var dialogo = new OpenFileDialog
        {
            Title = $"Ejecutable de {emulador.Nombre}",
            Filter = "Programas (*.exe)|*.exe",
            InitialDirectory = Directory.Exists(Path.GetDirectoryName(emulador.RutaExe)) ? Path.GetDirectoryName(emulador.RutaExe) : null,
        };
        if (dialogo.ShowDialog() != true)
            return;
        emulador.RutaExe = dialogo.FileName;
        var dir = Path.GetDirectoryName(dialogo.FileName)!;
        var portable = Path.Combine(dir, emulador.Tipo == TipoEmulador.Ryujinx ? "portable" : "user");
        if (Directory.Exists(portable))
            emulador.CarpetaDatos = portable;
        emulador.Activo = true;
        ListaEmuladores.Items.Refresh();
    }

    private void AlExaminarDatos(object sender, RoutedEventArgs e)
    {
        if (EmuladorDe(sender) is not { } emulador)
            return;
        var dialogo = new OpenFolderDialog { Title = $"Carpeta de datos de {emulador.Nombre}" };
        if (dialogo.ShowDialog() != true)
            return;
        emulador.CarpetaDatos = dialogo.FolderName;
        ListaEmuladores.Items.Refresh();
    }

    private void AlAgregarCarpeta(object sender, RoutedEventArgs e)
    {
        var dialogo = new OpenFolderDialog { Title = "Carpeta con juegos", Multiselect = true };
        if (dialogo.ShowDialog() != true)
            return;
        foreach (var carpeta in dialogo.FolderNames)
            if (!_carpetas.Contains(carpeta, StringComparer.OrdinalIgnoreCase))
                _carpetas.Add(carpeta);
    }

    private void AlQuitarCarpeta(object sender, RoutedEventArgs e)
    {
        if (ListaCarpetas.SelectedItem is string carpeta)
            _carpetas.Remove(carpeta);
    }

    private void AlDetectarCarpetas(object sender, RoutedEventArgs e)
    {
        foreach (var carpeta in GestorConfiguracion.DetectarCarpetasJuegos(_copia))
            if (!_carpetas.Contains(carpeta, StringComparer.OrdinalIgnoreCase))
                _carpetas.Add(carpeta);
    }

    private static void Elegir(ComboBox combo, string etiqueta) =>
        combo.SelectedItem = combo.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == etiqueta) ?? combo.Items[0];

    private static string Elegida(ComboBox combo) => (combo.SelectedItem as ComboBoxItem)?.Tag as string ?? (string)((ComboBoxItem)combo.Items[0]).Tag;

    private string MotorElegido => (ComboMotor.SelectedItem as ComboBoxItem)?.Tag as string ?? "claude";

    private async void AlProbarIA(object sender, RoutedEventArgs e)
    {
        var motor = MotorElegido;
        if (motor == "ninguno")
        {
            TextoPrueba.Text = "Elige Claude Code o Codex para probar.";
            return;
        }
        BotonProbar.IsEnabled = false;
        TextoPrueba.Text = $"Probando {TraductorIA.NombreMotor(motor)}… (puede tardar un poco)";
        try
        {
            var r = await TraductorIA.TraducirAsync(motor, CampoModelo.Text.Trim(), "Super Mario 3D World",
                [new ElementoATraducir("t0", "[Infinite Lives]", null), new ElementoATraducir("t1", "[Moon Jump]", "mantener B")],
                null, CancellationToken.None);
            TextoPrueba.Text = r.Count == 0
                ? "La IA respondió, pero sin traducciones."
                : "Funciona: " + string.Join(" · ", r.Values.Select(t => t.Nombre));
        }
        catch (Exception ex)
        {
            TextoPrueba.Text = "Error: " + ex.Message;
        }
        finally
        {
            BotonProbar.IsEnabled = true;
        }
    }

    private void AlGuardar(object sender, RoutedEventArgs e)
    {
        _copia.CarpetasJuegos = GestorConfiguracion.NormalizarCarpetas(_carpetas);
        _copia.MotorIA = MotorElegido;
        _copia.ModeloIA = CampoModelo.Text.Trim();
        _copia.PantallaCompleta = CasillaPantallaCompleta.IsChecked == true;
        // Elegirlo aquí cuenta como respuesta al aviso de la primera vez.
        _copia.ContenidoEnLinea = CasillaContenidoEnLinea.IsChecked == true;
        _copia.AvisoPrivacidadVisto = true;
        _copia.SonidosActivados = CasillaSonidos.IsChecked == true;
        _copia.VolumenSonido = ComboVolumen.SelectedItem is ComboBoxItem { Tag: int volumen } ? volumen : 60;
        _copia.ReducirMovimiento = CasillaReducirMovimiento.IsChecked == true;
        _copia.MostrarIntro = CasillaIntro.IsChecked == true;
        _copia.TeclaTrucos = ComboTeclaTrucos.SelectedItem is ComboBoxItem { Tag: string teclaTrucos } ? teclaTrucos : "F7";
        _copia.Actualizaciones.CanalEden = Elegida(ComboCanalEden);
        _copia.Actualizaciones.VarianteCitron = Elegida(ComboVarianteCitron);
        foreach (var emulador in _copia.Emuladores)
            emulador.RutaExe = emulador.RutaExe.Trim().Trim('"');

        var carpetasCambiaron = !_copia.CarpetasJuegos.SequenceEqual(Estado.Config.CarpetasJuegos, StringComparer.OrdinalIgnoreCase);
        var emuladoresCambiaron = JsonSerializer.Serialize(_copia.Emuladores) != JsonSerializer.Serialize(Estado.Config.Emuladores);
        var descargasActivadas = _copia.ContenidoEnLinea && !Http.ContenidoEnLinea;
        Estado.Config = _copia;
        Estado.ErrorIA = null;
        Estado.GuardarConfig();
        MainWindow.Actual.AplicarPreferencias();

        var ventana = MainWindow.Actual;
        if (ventana.EnPantallaCompleta != _copia.PantallaCompleta)
            ventana.PonerPantallaCompleta(_copia.PantallaCompleta);
        ventana.Volver();
        ventana.Aviso("Configuración guardada.");
        if (carpetasCambiaron || emuladoresCambiaron)
            _ = Catalogo.CargarAsync(false);
        else if (descargasActivadas)
            _ = Catalogo.DescargarFaltantesAsync();
    }

    private void AlCancelar(object sender, RoutedEventArgs e) => MainWindow.Actual.Volver();

    private void AlLeerPrivacidad(object sender, RoutedEventArgs e) => Menus.Abrir(MainWindow.UrlPrivacidad, null);

    /// <summary>Al cambiar el volumen suena «Elegir» con el nuevo volumen, para probarlo.</summary>
    private void AlCambiarVolumen(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || ComboVolumen.SelectedItem is not ComboBoxItem { Tag: int volumen })
            return;
        var antes = Sonidos.Volumen;
        Sonidos.Volumen = volumen / 100.0;
        Sonidos.Tocar(Sonido.Elegir);
        Sonidos.Volumen = antes;
    }
}
