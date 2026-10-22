using System.Diagnostics;
using System.Text;

namespace Lanzador.Core.Trucos;

public sealed class TrucoInstalado
{
    /// <summary>Nombre para mostrar.</summary>
    public string Nombre { get; init; } = "";

    /// <summary>Identificador para activarlo o desactivarlo: carpeta en Eden, "&lt;Nombre Cheat&gt;" en Ryujinx.</summary>
    public string Clave { get; init; } = "";

    public List<string> Lineas { get; init; } = new();
    public bool Activo { get; set; }
    public string Archivo { get; init; } = "";
    public string Codigo => string.Join("\n", Lineas);
    public string? Botones => LectorBotones.Describir(Lineas);
}

public sealed class TrucoNuevo
{
    public string Nombre { get; init; } = "";
    public IReadOnlyList<string> Lineas { get; init; } = [];
    public bool Activo { get; init; } = true;
}

public sealed class CambiosTrucos
{
    public List<TrucoNuevo> Instalar { get; } = new();

    /// <summary>Clave del truco instalado → activo.</summary>
    public Dictionary<string, bool> Estados { get; } = new();

    public bool Vacio => Instalar.Count == 0 && Estados.Count == 0;
}

public interface IGestorTrucos
{
    EmuladorConfig Emulador { get; }

    /// <summary>Motivo por el que no se pueden aplicar cambios ahora, o null si se puede.</summary>
    string? Bloqueo();

    List<TrucoInstalado> Leer(string titleId, string buildId);
    void Aplicar(string titleId, string buildId, CambiosTrucos cambios);
}

public static class GestoresTrucos
{
    public static IGestorTrucos Para(EmuladorConfig emulador) =>
        emulador.Tipo == TipoEmulador.Yuzu ? new GestorTrucosYuzu(emulador) : new GestorTrucosRyujinx(emulador);

    internal static bool EstaAbierto(EmuladorConfig emulador)
    {
        var procesos = Process.GetProcessesByName(emulador.NombreProceso);
        try
        {
            return procesos.Length > 0;
        }
        finally
        {
            foreach (var p in procesos)
                p.Dispose();
        }
    }

    /// <summary>Nombre válido como carpeta y como entrada del qt-config.ini (sin comas, comillas ni corchetes).</summary>
    internal static string NombreCarpeta(string nombre)
    {
        var invalidos = Path.GetInvalidFileNameChars().Concat([',', ';', '=', '"', '[', ']', '{', '}', '#', '\\']).ToHashSet();
        var sb = new StringBuilder();
        foreach (var c in nombre)
            sb.Append(invalidos.Contains(c) ? ' ' : c);
        var limpio = string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim(' ', '.');
        if (limpio.Length > 100)
            limpio = limpio[..100].Trim(' ', '.');
        return limpio.Length == 0 ? "Truco" : limpio;
    }
}

/// <summary>Eden, Citron y demás derivados de yuzu: user\load\&lt;ID&gt;\&lt;carpeta&gt;\cheats\&lt;BuildID&gt;.txt y [DisabledAddOns] en qt-config.ini.</summary>
public sealed class GestorTrucosYuzu(EmuladorConfig emulador) : IGestorTrucos
{
    public EmuladorConfig Emulador { get; } = emulador;

    private string RutaQtConfig => Path.Combine(Emulador.CarpetaDatos, "config", "qt-config.ini");
    private string CarpetaJuego(string titleId) => Path.Combine(Emulador.CarpetaDatos, "load", titleId.ToUpperInvariant());

    public string? Bloqueo() =>
        GestoresTrucos.EstaAbierto(Emulador)
            ? $"Cierra {Emulador.Nombre} antes de aplicar los cambios: guarda su configuración al cerrarse y los desharía."
            : null;

    public List<TrucoInstalado> Leer(string titleId, string buildId)
    {
        var resultado = new List<TrucoInstalado>();
        var carpeta = CarpetaJuego(titleId);
        if (!Directory.Exists(carpeta))
            return resultado;

        var deshabilitados = QtIni.Leer(RutaQtConfig).Deshabilitados(titleId);
        foreach (var dir in Directory.EnumerateDirectories(carpeta).OrderBy(d => d, StringComparer.CurrentCultureIgnoreCase))
        {
            var archivo = ArchivoTrucos(dir, buildId);
            if (archivo == null)
                continue;
            var secciones = ParserAtmosphere.Parsear(File.ReadAllText(archivo));
            if (secciones.Count == 0)
                continue;
            var nombreCarpeta = Path.GetFileName(dir);
            resultado.Add(new TrucoInstalado
            {
                Nombre = nombreCarpeta,
                Clave = nombreCarpeta,
                Lineas = secciones.SelectMany(s => s.Lineas).ToList(),
                Activo = !deshabilitados.Contains(nombreCarpeta),
                Archivo = archivo,
            });
        }
        return resultado;
    }

    public void Aplicar(string titleId, string buildId, CambiosTrucos cambios)
    {
        if (cambios.Vacio)
            return;
        if (Bloqueo() is { } motivo)
            throw new InvalidOperationException(motivo);

        var ini = QtIni.Leer(RutaQtConfig);
        var carpetaJuego = CarpetaJuego(titleId);
        buildId = CatalogoTrucos.NormalizarBuild(buildId);

        foreach (var nuevo in cambios.Instalar)
        {
            var nombreCarpeta = CarpetaLibre(carpetaJuego, GestoresTrucos.NombreCarpeta(nuevo.Nombre));
            var dirTrucos = Path.Combine(carpetaJuego, nombreCarpeta, "cheats");
            Directory.CreateDirectory(dirTrucos);
            File.WriteAllText(Path.Combine(dirTrucos, buildId + ".txt"), ParserAtmosphere.Escribir([(nuevo.Nombre, nuevo.Lineas)]), new UTF8Encoding(false));
            ini.Fijar(titleId, nombreCarpeta, !nuevo.Activo);
        }

        foreach (var (clave, activo) in cambios.Estados)
            ini.Fijar(titleId, clave, !activo);

        if (ini.Modificado)
        {
            Respaldos.Copiar(RutaQtConfig, $"{Emulador.Id}-qt-config.ini");
            ini.Guardar(RutaQtConfig);
        }
    }

    private static string? ArchivoTrucos(string carpetaMod, string buildId)
    {
        var dir = Path.Combine(carpetaMod, "cheats");
        if (!Directory.Exists(dir))
            return null;
        return Directory.EnumerateFiles(dir, "*.txt")
            .FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Equals(buildId, StringComparison.OrdinalIgnoreCase));
    }

    private static string CarpetaLibre(string carpetaJuego, string nombre)
    {
        var candidato = nombre;
        for (var i = 2; Directory.Exists(Path.Combine(carpetaJuego, candidato)); i++)
            candidato = $"{nombre} ({i})";
        return candidato;
    }
}

/// <summary>
/// Ryujinx: mods\contents\&lt;id&gt;\cheats\&lt;BuildID&gt;.txt; los activos se listan en
/// mods\contents\&lt;id&gt;\cheats\enabled.txt como "&lt;BuildID&gt;-&lt;Nombre Cheat&gt;".
/// </summary>
public sealed class GestorTrucosRyujinx(EmuladorConfig emulador) : IGestorTrucos
{
    public EmuladorConfig Emulador { get; } = emulador;

    private string CarpetaJuego(string titleId) => Path.Combine(Emulador.CarpetaDatos, "mods", "contents", titleId.ToLowerInvariant());
    private string RutaHabilitados(string titleId) => Path.Combine(CarpetaJuego(titleId), "cheats", "enabled.txt");

    public static string ClaveDe(string nombre) => $"<{nombre} Cheat>";

    // Ryujinx lee enabled.txt al iniciar cada juego, así que no hace falta cerrarlo.
    public string? Bloqueo() => null;

    public List<TrucoInstalado> Leer(string titleId, string buildId)
    {
        var resultado = new List<TrucoInstalado>();
        var habilitados = LeerHabilitados(titleId);
        foreach (var archivo in ArchivosTrucos(titleId, buildId))
        {
            var buildArchivo = Path.GetFileNameWithoutExtension(archivo);
            foreach (var s in ParserAtmosphere.Parsear(File.ReadAllText(archivo)))
            {
                var clave = ClaveDe(s.Nombre);
                resultado.Add(new TrucoInstalado
                {
                    Nombre = s.Nombre,
                    Clave = clave,
                    Lineas = s.Lineas.ToList(),
                    Activo = habilitados.Contains($"{buildArchivo}-{clave}"),
                    Archivo = archivo,
                });
            }
        }
        return resultado;
    }

    public void Aplicar(string titleId, string buildId, CambiosTrucos cambios)
    {
        if (cambios.Vacio)
            return;
        buildId = CatalogoTrucos.NormalizarBuild(buildId);
        var rutaHabilitados = RutaHabilitados(titleId);
        Directory.CreateDirectory(Path.GetDirectoryName(rutaHabilitados)!);

        var lineasHabilitadas = File.Exists(rutaHabilitados)
            ? File.ReadAllLines(rutaHabilitados).Where(l => l.Trim().Length > 0).ToList()
            : new List<string>();

        void Fijar(string entrada, bool activo)
        {
            var indice = lineasHabilitadas.FindIndex(l => l.Trim().Equals(entrada, StringComparison.OrdinalIgnoreCase));
            if (activo && indice < 0)
                lineasHabilitadas.Add(entrada);
            else if (!activo && indice >= 0)
                lineasHabilitadas.RemoveAt(indice);
        }

        if (cambios.Instalar.Count > 0)
        {
            var nombresUsados = Leer(titleId, buildId).Select(t => t.Nombre).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var nuevas = new List<(string Nombre, IReadOnlyList<string> Lineas)>();
            foreach (var nuevo in cambios.Instalar)
            {
                var nombre = ParserAtmosphere.NombreSeguroEnArchivo(nuevo.Nombre);
                var unico = nombre;
                for (var i = 2; nombresUsados.Contains(unico); i++)
                    unico = $"{nombre} ({i})";
                nombresUsados.Add(unico);
                nuevas.Add((unico, nuevo.Lineas));
                Fijar($"{buildId}-{ClaveDe(unico)}", nuevo.Activo);
            }

            var archivo = Path.Combine(Path.GetDirectoryName(rutaHabilitados)!, buildId + ".txt");
            var existente = File.Exists(archivo) ? File.ReadAllText(archivo).TrimEnd() : "";
            var agregado = ParserAtmosphere.Escribir(nuevas);
            File.WriteAllText(archivo, existente.Length > 0 ? existente + "\r\n\r\n" + agregado : agregado, new UTF8Encoding(false));
        }

        foreach (var (clave, activo) in cambios.Estados)
        {
            // La clave ya incluye "<… Cheat>"; el Build ID sale del archivo donde está el truco.
            var archivoDelTruco = Leer(titleId, buildId).FirstOrDefault(t => t.Clave == clave)?.Archivo;
            var buildArchivo = archivoDelTruco != null ? Path.GetFileNameWithoutExtension(archivoDelTruco) : buildId;
            Fijar($"{buildArchivo}-{clave}", activo);
        }

        if (File.Exists(rutaHabilitados))
            Respaldos.Copiar(rutaHabilitados, $"{Emulador.Id}-{titleId}-enabled.txt");
        File.WriteAllText(rutaHabilitados, string.Join("\r\n", lineasHabilitadas) + (lineasHabilitadas.Count > 0 ? "\r\n" : ""), new UTF8Encoding(false));
    }

    private HashSet<string> LeerHabilitados(string titleId)
    {
        var ruta = RutaHabilitados(titleId);
        return File.Exists(ruta)
            ? File.ReadAllLines(ruta).Select(l => l.Trim()).Where(l => l.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    private IEnumerable<string> ArchivosTrucos(string titleId, string buildId)
    {
        var carpeta = CarpetaJuego(titleId);
        if (!Directory.Exists(carpeta))
            yield break;
        var directorios = new List<string> { Path.Combine(carpeta, "cheats") };
        directorios.AddRange(Directory.EnumerateDirectories(carpeta)
            .Where(d => !Path.GetFileName(d).Equals("cheats", StringComparison.OrdinalIgnoreCase))
            .Select(d => Path.Combine(d, "cheats")));
        foreach (var dir in directorios.Where(Directory.Exists))
            foreach (var f in Directory.EnumerateFiles(dir, "*.txt"))
                if (Path.GetFileNameWithoutExtension(f).Equals(buildId, StringComparison.OrdinalIgnoreCase))
                    yield return f;
    }
}

public static class Respaldos
{
    /// <summary>Guarda una copia con fecha antes de modificar un archivo del emulador (conserva las 20 más recientes).</summary>
    public static void Copiar(string archivo, string nombreBase)
    {
        if (!File.Exists(archivo))
            return;
        var destino = Path.Combine(Rutas.Respaldos, $"{DateTime.Now:yyyyMMdd-HHmmss}-{nombreBase}");
        File.Copy(archivo, destino, true);
        foreach (var viejo in Directory.EnumerateFiles(Rutas.Respaldos, "*-" + nombreBase).OrderByDescending(f => f).Skip(20))
            File.Delete(viejo);
    }
}
