using System.IO;
using System.Net.Http;
using Lanzador.App.Modelos;
using Lanzador.Core;

namespace Lanzador.App;

/// <summary>
/// La lista de juegos que comparten Inicio y Biblioteca: la busca en las carpetas, baja las carátulas y la
/// compatibilidad de Ryubing, y avisa a las pantallas cuando cambia.
/// </summary>
public static class Catalogo
{
    private static Task? _carga;

    /// <summary>Se dispara al cambiar la lista o sus datos (compatibilidad, preferencias).</summary>
    public static event Action? AlCambiar;

    public static bool Cargando => _carga is { IsCompleted: false };

    /// <summary>Qué decir cuando no hay juegos que mostrar ("Buscando juegos…", "No encontré juegos…"), o null.</summary>
    public static string? Mensaje { get; private set; } = "Buscando juegos…";

    public static void AvisarCambio() => AlCambiar?.Invoke();

    /// <summary>Carga la lista solo si todavía no se cargó.</summary>
    public static Task AsegurarAsync() => _carga ??= CargarInternoAsync(false);

    /// <summary>
    /// Al permitir las descargas: baja las carátulas y la compatibilidad de los juegos ya cargados (los mismos
    /// objetos que muestran las pantallas), cuando termine la carga en curso si la hay.
    /// </summary>
    public static async Task DescargarFaltantesAsync()
    {
        if (_carga != null)
            await Task.WhenAny(_carga);
        await Task.WhenAll(CargarIconosAsync(Estado.Juegos), CargarCompatibilidadAsync(false));
    }

    /// <summary>Vuelve a buscar los juegos (y, si se pide, a descargar la compatibilidad).</summary>
    public static Task CargarAsync(bool forzarDescargas)
    {
        if (Cargando)
            return _carga!;
        return _carga = CargarInternoAsync(forzarDescargas);
    }

    private static async Task CargarInternoAsync(bool forzarDescargas)
    {
        Mensaje = "Buscando juegos…";
        AvisarCambio();
        var config = Estado.Config;
        var ryujinx = config.Emuladores.FirstOrDefault(e => e.Tipo == TipoEmulador.Ryujinx && e.Instalado);
        var juegos = await Task.Run(() =>
        {
            var nombres = ryujinx != null ? EscanerBiblioteca.NombresDeRyujinx(ryujinx.CarpetaDatos) : null;
            return EscanerBiblioteca.Escanear(config.CarpetasJuegos, nombres);
        });

        // Se conservan las carátulas ya cargadas de los juegos que siguen.
        var anteriores = Estado.Juegos.ToDictionary(j => j.Juego.Clave);
        Estado.Juegos = juegos.Select(j =>
        {
            var vm = new JuegoVM(j);
            if (anteriores.TryGetValue(j.Clave, out var antes))
                vm.Icono = antes.Icono;
            AsignarCompatibilidad(vm);
            return vm;
        }).ToList();
        Mensaje = Estado.Juegos.Count == 0 ? "No encontré juegos. Revisa las carpetas en Configuración (botón ☰ o tecla M)." : null;
        AvisarCambio();
        _ = CargarIconosAsync(Estado.Juegos);
        await CargarCompatibilidadAsync(forzarDescargas);
    }

    private static async Task CargarCompatibilidadAsync(bool forzarDescargas)
    {
        try
        {
            Estado.Compatibilidad = await Compatibilidad.CargarRyubingAsync(forzarDescargas, CancellationToken.None);
        }
        catch (HttpRequestException ex)
        {
            MainWindow.Actual.Aviso($"No se pudo descargar la lista de compatibilidad: {ex.Message}", true);
        }
        // Citron Neo y yuzu (referencia para Eden y Suyu): si no se pueden bajar, simplemente no se muestran.
        try
        {
            Estado.CompatibilidadCitron = await Compatibilidad.CargarCitronAsync(forzarDescargas, CancellationToken.None);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or IOException) { }
        try
        {
            Estado.CompatibilidadYuzu = await Compatibilidad.CargarYuzuAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or IOException) { }
        foreach (var vm in Estado.Juegos)
            AsignarCompatibilidad(vm);
        AvisarCambio();
    }

    private static void AsignarCompatibilidad(JuegoVM vm)
    {
        var titleId = vm.Juego.TitleId;
        vm.Compat = Estado.Compatibilidad.GetValueOrDefault(titleId);
        vm.CompatCitron = Estado.CompatibilidadCitron.GetValueOrDefault(titleId);
        vm.CompatYuzu = Estado.CompatibilidadYuzu.GetValueOrDefault(titleId);
    }

    private static async Task CargarIconosAsync(List<JuegoVM> juegos)
    {
        await Task.WhenAll(juegos.Where(j => j.Juego.TieneTitleId && j.Icono == null).Select(async vm =>
        {
            var ruta = await Imagenes.IconoGrandeAsync(vm.Juego.TitleId, CancellationToken.None);
            if (ruta != null)
                vm.Icono = await Task.Run(() => Imagen.Cargar(ruta, 512));
        }));
    }

    /// <summary>Los juegos abiertos más recientemente, del último al primero.</summary>
    public static List<JuegoVM> Recientes(int maximo) => Estado.Juegos
        .Select(j => (Juego: j, Cuando: Estado.Config.Juegos.GetValueOrDefault(j.Juego.Clave)?.UltimaVez))
        .Where(x => x.Cuando != null)
        .OrderByDescending(x => x.Cuando)
        .Take(maximo)
        .Select(x => x.Juego)
        .ToList();

    public static List<JuegoVM> Favoritos() => Estado.Juegos.Where(j => Estado.Config.Juegos.GetValueOrDefault(j.Juego.Clave)?.Favorito == true).ToList();
}
