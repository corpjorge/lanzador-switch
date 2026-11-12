using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lanzador.Core.Actualizaciones;

/// <summary>Una versión del lanzador publicada en GitHub que es más nueva que la que está abierta.</summary>
public sealed record VersionLanzador
{
    public Version Version { get; init; } = new(0, 0);

    /// <summary>La etiqueta del release, p. ej. <c>v1.2.0</c>.</summary>
    public string Etiqueta { get; init; } = "";
    public string Titulo { get; init; } = "";
    public string? Pagina { get; init; }

    /// <summary>El instalador (<c>LanzadorSwitch-…-instalador.exe</c>); null si el release no lo trae.</summary>
    public string? Instalador { get; init; }
    public string? Url { get; init; }
    public string? Sha256 { get; init; }
    public long? Tamano { get; init; }

    /// <summary>True si se puede descargar e instalar desde el lanzador (hay instalador y su sha256).</summary>
    public bool SePuedeInstalar => Url != null && Sha256 != null;

    public string Texto => Version.ToString(3);
}

/// <summary>
/// Actualización del propio lanzador: mira el último release de GitHub, descarga su instalador, verifica el sha256 y lo
/// ejecuta sobre la carpeta donde está el lanzador. La carpeta <c>datos</c> no se toca.
/// </summary>
public static partial class ActualizacionLanzador
{
    public const string Repositorio = "corpjorge/lanzador-switch";
    public const string UrlUltima = $"https://api.github.com/repos/{Repositorio}/releases/latest";
    public const string PaginaVersiones = $"https://github.com/{Repositorio}/releases/latest";

    /// <summary>Con el que termina el nombre del instalador en cada release.</summary>
    public const string SufijoInstalador = "-instalador.exe";
    public const string ArchivoSumas = "SHA256SUMS.txt";

    /// <summary>La versión que está abierta (o la de <c>LANZADOR_VERSION</c>, para probar el aviso).</summary>
    public static Version VersionActual { get; } = LeerVersionActual();

    private static Version LeerVersionActual()
    {
        if (LeerVersion(Environment.GetEnvironmentVariable("LANZADOR_VERSION")) is { } forzada)
            return forzada;
        var ensamblado = Assembly.GetEntryAssembly() ?? typeof(ActualizacionLanzador).Assembly;
        return LeerVersion(ensamblado.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion)
               ?? Tres(ensamblado.GetName().Version ?? new Version(0, 0, 0));
    }

    /// <summary>"v1.2.0", "1.2", "1.2.0+abc" → 1.2.0; null si no es una versión.</summary>
    public static Version? LeerVersion(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto) || RxVersion().Match(texto.Trim()) is not { Success: true } m)
            return null;
        return Version.TryParse(m.Groups[1].Value, out var v) ? Tres(v) : null;
    }

    private static Version Tres(Version v) => new(v.Major, Math.Max(0, v.Minor), Math.Max(0, v.Build));

    /// <summary>La versión publicada si es más nueva que la abierta; null si el lanzador está al día.</summary>
    public static async Task<VersionLanzador?> ConsultarAsync(CancellationToken ct, HttpClient? cliente = null)
    {
        using var doc = await FuentesActualizacion.JsonAsync(UrlUltima, ct, github: true);
        var version = Interpretar(doc.RootElement, null);
        if (version == null || version.Version <= VersionActual)
            return null;
        if (version is { Url: not null, Sha256: null } && UrlSumas(doc.RootElement) is { } urlSumas)
        {
            // Si GitHub no trae el sha256 del archivo, se toma de SHA256SUMS.txt del mismo release.
            try
            {
                var sumas = await (cliente ?? Http.Cliente).GetStringAsync(urlSumas, ct);
                version = Interpretar(doc.RootElement, sumas) ?? version;
            }
            catch (HttpRequestException) { }
        }
        return version;
    }

    /// <summary>Lee un release de GitHub. <paramref name="sumas"/> es el contenido de SHA256SUMS.txt, si se tiene.</summary>
    public static VersionLanzador? Interpretar(JsonElement release, string? sumas)
    {
        var (etiqueta, nombre, pagina, _, archivos) = FuentesActualizacion.LeerRelease(release);
        if (LeerVersion(etiqueta) is not { } version)
            return null;
        var instalador = archivos.FirstOrDefault(a => a.Nombre.StartsWith("LanzadorSwitch-", StringComparison.OrdinalIgnoreCase)
                                                      && a.Nombre.EndsWith(SufijoInstalador, StringComparison.OrdinalIgnoreCase));
        // Solo se descarga de los releases de este repositorio en GitHub.
        if (instalador != null && !instalador.Url.StartsWith($"https://github.com/{Repositorio}/releases/download/", StringComparison.OrdinalIgnoreCase))
            instalador = null;
        return new VersionLanzador
        {
            Version = version,
            Etiqueta = etiqueta,
            Titulo = nombre.Length > 0 ? nombre : etiqueta,
            Pagina = pagina ?? PaginaVersiones,
            Instalador = instalador?.Nombre,
            Url = instalador?.Url,
            Sha256 = instalador == null ? null : instalador.Sha256 ?? Sha256DeSumas(sumas, instalador.Nombre),
            Tamano = instalador?.Tamano,
        };
    }

    /// <summary>Busca en un SHA256SUMS.txt ("&lt;hash&gt;  &lt;archivo&gt;" por línea) el hash de un archivo.</summary>
    public static string? Sha256DeSumas(string? sumas, string archivo)
    {
        if (sumas == null)
            return null;
        foreach (var linea in sumas.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var partes = linea.Split([' ', '\t'], 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (partes.Length == 2 && partes[1].TrimStart('*').Equals(archivo, StringComparison.OrdinalIgnoreCase))
                return FuentesActualizacion.Sha256DeDigest(partes[0]);
        }
        return null;
    }

    private static string? UrlSumas(JsonElement release) =>
        FuentesActualizacion.LeerRelease(release).Archivos.FirstOrDefault(a => a.Nombre.Equals(ArchivoSumas, StringComparison.OrdinalIgnoreCase))?.Url;

    /// <summary>Descarga el instalador a la carpeta temporal y verifica su sha256. Devuelve su ruta.</summary>
    public static async Task<string> DescargarAsync(VersionLanzador version, IProgress<ProgresoActualizacion>? progreso, CancellationToken ct, HttpClient? cliente = null)
    {
        if (version.Url == null || version.Sha256 == null || version.Instalador == null)
            throw new InvalidOperationException("Esta versión no tiene un instalador que se pueda verificar.");
        var actualizador = new Actualizador(cliente);
        var carpeta = Path.Combine(actualizador.CarpetaTemporal, "lanzador-" + version.Texto);
        Directory.CreateDirectory(carpeta);
        // El nombre sale de GitHub: solo se usa su parte final, para que nunca escriba fuera de la carpeta temporal.
        var destino = Path.Combine(carpeta, Path.GetFileName(version.Instalador));
        var hash = await actualizador.DescargarArchivoAsync(version.Url, destino, version.Tamano, progreso, ct);
        if (!hash.Equals(version.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(destino);
            throw new InvalidDataException($"el instalador descargado no coincide con el sha256 publicado (esperado {version.Sha256[..12]}…, llegó {hash[..12]}…)");
        }
        return destino;
    }

    /// <summary>
    /// Ejecuta el instalador sobre <paramref name="carpetaLanzador"/>, sin preguntas y pidiéndole que vuelva a abrir el
    /// lanzador al terminar. Quien llama debe cerrar el lanzador enseguida para que se puedan reemplazar sus archivos.
    /// </summary>
    public static void Instalar(string instalador, string carpetaLanzador)
    {
        var carpeta = Path.TrimEndingDirectorySeparator(Path.GetFullPath(carpetaLanzador));
        using var proceso = Process.Start(new ProcessStartInfo(instalador)
        {
            Arguments = $"/SILENT /SUPPRESSMSGBOXES /NORESTART /RELANZAR=1 /DIR=\"{carpeta}\"",
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(instalador)!,
        });
    }

    [GeneratedRegex(@"^[vV]?(\d+(?:\.\d+){1,3})")]
    private static partial Regex RxVersion();
}
