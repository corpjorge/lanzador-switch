using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lanzador.Core.Actualizaciones;

/// <summary>Última versión publicada de un emulador y el archivo que le corresponde a este equipo.</summary>
public sealed record VersionDisponible
{
    /// <summary>Hash del commit en las nightly (p. ej. <c>de7e6e01d4</c>) o etiqueta en las estables (<c>v0.2.1</c>).</summary>
    public string Version { get; init; } = "";
    public string Titulo { get; init; } = "";
    public string Canal { get; init; } = "";
    public string? Archivo { get; init; }
    public string? Url { get; init; }

    /// <summary>sha256 en hexadecimal minúsculo; sin él no se instala.</summary>
    public string? Sha256 { get; init; }

    public long? Tamano { get; init; }
    public DateTimeOffset? Fecha { get; init; }
    public string? PaginaNotas { get; init; }
}

public interface IFuenteActualizacion
{
    /// <summary>Texto corto para la pantalla, p. ej. "Eden Nightly · Windows amd64 clang PGO".</summary>
    string Descripcion { get; }

    /// <summary>False si el lanzador no puede descargarla y solo abre la página (Ryujinx).</summary>
    bool PermiteDescarga { get; }

    string PaginaDescargas { get; }

    Task<VersionDisponible> ConsultarAsync(CancellationToken ct);
}

public static class FuentesActualizacion
{
    /// <summary>La fuente que corresponde al emulador según la configuración, o null si no tiene actualizador.</summary>
    public static IFuenteActualizacion? Para(EmuladorConfig emulador, OpcionesActualizacion opciones) => emulador.Id.ToLowerInvariant() switch
    {
        "eden" => opciones.CanalEden == "estable" ? new FuenteEdenEstable() : new FuenteEdenNightly(),
        "citron" => new FuenteCitronNeo(opciones.VarianteCitron),
        "suyu" => new FuenteSuyu(),
        "ryujinx" => new FuenteRyujinx(),
        _ => null,
    };

    /// <summary>Lee un release con formato de GitHub (tag_name, name, assets[] con name, browser_download_url, size y digest).</summary>
    internal static (string Tag, string Nombre, string? Pagina, DateTimeOffset? Fecha, List<ArchivoPublicado> Archivos) LeerRelease(JsonElement release)
    {
        var archivos = new List<ArchivoPublicado>();
        if (release.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            foreach (var a in assets.EnumerateArray())
            {
                var nombre = Texto(a, "name");
                var url = Texto(a, "browser_download_url");
                if (nombre == null || url == null)
                    continue;
                var digest = Texto(a, "digest");
                long? tamano = a.TryGetProperty("size", out var s) && s.TryGetInt64(out var t) && t > 0 ? t : null;
                archivos.Add(new ArchivoPublicado(nombre, url, Sha256DeDigest(digest), tamano));
            }
        DateTimeOffset? fecha = DateTimeOffset.TryParse(Texto(release, "published_at") ?? Texto(release, "created_at"), out var f) ? f : null;
        return (Texto(release, "tag_name") ?? "", Texto(release, "name") ?? "", Texto(release, "html_url"), fecha, archivos);
    }

    /// <summary>"sha256:abc…" → "abc…"; null si no es un sha256 válido.</summary>
    public static string? Sha256DeDigest(string? digest)
    {
        if (digest == null)
            return null;
        var valor = digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? digest[7..] : digest;
        return Regex.IsMatch(valor, "^[0-9a-fA-F]{64}$") ? valor.ToLowerInvariant() : null;
    }

    internal static async Task<JsonDocument> JsonAsync(string url, CancellationToken ct, bool github = false)
    {
        using var peticion = new HttpRequestMessage(HttpMethod.Get, url);
        if (github)
        {
            peticion.Headers.Accept.ParseAdd("application/vnd.github+json");
            peticion.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        }
        using var respuesta = await Http.Cliente.SendAsync(peticion, ct);
        if (github && respuesta.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            throw new HttpRequestException("GitHub limitó las consultas por un rato. Vuelve a intentarlo más tarde.");
        if (respuesta.StatusCode == HttpStatusCode.NotFound)
            throw new HttpRequestException($"El servidor no tiene esa versión ({new Uri(url).Host} respondió 404).");
        if (!respuesta.IsSuccessStatusCode)
            throw new HttpRequestException($"{new Uri(url).Host} respondió {(int)respuesta.StatusCode}.", null, respuesta.StatusCode);
        await using var flujo = await respuesta.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(flujo, cancellationToken: ct);
    }

    private static string? Texto(JsonElement e, string propiedad) =>
        e.TryGetProperty(propiedad, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}

public sealed record ArchivoPublicado(string Nombre, string Url, string? Sha256, long? Tamano);

/// <summary>Eden Nightly: nightly.eden-emu.dev/latest/release.json, archivo Windows amd64 clang PGO.</summary>
public sealed class FuenteEdenNightly : IFuenteActualizacion
{
    public const string UrlUltima = "https://nightly.eden-emu.dev/latest/release.json";
    public const string SufijoPgo = "-amd64-clang-pgo.zip";

    public string Descripcion => "Eden Nightly · Windows amd64 clang PGO";
    public bool PermiteDescarga => true;
    public string PaginaDescargas => "https://nightly.eden-emu.dev/";

    public async Task<VersionDisponible> ConsultarAsync(CancellationToken ct)
    {
        using var doc = await FuentesActualizacion.JsonAsync(UrlUltima, ct);
        return Interpretar(doc.RootElement);
    }

    public static VersionDisponible Interpretar(JsonElement release)
    {
        var (tag, nombre, pagina, fecha, archivos) = FuentesActualizacion.LeerRelease(release);
        var archivo = archivos.FirstOrDefault(a => a.Nombre.StartsWith("Eden-Windows-", StringComparison.OrdinalIgnoreCase)
                                                   && a.Nombre.EndsWith(SufijoPgo, StringComparison.OrdinalIgnoreCase))
                      ?? throw new InvalidDataException($"La nightly {tag} todavía no tiene el archivo para Windows (amd64 clang PGO).");

        // tag "v1792353251.de7e6e01d4" → hash del commit; si no, el que trae el nombre del archivo.
        var hash = Regex.Match(tag, @"\.([0-9a-f]{7,40})$") is { Success: true } m
            ? m.Groups[1].Value
            : Regex.Match(archivo.Nombre, @"Eden-Windows-([0-9a-f]{7,40})-").Groups[1].Value;
        return new VersionDisponible
        {
            Version = hash.Length > 0 ? hash : tag,
            Titulo = nombre,
            Canal = "nightly",
            Archivo = archivo.Nombre,
            Url = archivo.Url,
            Sha256 = archivo.Sha256,
            Tamano = archivo.Tamano,
            Fecha = fecha,
            PaginaNotas = pagina,
        };
    }
}

/// <summary>
/// Eden estable: la versión sale de la API Gitea de git.eden-emu.dev y el sha256 del release.json
/// de stable.eden-emu.dev (la API Gitea no publica hashes).
/// </summary>
public sealed class FuenteEdenEstable : IFuenteActualizacion
{
    public const string UrlReleases = "https://git.eden-emu.dev/api/v1/repos/eden-emu/eden/releases?limit=10";
    public static string UrlReleaseJson(string tag) => $"https://stable.eden-emu.dev/{Uri.EscapeDataString(tag)}/release.json";

    public string Descripcion => "Eden estable · Windows amd64 clang PGO";
    public bool PermiteDescarga => true;
    public string PaginaDescargas => "https://git.eden-emu.dev/eden-emu/eden/releases";

    public async Task<VersionDisponible> ConsultarAsync(CancellationToken ct)
    {
        using var lista = await FuentesActualizacion.JsonAsync(UrlReleases, ct);
        var release = ElegirRelease(lista.RootElement);
        var (tag, _, _, _, _) = FuentesActualizacion.LeerRelease(release);
        JsonDocument? conHashes = null;
        try
        {
            conHashes = await FuentesActualizacion.JsonAsync(UrlReleaseJson(tag), ct);
        }
        catch (HttpRequestException) { }
        using (conHashes)
            return Interpretar(release, conHashes?.RootElement);
    }

    /// <summary>La más reciente que no sea borrador ni prerelease.</summary>
    public static JsonElement ElegirRelease(JsonElement lista)
    {
        if (lista.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("La API de git.eden-emu.dev respondió algo inesperado.");
        foreach (var r in lista.EnumerateArray())
        {
            if (r.TryGetProperty("draft", out var d) && d.ValueKind == JsonValueKind.True)
                continue;
            if (r.TryGetProperty("prerelease", out var p) && p.ValueKind == JsonValueKind.True)
                continue;
            return r.Clone();
        }
        throw new InvalidDataException("git.eden-emu.dev no tiene versiones estables publicadas.");
    }

    public static VersionDisponible Interpretar(JsonElement releaseGitea, JsonElement? releaseConHashes)
    {
        var (tag, nombre, pagina, fecha, archivos) = FuentesActualizacion.LeerRelease(releaseGitea);
        static bool EsPgo(ArchivoPublicado a) =>
            a.Nombre.StartsWith("Eden-Windows-", StringComparison.OrdinalIgnoreCase) && a.Nombre.EndsWith(FuenteEdenNightly.SufijoPgo, StringComparison.OrdinalIgnoreCase);

        var archivo = archivos.FirstOrDefault(EsPgo)
                      ?? throw new InvalidDataException($"La versión {tag} no tiene archivo para Windows (amd64 clang PGO).");
        if (releaseConHashes is { } extra)
        {
            var (tagExtra, _, _, _, conHash) = FuentesActualizacion.LeerRelease(extra);
            var igual = conHash.FirstOrDefault(a => a.Nombre.Equals(archivo.Nombre, StringComparison.OrdinalIgnoreCase));
            if (tagExtra == tag && igual != null)
                archivo = archivo with { Sha256 = igual.Sha256 ?? archivo.Sha256, Tamano = igual.Tamano ?? archivo.Tamano, Url = igual.Url };
        }
        return new VersionDisponible
        {
            Version = tag,
            Titulo = nombre,
            Canal = "estable",
            Archivo = archivo.Nombre,
            Url = archivo.Url,
            Sha256 = archivo.Sha256,
            Tamano = archivo.Tamano,
            Fecha = fecha,
            PaginaNotas = pagina,
        };
    }
}

/// <summary>Citron Neo: release "nightly-windows" de github.com/citron-neo/CI, variante clang-cl o clangtron.</summary>
public sealed class FuenteCitronNeo(string variante) : IFuenteActualizacion
{
    public const string UrlRelease = "https://api.github.com/repos/citron-neo/CI/releases/tags/nightly-windows";
    public static readonly string[] Variantes = ["clang-cl", "clangtron"];

    public string Variante { get; } = Variantes.Contains(variante) ? variante : Variantes[0];
    public string Descripcion => $"Citron Neo nightly · Windows x64 {Variante}";
    public bool PermiteDescarga => true;
    public string PaginaDescargas => "https://github.com/citron-neo/CI/releases/tag/nightly-windows";

    public async Task<VersionDisponible> ConsultarAsync(CancellationToken ct)
    {
        using var doc = await FuentesActualizacion.JsonAsync(UrlRelease, ct, github: true);
        return Interpretar(doc.RootElement, Variante);
    }

    public static VersionDisponible Interpretar(JsonElement release, string variante)
    {
        var (_, nombre, pagina, fecha, archivos) = FuentesActualizacion.LeerRelease(release);
        var sufijo = $"-x64-{variante}.zip";
        var archivo = archivos.FirstOrDefault(a => a.Nombre.StartsWith("Citron-windows-", StringComparison.OrdinalIgnoreCase)
                                                   && a.Nombre.EndsWith(sufijo, StringComparison.OrdinalIgnoreCase))
                      ?? throw new InvalidDataException($"La nightly de Citron Neo no tiene el archivo x64 {variante}.");
        // "Citron-windows-nightly-598b70088-x64-clang-cl.zip" o "Continuous Build (Windows Nightly: 598b70088)".
        var hash = Regex.Match(archivo.Nombre, @"-([0-9a-f]{7,40})-x64-", RegexOptions.IgnoreCase) is { Success: true } m
            ? m.Groups[1].Value
            : Regex.Match(nombre, @":\s*([0-9a-f]{7,40})\)").Groups[1].Value;
        return new VersionDisponible
        {
            Version = hash.Length > 0 ? hash.ToLowerInvariant() : (fecha?.ToString("yyyy-MM-dd") ?? "nightly"),
            Titulo = nombre,
            Canal = variante,
            Archivo = archivo.Nombre,
            Url = archivo.Url,
            Sha256 = archivo.Sha256,
            Tamano = archivo.Tamano,
            Fecha = fecha,
            PaginaNotas = pagina,
        };
    }
}

/// <summary>Suyu: último release de github.com/suyu-emu/suyu-main, archivo suyu-windows-x86_64.zip (trae suyu.exe en la raíz).</summary>
public sealed class FuenteSuyu : IFuenteActualizacion
{
    public const string UrlUltima = "https://api.github.com/repos/suyu-emu/suyu-main/releases/latest";
    public const string NombreArchivo = "suyu-windows-x86_64.zip";

    public string Descripcion => "Suyu (suyu-main) · Windows x86_64";
    public bool PermiteDescarga => true;
    public string PaginaDescargas => "https://github.com/suyu-emu/suyu-main/releases/latest";

    public async Task<VersionDisponible> ConsultarAsync(CancellationToken ct)
    {
        using var doc = await FuentesActualizacion.JsonAsync(UrlUltima, ct, github: true);
        return Interpretar(doc.RootElement);
    }

    public static VersionDisponible Interpretar(JsonElement release)
    {
        var (tag, nombre, pagina, fecha, archivos) = FuentesActualizacion.LeerRelease(release);
        var archivo = archivos.FirstOrDefault(a => a.Nombre.Equals(NombreArchivo, StringComparison.OrdinalIgnoreCase))
                      ?? throw new InvalidDataException($"La versión {tag} de Suyu no tiene {NombreArchivo}.");
        return new VersionDisponible
        {
            Version = tag,
            Titulo = nombre,
            Canal = "estable",
            Archivo = archivo.Nombre,
            Url = archivo.Url,
            Sha256 = archivo.Sha256,
            Tamano = archivo.Tamano,
            Fecha = fecha,
            PaginaNotas = pagina,
        };
    }
}

/// <summary>
/// Ryubing (Ryujinx): su servidor de actualizaciones (update.ryujinx.app) no respondía al crear el lanzador,
/// así que no se descarga desde aquí; solo se abre su página de descargas.
/// </summary>
public sealed class FuenteRyujinx : IFuenteActualizacion
{
    public string Descripcion => "Ryubing · página de descargas";
    public bool PermiteDescarga => false;
    public string PaginaDescargas => "https://ryujinx.app/download";

    public Task<VersionDisponible> ConsultarAsync(CancellationToken ct) =>
        throw new NotSupportedException("Ryujinx se actualiza desde su página de descargas.");
}
