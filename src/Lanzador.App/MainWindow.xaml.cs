using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Lanzador.App.Entrada;
using Lanzador.App.Modelos;
using Lanzador.App.Vistas;
using Lanzador.Core;
using Lanzador.Core.Actualizaciones;
using Lanzador.Core.IA;
using Lanzador.App.Audio;

namespace Lanzador.App;

public partial class MainWindow : Window
{
    public static MainWindow Actual { get; private set; } = null!;

    private readonly Mando _mando = new();
    private readonly List<FrameworkElement> _pila = new();
    private readonly DispatcherTimer _temporizadorAviso = new();
    private DateTime _ignorarHasta;
    private string? _fondoActual;
    private Process? _procesoJuego;
    private bool _cerradoConMando;
    private string _comboUsado = Mando.TextoComboCerrar;
    private readonly EscuchaHid _escuchaHid = new();
    private readonly DispatcherTimer _reloj = new() { Interval = TimeSpan.FromSeconds(10) };
    private readonly DispatcherTimer _cerrarAudio = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly Dictionary<Seccion, FrameworkElement> _secciones = new();
    private Seccion _seccion = Seccion.Inicio;

    public MainWindow()
    {
        InitializeComponent();
        Actual = this;

        Estado.Config = GestorConfiguracion.Cargar();
        Estado.Traducciones = CacheTraducciones.Cargar();
        AplicarPreferencias();

        _mando.AlPulsar += Despachar;
        _mando.AlMantenerComboCerrar += () => _ = CerrarJuegoAsync(Mando.TextoComboCerrar);
        // Los glifos de la interfaz se dibujan con los botones del último mando que se tocó.
        _mando.AlCambiarFamilia += familia => Tema.EstiloMando.Actual.Familia = familia;
        // PS5 y Pro Controller: se escuchan con Raw Input solo mientras se juega (sin quitarle el mando al emulador).
        _escuchaHid.AlMantenerComboCerrar += combinacion => _ = CerrarJuegoAsync(combinacion);
        SourceInitialized += (_, _) =>
        {
            _escuchaHid.Conectar(this);
            HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(ProcesarTeclaGlobal);
        };
        CrearAyudaVolver();
        // Pro Controller y mandos PlayStation (SDL3) solo con el lanzador al frente: durante el juego son del emulador.
        Activated += (_, _) =>
        {
            _mando.SdlActivo = true;
            _cerrarAudio.Stop();
            Sonidos.MantenerSalida(true);
        };
        Deactivated += (_, _) =>
        {
            _mando.SdlActivo = false;
            _cerrarAudio.Stop();
            _cerrarAudio.Start(); // deja terminar el sonido de «Lanzar» antes de soltar la salida
        };
        _cerrarAudio.Tick += (_, _) =>
        {
            _cerrarAudio.Stop();
            if (!IsActive)
                Sonidos.MantenerSalida(false);
        };
        PreviewKeyDown += AlPresionarTecla;
        Raiz.SizeChanged += (_, _) => AjustarEscala();
        _temporizadorAviso.Tick += (_, _) =>
        {
            _temporizadorAviso.Stop();
            Animar(CajaAviso, 0);
        };

        if (Estado.Config.PantallaCompleta)
            PonerPantallaCompleta(true);
        _reloj.Tick += (_, _) => TextoReloj.Text = DateTime.Now.ToString("HH:mm");
        _reloj.Start();
        TextoReloj.Text = DateTime.Now.ToString("HH:mm");
        Loaded += (_, _) =>
        {
            CrearPestanas();
            CambiarSeccion(Seccion.Inicio, 0);
            _ = Catalogo.AsegurarAsync();
            // Mientras corre la intro se cargan los juegos y las carátulas detrás.
            if (Estado.Config.MostrarIntro && !ReducirMovimiento && Environment.GetEnvironmentVariable("LANZADOR_PRUEBA") == null)
                MostrarIntro();
        };
        Closing += (_, e) =>
        {
            if (Estado.CopiaEnCurso is { } copia)
            {
                e.Cancel = true;
                Aviso($"Espera unos segundos: {copia}. Si cierras ahora, el emulador quedaría a medias.", true);
            }
        };
        Closed += (_, _) => _mando.Dispose();
    }

    private IPantalla? PantallaArriba =>
        Superpuestos.Children.Count > 0 ? Superpuestos.Children[^1] as IPantalla : Contenido.Content as IPantalla;

    /// <summary>Una sección a la vista y sin menús encima: LB y RB cambian de sección.</summary>
    private bool EnSeccion => Superpuestos.Children.Count == 0 && _pila.Count == 1 && _pila[0] is ISeccion;

    public Seccion SeccionActual => _seccion;

    /// <summary>Volumen, sonidos y animaciones según la configuración.</summary>
    public void AplicarPreferencias()
    {
        Sonidos.Activados = Estado.Config.SonidosActivados;
        Sonidos.Volumen = Math.Clamp(Estado.Config.VolumenSonido, 0, 100) / 100.0;
        Sonidos.MantenerSalida(IsActive);
        ReducirMovimiento = Estado.Config.ReducirMovimiento;
        // Con movimiento reducido, todas las duraciones valen 0 (lo que se cree a partir de ahora ya no se anima).
        if (ReducirMovimiento)
            Application.Current.Resources["visibilidad-destello"] = Visibility.Collapsed;
        else
            Application.Current.Resources.Remove("visibilidad-destello");
        foreach (var token in new[] { "dur-fast", "dur-focus", "dur-slide", "dur-fade", "dur-page" })
        {
            if (ReducirMovimiento)
                Application.Current.Resources[token] = new Duration(TimeSpan.Zero);
            else
                Application.Current.Resources.Remove(token);
        }
    }

    public static bool ReducirMovimiento { get; private set; }

    /// <summary>Abre una pantalla encima de la actual (Trucos, Configuración…).</summary>
    public void Mostrar(FrameworkElement pantalla)
    {
        Sonidos.Tocar(Sonido.Elegir);
        _pila.Add(pantalla);
        Poner(pantalla, 0, 24);
    }

    /// <summary>Vuelve a la pantalla anterior; desde una sección que no es Inicio, vuelve a Inicio.</summary>
    public void Volver()
    {
        if (_pila.Count <= 1)
        {
            if (_pila.Count == 1 && _pila[0] is ISeccion { Seccion: not Seccion.Inicio })
            {
                Sonidos.Tocar(Sonido.Volver);
                CambiarSeccion(Seccion.Inicio, -1);
            }
            return;
        }
        Sonidos.Tocar(Sonido.Volver);
        _pila.RemoveAt(_pila.Count - 1);
        Poner(_pila[^1], 0, 0);
    }

    /// <summary>Va a una sección de la cabecera, preguntando antes a la pantalla actual si se puede salir.</summary>
    public void IrASeccion(Seccion destino)
    {
        if (_pila.Count == 1 && destino == _seccion)
            return;
        var direccion = Math.Sign(destino.CompareTo(_seccion));
        if (_pila.Count > 0 && _pila[^1] is IPantalla actual)
            actual.PedirSalir(() => CambiarSeccion(destino, direccion == 0 ? 1 : direccion));
        else
            CambiarSeccion(destino, direccion);
    }

    private void CambiarSeccion(Seccion destino, int direccion)
    {
        _seccion = destino;
        var pantalla = PantallaDeSeccion(destino);
        _pila.Clear();
        _pila.Add(pantalla);
        Poner(pantalla, 48 * direccion, 0);
    }

    /// <summary>Inicio y Biblioteca se conservan (el foco queda donde estaba); Emuladores y Controles se crean de nuevo.</summary>
    private FrameworkElement PantallaDeSeccion(Seccion seccion)
    {
        if (_secciones.TryGetValue(seccion, out var guardada))
            return guardada;
        FrameworkElement pantalla = seccion switch
        {
            Seccion.Inicio => new PantallaInicio(),
            Seccion.Biblioteca => new PantallaBiblioteca(),
            Seccion.Emuladores => new PantallaEmuladores(),
            _ => new PantallaControles(),
        };
        if (seccion is Seccion.Inicio or Seccion.Biblioteca)
            _secciones[seccion] = pantalla;
        return pantalla;
    }

    private void Poner(FrameworkElement pantalla, double desdeX, double desdeY)
    {
        Contenido.Content = pantalla;
        Cabecera.Visibility = pantalla is ISeccion ? Visibility.Visible : Visibility.Collapsed;
        if (pantalla is ISeccion seccion)
        {
            MarcarPestana(seccion.Seccion, desdeX != 0);
            if (seccion.Seccion is Seccion.Emuladores or Seccion.Controles)
                FijarFondo(null); // sin arte de juego, como en las maquetas
        }
        ActualizarAyudas();
        (pantalla as IPantalla)?.AlMostrar();

        // Entrada de la pantalla: fundido y un desplazamiento corto (dur-page, ease-out).
        var duracion = Duracion("dur-page");
        var curva = Curva("ease-out");
        Contenido.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duracion) { EasingFunction = curva });
        DesplazamientoContenido.BeginAnimation(TranslateTransform.XProperty, desdeX == 0 ? null : new DoubleAnimation(desdeX, 0, duracion) { EasingFunction = curva });
        DesplazamientoContenido.BeginAnimation(TranslateTransform.YProperty, desdeY == 0 ? null : new DoubleAnimation(desdeY, 0, duracion) { EasingFunction = curva });
    }

    /// <summary>LB/RB en una sección: pasa a la anterior o la siguiente. True si se usó la acción.</summary>
    private bool CambiarSeccionConBotones(Accion accion)
    {
        if (!EnSeccion || accion is not (Accion.AnteriorPestana or Accion.SiguientePestana))
            return false;
        var destino = (int)_seccion + (accion == Accion.SiguientePestana ? 1 : -1);
        var existe = destino >= 0 && destino < Enum.GetValues<Seccion>().Length;
        Sonidos.Moverse(existe);
        if (existe)
            IrASeccion((Seccion)destino);
        return true;
    }

    private void CrearPestanas()
    {
        foreach (var seccion in Enum.GetValues<Seccion>())
        {
            var texto = new TextBlock
            {
                Text = NombreSeccion(seccion),
                FontFamily = (FontFamily)FindResource("font-text"),
                FontSize = 20,
                Foreground = (Brush)FindResource("ink-200"),
            };
            var pestana = new Border { Padding = new Thickness(22, 8, 22, 8), Background = Brushes.Transparent, Child = texto, Cursor = Cursors.Hand };
            pestana.MouseLeftButtonUp += (_, _) => IrASeccion(seccion);
            ListaPestanas.Children.Add(pestana);
        }
    }

    public static string NombreSeccion(Seccion seccion) => seccion switch
    {
        Seccion.Inicio => "Inicio",
        Seccion.Biblioteca => "Biblioteca",
        Seccion.Emuladores => "Emuladores",
        _ => "Controles",
    };

    /// <summary>Resalta la pestaña de la sección y desliza la pastilla de color hasta ella (dur-slide).</summary>
    private void MarcarPestana(Seccion seccion, bool animar)
    {
        for (var i = 0; i < ListaPestanas.Children.Count; i++)
        {
            var activa = i == (int)seccion;
            var texto = (TextBlock)((Border)ListaPestanas.Children[i]).Child;
            texto.Foreground = (Brush)FindResource(activa ? "on-accent" : "ink-200");
            texto.FontWeight = activa ? FontWeights.SemiBold : FontWeights.Normal;
        }
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (ListaPestanas.Children[(int)seccion] is not Border pestana || pestana.ActualWidth <= 0)
                return;
            var x = pestana.TranslatePoint(new Point(0, 0), ListaPestanas).X;
            IndicadorPestana.Height = pestana.ActualHeight;
            if (animar && IndicadorPestana.ActualWidth > 0)
            {
                var duracion = Duracion("dur-slide");
                var curva = Curva("ease-out");
                PosicionIndicador.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x, duracion) { EasingFunction = curva });
                IndicadorPestana.BeginAnimation(WidthProperty, new DoubleAnimation(pestana.ActualWidth, duracion) { EasingFunction = curva });
            }
            else
            {
                PosicionIndicador.BeginAnimation(TranslateTransform.XProperty, null);
                IndicadorPestana.BeginAnimation(WidthProperty, null);
                PosicionIndicador.X = x;
                IndicadorPestana.Width = pestana.ActualWidth;
            }
        });
    }

    public void AbrirMenu(FrameworkElement menu)
    {
        Sonidos.Tocar(Sonido.Elegir);
        Superpuestos.Children.Add(menu);
        ActualizarAyudas();
    }

    public void CerrarMenu(FrameworkElement menu)
    {
        Superpuestos.Children.Remove(menu);
        ActualizarAyudas();
        if (Superpuestos.Children.Count == 0)
            (Contenido.Content as IPantalla)?.AlMostrar();
    }

    /// <summary>Barra inferior de la pantalla de arriba; en las secciones se agrega «LB RB Sección».</summary>
    public void ActualizarAyudas()
    {
        var ayudas = PantallaArriba?.Ayudas;
        if (ayudas != null && EnSeccion)
        {
            var lista = ayudas.ToList();
            var posicion = lista.FindIndex(a => a.Boton is "☰" or "B");
            lista.Insert(posicion < 0 ? lista.Count : posicion, new Ayuda("LB RB", "Sección", "Q / E"));
            ayudas = lista;
        }
        ListaAyudas.ItemsSource = ayudas;
    }

    public void FijarEstado(string texto) => TextoEstado.Text = texto;

    /// <summary>
    /// Escala el lienzo de 1920 × 1080 a la ventana: manda la altura (como en una tele), y el ancho se adapta
    /// sin bajar de 1280 para que las pantallas angostas no se amontonen.
    /// </summary>
    private void AjustarEscala()
    {
        var alto = Raiz.ActualHeight;
        var ancho = Raiz.ActualWidth;
        if (alto <= 0 || ancho <= 0)
            return;
        var escala = Math.Min(alto / AltoDiseno, ancho / AnchoMinimoDiseno);
        Escala.ScaleX = Escala.ScaleY = escala;
        Lienzo.Width = ancho / escala;
        Lienzo.Height = alto / escala;
    }

    private const double AltoDiseno = 1080;
    private const double AnchoMinimoDiseno = 1280;

    public void Aviso(string texto, bool error = false)
    {
        TextoAviso.Text = texto;
        BordeAviso.BorderBrush = (Brush)FindResource(error ? "danger-500" : "focus");
        Sonidos.Tocar(error ? Sonido.Error : Sonido.Aplicado);
        if (CajaAviso.Opacity < 0.5)
        {
            // Entra bajando un poco, como el resto de los paneles (dur-page, ease-out).
            DesplazamientoAviso.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(-16, 0, Duracion("dur-page")) { EasingFunction = Curva("ease-out") });
        }
        Animar(CajaAviso, 1);
        _temporizadorAviso.Stop();
        _temporizadorAviso.Interval = TimeSpan.FromSeconds(error ? 8 : 4);
        _temporizadorAviso.Start();
    }

    public void FijarFondo(string? ruta)
    {
        if (ruta == _fondoActual)
            return;
        _fondoActual = ruta;
        var imagen = Imagen.Cargar(ruta, 1280);
        if (imagen == null)
        {
            Animar(ImagenFondo, 0);
            return;
        }
        ImagenFondo.BeginAnimation(OpacityProperty, null);
        ImagenFondo.Opacity = 0;
        ImagenFondo.Source = imagen;
        Animar(ImagenFondo, 0.6);
    }

    public void PonerPantallaCompleta(bool completa)
    {
        if (completa)
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Normal;
            WindowState = WindowState.Maximized;
        }
        else
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            WindowState = WindowState.Normal;
        }
    }

    public bool EnPantallaCompleta => WindowStyle == WindowStyle.None;

    /// <summary>Abre el juego, esconde el lanzador mientras se juega y vuelve al cerrar el emulador.</summary>
    public async Task JugarAsync(JuegoVM juego, EmuladorConfig emulador)
    {
        Sonidos.Tocar(Sonido.Lanzar);
        var tecla = TeclaTrucos;
        TextoTeclaTrucos.Text = tecla != null ? $"Durante el juego, {tecla} muestra tus trucos activos." : "";
        TextoTeclaTrucos.Visibility = tecla != null ? Visibility.Visible : Visibility.Collapsed;
        TextoAbriendo.Text = juego.Nombre;
        TextoAbriendoCon.Text = $"Abriendo con {emulador.Nombre}…";
        AyudaVolver.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(ReducirMovimiento ? 0 : 300))
        {
            BeginTime = TimeSpan.FromMilliseconds(ReducirMovimiento ? 0 : 200),
        });
        await FundirANegroAsync(true);
        Process proceso;
        try
        {
            proceso = Lanzamiento.Iniciar(emulador, juego.Juego);
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidOperationException or Win32Exception)
        {
            await FundirANegroAsync(false);
            Aviso(ex.Message, true);
            return;
        }
        // Se deja leer cómo volver mientras el emulador arranca (tarda más que esto en abrir su ventana).
        await Task.Delay(2200);

        var preferencias = Estado.Config.PreferenciasDe(juego.Juego.Clave);
        preferencias.UltimaVez = DateTimeOffset.Now;
        preferencias.UltimoEmulador = emulador.Id;
        Estado.GuardarConfig();

        _ = PrepararTrucosAsync(juego, emulador);
        RegistrarTeclaTrucos();
        var (codigo, duracion) = await EsperarEmuladorAsync(proceso, $"Jugando {juego.Nombre} en {emulador.Nombre}…");
        QuitarTeclaTrucos();
        Catalogo.AvisarCambio(); // «Continuar jugando» lo pone primero

        if (_cerradoConMando)
            Aviso($"Cerraste {emulador.Nombre} con {_comboUsado}.");
        else if (duracion < TimeSpan.FromSeconds(8) && codigo != 0)
        {
            Aviso($"{emulador.Nombre} se cerró al iniciar (código {codigo}). Revisa la ruta y los argumentos en Configuración.", true);
            return;
        }
        if (Estado.Config.PreferenciasDe(juego.Juego.Clave).CalificacionDe(emulador.Id) == Calificacion.SinProbar)
            AbrirMenu(Menus.Calificar(juego, emulador, $"¿Cómo funcionó en {emulador.Nombre}?"));
    }

    /// <summary>Abre el emulador sin juego (p. ej. para configurar los mandos) y vuelve al lanzador cuando se cierra.</summary>
    public async Task<bool> AbrirEmuladorAsync(EmuladorConfig emulador, string motivo)
    {
        Process proceso;
        try
        {
            proceso = Process.Start(new ProcessStartInfo(emulador.RutaExe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(emulador.RutaExe)! })
                      ?? throw new InvalidOperationException($"No se pudo iniciar {emulador.Nombre}.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            Aviso(ex.Message, true);
            return false;
        }
        await EsperarEmuladorAsync(proceso, motivo);
        return true;
    }

    /// <summary>Esconde el lanzador mientras el emulador está abierto y lo trae de vuelta al cerrarse.</summary>
    private async Task<(int Codigo, TimeSpan Duracion)> EsperarEmuladorAsync(Process proceso, string estado)
    {
        FijarEstado(estado);
        var inicio = DateTime.UtcNow;
        WindowState = WindowState.Minimized;
        _cerradoConMando = false;
        _procesoJuego = proceso;
        _escuchaHid.Activar();
        int codigo;
        using (proceso)
        {
            await proceso.WaitForExitAsync();
            codigo = proceso.ExitCode;
            _procesoJuego = null;
        }
        _escuchaHid.Desactivar();

        WindowState = EnPantallaCompleta ? WindowState.Maximized : WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        _ignorarHasta = DateTime.UtcNow.AddMilliseconds(800);
        _mando.Reiniciar();
        FijarEstado("");
        _ = FundirANegroAsync(false);
        return (codigo, DateTime.UtcNow - inicio);
    }

    /// <summary>
    /// Cierra el emulador al mantener la combinación del mando: primero le pide cerrar como si se pulsara la X
    /// de la ventana y, si sigue abierto (p. ej. por un "¿seguro que quieres salir?"), lo termina.
    /// </summary>
    private async Task CerrarJuegoAsync(string combinacion)
    {
        var proceso = _procesoJuego;
        if (proceso == null)
            return;
        try
        {
            if (proceso.HasExited)
                return;
            _cerradoConMando = true;
            _comboUsado = combinacion;
            proceso.CloseMainWindow();
            var salida = proceso.WaitForExitAsync();
            if (await Task.WhenAny(salida, Task.Delay(4000)) != salida && !proceso.HasExited)
                proceso.Kill(true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // El proceso terminó mientras tanto.
        }
    }

    /// <summary>
    /// Modo de prueba (variable LANZADOR_PRUEBA="Accion,Accion,…" y LANZADOR_CAPTURAS=carpeta):
    /// ejecuta las acciones una a una y guarda una captura de la ventana tras cada una.
    /// </summary>
    public async Task EjecutarPruebaAsync(IReadOnlyList<string> acciones, string carpetaCapturas)
    {
        Directory.CreateDirectory(carpetaCapturas);
        await Task.Delay(7000);
        Capturar(Path.Combine(carpetaCapturas, "00-inicio.png"));
        for (var i = 0; i < acciones.Count; i++)
        {
            var nombre = acciones[i].Trim();
            if (nombre.StartsWith("Esperar", StringComparison.OrdinalIgnoreCase))
            {
                await Task.Delay(int.TryParse(nombre[7..], out var ms) ? ms : 3000);
                continue;
            }
            if (nombre.StartsWith("Ir:", StringComparison.OrdinalIgnoreCase))
            {
                if (Contenido.Content is PantallaBiblioteca biblioteca)
                    biblioteca.SeleccionarPorNombre(nombre[3..]);
                else if (Contenido.Content is PantallaInicio inicio)
                    inicio.SeleccionarPorNombre(nombre[3..]);
            }
            else if (nombre.StartsWith("Seccion:", StringComparison.OrdinalIgnoreCase) && Enum.TryParse<Seccion>(nombre[8..], true, out var seccion))
                IrASeccion(seccion);
            else if (nombre.Equals("Intro", StringComparison.OrdinalIgnoreCase))
                MostrarIntro();
            else if (nombre.StartsWith("Negro:", StringComparison.OrdinalIgnoreCase))
            {
                // La pantalla negra de «abriendo juego» con la ayuda para volver, sin abrir ningún emulador.
                TextoAbriendo.Text = nombre[6..];
                TextoAbriendoCon.Text = "Abriendo con Eden…";
                AyudaVolver.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)));
                _ = FundirANegroAsync(true);
            }
            else if (nombre.Equals("QuitarNegro", StringComparison.OrdinalIgnoreCase))
                _ = FundirANegroAsync(false);
            else if (nombre.Equals("Escucha", StringComparison.OrdinalIgnoreCase))
                _escuchaHid.Activar();
            else if (nombre.Equals("AbrirMandosSdl", StringComparison.OrdinalIgnoreCase))
            {
                // Abre los mandos que no son XInput con SDL, como un emulador (pasan a reportar sin parar).
                var abiertos = Lanzador.Core.Controles.Sdl3.Mandos().Where(m => !m.EsXInput)
                    .Select(m => (m, Lanzador.Core.Controles.Sdl3.Abrir(m.IdSdl))).ToList();
                File.AppendAllLines(Path.Combine(carpetaCapturas, "mandos-sdl.txt"),
                    abiertos.Select(a => $"{a.m.Nombre} {a.m.Fabricante:X4}:{a.m.Producto:X4} controlador '{a.m.Controlador}' abierto={a.Item2 != IntPtr.Zero}"));
                var bombeo = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
                bombeo.Tick += (_, _) => Lanzador.Core.Controles.Sdl3.Actualizar();
                bombeo.Start();
            }
            else if (nombre.StartsWith("Familia:", StringComparison.OrdinalIgnoreCase) && Enum.TryParse<FamiliaMando>(nombre[8..], true, out var familia))
                Tema.EstiloMando.Actual.Familia = familia;
            else if (nombre.Equals("TeclaTrucos", StringComparison.OrdinalIgnoreCase))
            {
                RegistrarTeclaTrucos();
                File.WriteAllText(Path.Combine(carpetaCapturas, "tecla-trucos.txt"), $"{TeclaTrucos}: registrada={_teclaRegistrada}");
                QuitarTeclaTrucos();
            }
            else if (nombre.StartsWith("VentanaTrucos:", StringComparison.OrdinalIgnoreCase))
            {
                // La ventana flotante de trucos para un juego (con su emulador preferido), sin abrir el emulador.
                var juego = Estado.Juegos.FirstOrDefault(j => j.Nombre.Contains(nombre[14..], StringComparison.CurrentCultureIgnoreCase));
                var emulador = juego == null ? null
                    : Estado.Config.Emulador(Estado.Config.PreferenciasDe(juego.Juego.Clave).EmuladorPreferido) ?? Estado.EmuladoresDisponibles.FirstOrDefault();
                if (juego != null && emulador != null)
                {
                    await PrepararTrucosAsync(juego, emulador);
                    AlternarVentanaTrucos();
                    await Task.Delay(800);
                    _ventanaTrucos?.GuardarCaptura(Path.Combine(carpetaCapturas, "ventana-trucos.png"));
                }
            }
            else if (nombre.Equals("GuardarSonidos", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var sonido in Enum.GetValues<Sonido>())
                    Sonidos.GuardarWav(sonido, Path.Combine(carpetaCapturas, $"sonido-{sonido}.wav"));
            }
            else if (nombre.StartsWith("Aviso:", StringComparison.OrdinalIgnoreCase))
                Aviso(nombre[6..]);
            else if (nombre.StartsWith("Error:", StringComparison.OrdinalIgnoreCase))
                Aviso(nombre[6..], true);
            else if (Enum.TryParse<Accion>(nombre, true, out var accion))
                EjecutarAccion(accion);
            await Task.Delay(1500);
            var nombreArchivo = string.Concat(nombre.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            Capturar(Path.Combine(carpetaCapturas, $"{i + 1:00}-{nombreArchivo}.png"));
        }
        Close();
    }

    private void Capturar(string archivo)
    {
        if (Content is not FrameworkElement raiz)
            return;
        var dpi = VisualTreeHelper.GetDpi(this);
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
            (int)(raiz.ActualWidth * dpi.DpiScaleX), (int)(raiz.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(raiz);
        var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
        png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var archivoSalida = File.Create(archivo);
        png.Save(archivoSalida);
    }

    private void Despachar(Accion accion)
    {
        if (!IsActive || DateTime.UtcNow < _ignorarHasta)
            return;
        EjecutarAccion(accion);
    }

    private void EjecutarAccion(Accion accion)
    {
        if (_introVisible)
        {
            SaltarIntro(); // cualquier botón salta la intro (y no hace nada más)
            return;
        }
        if (!CambiarSeccionConBotones(accion))
            PantallaArriba?.Ejecutar(accion);
    }

    private void AlPresionarTecla(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11)
        {
            PonerPantallaCompleta(!EnPantallaCompleta);
            e.Handled = true;
            return;
        }

        if (_introVisible)
        {
            e.Handled = true;
            SaltarIntro();
            return;
        }

        var pantalla = PantallaArriba;
        if (pantalla == null || (pantalla.UsaTeclado && e.Key != Key.Escape))
            return;

        Accion? accion = e.Key switch
        {
            Key.Up => Accion.Arriba,
            Key.Down => Accion.Abajo,
            Key.Left => Accion.Izquierda,
            Key.Right => Accion.Derecha,
            Key.Enter or Key.Space => Accion.Aceptar,
            Key.Escape or Key.Back => Accion.Atras,
            Key.T => Accion.Trucos,
            Key.O => Accion.Opciones,
            Key.M or Key.F2 or Key.Apps => Accion.Menu,
            Key.V or Key.F1 => Accion.Vista,
            Key.PageUp or Key.Q => Accion.AnteriorPestana,
            Key.PageDown or Key.E => Accion.SiguientePestana,
            _ => null,
        };
        if (accion is { } a)
        {
            e.Handled = true;
            EjecutarAccion(a);
        }
    }

    // ───── Ventana de trucos durante el juego ─────

    private const int WmHotkey = 0x0312;
    private const int IdTeclaTrucos = 0x5452;
    private const uint ModNoRepeat = 0x4000;

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr ventana, int id, uint modificadores, uint tecla);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr ventana, int id);

    private VentanaTrucos? _ventanaTrucos;
    private (string Juego, string Detalle, List<TrucoEnPantalla>? Trucos, string? Mensaje)? _trucosJuego;
    private bool _teclaRegistrada;
    private int _consultaTrucos;

    /// <summary>La tecla configurada ("F7"), o null si está desactivada o no es válida.</summary>
    public static string? TeclaTrucos =>
        Enum.TryParse<Key>(Estado.Config.TeclaTrucos, true, out var tecla) && tecla != Key.None ? tecla.ToString() : null;

    /// <summary>Mientras se juega, la tecla llega al lanzador aunque el emulador tenga el foco (el emulador no la recibe).</summary>
    private void RegistrarTeclaTrucos()
    {
        if (_teclaRegistrada || TeclaTrucos is not { } nombre || !Enum.TryParse<Key>(nombre, out var tecla))
            return;
        _teclaRegistrada = RegisterHotKey(new WindowInteropHelper(this).Handle, IdTeclaTrucos, ModNoRepeat, (uint)KeyInterop.VirtualKeyFromKey(tecla));
    }

    private void QuitarTeclaTrucos()
    {
        if (_teclaRegistrada)
            UnregisterHotKey(new WindowInteropHelper(this).Handle, IdTeclaTrucos);
        _teclaRegistrada = false;
        _ventanaTrucos?.Ocultar();
    }

    private IntPtr ProcesarTeclaGlobal(IntPtr ventana, int mensaje, IntPtr wParam, IntPtr lParam, ref bool manejado)
    {
        if (mensaje == WmHotkey && wParam.ToInt32() == IdTeclaTrucos)
        {
            AlternarVentanaTrucos();
            manejado = true;
        }
        return IntPtr.Zero;
    }

    private void AlternarVentanaTrucos()
    {
        _ventanaTrucos ??= new VentanaTrucos();
        LlenarVentanaTrucos();
        _ventanaTrucos.Alternar();
    }

    private void LlenarVentanaTrucos()
    {
        if (_ventanaTrucos == null)
            return;
        var tecla = TeclaTrucos ?? "F7";
        if (_trucosJuego is { } datos)
            _ventanaTrucos.Fijar(datos.Juego, datos.Detalle, datos.Trucos, datos.Mensaje, tecla);
        else
            _ventanaTrucos.Fijar("", "", null, "Leyendo los trucos…", tecla);
    }

    /// <summary>Lee los trucos activos del juego en ese emulador y para la versión que usa (al abrirlo, en segundo plano).</summary>
    private async Task PrepararTrucosAsync(JuegoVM juego, EmuladorConfig emulador)
    {
        var consulta = ++_consultaTrucos;
        _trucosJuego = null;
        (string, string, List<TrucoEnPantalla>?, string?) resultado;
        if (!juego.Juego.TieneTitleId)
            resultado = (juego.Nombre, emulador.Nombre, null, "El nombre del archivo no trae el ID del juego, así que no sé qué trucos tiene.");
        else
        {
            try
            {
                var titleId = juego.Juego.TitleId;
                var catalogo = await Lanzador.Core.Trucos.FuentesTrucos.CargarAsync(titleId, false, CancellationToken.None);
                resultado = await Task.Run(() =>
                {
                    var version = Lanzador.Core.Trucos.DetectorVersion.Detectar(juego.Juego, emulador, catalogo);
                    var textoVersion = Lanzador.Core.Trucos.VersionDetectada.Texto(version.Version);
                    var buildId = catalogo.BuildDeVersion(version.Version);
                    if (buildId == null)
                        return (juego.Nombre, $"{emulador.Nombre} · {textoVersion}", (List<TrucoEnPantalla>?)null,
                            (string?)$"No conozco el Build ID de la {textoVersion}, así que no puedo leer sus trucos.");
                    var activos = Lanzador.Core.Trucos.GestoresTrucos.Para(emulador).Leer(titleId, buildId)
                        .Where(t => t.Activo)
                        .Select(t => new TrucoEnPantalla(t.Nombre, t.Botones is { Length: > 0 } b ? char.ToUpper(b[0]) + b[1..] : null))
                        .ToList();
                    return (juego.Nombre, $"{emulador.Nombre} · {textoVersion} · {activos.Count} activos", activos,
                        activos.Count == 0 ? $"No hay trucos activos en {emulador.Nombre} para esta versión." : null);
                });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Net.Http.HttpRequestException or System.Text.Json.JsonException)
            {
                resultado = (juego.Nombre, emulador.Nombre, null, $"No se pudieron leer los trucos: {ex.Message}");
            }
        }
        if (consulta != _consultaTrucos)
            return;
        _trucosJuego = resultado;
        if (_ventanaTrucos?.Visible == true)
            LlenarVentanaTrucos();
    }

    // ───── Intro ─────

    private bool _introVisible;
    private Storyboard? _historiaIntro;
    private DispatcherTimer? _sonidoIntro;

    /// <summary>
    /// Intro de ~2,2 s: «Switch» aparece creciendo con su resplandor, una franja de luz cruza las letras, la línea de
    /// acento se abre y todo se funde en Inicio, que sube desde abajo.
    /// </summary>
    public void MostrarIntro()
    {
        _historiaIntro?.Stop();
        _introVisible = true;
        Intro.BeginAnimation(OpacityProperty, null);
        Intro.Opacity = 1;
        Intro.Visibility = Visibility.Visible;
        Sonidos.MantenerSalida(true); // despierta la salida de audio antes de que suene

        var salida = Curva("ease-out");
        var suave = Curva("ease-in-out");
        var historia = new Storyboard();
        void Animar(DependencyObject objetivo, DependencyProperty propiedad, double? desde, double hasta, double inicio, double segundos, IEasingFunction curva)
        {
            var animacion = new DoubleAnimation
            {
                To = hasta,
                BeginTime = TimeSpan.FromSeconds(inicio),
                Duration = TimeSpan.FromSeconds(segundos),
                EasingFunction = curva,
                FillBehavior = FillBehavior.HoldEnd,
            };
            if (desde is { } d)
                animacion.From = d;
            Storyboard.SetTarget(animacion, objetivo);
            Storyboard.SetTargetProperty(animacion, new PropertyPath(propiedad));
            historia.Children.Add(animacion);
        }
        Animar(LogoIntro, OpacityProperty, 0, 1, 0.15, 0.6, salida);
        Animar(EscalaIntro, ScaleTransform.ScaleXProperty, 0.92, 1, 0.15, 0.7, salida);
        Animar(EscalaIntro, ScaleTransform.ScaleYProperty, 0.92, 1, 0.15, 0.7, salida);
        Animar(HaloIntro, OpacityProperty, 0, 1, 0.2, 0.8, salida);
        Animar(ResplandorIntro, OpacityProperty, 0, 0.85, 0.25, 0.6, salida);
        Animar(ResplandorIntro, OpacityProperty, null, 0.4, 1.3, 0.5, suave);
        Animar(DestelloIntro, TranslateTransform.XProperty, -1.2, 1.2, 0.6, 0.85, suave);
        Animar(LineaIntro, WidthProperty, 0, 240, 0.7, 0.5, salida);
        Animar(Intro, OpacityProperty, 1, 0, 1.85, 0.4, suave);
        Animar(EscalaIntro, ScaleTransform.ScaleXProperty, null, 1.05, 1.85, 0.4, suave);
        Animar(EscalaIntro, ScaleTransform.ScaleYProperty, null, 1.05, 1.85, 0.4, suave);
        historia.Completed += (_, _) => TerminarIntro();
        _historiaIntro = historia;
        historia.Begin();

        // Inicio sube desde abajo mientras la intro se funde.
        var entrada = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.85) };
        entrada.Tick += (_, _) =>
        {
            entrada.Stop();
            if (_historiaIntro == historia)
                EntrarContenido();
        };
        entrada.Start();

        _sonidoIntro?.Stop();
        _sonidoIntro = new DispatcherTimer { Interval = TimeSpan.FromSeconds(0.35) };
        _sonidoIntro.Tick += (_, _) =>
        {
            _sonidoIntro.Stop();
            if (_introVisible)
                Sonidos.Tocar(Sonido.Arranque);
        };
        _sonidoIntro.Start();
    }

    /// <summary>Corta la intro con un fundido rápido.</summary>
    private void SaltarIntro()
    {
        if (!_introVisible)
            return;
        _sonidoIntro?.Stop();
        var historia = _historiaIntro;
        historia?.Pause(); // congela el logo donde va y se funde todo encima
        _historiaIntro = null;
        _introVisible = false;
        var fundido = new DoubleAnimation(Intro.Opacity, 0, TimeSpan.FromMilliseconds(180));
        fundido.Completed += (_, _) =>
        {
            historia?.Stop();
            if (!_introVisible)
                Intro.Visibility = Visibility.Collapsed;
        };
        Intro.BeginAnimation(OpacityProperty, fundido);
        EntrarContenido();
    }

    private void TerminarIntro()
    {
        _introVisible = false;
        _historiaIntro = null;
        Intro.Visibility = Visibility.Collapsed;
    }

    private void EntrarContenido()
    {
        var duracion = Duracion("dur-page");
        var curva = Curva("ease-out");
        Contenido.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duracion) { EasingFunction = curva });
        DesplazamientoContenido.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(32, 0, TimeSpan.FromMilliseconds(320)) { EasingFunction = curva });
    }

    private void AlPulsarIntro(object sender, MouseButtonEventArgs e) => SaltarIntro();

    /// <summary>Las filas «Xbox: Vista + LB + RB»… de la pantalla negra al abrir un juego.</summary>
    private void CrearAyudaVolver()
    {
        var plantilla = (DataTemplate)FindResource("PlantillaGlifoFijo"); // cada fila con los botones de su mando
        foreach (var (mando, glifos) in Mando.CombosCerrar)
        {
            var fila = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
            fila.Children.Add(new TextBlock
            {
                Text = mando,
                Style = (Style)FindResource("body-l"),
                Foreground = (Brush)FindResource("ink-200"),
                Width = 170,
                VerticalAlignment = VerticalAlignment.Center,
            });
            for (var i = 0; i < glifos.Length; i++)
            {
                if (i > 0)
                    fila.Children.Add(new TextBlock
                    {
                        Text = "+",
                        Style = (Style)FindResource("body-l"),
                        Foreground = (Brush)FindResource("ink-300"),
                        Margin = new Thickness(10, 0, 10, 2),
                        VerticalAlignment = VerticalAlignment.Center,
                    });
                fila.Children.Add(new ContentPresenter { Content = glifos[i], ContentTemplate = plantilla, VerticalAlignment = VerticalAlignment.Center });
            }
            ListaCombos.Children.Add(fila);
        }
    }

    /// <summary>Funde la ventana a negro antes de abrir un juego (400 ms) y la vuelve a mostrar al regresar.</summary>
    private async Task FundirANegroAsync(bool aNegro)
    {
        if (!aNegro && Negro.Opacity == 0)
            return;
        if (!aNegro)
        {
            // La ayuda se va con el fundido y queda oculta para la próxima vez.
            var duracionAyuda = TimeSpan.FromMilliseconds(ReducirMovimiento ? 0 : 320);
            var ocultar = new DoubleAnimation(0, duracionAyuda);
            AyudaVolver.BeginAnimation(OpacityProperty, ocultar);
        }
        var duracion = ReducirMovimiento ? TimeSpan.Zero : TimeSpan.FromMilliseconds(aNegro ? 400 : 320);
        Negro.IsHitTestVisible = aNegro;
        Negro.BeginAnimation(OpacityProperty, new DoubleAnimation(aNegro ? 1 : 0, duracion) { EasingFunction = Curva("ease-in-out") });
        if (duracion > TimeSpan.Zero)
            await Task.Delay(duracion);
    }

    private static void Animar(UIElement elemento, double opacidad) =>
        elemento.BeginAnimation(OpacityProperty, new DoubleAnimation(opacidad, Duracion("dur-fade")) { EasingFunction = Curva("ease-in-out") });

    /// <summary>Duración de un token de movimiento (dur-fast, dur-focus, dur-slide, dur-fade, dur-page).</summary>
    public static Duration Duracion(string token) => (Duration)Application.Current.FindResource(token);

    /// <summary>Curva de un token de movimiento (ease-out, ease-in-out).</summary>
    public static IEasingFunction Curva(string token) => (IEasingFunction)Application.Current.FindResource(token);
}
