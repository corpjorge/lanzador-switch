using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lanzador.Core;

public static class GestorConfiguracion
{
    public static readonly JsonSerializerOptions OpcionesJson = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };

    public static Configuracion Cargar()
    {
        var ruta = Rutas.ArchivoConfig;
        if (File.Exists(ruta))
        {
            try
            {
                var leida = JsonSerializer.Deserialize<Configuracion>(File.ReadAllText(ruta), OpcionesJson);
                if (leida != null)
                {
                    leida.Juegos = new Dictionary<string, PreferenciasJuego>(leida.Juegos, StringComparer.OrdinalIgnoreCase);
                    if (Completar(leida, Detectar(AppContext.BaseDirectory)))
                        Guardar(leida);
                    return leida;
                }
            }
            catch (JsonException)
            {
                File.Copy(ruta, ruta + ".danado", true);
            }
        }

        var nueva = Detectar(AppContext.BaseDirectory);
        Guardar(nueva);
        return nueva;
    }

    public static void Guardar(Configuracion config)
    {
        var temporal = Rutas.ArchivoConfig + ".tmp";
        File.WriteAllText(temporal, JsonSerializer.Serialize(config, OpcionesJson));
        File.Move(temporal, Rutas.ArchivoConfig, true);
    }

    /// <summary>Busca los emuladores en las carpetas superiores a la del lanzador (p. ej. C:\Switch).</summary>
    public static Configuracion Detectar(string carpetaInicio)
    {
        var raices = new List<string>();
        for (var dir = new DirectoryInfo(carpetaInicio); dir != null && raices.Count < 4; dir = dir.Parent)
            raices.Add(dir.FullName);

        var config = new Configuracion();
        config.Emuladores.Add(DetectarYuzu("eden", "Eden", raices, ["Eden", "eden"], "eden.exe", "eden"));
        config.Emuladores.Add(DetectarRyujinx(raices));

        // Citron Neo aún no está instalado: se propone junto a los demás emuladores encontrados.
        var junto = config.Emuladores.Where(e => e.Instalado).Select(e => Path.GetDirectoryName(Path.GetDirectoryName(e.RutaExe))).FirstOrDefault();
        if (junto != null)
            raices.Insert(Math.Min(2, raices.Count), junto);
        config.Emuladores.Add(DetectarYuzu("citron", "Citron Neo", raices, ["Citron", "citron", "Citron Neo", "citron-neo"], "citron.exe", "citron"));
        config.Emuladores.Add(DetectarYuzu("suyu", "Suyu", raices, ["Suyu", "suyu"], "suyu.exe", "suyu"));

        config.CarpetasJuegos = DetectarCarpetasJuegos(config);
        return config;
    }

    /// <summary>
    /// Agrega a una configuración ya guardada lo que se incorporó después (p. ej. Suyu),
    /// sin tocar lo que el usuario ya configuró. Devuelve true si cambió algo.
    /// </summary>
    public static bool Completar(Configuracion config, Configuracion detectada)
    {
        var cambio = false;
        foreach (var emulador in detectada.Emuladores)
            if (config.Emulador(emulador.Id) == null)
            {
                config.Emuladores.Add(emulador);
                cambio = true;
            }
        return cambio;
    }

    private static EmuladorConfig DetectarYuzu(string id, string nombre, List<string> raices, string[] carpetas, string exe, string carpetaAppData)
    {
        var ruta = BuscarExe(raices, carpetas, exe) ?? Path.Combine(RaizPredeterminada(raices), carpetas[0], exe);
        var dirExe = Path.GetDirectoryName(ruta)!;
        var portable = Path.Combine(dirExe, "user");
        return new EmuladorConfig
        {
            Id = id,
            Nombre = nombre,
            Tipo = TipoEmulador.Yuzu,
            RutaExe = ruta,
            Argumentos = "-f -g \"{juego}\"",
            CarpetaDatos = Directory.Exists(portable) || !File.Exists(ruta)
                ? portable
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), carpetaAppData),
            Activo = File.Exists(ruta),
        };
    }

    private static EmuladorConfig DetectarRyujinx(List<string> raices)
    {
        var ruta = BuscarExe(raices, ["ryujinx", "Ryujinx", "Ryubing"], "Ryujinx.exe") ?? Path.Combine(RaizPredeterminada(raices), "ryujinx", "Ryujinx.exe");
        var portable = Path.Combine(Path.GetDirectoryName(ruta)!, "portable");
        return new EmuladorConfig
        {
            Id = "ryujinx",
            Nombre = "Ryujinx",
            Tipo = TipoEmulador.Ryujinx,
            RutaExe = ruta,
            Argumentos = "--fullscreen \"{juego}\"",
            CarpetaDatos = Directory.Exists(portable) || !File.Exists(ruta)
                ? portable
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ryujinx"),
            Activo = File.Exists(ruta),
        };
    }

    /// <summary>
    /// Si un emulador no está, se propone en la carpeta que contiene la del lanzador (p. ej. C:\Switch):
    /// la de encima de Lanzador\app al compilarlo, o la de encima de Lanzador al descomprimir el ZIP.
    /// </summary>
    private static string RaizPredeterminada(List<string> raices) =>
        new DirectoryInfo(raices[0]).Name.Equals("app", StringComparison.OrdinalIgnoreCase)
            ? raices.Count > 2 ? raices[2] : raices[^1]
            : raices.Count > 1 ? raices[1] : raices[0];

    private static string? BuscarExe(List<string> raices, string[] carpetas, string exe)
    {
        foreach (var raiz in raices)
            foreach (var carpeta in carpetas)
            {
                var ruta = Path.Combine(raiz, carpeta, exe);
                if (File.Exists(ruta))
                    return ruta;
            }
        return null;
    }

    /// <summary>Junta las carpetas de juegos que ya usan Eden y Ryujinx.</summary>
    public static List<string> DetectarCarpetasJuegos(Configuracion config)
    {
        var carpetas = new List<string>();
        foreach (var emu in config.Emuladores.Where(e => e.Instalado))
        {
            try
            {
                if (emu.Tipo == TipoEmulador.Yuzu)
                    carpetas.AddRange(CarpetasDeQtConfig(Path.Combine(emu.CarpetaDatos, "config", "qt-config.ini")));
                else
                    carpetas.AddRange(CarpetasDeRyujinx(Path.Combine(emu.CarpetaDatos, "Config.json")));
            }
            catch (IOException) { }
            catch (JsonException) { }
        }
        return NormalizarCarpetas(carpetas);
    }

    public static List<string> NormalizarCarpetas(IEnumerable<string> carpetas)
    {
        var limpias = carpetas
            .Select(c => c.Trim().Replace('/', '\\').TrimEnd('\\'))
            .Where(c => c.Length > 2 && Directory.Exists(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Quita las que ya están dentro de otra (el escaneo es recursivo).
        return limpias
            .Where(c => !limpias.Any(otra => !otra.Equals(c, StringComparison.OrdinalIgnoreCase)
                                             && c.StartsWith(otra + "\\", StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    private static IEnumerable<string> CarpetasDeQtConfig(string ruta)
    {
        if (!File.Exists(ruta))
            yield break;
        foreach (var linea in File.ReadLines(ruta))
        {
            var igual = linea.IndexOf('=');
            if (igual < 0)
                continue;
            var clave = linea[..igual];
            if (!(clave.StartsWith(@"Paths\gamedirs\") || clave.StartsWith(@"Paths\external_content_dirs\")) || !clave.EndsWith(@"\path"))
                continue;
            var valor = QtIni.Decodificar(linea[(igual + 1)..]);
            if (valor is "SDMC" or "UserNAND" or "SysNAND" || valor.Length == 0)
                continue;
            yield return valor;
        }
    }

    private static IEnumerable<string> CarpetasDeRyujinx(string ruta)
    {
        if (!File.Exists(ruta))
            return [];
        using var doc = JsonDocument.Parse(File.ReadAllText(ruta));
        if (!doc.RootElement.TryGetProperty("game_dirs", out var dirs) || dirs.ValueKind != JsonValueKind.Array)
            return [];
        return dirs.EnumerateArray().Where(d => d.ValueKind == JsonValueKind.String).Select(d => d.GetString()!).ToList();
    }
}
