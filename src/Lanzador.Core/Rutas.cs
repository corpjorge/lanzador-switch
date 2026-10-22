using System.Net;

namespace Lanzador.Core;

public static class Rutas
{
    public static string Datos { get; private set; } = Path.Combine(AppContext.BaseDirectory, "datos");

    public static void Configurar(string carpetaDatos) => Datos = carpetaDatos;

    public static string ArchivoConfig => Path.Combine(Carpeta(Datos), "config.json");
    public static string ArchivoTraducciones => Path.Combine(Cache, "traducciones.json");
    public static string Cache => Carpeta(Path.Combine(Datos, "cache"));
    public static string Iconos => Carpeta(Path.Combine(Cache, "iconos"));
    public static string Fondos => Carpeta(Path.Combine(Cache, "fondos"));
    public static string Trucos => Carpeta(Path.Combine(Cache, "trucos"));
    public static string Respaldos => Carpeta(Path.Combine(Datos, "respaldos"));

    private static string Carpeta(string ruta)
    {
        Directory.CreateDirectory(ruta);
        return ruta;
    }
}

public static class Http
{
    public static readonly HttpClient Cliente = Crear();

    /// <summary>
    /// Si es false (Configuración → Internet, o mientras no se responde el aviso de la primera vez), no se descargan
    /// carátulas, fondos, trucos ni listas de compatibilidad: solo se usa lo que ya está en caché. Las actualizaciones
    /// de emuladores no dependen de esto.
    /// </summary>
    public static bool ContenidoEnLinea { get; set; } = true;

    private static HttpClient Crear()
    {
        var cliente = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        })
        {
            Timeout = TimeSpan.FromSeconds(40),
        };
        cliente.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) LanzadorSwitch/1.0");
        return cliente;
    }

    /// <summary>
    /// Descarga un texto y lo guarda en caché. Devuelve "" si el servidor responde 404.
    /// Sin internet, usa la copia guardada aunque esté vencida; con las descargas desactivadas, también.
    /// </summary>
    public static async Task<string> TextoConCacheAsync(string url, string archivoCache, TimeSpan vigencia, bool forzar, CancellationToken ct)
    {
        if (!forzar && File.Exists(archivoCache) && DateTime.UtcNow - File.GetLastWriteTimeUtc(archivoCache) < vigencia)
            return await File.ReadAllTextAsync(archivoCache, ct);
        if (!ContenidoEnLinea)
            return File.Exists(archivoCache) ? await File.ReadAllTextAsync(archivoCache, ct) : "";

        try
        {
            using var respuesta = await Cliente.GetAsync(url, ct);
            if (respuesta.StatusCode == HttpStatusCode.NotFound)
            {
                await File.WriteAllTextAsync(archivoCache, "", ct);
                return "";
            }
            respuesta.EnsureSuccessStatusCode();
            var texto = await respuesta.Content.ReadAsStringAsync(ct);
            var temporal = archivoCache + ".tmp";
            await File.WriteAllTextAsync(temporal, texto, ct);
            File.Move(temporal, archivoCache, true);
            return texto;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested && File.Exists(archivoCache))
        {
            return await File.ReadAllTextAsync(archivoCache, ct);
        }
    }
}
