using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Lanzador.Core.ClavesFirmware;

public sealed record ArchivoOrigen(string Ruta, string? Sha256);

/// <summary>
/// Claves o firmware encontrados en la carpeta elegida. <see cref="Ruta"/> es la carpeta de las claves, o el zip
/// o la carpeta con los .nca del firmware. Si el nombre no dice la versión, se usa la fecha del archivo.
/// </summary>
public sealed record CopiaOrigen(string Version, bool VersionConocida, string Ruta, IReadOnlyDictionary<string, ArchivoOrigen> Archivos)
{
    public bool EsZip => Ruta.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Lo que hay en la carpeta de claves y firmware que eligió el usuario (Configuración → Claves y firmware):
/// un prod.keys (con su title.keys al lado, si está) y un firmware, como zip de .nca o como carpeta de .nca.
/// El lanzador no descarga nada de esto: solo lee lo que el usuario ponga ahí.
/// </summary>
public sealed partial record OrigenClavesFirmware(string Carpeta, CopiaOrigen? Claves, CopiaOrigen? Firmware, DateTime Leido)
{
    private const int Profundidad = 4;
    private const int MinimoNca = 20;

    /// <summary>Recorre la carpeta (hasta 4 niveles) y se queda con las claves y el firmware más nuevos.</summary>
    public static OrigenClavesFirmware Leer(string carpeta)
    {
        carpeta = Path.TrimEndingDirectorySeparator(Path.GetFullPath(carpeta));
        if (!Directory.Exists(carpeta))
            throw new DirectoryNotFoundException($"No encuentro la carpeta {carpeta}.");

        var opciones = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = Profundidad, IgnoreInaccessible = true };
        var claves = new List<(CopiaOrigen Copia, DateTime Fecha)>();
        var firmware = new List<(CopiaOrigen Copia, DateTime Fecha)>();

        foreach (var prod in Directory.EnumerateFiles(carpeta, "prod.keys", opciones))
        {
            var archivos = new Dictionary<string, ArchivoOrigen>(StringComparer.OrdinalIgnoreCase) { ["prod.keys"] = new(prod, Sha256(prod)) };
            var titulo = Path.Combine(Path.GetDirectoryName(prod)!, "title.keys");
            if (File.Exists(titulo))
                archivos["title.keys"] = new(titulo, Sha256(titulo));
            var fecha = File.GetLastWriteTime(prod);
            claves.Add((Crear(carpeta, Path.GetDirectoryName(prod)!, null, fecha, archivos), fecha));
        }

        foreach (var zip in Directory.EnumerateFiles(carpeta, "*.zip", opciones))
            if (TieneNca(zip))
            {
                var fecha = File.GetLastWriteTime(zip);
                firmware.Add((Crear(carpeta, zip, Path.GetFileNameWithoutExtension(zip), fecha, Vacio), fecha));
            }

        foreach (var dir in Directory.EnumerateDirectories(carpeta, "*", opciones).Prepend(carpeta))
        {
            var nca = Directory.EnumerateFiles(dir, "*.nca").Take(MinimoNca).ToList();
            if (nca.Count < MinimoNca)
                continue;
            var fecha = nca.Max(File.GetLastWriteTime);
            firmware.Add((Crear(carpeta, dir, null, fecha, Vacio), fecha));
        }

        return new OrigenClavesFirmware(carpeta, MasNueva(claves), MasNueva(firmware), DateTime.Now);
    }

    private static readonly IReadOnlyDictionary<string, ArchivoOrigen> Vacio = new Dictionary<string, ArchivoOrigen>();

    private static CopiaOrigen Crear(string raiz, string ruta, string? nombre, DateTime fecha, IReadOnlyDictionary<string, ArchivoOrigen> archivos)
    {
        var version = VersionEnNombre(raiz, ruta, nombre);
        return new CopiaOrigen(version ?? fecha.ToString("yyyy.MM.dd"), version != null, ruta, archivos);
    }

    /// <summary>La versión (p. ej. 20.1.0) escrita en el nombre del archivo o de sus carpetas, sin salir de la elegida.</summary>
    private static string? VersionEnNombre(string raiz, string ruta, string? nombre)
    {
        if (nombre != null && RxVersion().Match(nombre) is { Success: true } m)
            return m.Groups[1].Value;
        for (var dir = nombre != null ? Path.GetDirectoryName(ruta) : ruta;
             dir != null && dir.Length > raiz.Length && dir.StartsWith(raiz, StringComparison.OrdinalIgnoreCase);
             dir = Path.GetDirectoryName(dir))
            if (RxVersion().Match(Path.GetFileName(dir)) is { Success: true } c)
                return c.Groups[1].Value;
        return null;
    }

    /// <summary>Primero las que dicen su versión (la mayor); si ninguna la dice, la de fecha más reciente.</summary>
    private static CopiaOrigen? MasNueva(List<(CopiaOrigen Copia, DateTime Fecha)> candidatas)
    {
        CopiaOrigen? mejor = null;
        var fechaMejor = DateTime.MinValue;
        foreach (var (copia, fecha) in candidatas)
        {
            var gana = mejor == null
                       || (copia.VersionConocida && !mejor.VersionConocida)
                       || (copia.VersionConocida == mejor.VersionConocida
                           && (copia.VersionConocida
                               ? InstaladorClavesFirmware.EsMasNueva(copia.Version, mejor.Version) || (copia.Version == mejor.Version && fecha > fechaMejor)
                               : fecha > fechaMejor));
            if (gana)
                (mejor, fechaMejor) = (copia, fecha);
        }
        return mejor;
    }

    private static bool TieneNca(string zip)
    {
        try
        {
            using var archivo = ZipFile.OpenRead(zip);
            return archivo.Entries.Any(InstaladorClavesFirmware.EsNca);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string? Sha256(string archivo)
    {
        try
        {
            using var flujo = new FileStream(archivo, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return Convert.ToHexStringLower(SHA256.HashData(flujo));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"(?<!\d)(\d{1,2}\.\d{1,2}\.\d{1,2})(?!\d)")]
    private static partial Regex RxVersion();
}
