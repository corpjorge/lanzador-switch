using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using Lanzador.App.Vistas;
using Lanzador.Core;
using Lanzador.Core.Actualizaciones;

namespace Lanzador.App;

/// <summary>Aviso de versiones nuevas (del lanzador y de los emuladores) al iniciar, y actualización del propio lanzador.</summary>
public partial class MainWindow
{
    /// <summary>Un emulador instalado que tiene una versión más nueva publicada.</summary>
    private sealed record EmuladorPorActualizar(EmuladorConfig Emulador, string Instalada, VersionDisponible Disponible);

    private bool _buscandoActualizaciones;
    private bool _actualizandoLanzador;

    /// <summary>
    /// Busca versiones nuevas del lanzador y de los emuladores instalados y, si hay, pregunta qué actualizar.
    /// Al iniciar no dice nada si todo está al día o no hay internet; desde el menú (<paramref name="aPedido"/>) sí.
    /// </summary>
    public async Task BuscarActualizacionesAsync(bool aPedido)
    {
        if (_buscandoActualizaciones || _actualizandoLanzador)
            return;
        _buscandoActualizaciones = true;
        try
        {
            if (aPedido)
                FijarEstado("Buscando actualizaciones…");
            var opciones = Estado.Config.Actualizaciones;
            var tareaLanzador = ConsultarLanzadorAsync();
            var tareas = Estado.Config.Emuladores.Where(e => e.Instalado).Select(e => ConsultarEmuladorAsync(e, opciones)).ToList();
            var (lanzador, errorLanzador) = await tareaLanzador;
            var consultas = await Task.WhenAll(tareas);
            var emuladores = consultas.Where(c => c.Pendiente != null).Select(c => c.Pendiente!).ToList();
            var errores = consultas.Where(c => c.Error != null).Select(c => c.Error!).ToList();
            if (errorLanzador != null)
                errores.Insert(0, errorLanzador);
            if (aPedido)
                FijarEstado("");

            if (lanzador == null && emuladores.Count == 0)
            {
                if (aPedido)
                    Aviso(errores.Count == 0 ? $"Todo está al día (Lanzador Switch {ActualizacionLanzador.VersionActual.ToString(3)})."
                        : "No se pudo consultar: " + string.Join(" ", errores), errores.Count > 0);
                return;
            }

            // El aviso espera a que termine la intro y no interrumpe una partida ni el aviso de privacidad.
            while (_introVisible)
                await Task.Delay(200);
            if (_procesoJuego != null || !Estado.Config.AvisoPrivacidadVisto)
                return;
            MostrarActualizaciones(lanzador, emuladores, aPedido);
        }
        finally
        {
            _buscandoActualizaciones = false;
        }
    }

    private static async Task<(VersionLanzador? Version, string? Error)> ConsultarLanzadorAsync()
    {
        try
        {
            return (await ActualizacionLanzador.ConsultarAsync(CancellationToken.None), null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidDataException or IOException)
        {
            return (null, $"Lanzador: {(ex is TaskCanceledException ? "GitHub tardó demasiado en responder" : Actualizador.Explicar(ex))}.");
        }
    }

    private static async Task<(EmuladorPorActualizar? Pendiente, string? Error)> ConsultarEmuladorAsync(EmuladorConfig emulador, OpcionesActualizacion opciones)
    {
        if (FuentesActualizacion.Para(emulador, opciones) is not { PermiteDescarga: true } fuente)
            return (null, null);
        try
        {
            var instalada = await Task.Run(() => VersionesInstaladas.Leer(emulador));
            // Sin saber qué versión hay instalada no se puede afirmar que la publicada sea más nueva.
            if (instalada.Version == null)
                return (null, null);
            var disponible = await fuente.ConsultarAsync(CancellationToken.None);
            return disponible is { Url: not null, Verificable: true } && !VersionesInstaladas.MismaVersion(instalada.Version, disponible.Version)
                ? (new EmuladorPorActualizar(emulador, instalada.Version, disponible), null)
                : (null, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidDataException or IOException)
        {
            return (null, $"{emulador.Nombre}: {(ex is TaskCanceledException ? "el servidor tardó demasiado en responder" : Actualizador.Explicar(ex))}.");
        }
    }

    private void MostrarActualizaciones(VersionLanzador? lanzador, List<EmuladorPorActualizar> emuladores, bool aPedido)
    {
        var lineas = new List<string>();
        if (lanzador != null)
            lineas.Add($"Lanzador Switch: {ActualizacionLanzador.VersionActual.ToString(3)} → {lanzador.Texto}");
        lineas.AddRange(emuladores.Select(e => $"{e.Emulador.Nombre}: {e.Instalada} → {e.Disponible.Version}"));

        var opciones = new List<OpcionMenu>();
        if (lanzador != null)
            opciones.Add(lanzador.SePuedeInstalar
                ? new OpcionMenu($"Actualizar el lanzador a {lanzador.Texto}", "Se descarga el instalador, se verifica y el lanzador se vuelve a abrir. Tus datos no se tocan.",
                    () => _ = ActualizarLanzadorAsync(lanzador))
                : new OpcionMenu($"Ver el lanzador {lanzador.Texto}", "Esta versión no trae instalador verificable: se abre su página de descarga",
                    () => Menus.Abrir(lanzador.Pagina ?? ActualizacionLanzador.PaginaVersiones, null)));
        if (emuladores.Count > 1)
            opciones.Add(new OpcionMenu($"Actualizar los {emuladores.Count} emuladores", string.Join(", ", emuladores.Select(e => e.Emulador.Nombre)) + " · con respaldo para volver atrás",
                () => ActualizarEmuladores(emuladores.Select(e => e.Emulador.Id))));
        foreach (var e in emuladores)
            opciones.Add(new OpcionMenu($"Actualizar {e.Emulador.Nombre}", $"{e.Instalada} → {e.Disponible.Version} · con respaldo para volver atrás",
                () => ActualizarEmuladores([e.Emulador.Id]), icono: Tema.IconosEmulador.De(e.Emulador)));
        if (lanzador is { SePuedeInstalar: true, Pagina: { } pagina })
            opciones.Add(new OpcionMenu("Ver los cambios del lanzador", pagina, () => Menus.Abrir(pagina, null)));
        opciones.Add(new OpcionMenu("Ahora no", null, null));
        if (!aPedido)
            opciones.Add(new OpcionMenu("No avisar al abrir el lanzador", "Se vuelve a activar en Configuración → Actualizaciones", () =>
            {
                Estado.Config.Actualizaciones.BuscarAlIniciar = false;
                Estado.GuardarConfig();
            }));

        AbrirMenu(new MenuEmergente(lineas.Count == 1 ? "Hay una actualización" : "Hay actualizaciones", string.Join("\n", lineas), opciones));
    }

    /// <summary>
    /// Da de alta carpetas de actualizaciones y DLC: las suma a la configuración y a los emuladores que las cargan
    /// solos (Eden, Citron, Ryujinx). Devuelve una frase con lo que pasó, para mostrarla.
    /// </summary>
    public async Task<string> AsegurarContenidoAsync(IEnumerable<string> carpetas)
    {
        var nuevas = carpetas.Select(ContenidoExterno.Normalizar).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var agregadas = nuevas.Where(c => !Estado.Config.CarpetasContenido.Any(x => ContenidoExterno.Normalizar(x).Equals(c, StringComparison.OrdinalIgnoreCase))).ToList();
        if (agregadas.Count > 0)
        {
            Estado.Config.CarpetasContenido.AddRange(agregadas);
            Estado.GuardarConfig();
        }
        return await SincronizarContenidoAsync();
    }

    /// <summary>Pone en cada emulador instalado las carpetas de actualizaciones y DLC de la configuración que le falten.</summary>
    public async Task<string> SincronizarContenidoAsync()
    {
        var carpetas = Estado.Config.CarpetasContenido.ToList();
        if (carpetas.Count == 0)
            return "";
        var cargan = new List<string>();
        var manual = new List<string>();
        var errores = new List<string>();
        foreach (var emulador in Estado.Config.Emuladores.Where(e => e.Instalado).ToList())
        {
            var (admite, resultado) = await Task.Run(() => (ContenidoExterno.Admite(emulador), ContenidoExterno.Agregar(emulador, carpetas)));
            if (!resultado.Exito)
                errores.Add(resultado.Mensaje);
            else if (admite)
                cargan.Add(emulador.Nombre);
            else
                manual.Add(emulador.Nombre);
        }
        var partes = new List<string>();
        if (cargan.Count > 0)
            partes.Add($"{Lista(cargan)} {(cargan.Count == 1 ? "carga" : "cargan")} solos las actualizaciones y DLC de esa carpeta.");
        if (manual.Count > 0)
            partes.Add($"En {Lista(manual)} hay que instalarlos desde el propio emulador.");
        partes.AddRange(errores);
        if (errores.Count > 0)
            Aviso(string.Join(" ", errores), true);
        return string.Join(" ", partes);
    }

    /// <summary>"Eden", "Eden y Ryujinx", "Eden, Citron Neo y Ryujinx".</summary>
    private static string Lista(IReadOnlyList<string> nombres) =>
        nombres.Count <= 1 ? string.Concat(nombres) : string.Join(", ", nombres.Take(nombres.Count - 1)) + " y " + nombres[^1];

    /// <summary>Va a la sección Emuladores y actualiza esos emuladores, uno tras otro.</summary>
    private void ActualizarEmuladores(IEnumerable<string> ids)
    {
        PantallaEmuladores.Pendientes.Clear();
        PantallaEmuladores.Pendientes.AddRange(ids);
        if (_pila.Count == 1 && _pila[0] is PantallaEmuladores abierta)
            _ = abierta.ActualizarPendientesAsync();
        else
            IrASeccion(Seccion.Emuladores);
    }

    /// <summary>Descarga el instalador de la versión nueva, lo verifica, lo ejecuta sobre esta carpeta y cierra el lanzador.</summary>
    private async Task ActualizarLanzadorAsync(VersionLanzador version)
    {
        if (_actualizandoLanzador)
            return;
        if (_procesoJuego != null || Estado.CopiaEnCurso != null)
        {
            Aviso("Termina lo que está en curso antes de actualizar el lanzador.", true);
            return;
        }
        _actualizandoLanzador = true;
        try
        {
            Aviso($"Descargando el lanzador {version.Texto}… Se volverá a abrir solo al terminar.");
            var progreso = new Progress<ProgresoActualizacion>(p => FijarEstado($"Lanzador {version.Texto}: {p.Texto}"));
            var instalador = await ActualizacionLanzador.DescargarAsync(version, progreso, CancellationToken.None);
            if (_procesoJuego != null || Estado.CopiaEnCurso != null)
            {
                FijarEstado("");
                Aviso("El instalador quedó descargado, pero hay algo en curso. Vuelve a intentarlo desde ☰ → Buscar actualizaciones.", true);
                return;
            }
            FijarEstado($"Instalando el lanzador {version.Texto}…");
            ActualizacionLanzador.Instalar(instalador, AppContext.BaseDirectory);
            Application.Current.Shutdown();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or InvalidDataException or UnauthorizedAccessException
                                       or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            FijarEstado("");
            Aviso($"No se pudo actualizar el lanzador: {Actualizador.Explicar(ex)}. Puedes descargarlo desde su página.", true);
        }
        finally
        {
            _actualizandoLanzador = false;
        }
    }
}
