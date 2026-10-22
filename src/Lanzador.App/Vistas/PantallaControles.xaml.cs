using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Lanzador.App.Entrada;
using Lanzador.App.Modelos;
using Lanzador.Core;
using Lanzador.Core.Actualizaciones;
using Lanzador.Core.Controles;
using Lanzador.App.Audio;

namespace Lanzador.App.Vistas;

/// <summary>
/// Mandos por jugador elegidos en el lanzador. Al aplicar se escriben en todos los emuladores instalados
/// (Eden, Citron, Suyu y Ryujinx) con <see cref="GeneradorControles"/>.
/// </summary>
public partial class PantallaControles : UserControl, ISeccion
{
    /// <summary>Los 8 jugadores que admiten Switch y los emuladores, siempre a la vista.</summary>
    private const int Visibles = Controles.MaxJugadores;
    private static readonly TimeSpan TiempoDeteccion = TimeSpan.FromSeconds(6);

    private readonly List<JugadorVM> _jugadores;
    private readonly List<EstadoControlesVM> _estados;
    private readonly DispatcherTimer _refresco = new() { Interval = TimeSpan.FromSeconds(1.5) };
    private readonly DispatcherTimer _deteccion = new() { Interval = TimeSpan.FromMilliseconds(30) };
    private List<MandoConectado> _conectados = new();
    private List<(MandoConectado Mando, IntPtr Abierto, bool[] Antes)> _escuchando = new();
    private DateTime _finDeteccion;
    private DateTime _ignorarHasta;
    private int _indice;
    private bool _cambios;
    private bool _aplicando;

    public PantallaControles()
    {
        InitializeComponent();
        _jugadores = Enumerable.Range(1, Controles.MaxJugadores).Select(n => new JugadorVM(n)).ToList();
        _estados = Estado.Config.Emuladores.Where(e => e.Instalado).Select(e => new EstadoControlesVM(e)).ToList();
        ListaEstados.ItemsSource = _estados;

        // Lo último aplicado desde el lanzador o, si nunca se hizo, lo que tiene el emulador de origen (Eden).
        var iniciales = Estado.Config.Jugadores.Count > 0
            ? Estado.Config.Jugadores
            : Estado.Config.Emulador(Estado.Config.OrigenControles) is { Instalado: true } origen ? GeneradorControles.DesdeEmulador(origen) : new();
        foreach (var a in iniciales.Where(a => a.Jugador is >= 1 and <= Controles.MaxJugadores))
            _jugadores[a.Jugador - 1].Asignacion = Copia(a);

        SembrarConocidos();
        MostrarJugadores(Math.Max(Visibles, iniciales.Select(a => a.Jugador).DefaultIfEmpty(0).Max()));
        Seleccionar(0);

        _refresco.Tick += (_, _) => ActualizarConectados();
        _deteccion.Tick += (_, _) => RevisarDeteccion();
        Loaded += (_, _) =>
        {
            ActualizarConectados();
            _refresco.Start();
            _ = ActualizarEstadosAsync();
        };
        Unloaded += (_, _) =>
        {
            _refresco.Stop();
            TerminarDeteccion();
        };
    }

    public IReadOnlyList<Ayuda> Ayudas { get; } =
    [
        new("A", "Detectar mando", "Enter"),
        new("Y", "Tipo", "O"),
        new("X", "Aplicar a todos", "T"),
        new("☰", "Más", "M"),
        new("B", "Volver", "Esc"),
    ];

    private List<JugadorVM> JugadoresVisibles => (List<JugadorVM>)ListaJugadores.ItemsSource;
    private JugadorVM? Actual => _indice >= 0 && _indice < JugadoresVisibles.Count ? JugadoresVisibles[_indice] : null;
    private List<AsignacionJugador> Asignaciones => _jugadores.Where(j => j.Asignacion != null).Select(j => j.Asignacion!).ToList();

    private static AsignacionJugador Copia(AsignacionJugador a) => new()
    {
        Jugador = a.Jugador, Guid = a.Guid.ToLowerInvariant(), Puerto = a.Puerto, Tipo = a.Tipo, Nombre = a.Nombre, Mapeo = a.Mapeo,
    };

    private void MostrarJugadores(int cantidad)
    {
        ListaJugadores.ItemsSource = _jugadores.Take(Math.Clamp(cantidad, Visibles, Controles.MaxJugadores)).ToList();
        Seleccionar(Math.Min(_indice, JugadoresVisibles.Count - 1));
    }

    public void Ejecutar(Accion accion)
    {
        if (CapaDeteccion.Visibility == Visibility.Visible || DateTime.UtcNow < _ignorarHasta || _aplicando)
            return;
        switch (accion)
        {
            case Accion.Arriba: { var antes = _indice; Seleccionar(_indice - 1); Sonidos.Moverse(antes != _indice); break; }
            case Accion.Abajo: { var antes = _indice; Seleccionar(_indice + 1); Sonidos.Moverse(antes != _indice); break; }
            case Accion.Izquierda: Sonidos.Tocar(Sonido.Paso); CambiarMando(-1); break;
            case Accion.Derecha: Sonidos.Tocar(Sonido.Paso); CambiarMando(1); break;
            case Accion.Aceptar: EmpezarDeteccion(); break;
            case Accion.Opciones: ElegirTipo(); break;
            case Accion.Trucos: ConfirmarAplicar(); break;
            case Accion.Menu: MasOpciones(); break;
            case Accion.Atras: PedirSalir(MainWindow.Actual.Volver); break;
        }
    }

    private void Seleccionar(int indice)
    {
        var visibles = JugadoresVisibles;
        indice = Math.Clamp(indice, 0, visibles.Count - 1);
        foreach (var j in _jugadores)
            j.Seleccionado = false;
        _indice = indice;
        visibles[indice].Seleccionado = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (ListaJugadores.ItemContainerGenerator.ContainerFromIndex(_indice) is FrameworkElement fila)
                fila.BringIntoView();
        });
    }

    /// <summary>
    /// Suma al historial los mandos que tienen los jugadores del lanzador y los emuladores (conectados o no), para
    /// poder elegirlos con ← → aunque estén apagados.
    /// </summary>
    private void SembrarConocidos()
    {
        var cambio = false;
        foreach (var a in _jugadores.Select(j => j.Asignacion).OfType<AsignacionJugador>())
            cambio |= Recordar(a.Guid, a.Puerto, a.Nombre, a.Mapeo, null);
        foreach (var emulador in Estado.Config.Emuladores.Where(e => e.Instalado))
        {
            try
            {
                foreach (var j in Controles.Leer(emulador).Where(j => j.EsMando))
                    cambio |= Recordar(j.Guid!, j.Puerto, j.Dispositivo, null, null);
            }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.Text.Json.JsonException) { }
        }
        if (cambio)
            Estado.GuardarConfig();
    }

    /// <summary>Anota un mando en el historial; true si es nuevo.</summary>
    private static bool Recordar(string guid, int puerto, string nombre, string? mapeo, DateTimeOffset? visto)
    {
        if (string.IsNullOrWhiteSpace(guid))
            return false;
        var conocidos = Estado.Config.MandosConocidos;
        var previo = conocidos.FirstOrDefault(m => string.Equals(m.Guid, guid, StringComparison.OrdinalIgnoreCase) && m.Puerto == puerto);
        if (previo != null)
        {
            if (visto != null)
                previo.VistoUltimaVez = visto;
            if (mapeo != null)
                previo.Mapeo = mapeo;
            if (nombre.Length > 0 && visto != null)
                previo.Nombre = nombre;
            return false;
        }
        conocidos.Add(new MandoConocido
        {
            Guid = guid.ToLowerInvariant(),
            Puerto = puerto,
            Nombre = nombre.Length > 0 ? nombre : Controles.NombreMando(guid),
            Mapeo = mapeo,
            VistoUltimaVez = visto,
        });
        return true;
    }

    private void ActualizarConectados()
    {
        _conectados = Sdl3.Mandos();
        var nuevos = false;
        foreach (var m in _conectados)
            nuevos |= Recordar(m.Guid, m.Puerto, m.NombreLegible, m.Mapeo, DateTimeOffset.Now);
        if (nuevos)
            Estado.GuardarConfig();
        TextoConectados.Text = !Sdl3.Disponible ? $"No se pueden detectar mandos: {Sdl3.Error}"
            : _conectados.Count == 0 ? "No hay mandos conectados. Conéctalos y aparecerán aquí."
            : "Mandos conectados: " + string.Join("  ·  ", _conectados.Select(Descripcion));
        foreach (var j in _jugadores)
        {
            j.MandoConectado = j.Asignacion != null && _conectados.Any(m => m.Guid == j.Asignacion.Guid && m.Puerto == j.Asignacion.Puerto);
            // El mismo mando (mismo identificador y puerto) en otros jugadores.
            var otros = j.Asignacion == null ? []
                : _jugadores.Where(o => o != j && o.Asignacion != null && o.Asignacion.Guid == j.Asignacion.Guid && o.Asignacion.Puerto == j.Asignacion.Puerto)
                    .Select(o => $"J{o.Numero}").ToList();
            j.Compartido = otros.Count == 0 ? "" : "También en " + string.Join(", ", otros);
            j.Refrescar();
        }
    }

    private static string Descripcion(MandoConectado m) => m.NombreLegible + (m.Puerto > 0 ? $" ({m.Puerto + 1}º)" : "");

    private void Cambio(JugadorVM jugador)
    {
        _cambios = true;
        ActualizarConectados();
        jugador.Refrescar();
        _ = ActualizarEstadosAsync();
    }

    /// <summary>← →: recorre los mandos conectados (y el que tenía, si está desconectado) y "Ninguno".</summary>
    private void CambiarMando(int paso)
    {
        if (Actual is not { } jugador)
            return;
        var opciones = new List<AsignacionJugador?> { null };
        foreach (var m in _conectados)
            opciones.Add(new AsignacionJugador { Jugador = jugador.Numero, Guid = m.Guid, Puerto = m.Puerto, Nombre = m.NombreLegible, Mapeo = m.Mapeo, Tipo = GeneradorControles.TipoPorDefecto(m.Guid) });
        // Después, los mandos ya usados que ahora están apagados (los más recientes primero).
        foreach (var m in Estado.Config.MandosConocidos.OrderByDescending(m => m.VistoUltimaVez ?? DateTimeOffset.MinValue))
            if (!opciones.Any(o => o != null && string.Equals(o.Guid, m.Guid, StringComparison.OrdinalIgnoreCase) && o.Puerto == m.Puerto))
                opciones.Add(new AsignacionJugador { Jugador = jugador.Numero, Guid = m.Guid, Puerto = m.Puerto, Nombre = m.Nombre, Mapeo = m.Mapeo, Tipo = GeneradorControles.TipoPorDefecto(m.Guid) });
        if (jugador.Asignacion is { } actual && !opciones.Any(o => o != null && o.Guid == actual.Guid && o.Puerto == actual.Puerto))
            opciones.Add(actual);

        var indice = opciones.FindIndex(o => o == null ? jugador.Asignacion == null : jugador.Asignacion != null && o.Guid == jugador.Asignacion.Guid && o.Puerto == jugador.Asignacion.Puerto);
        var siguiente = opciones[(Math.Max(0, indice) + paso + opciones.Count) % opciones.Count];
        if (siguiente != null && jugador.Asignacion != null && siguiente.Guid == jugador.Asignacion.Guid)
            siguiente.Tipo = jugador.Asignacion.Tipo; // al volver al mismo mando se conserva el tipo elegido
        Asignar(jugador, siguiente);
    }

    /// <summary>
    /// Asigna un mando a un jugador. No se le quita a nadie: como en los emuladores, el mismo mando puede estar en
    /// varios jugadores (la fila lo indica con «También en J1»), y recorrer los mandos con ← → no desarma a los demás.
    /// </summary>
    private void Asignar(JugadorVM jugador, AsignacionJugador? asignacion)
    {
        if (asignacion != null)
            asignacion.Jugador = jugador.Numero;
        jugador.Asignacion = asignacion;
        Cambio(jugador);
    }

    private void ElegirTipo()
    {
        if (Actual is not { Asignacion: { } asignacion } jugador)
        {
            MainWindow.Actual.Aviso("Primero elige un mando para este jugador (← →).");
            return;
        }
        MainWindow.Actual.AbrirMenu(new MenuEmergente($"Tipo de mando del jugador {jugador.Numero}", jugador.TextoMando,
            GeneradorControles.Tipos.Select(t => new OpcionMenu(t, t switch
            {
                "Pro Controller" => "El recomendado para mandos Xbox, PlayStation y el Pro",
                "Portátil" => "Solo para el jugador 1 jugando en modo portátil (no en dock)",
                _ => null,
            }, () =>
            {
                asignacion.Tipo = t;
                Cambio(jugador);
            }, t == asignacion.Tipo))));
    }

    // ───────────── Detección: pulsar un botón en el mando ─────────────

    private void EmpezarDeteccion()
    {
        if (Actual is not { } jugador)
            return;
        if (!Sdl3.Disponible)
        {
            MainWindow.Actual.Aviso($"No se pueden detectar mandos: {Sdl3.Error}", true);
            return;
        }
        ActualizarConectados();
        if (_conectados.Count == 0)
        {
            MainWindow.Actual.Aviso("No hay mandos conectados.", true);
            return;
        }
        Sdl3.Actualizar();
        _escuchando = _conectados.Select(m =>
        {
            var abierto = Sdl3.Abrir(m.IdSdl);
            return (m, abierto, Enumerable.Range(0, 21).Select(b => Sdl3.Boton(abierto, b)).ToArray());
        }).ToList();
        _finDeteccion = DateTime.UtcNow + TiempoDeteccion;
        TextoDeteccion.Text = $"Pulsa un botón en el mando del jugador {jugador.Numero}";
        CapaDeteccion.Visibility = Visibility.Visible;
        _deteccion.Start();
        RevisarDeteccion();
    }

    private void RevisarDeteccion()
    {
        var restante = _finDeteccion - DateTime.UtcNow;
        if (restante <= TimeSpan.Zero)
        {
            TerminarDeteccion();
            MainWindow.Actual.Aviso("No se pulsó ningún botón; el jugador sigue igual.");
            return;
        }
        TextoCuenta.Text = $"{Math.Ceiling(restante.TotalSeconds)} s · se usará el mando donde lo pulses";
        Sdl3.Actualizar();
        foreach (var (mando, abierto, antes) in _escuchando)
            for (var b = 0; b < antes.Length; b++)
            {
                var ahora = Sdl3.Boton(abierto, b);
                if (ahora && !antes[b])
                {
                    var jugador = Actual!;
                    TerminarDeteccion();
                    var previo = jugador.Asignacion;
                    Asignar(jugador, new AsignacionJugador
                    {
                        Guid = mando.Guid, Puerto = mando.Puerto, Nombre = mando.NombreLegible, Mapeo = mando.Mapeo,
                        Tipo = previo != null && previo.Guid == mando.Guid ? previo.Tipo : GeneradorControles.TipoPorDefecto(mando.Guid),
                    });
                    MainWindow.Actual.Aviso($"Jugador {jugador.Numero}: {Descripcion(mando)}.");
                    return;
                }
                antes[b] = ahora;
            }
    }

    private void TerminarDeteccion()
    {
        _deteccion.Stop();
        foreach (var (_, abierto, _) in _escuchando)
            Sdl3.Cerrar(abierto);
        _escuchando.Clear();
        CapaDeteccion.Visibility = Visibility.Collapsed;
        // La misma pulsación también llega por la navegación normal: se ignora un momento.
        _ignorarHasta = DateTime.UtcNow.AddMilliseconds(600);
    }

    // ───────────── Aplicar ─────────────

    private async Task ActualizarEstadosAsync()
    {
        var asignaciones = Asignaciones;
        foreach (var e in _estados)
        {
            var emulador = e.Emulador;
            var (coincide, abierto) = await Task.Run(() => (GeneradorControles.Coincide(emulador, asignaciones), Actualizador.EmuladorAbierto(emulador) != null));
            e.Texto = $"{emulador.Nombre}: " + (coincide ? "igual ✓" : abierto ? "distinto · está abierto, ciérralo para aplicar" : "distinto");
            e.Nivel = coincide ? "ok" : abierto ? "error" : "aviso";
            e.Refrescar();
        }
    }

    private void ConfirmarAplicar()
    {
        var asignaciones = Asignaciones;
        var resumen = asignaciones.Count == 0 ? "Ningún jugador tiene mando" : string.Join(", ", asignaciones.Select(a => $"J{a.Jugador} {a.Nombre}"));
        MainWindow.Actual.AbrirMenu(new MenuEmergente("Aplicar los mandos a todos los emuladores", $"{resumen}.\nSe escriben en {string.Join(", ", _estados.Select(e => e.Emulador.Nombre))}, con respaldo de lo anterior. Tienen que estar cerrados.",
        [
            new OpcionMenu("Aplicar ahora", null, () => _ = AplicarAsync()),
            new OpcionMenu("Cancelar", null, null),
        ]));
    }

    private async Task<bool> AplicarAsync()
    {
        _aplicando = true;
        var asignaciones = Asignaciones.Select(Copia).ToList();
        Estado.Config.Jugadores = asignaciones;
        Estado.GuardarConfig();
        var todos = Estado.Config.Emuladores.ToList();
        var errores = new List<string>();
        foreach (var e in _estados)
        {
            var emulador = e.Emulador;
            e.Texto = $"{emulador.Nombre}: escribiendo…";
            e.Nivel = "neutro";
            e.Refrescar();
            var r = await Task.Run(() => GeneradorControles.Aplicar(emulador, asignaciones, todos));
            if (!r.Exito)
                errores.Add(r.Mensaje);
        }
        _aplicando = false;
        _cambios = false;
        await ActualizarEstadosAsync();
        MainWindow.Actual.Aviso(errores.Count == 0
            ? "Listo: todos los emuladores tienen estos mandos."
            : string.Join(" ", errores), errores.Count > 0);
        return errores.Count == 0;
    }

    // ───────────── Más opciones y salir ─────────────

    private void MasOpciones()
    {
        var opciones = new List<OpcionMenu>();
        foreach (var emulador in _estados.Select(e => e.Emulador))
            opciones.Add(new($"Empezar desde los mandos de {emulador.Nombre}", "Toma los jugadores que tiene configurados ese emulador", () =>
            {
                var desde = GeneradorControles.DesdeEmulador(emulador);
                foreach (var j in _jugadores)
                    j.Asignacion = desde.FirstOrDefault(a => a.Jugador == j.Numero) is { } a ? Copia(a) : null;
                MostrarJugadores(Math.Max(Visibles, desde.Select(a => a.Jugador).DefaultIfEmpty(0).Max()));
                _cambios = true;
                ActualizarConectados();
                _ = ActualizarEstadosAsync();
            }));
        foreach (var emulador in _estados.Select(e => e.Emulador))
            opciones.Add(new($"Abrir {emulador.Nombre} para ajustar botones uno por uno", "Después usa \"Empezar desde los mandos de…\" y aplica", async () =>
            {
                await MainWindow.Actual.AbrirEmuladorAsync(emulador, $"Ajustando los mandos en {emulador.Nombre}…");
                await ActualizarEstadosAsync();
            }));
        MainWindow.Actual.AbrirMenu(new MenuEmergente("Más opciones de controles", null, opciones));
    }

    public Seccion Seccion => Seccion.Controles;

    public void PedirSalir(Action salir)
    {
        if (!_cambios)
        {
            salir();
            return;
        }
        MainWindow.Actual.AbrirMenu(new MenuEmergente("Hay cambios sin aplicar", "Los mandos elegidos todavía no se escribieron en los emuladores.",
        [
            new OpcionMenu("Aplicar y salir", null, async () =>
            {
                if (await AplicarAsync())
                    salir();
            }),
            new OpcionMenu("Salir sin aplicar", null, salir),
            new OpcionMenu("Seguir aquí", null, null),
        ]));
    }

    private void AlPulsarJugador(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not JugadorVM jugador)
            return;
        var indice = JugadoresVisibles.IndexOf(jugador);
        if (indice == _indice)
            CambiarMando(1);
        else
            Seleccionar(indice);
    }
}
