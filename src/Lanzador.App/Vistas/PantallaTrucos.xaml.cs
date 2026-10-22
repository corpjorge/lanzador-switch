using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Lanzador.App.Entrada;
using Lanzador.App.Modelos;
using Lanzador.Core;
using Lanzador.Core.IA;
using Lanzador.Core.Trucos;
using Lanzador.App.Audio;

namespace Lanzador.App.Vistas;

public partial class PantallaTrucos : UserControl, IPantalla
{
    private readonly JuegoVM _juego;
    private readonly List<EmuladorConfig> _emuladores;
    private readonly List<OpcionMenu> _pestanas;
    private readonly ObservableCollection<TrucoVM> _filas = new();
    private CatalogoTrucos? _catalogo;
    private VersionDetectada? _detectada;
    private List<int> _versiones = new();
    private int _version;
    private string? _buildId;
    private int _emulador;
    private int _seleccion; // 0 = fila de versión; 1.. = trucos (sin contar encabezados)
    private CancellationTokenSource? _traduccion;
    private bool _cargando;

    public PantallaTrucos(JuegoVM juego, string? emuladorId = null)
    {
        InitializeComponent();
        _juego = juego;
        TextoNombre.Text = juego.Nombre;
        ListaTrucos.ItemsSource = _filas;

        _emuladores = Estado.EmuladoresDisponibles.ToList();
        var preferido = emuladorId ?? Estado.Config.PreferenciasDe(juego.Juego.Clave).EmuladorPreferido;
        _emulador = Math.Max(0, _emuladores.FindIndex(e => e.Id == preferido));
        _pestanas = _emuladores.Select(e => new OpcionMenu(e.Nombre, null, null)).ToList();
        ListaEmuladores.ItemsSource = _pestanas;
        _pestanas[_emulador].Seleccionada = true;

        Loaded += async (_, _) =>
        {
            if (_catalogo == null && !_cargando)
                await CargarAsync(false);
        };
        Unloaded += (_, _) => _traduccion?.Cancel();
    }

    public IReadOnlyList<Ayuda> Ayudas
    {
        get
        {
            var lista = new List<Ayuda> { new("A", "Activar / desactivar", "Enter") };
            if (TextoTodos() is { } todos)
                lista.Add(new("Vista", todos, "V"));
            lista.Add(new("X", "Actualizar trucos", "T"));
            lista.Add(new("Y", "Traducir de nuevo", "O"));
            lista.Add(new("LB RB", "Emulador", "Q / E"));
            lista.Add(new("☰", "Aplicar", "M"));
            lista.Add(new("B", "Volver", "Esc"));
            return lista;
        }
    }

    // ───── Apagar todos / encender los de antes ─────

    private string? ClaveGuardados => _buildId == null ? null : $"{Emulador.Id}|{_buildId}";

    private List<TrucoVM> Instalados => Trucos.Where(t => t.Instalado != null).ToList();

    /// <summary>Los trucos guardados al apagar todos que siguen instalados.</summary>
    private List<string> Guardados()
    {
        if (ClaveGuardados is not { } clave
            || Estado.Config.Juegos.GetValueOrDefault(_juego.Juego.Clave)?.TrucosGuardados.GetValueOrDefault(clave) is not { } guardados)
            return [];
        var instalados = Instalados.Select(t => t.Instalado!.Clave).ToHashSet(StringComparer.Ordinal);
        return guardados.Where(instalados.Contains).ToList();
    }

    /// <summary>Qué haría Vista ahora: "Apagar todos", "Encender los 5 de antes" o nada.</summary>
    private string? TextoTodos()
    {
        if (_cargando || _buildId == null)
            return null;
        if (Instalados.Any(t => t.ActivoDeseado))
            return "Apagar todos";
        var guardados = Guardados().Count;
        return guardados > 0 ? $"Encender los {guardados} de antes" : null;
    }

    /// <summary>
    /// Vista: apaga todos los trucos activos de un golpe guardando cuáles eran, o vuelve a encender exactamente esos
    /// (ni uno más). Se aplica en el mismo paso.
    /// </summary>
    private void AlternarTodos()
    {
        if (ClaveGuardados is not { } clave)
            return;
        var instalados = Instalados;
        var activos = instalados.Where(t => t.ActivoDeseado).ToList();
        if (activos.Count > 0)
        {
            var preferencias = Estado.Config.PreferenciasDe(_juego.Juego.Clave);
            preferencias.TrucosGuardados[clave] = activos.Select(t => t.Instalado!.Clave).ToList();
            Estado.GuardarConfig();
            foreach (var t in instalados)
                t.ActivoDeseado = false;
            foreach (var t in Trucos.Where(t => t.Disponible != null && t.ActivoDeseado))
                t.ActivoDeseado = false; // apagar todos tampoco instala los que estaban marcados
            Sonidos.Tocar(Sonido.Apagar);
        }
        else if (Guardados() is { Count: > 0 } guardados)
        {
            var encender = guardados.ToHashSet(StringComparer.Ordinal);
            foreach (var t in instalados)
                t.ActivoDeseado = encender.Contains(t.Instalado!.Clave);
            Sonidos.Tocar(Sonido.Encender);
        }
        else
        {
            MainWindow.Actual.Aviso("No hay trucos activos ni una lista guardada para volver a encender.");
            return;
        }
        ActualizarEstadoPendientes();
        Aplicar();
    }

    private EmuladorConfig Emulador => _emuladores[_emulador];
    private string TitleId => _juego.Juego.TitleId;
    private IEnumerable<TrucoVM> Trucos => _filas.Where(f => !f.EsEncabezado);
    private int CambiosPendientes => Trucos.Count(f => f.Pendiente);

    private async Task CargarAsync(bool forzar)
    {
        _cargando = true;
        TextoVacio.Text = "Buscando trucos en switch-cheats-db y tinfoil.io…";
        TextoVacio.Visibility = Visibility.Visible;
        _filas.Clear();
        try
        {
            _catalogo = await FuentesTrucos.CargarAsync(TitleId, forzar, CancellationToken.None);
            foreach (var aviso in _catalogo.Avisos)
                MainWindow.Actual.Aviso(aviso, true);
            PrepararEmulador();
        }
        finally
        {
            _cargando = false;
            ActualizarEstadoPendientes();
        }
    }

    private void PrepararEmulador()
    {
        if (_catalogo == null)
            return;
        _detectada = DetectorVersion.Detectar(_juego.Juego, Emulador, _catalogo);
        _versiones = _catalogo.Versiones.Keys.Append(_detectada.Version).Distinct().Order().ToList();
        _version = _detectada.Version;
        ConstruirFilas();
        ActualizarEstadoPendientes();
    }

    private void ConstruirFilas()
    {
        _traduccion?.Cancel();
        _filas.Clear();
        if (_catalogo == null)
            return;

        _buildId = _catalogo.BuildDeVersion(_version);
        ActualizarFilaVersion();

        if (_buildId == null)
        {
            TextoVacio.Text = _catalogo.Versiones.Count == 0
                ? Http.ContenidoEnLinea
                    ? "No encontré información de este juego en switch-cheats-db ni en tinfoil.io."
                    : "Las descargas están desactivadas (☰ → Configuración → Internet), así que no puedo buscar los trucos de este juego."
                : $"No conozco el Build ID de la {VersionDetectada.Texto(_version)}. Cambia la versión con ← →.";
            TextoVacio.Visibility = Visibility.Visible;
            Seleccionar(0);
            return;
        }

        List<TrucoInstalado> instalados;
        try
        {
            instalados = GestoresTrucos.Para(Emulador).Leer(TitleId, _buildId);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MainWindow.Actual.Aviso($"No se pudieron leer los trucos de {Emulador.Nombre}: {ex.Message}", true);
            instalados = new();
        }
        var codigosInstalados = instalados.Select(i => i.Codigo).ToHashSet();
        var disponibles = _catalogo.Para(_buildId).Where(d => !codigosInstalados.Contains(d.Codigo)).ToList();

        if (instalados.Count > 0)
        {
            _filas.Add(new TrucoVM { EsEncabezado = true, Titulo = $"Instalados en {Emulador.Nombre} ({instalados.Count})" });
            foreach (var t in instalados)
                _filas.Add(new TrucoVM { Instalado = t, Nombre = t.Nombre, ActivoDeseado = t.Activo, Traducido = true });
        }
        if (disponibles.Count > 0)
        {
            _filas.Add(new TrucoVM { EsEncabezado = true, Titulo = $"Disponibles para instalar ({disponibles.Count})" });
            foreach (var d in disponibles.OrderBy(d => !d.EsMaestro))
            {
                var fila = new TrucoVM { Disponible = d, Nombre = TraductorIA.LimpiarNombre(d.NombreOriginal) };
                if (Estado.Traducciones.Buscar(TitleId, d.NombreOriginal) is { } t)
                    AplicarTraduccion(fila, t);
                _filas.Add(fila);
            }
        }

        if (_filas.Count == 0)
        {
            TextoVacio.Text = $"No hay trucos para la {VersionDetectada.Texto(_version)} (Build ID {_buildId}) en ninguna de las dos fuentes.";
            TextoVacio.Visibility = Visibility.Visible;
        }
        else
        {
            TextoVacio.Visibility = Visibility.Collapsed;
        }

        _seleccion = 0;
        Seleccionar(Trucos.Any() ? 1 : 0);
        _ = TraducirAsync(false);
    }

    private void ActualizarFilaVersion()
    {
        TextoVersion.Text = VersionDetectada.Texto(_version);
        var detectada = _detectada != null && _detectada.Version == _version;
        var origen = detectada
            ? $"Detectada: {_detectada!.Motivo}"
            : $"Elegida a mano (detectada: {VersionDetectada.Texto(_detectada?.Version ?? 0)})";
        TextoDetalleVersion.Text = origen + (_buildId != null ? $" · Build ID {_buildId}" : " · Build ID desconocido");
        TextoDetalleVersion.Foreground = (Brush)FindResource(detectada && _detectada!.Aproximada ? "Ambar" : "Texto");

        // Qué versiones tienen trucos, agrupando las que comparten Build ID ("v1–v3").
        var grupos = new List<string>();
        if (_catalogo != null)
            foreach (var grupo in _catalogo.Versiones.GroupBy(v => v.Value))
            {
                var cantidad = _catalogo.Para(grupo.Key).Count;
                if (cantidad == 0)
                    continue;
                var desde = VersionDetectada.Texto(grupo.Min(v => v.Key));
                var hasta = VersionDetectada.Texto(grupo.Max(v => v.Key));
                grupos.Add($"{(desde == hasta ? desde : $"{desde}–{hasta}")} ({cantidad})");
            }
        TextoVersionesConTrucos.Text = grupos.Count > 0 ? "Hay trucos para: " + string.Join(", ", grupos) : "Ninguna versión tiene trucos en las fuentes";
    }

    private static void AplicarTraduccion(TrucoVM fila, Traduccion t)
    {
        fila.Nombre = t.Nombre;
        fila.Descripcion = t.Descripcion;
        fila.Categoria = t.Categoria;
        fila.Traducido = true;
    }

    private async Task TraducirAsync(bool forzar)
    {
        var motor = Estado.Config.MotorIA;
        if (motor is not ("claude" or "codex"))
            return;

        var filas = Trucos.Where(f => f.Disponible != null && (forzar || !f.Traducido)).ToList();
        if (filas.Count == 0)
            return;
        if (!forzar && Estado.ErrorIA != null)
        {
            MainWindow.Actual.FijarEstado("Trucos sin traducir · Y para reintentar");
            return;
        }
        var nombres = filas.Select(f => f.NombreOriginal).Distinct().ToList();
        if (forzar)
            Estado.Traducciones.Olvidar(TitleId, nombres);

        _traduccion?.Cancel();
        var cancelacion = _traduccion = new CancellationTokenSource();
        var elementos = nombres.Select((n, i) => new ElementoATraducir($"t{i}", n, filas.First(f => f.NombreOriginal == n).Botones)).ToList();
        var progreso = new Progress<string>(texto => MainWindow.Actual.FijarEstado(texto));
        try
        {
            var resultado = await TraductorIA.TraducirAsync(motor, Estado.Config.ModeloIA, _catalogo?.Titulo ?? _juego.Nombre, elementos, progreso, cancelacion.Token);
            var guardar = new List<(string, Traduccion)>();
            foreach (var e in elementos)
            {
                if (!resultado.TryGetValue(e.Id, out var t))
                    continue;
                guardar.Add((e.Nombre, t));
                foreach (var f in filas.Where(f => f.NombreOriginal == e.Nombre))
                    AplicarTraduccion(f, t);
            }
            Estado.Traducciones.Guardar(TitleId, guardar);
            Estado.ErrorIA = null;
            MainWindow.Actual.FijarEstado(guardar.Count < elementos.Count ? $"Se tradujeron {guardar.Count} de {elementos.Count} nombres." : "");
        }
        catch (OperationCanceledException)
        {
            MainWindow.Actual.FijarEstado("");
        }
        catch (Exception ex)
        {
            Estado.ErrorIA = ex.Message;
            MainWindow.Actual.FijarEstado("Trucos sin traducir · Y para reintentar");
            MainWindow.Actual.Aviso($"No se pudieron traducir los trucos: {ex.Message}", true);
        }
    }

    public void Ejecutar(Accion accion)
    {
        if (_cargando)
        {
            if (accion == Accion.Atras)
                Salir();
            return;
        }
        switch (accion)
        {
            case Accion.Arriba: { var antes = _seleccion; Seleccionar(_seleccion - 1); Sonidos.Moverse(antes != _seleccion); break; }
            case Accion.Abajo: { var antes = _seleccion; Seleccionar(_seleccion + 1); Sonidos.Moverse(antes != _seleccion); break; }
            case Accion.Izquierda: if (_seleccion == 0) { var antes = _version; CambiarVersion(-1); Sonidos.Moverse(antes != _version); } break;
            case Accion.Derecha: if (_seleccion == 0) { var antes = _version; CambiarVersion(1); Sonidos.Moverse(antes != _version); } break;
            case Accion.Aceptar:
                if (_seleccion > 0 && Trucos.ElementAtOrDefault(_seleccion - 1) is { } truco)
                {
                    truco.ActivoDeseado = !truco.ActivoDeseado;
                    Sonidos.Tocar(truco.ActivoDeseado ? Sonido.Encender : Sonido.Apagar);
                }
                ActualizarEstadoPendientes();
                break;
            case Accion.Menu: Aplicar(); break;
            case Accion.Vista: AlternarTodos(); break;
            case Accion.AnteriorPestana: Sonidos.Tocar(Sonido.Paso); CambiarEmulador(-1); break;
            case Accion.SiguientePestana: Sonidos.Tocar(Sonido.Paso); CambiarEmulador(1); break;
            case Accion.Trucos:
                if (SinCambiosPendientes("actualizar los trucos"))
                    _ = CargarAsync(true);
                break;
            case Accion.Opciones:
                if (Estado.Config.MotorIA is "claude" or "codex")
                    _ = TraducirAsync(true);
                else
                    MainWindow.Actual.Aviso("No hay IA configurada para traducir. Elígela en Configuración.", true);
                break;
            case Accion.Atras: Salir(); break;
        }
    }

    private void Seleccionar(int seleccion)
    {
        var trucos = Trucos.ToList();
        seleccion = Math.Clamp(seleccion, 0, trucos.Count);
        foreach (var t in trucos)
            t.Seleccionado = false;
        _seleccion = seleccion;
        FilaVersion.BorderBrush = seleccion == 0 ? (Brush)FindResource("focus") : Brushes.Transparent;
        FilaVersion.Background = (Brush)FindResource(seleccion == 0 ? "surface-200" : "surface-100");
        FilaVersion.Effect = seleccion == 0 ? (System.Windows.Media.Effects.Effect)FindResource("shadow-focus") : null;
        if (seleccion == 0)
        {
            Desplazador.ScrollToTop();
            return;
        }
        var actual = trucos[seleccion - 1];
        actual.Seleccionado = true;
        var indice = _filas.IndexOf(actual);
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (ListaTrucos.ItemContainerGenerator.ContainerFromIndex(indice) is FrameworkElement contenedor)
                contenedor.BringIntoView(new Rect(0, -60, contenedor.ActualWidth, contenedor.ActualHeight + 120));
        });
    }

    private void CambiarVersion(int paso)
    {
        if (_versiones.Count == 0 || !SinCambiosPendientes("cambiar de versión"))
            return;
        var i = Math.Clamp(_versiones.IndexOf(_version) + paso, 0, _versiones.Count - 1);
        if (_versiones[i] == _version)
            return;
        _version = _versiones[i];
        ConstruirFilas();
        ActualizarEstadoPendientes();
        Seleccionar(0);
    }

    private void CambiarEmulador(int paso)
    {
        if (_emuladores.Count < 2 || !SinCambiosPendientes("cambiar de emulador"))
            return;
        _pestanas[_emulador].Seleccionada = false;
        _emulador = (_emulador + paso + _emuladores.Count) % _emuladores.Count;
        _pestanas[_emulador].Seleccionada = true;
        PrepararEmulador();
    }

    private bool SinCambiosPendientes(string accion)
    {
        if (CambiosPendientes == 0)
            return true;
        MainWindow.Actual.Aviso($"Tienes cambios sin aplicar. Aplícalos (☰) o descártalos (B) antes de {accion}.", true);
        return false;
    }

    private void ActualizarEstadoPendientes()
    {
        var n = CambiosPendientes;
        MainWindow.Actual.FijarEstado(n == 0 ? "" : $"{n} cambio{(n == 1 ? "" : "s")} sin aplicar · ☰ para aplicar");
        if (ReferenceEquals(MainWindow.Actual.Contenido.Content, this))
            MainWindow.Actual.ActualizarAyudas(); // «Apagar todos» / «Encender los de antes»
    }

    private bool Aplicar()
    {
        if (_buildId == null)
            return false;
        var cambios = new CambiosTrucos();
        foreach (var f in Trucos)
        {
            if (f.Instalado != null && f.Pendiente)
                cambios.Estados[f.Instalado.Clave] = f.ActivoDeseado;
            else if (f.Disponible != null && f.ActivoDeseado)
                cambios.Instalar.Add(new TrucoNuevo { Nombre = f.NombreParaInstalar(), Lineas = f.Lineas, Activo = true });
        }
        if (cambios.Vacio)
        {
            MainWindow.Actual.Aviso("No hay cambios que aplicar.");
            return true;
        }

        // Si hay un código maestro para esta versión y no está instalado, se instala junto con los trucos nuevos.
        var maestroAgregado = false;
        if (cambios.Instalar.Count > 0)
            foreach (var m in Trucos.Where(f => f.Disponible != null && f.EsMaestro && !f.ActivoDeseado))
            {
                cambios.Instalar.Insert(0, new TrucoNuevo { Nombre = m.NombreParaInstalar(), Lineas = m.Lineas, Activo = true });
                maestroAgregado = true;
            }

        var gestor = GestoresTrucos.Para(Emulador);
        if (gestor.Bloqueo() is { } motivo)
        {
            MainWindow.Actual.Aviso(motivo, true);
            return false;
        }
        try
        {
            gestor.Aplicar(TitleId, _buildId, cambios);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            MainWindow.Actual.Aviso($"No se pudieron aplicar los cambios: {ex.Message}", true);
            return false;
        }

        var activados = cambios.Estados.Count(e => e.Value);
        var desactivados = cambios.Estados.Count(e => !e.Value);
        var partes = new List<string>();
        if (cambios.Instalar.Count > 0) partes.Add($"{cambios.Instalar.Count} instalado{(cambios.Instalar.Count == 1 ? "" : "s")}");
        if (activados > 0) partes.Add($"{activados} activado{(activados == 1 ? "" : "s")}");
        if (desactivados > 0) partes.Add($"{desactivados} desactivado{(desactivados == 1 ? "" : "s")}");
        var nota = Emulador.Tipo == TipoEmulador.Ryujinx ? " Se aplican la próxima vez que abras el juego." : "";
        MainWindow.Actual.Aviso($"Listo en {Emulador.Nombre}: {string.Join(", ", partes)}.{(maestroAgregado ? " También se instaló el código maestro." : "")}{nota}");
        MainWindow.Actual.FijarEstado("");
        ConstruirFilas();
        ActualizarEstadoPendientes();
        return true;
    }

    private void Salir()
    {
        if (CambiosPendientes == 0)
        {
            _traduccion?.Cancel();
            MainWindow.Actual.FijarEstado("");
            MainWindow.Actual.Volver();
            return;
        }
        MainWindow.Actual.AbrirMenu(new MenuEmergente("Tienes cambios sin aplicar", $"{CambiosPendientes} cambio(s) en {Emulador.Nombre}",
        [
            new OpcionMenu("Aplicar y volver", null, () =>
            {
                if (Aplicar())
                    MainWindow.Actual.Volver();
            }),
            new OpcionMenu("Descartar cambios", null, () =>
            {
                _traduccion?.Cancel();
                MainWindow.Actual.FijarEstado("");
                MainWindow.Actual.Volver();
            }),
            new OpcionMenu("Seguir editando", null, null),
        ]));
    }

    private void AlPulsarTruco(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not TrucoVM truco)
            return;
        Seleccionar(Trucos.ToList().IndexOf(truco) + 1);
        truco.ActivoDeseado = !truco.ActivoDeseado;
        Sonidos.Tocar(truco.ActivoDeseado ? Sonido.Encender : Sonido.Apagar);
        ActualizarEstadoPendientes();
    }

    private void AlPulsarPestana(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is OpcionMenu pestana)
            CambiarEmulador(_pestanas.IndexOf(pestana) - _emulador);
    }
}
