using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lanzador.Core;

public static partial class EscanerBiblioteca
{
    private static readonly string[] Extensiones = [".nsp", ".xci", ".nsz", ".xcz"];
    private static readonly string[] CarpetasExtra = ["update", "updates", "dlc", "dlcs", "mod", "mods"];

    public static List<Juego> Escanear(IEnumerable<string> carpetas, IReadOnlyDictionary<string, string>? nombresConocidos = null)
    {
        var archivos = new List<string>();
        foreach (var carpeta in carpetas.Where(Directory.Exists))
        {
            archivos.AddRange(Directory
                .EnumerateFiles(carpeta, "*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true })
                .Where(f => Extensiones.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)));
        }

        var juegos = new Dictionary<string, Juego>(StringComparer.OrdinalIgnoreCase);
        var sinId = new List<Juego>();
        var actualizaciones = new List<(string BaseId, ArchivoActualizacion Archivo)>();

        foreach (var ruta in archivos.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(EnCarpetaExtra))
        {
            var nombreArchivo = Path.GetFileNameWithoutExtension(ruta);
            var (version, versionTexto) = LeerVersion(nombreArchivo);
            var m = RxTitleId().Match(nombreArchivo);
            if (!m.Success)
            {
                if (!EnCarpetaExtra(ruta))
                    sinId.Add(new Juego { RutaArchivo = ruta, Nombre = LimpiarNombre(nombreArchivo), VersionTexto = versionTexto });
                continue;
            }

            var id = Convert.ToUInt64(m.Groups[1].Value, 16);
            var tipo = id & 0xFFF;
            if (tipo == 0x800)
            {
                actualizaciones.Add(((id & ~0xFFFUL).ToString("X16"), new ArchivoActualizacion(ruta, version, versionTexto)));
                continue;
            }
            if (tipo != 0)
                continue; // DLC

            var tid = id.ToString("X16");
            if (juegos.ContainsKey(tid))
                continue;
            juegos[tid] = new Juego
            {
                TitleId = tid,
                RutaArchivo = ruta,
                VersionIncluida = version is > 0 ? version : null,
                VersionTexto = versionTexto,
                Nombre = nombresConocidos != null && nombresConocidos.TryGetValue(tid, out var conocido) && !string.IsNullOrWhiteSpace(conocido)
                    ? conocido
                    : LimpiarNombre(nombreArchivo),
            };
        }

        foreach (var (baseId, archivo) in actualizaciones)
            if (juegos.TryGetValue(baseId, out var juego))
                juego.Actualizaciones.Add(archivo);

        return juegos.Values
            .Concat(sinId)
            .OrderBy(j => j.Nombre, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>Nombres que Ryujinx ya guardó (en el idioma del sistema) en portable\games\&lt;id&gt;\gui\metadata.json.</summary>
    public static Dictionary<string, string> NombresDeRyujinx(string carpetaDatosRyujinx)
    {
        var nombres = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var carpeta = Path.Combine(carpetaDatosRyujinx, "games");
        if (!Directory.Exists(carpeta))
            return nombres;
        foreach (var dir in Directory.EnumerateDirectories(carpeta))
        {
            var meta = Path.Combine(dir, "gui", "metadata.json");
            if (!File.Exists(meta))
                continue;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(meta));
                if (doc.RootElement.TryGetProperty("title", out var t) && t.GetString() is { Length: > 0 } titulo)
                    nombres[Path.GetFileName(dir).ToUpperInvariant()] = titulo;
            }
            catch (JsonException) { }
            catch (IOException) { }
        }
        return nombres;
    }

    public static (int? Version, string? Texto) LeerVersion(string nombreArchivo)
    {
        var numero = RxVersionNumero().Match(nombreArchivo);
        if (numero.Success && int.TryParse(numero.Groups[1].Value, out var v))
            return (v, null);
        var texto = RxVersionTexto().Match(nombreArchivo);
        return (null, texto.Success ? texto.Groups[1].Value : null);
    }

    public static string LimpiarNombre(string nombreArchivo)
    {
        var limpio = RxEtiquetas().Replace(nombreArchivo, " ");
        limpio = limpio.Replace('_', ' ');
        limpio = RxEspacios().Replace(limpio, " ").Trim(' ', '+', '-');
        return limpio.Length > 0 ? limpio : nombreArchivo;
    }

    private static bool EnCarpetaExtra(string ruta)
    {
        var carpeta = Path.GetFileName(Path.GetDirectoryName(ruta)) ?? "";
        return CarpetasExtra.Contains(carpeta, StringComparer.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"\[([0-9A-Fa-f]{16})\]")]
    private static partial Regex RxTitleId();

    [GeneratedRegex(@"\[v(\d+)\]", RegexOptions.IgnoreCase)]
    private static partial Regex RxVersionNumero();

    [GeneratedRegex(@"\[v(\d+(?:\.\d+)+)\]", RegexOptions.IgnoreCase)]
    private static partial Regex RxVersionTexto();

    [GeneratedRegex(@"\[[^\]]*\]|\([^\)]*\)")]
    private static partial Regex RxEtiquetas();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex RxEspacios();
}
