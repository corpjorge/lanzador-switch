using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lanzador.Core.Actualizaciones;

/// <summary>Versión que tiene instalada un emulador y de dónde se sacó.</summary>
public sealed record VersionLocal(string? Version, string Origen, string? Canal = null)
{
    public static readonly VersionLocal Desconocida = new(null, "No se pudo leer; se sabrá tras la primera actualización desde aquí");
}

/// <summary>Lo que el lanzador instaló por última vez en cada emulador (app\datos\versiones.json).</summary>
public sealed class RegistroVersion
{
    public string Version { get; set; } = "";
    public string Canal { get; set; } = "";
    public string? Archivo { get; set; }
    public DateTime Fecha { get; set; }

    /// <summary>Tamaño y fecha del ejecutable tras instalar: si cambian, alguien lo actualizó por fuera y el registro ya no vale.</summary>
    public long TamanoExe { get; set; }
    public DateTime FechaExeUtc { get; set; }
}

public static class VersionesInstaladas
{
    public const string OrigenLanzador = "Instalada desde el lanzador";
    private const string OrigenLog = "Leída del registro de Eden (la última vez que se abrió)";

    private static string Archivo => Path.Combine(Rutas.Datos, "versiones.json");

    public static VersionLocal Leer(EmuladorConfig emulador)
    {
        if (!File.Exists(emulador.RutaExe))
            return new VersionLocal(null, "No instalado");

        if (Registro().TryGetValue(emulador.Id, out var r) && CoincideExe(r, emulador.RutaExe))
            return new VersionLocal(r.Version, OrigenLanzador, r.Canal);

        try
        {
            if (emulador.Tipo == TipoEmulador.Ryujinx)
                return DeRyujinx(emulador.RutaExe);
            if (emulador.Id.Equals("eden", StringComparison.OrdinalIgnoreCase))
                return DeEden(emulador);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return VersionLocal.Desconocida;
    }

    public static void Registrar(EmuladorConfig emulador, string version, string canal, string? archivo)
    {
        var todos = Registro();
        var info = new FileInfo(emulador.RutaExe);
        todos[emulador.Id] = new RegistroVersion
        {
            Version = version,
            Canal = canal,
            Archivo = archivo,
            Fecha = DateTime.Now,
            TamanoExe = info.Exists ? info.Length : 0,
            FechaExeUtc = info.Exists ? info.LastWriteTimeUtc : default,
        };
        Directory.CreateDirectory(Rutas.Datos);
        var temporal = Archivo + ".tmp";
        File.WriteAllText(temporal, JsonSerializer.Serialize(todos, GestorConfiguracion.OpcionesJson));
        File.Move(temporal, Archivo, true);
    }

    /// <summary>Quita lo registrado (p. ej. al volver a una versión que no se sabe cuál era).</summary>
    public static void Olvidar(string emuladorId)
    {
        var todos = Registro();
        if (!todos.Remove(emuladorId))
            return;
        File.WriteAllText(Archivo + ".tmp", JsonSerializer.Serialize(todos, GestorConfiguracion.OpcionesJson));
        File.Move(Archivo + ".tmp", Archivo, true);
    }

    public static Dictionary<string, RegistroVersion> Registro()
    {
        try
        {
            if (File.Exists(Archivo))
                return new Dictionary<string, RegistroVersion>(
                    JsonSerializer.Deserialize<Dictionary<string, RegistroVersion>>(File.ReadAllText(Archivo), GestorConfiguracion.OpcionesJson) ?? new(),
                    StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException) { }
        catch (IOException) { }
        return new Dictionary<string, RegistroVersion>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True si las dos versiones son la misma: iguales sin la "v" inicial, o dos hashes de commit
    /// donde uno empieza por el otro (Eden usa 10 caracteres y Citron 9).
    /// </summary>
    public static bool MismaVersion(string? instalada, string? disponible)
    {
        if (string.IsNullOrWhiteSpace(instalada) || string.IsNullOrWhiteSpace(disponible))
            return false;
        var a = Normalizar(instalada);
        var b = Normalizar(disponible);
        if (a == b)
            return true;
        return EsHash(a) && EsHash(b) && Math.Min(a.Length, b.Length) >= 7 && (a.StartsWith(b) || b.StartsWith(a));
    }

    private static string Normalizar(string v) => v.Trim().ToLowerInvariant().TrimStart('v');
    private static bool EsHash(string v) => Regex.IsMatch(v, "^[0-9a-f]{7,40}$");

    private static bool CoincideExe(RegistroVersion r, string rutaExe)
    {
        var info = new FileInfo(rutaExe);
        return info.Exists && info.Length == r.TamanoExe && Math.Abs((info.LastWriteTimeUtc - r.FechaExeUtc).TotalSeconds) < 2;
    }

    /// <summary>Ryujinx.exe trae la versión en sus propiedades: "1.3.351+475615f" → "1.3.351".</summary>
    private static VersionLocal DeRyujinx(string rutaExe)
    {
        var info = FileVersionInfo.GetVersionInfo(rutaExe);
        var version = (info.ProductVersion ?? info.FileVersion)?.Split('+')[0].Trim();
        return string.IsNullOrEmpty(version) ? VersionLocal.Desconocida : new VersionLocal(version, "Leída de Ryujinx.exe");
    }

    /// <summary>
    /// Eden: la cadena "Eden Nightly &lt;hash&gt;-master" dentro de eden.exe y, si no está,
    /// la línea "Eden Version: …" de user\log\eden_log.txt (es la de la última vez que se abrió).
    /// </summary>
    private static VersionLocal DeEden(EmuladorConfig emulador)
    {
        if (BuscarEnArchivo(emulador.RutaExe, "Eden Nightly ") is { } nightly
            && Regex.Match(nightly, "^([0-9a-f]{7,40})") is { Success: true } hash)
            return new VersionLocal(hash.Groups[1].Value, "Leída de eden.exe", "nightly");

        var log = Path.Combine(emulador.CarpetaDatos, "log", "eden_log.txt");
        if (File.Exists(log))
        {
            using var lector = new StreamReader(new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
            for (var i = 0; i < 40 && lector.ReadLine() is { } linea; i++)
            {
                var indice = linea.IndexOf("Eden Version:", StringComparison.Ordinal);
                if (indice < 0)
                    continue;
                var texto = linea[(indice + 13)..];
                if (Regex.Match(texto, @"master-([0-9a-f]{7,40})") is { Success: true } m)
                    return new VersionLocal(m.Groups[1].Value, OrigenLog, "nightly");
                if (Regex.Match(texto, @"\bv?(\d+\.\d+\.\d+(?:-rc\d+)?)\b") is { Success: true } e)
                    return new VersionLocal("v" + e.Groups[1].Value, OrigenLog, "estable");
            }
        }
        return VersionLocal.Desconocida;
    }

    /// <summary>Busca un texto ASCII dentro de un archivo binario y devuelve lo que le sigue (letras, números, '.', '-').</summary>
    internal static string? BuscarEnArchivo(string ruta, string prefijo)
    {
        var patron = Encoding.ASCII.GetBytes(prefijo);
        const int bloque = 1 << 20;
        var buffer = new byte[bloque + 128];
        using var flujo = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16);
        var arrastre = 0;
        while (true)
        {
            var leidos = flujo.ReadAtLeast(buffer.AsSpan(arrastre, bloque), bloque, throwOnEndOfStream: false);
            var total = arrastre + leidos;
            var finArchivo = leidos < bloque;
            var indice = buffer.AsSpan(0, total).IndexOf(patron);
            if (indice >= 0)
            {
                var inicio = indice + patron.Length;
                var fin = inicio;
                while (fin < total && fin - inicio < 64 && (char.IsAsciiLetterOrDigit((char)buffer[fin]) || buffer[fin] is (byte)'.' or (byte)'-'))
                    fin++;
                if (fin < total || finArchivo)
                    return Encoding.ASCII.GetString(buffer, inicio, fin - inicio);
                // El texto quedó cortado al final del bloque: se vuelve a leer desde donde empieza.
                arrastre = total - indice;
                Array.Copy(buffer, indice, buffer, 0, arrastre);
                continue;
            }
            if (finArchivo)
                return null;
            // Conserva el final del bloque por si el prefijo quedó partido entre dos lecturas.
            arrastre = Math.Min(patron.Length - 1, total);
            Array.Copy(buffer, total - arrastre, buffer, 0, arrastre);
        }
    }
}
