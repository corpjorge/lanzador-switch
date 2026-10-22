using System.Text;

namespace Lanzador.Core;

public sealed record EntradaCompatibilidad(string TitleId, string Nombre, string Estado, string[] Etiquetas, string Fecha)
{
    public string EstadoEs => Estado switch
    {
        "playable" => "Jugable",
        "ingame" => "Jugable con fallos",
        "menus" => "Llega a los menús",
        "boots" => "Solo arranca",
        "nothing" => "No arranca",
        _ => Estado,
    };

    /// <summary>0 = peor, 4 = mejor; sirve para colorear.</summary>
    public int Nivel => Estado switch
    {
        "playable" => 4,
        "ingame" => 3,
        "menus" => 2,
        "boots" => 1,
        _ => 0,
    };

    public string EtiquetasEs => string.Join(", ", Etiquetas.Select(Compatibilidad.TraducirEtiqueta));
}

/// <summary>
/// Una entrada de las listas con el formato de yuzu: la de Citron Neo y la de yuzu (archivada en marzo de 2024),
/// que sirve de referencia para Eden y Suyu porque salen de yuzu.
/// </summary>
public sealed record CompatibilidadYuzu(string TitleId, string Nombre, int Codigo)
{
    public string EstadoEs => Codigo switch
    {
        0 => "Perfecto",
        1 => "Bien",
        2 => "Jugable con fallos",
        3 => "Fallos graves",
        4 => "Solo intro o menú",
        5 => "No arranca",
        _ => "Sin probar",
    };

    /// <summary>Misma escala que Ryubing (4 = mejor, 0 = peor) para colorear; -1 = sin probar.</summary>
    public int Nivel => Codigo switch
    {
        0 or 1 => 4,
        2 => 3,
        3 => 2,
        4 => 1,
        5 => 0,
        _ => -1,
    };
}

/// <summary>
/// Listas de compatibilidad: la oficial de Ryubing (github.com/Ryubing/Compatibility), la de Citron Neo
/// (github.com/citron-neo/Citron-Compatability) y la de yuzu archivada (github.com/yuzu-mirror/api).
/// </summary>
public static class Compatibilidad
{
    private const string UrlRyubing = "https://raw.githubusercontent.com/Ryubing/Compatibility/main/compatibility.csv";
    private const string UrlCitron = "https://raw.githubusercontent.com/citron-neo/Citron-Compatability/main/compatibility_list.json";
    private const string UrlYuzu = "https://raw.githubusercontent.com/yuzu-mirror/api/master/gamedb/websiteFeed";

    /// <summary>Lista de Citron Neo (la mantiene su comunidad; se revisa cada día).</summary>
    public static async Task<Dictionary<string, CompatibilidadYuzu>> CargarCitronAsync(bool forzar, CancellationToken ct) =>
        ParsearYuzu(await Http.TextoConCacheAsync(UrlCitron, Path.Combine(Rutas.Cache, "compatibilidad_citron.json"), TimeSpan.FromDays(1), forzar, ct));

    /// <summary>Lista de yuzu congelada en marzo de 2024 (ya no cambia: se descarga una vez).</summary>
    public static async Task<Dictionary<string, CompatibilidadYuzu>> CargarYuzuAsync(CancellationToken ct) =>
        ParsearYuzu(await Http.TextoConCacheAsync(UrlYuzu, Path.Combine(Rutas.Cache, "compatibilidad_yuzu_2024.json"), TimeSpan.FromDays(3650), false, ct));

    /// <summary>
    /// Formato de yuzu: [{ "title" (o "directory"), "compatibility": 0–5 o 99, "releases": [{ "id": TitleID }] }].
    /// </summary>
    public static Dictionary<string, CompatibilidadYuzu> ParsearYuzu(string json)
    {
        var resultado = new Dictionary<string, CompatibilidadYuzu>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(json))
            return resultado;
        using var documento = System.Text.Json.JsonDocument.Parse(json);
        if (documento.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array)
            return resultado;
        foreach (var juego in documento.RootElement.EnumerateArray())
        {
            if (juego.ValueKind != System.Text.Json.JsonValueKind.Object
                || !juego.TryGetProperty("compatibility", out var compat) || compat.ValueKind != System.Text.Json.JsonValueKind.Number
                || !compat.TryGetInt32(out var codigo)
                || !juego.TryGetProperty("releases", out var versiones) || versiones.ValueKind != System.Text.Json.JsonValueKind.Array)
                continue;
            var nombre = juego.TryGetProperty("title", out var t) && t.ValueKind == System.Text.Json.JsonValueKind.String ? t.GetString()
                : juego.TryGetProperty("directory", out var dir) && dir.ValueKind == System.Text.Json.JsonValueKind.String ? dir.GetString() : null;
            foreach (var version in versiones.EnumerateArray())
                if (version.ValueKind == System.Text.Json.JsonValueKind.Object && version.TryGetProperty("id", out var id)
                    && id.ValueKind == System.Text.Json.JsonValueKind.String && id.GetString() is { Length: 16 } titleId)
                    resultado[titleId] = new CompatibilidadYuzu(titleId.ToUpperInvariant(), nombre ?? "", codigo);
        }
        return resultado;
    }

    public static async Task<Dictionary<string, EntradaCompatibilidad>> CargarRyubingAsync(bool forzar, CancellationToken ct)
    {
        var csv = await Http.TextoConCacheAsync(UrlRyubing, Path.Combine(Rutas.Cache, "compatibilidad_ryubing.csv"), TimeSpan.FromDays(7), forzar, ct);
        return Parsear(csv);
    }

    public static Dictionary<string, EntradaCompatibilidad> Parsear(string csv)
    {
        var resultado = new Dictionary<string, EntradaCompatibilidad>(StringComparer.OrdinalIgnoreCase);
        var primera = true;
        foreach (var campos in LeerCsv(csv))
        {
            if (primera)
            {
                primera = false;
                if (campos.Count > 0 && campos[0].Equals("title_id", StringComparison.OrdinalIgnoreCase))
                    continue;
            }
            if (campos.Count < 4 || campos[0].Length != 16)
                continue;
            var etiquetas = campos[2].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            resultado[campos[0]] = new EntradaCompatibilidad(campos[0].ToUpperInvariant(), campos[1], campos[3].Trim().ToLowerInvariant(), etiquetas, campos.Count > 4 ? campos[4] : "");
        }
        return resultado;
    }

    public static string TraducirEtiqueta(string etiqueta) => etiqueta.ToLowerInvariant() switch
    {
        "slow" => "lento",
        "nvdec" => "videos (NVDEC)",
        "crash" => "se cierra",
        "gpu" => "fallos gráficos",
        "audio" => "fallos de audio",
        "deadlock" => "se congela",
        "cpu" => "fallos de CPU",
        "online-broken" => "en línea no funciona",
        "online-working" => "en línea funciona",
        "ldn-works" => "LAN funciona",
        "ldn-broken" => "LAN no funciona",
        "ldn-untested" => "LAN sin probar",
        "ldn-partial" => "LAN parcial",
        "services" or "services-horizon" => "servicios incompletos",
        "savedata" => "partidas guardadas",
        "amd-vendor-bug" => "fallo con AMD",
        "nvidia-vendor-bug" => "fallo con NVIDIA",
        "intel-vendor-bug" => "fallo con Intel",
        "mac-bug" => "fallo en Mac",
        "ue4" => "Unreal Engine 4",
        "needs update" => "pide actualización",
        "needs more attention" => "requiere revisión",
        "lan" => "LAN",
        "incomplete" => "incompleto",
        _ => etiqueta,
    };

    private static IEnumerable<List<string>> LeerCsv(string texto)
    {
        var campos = new List<string>();
        var actual = new StringBuilder();
        var entreComillas = false;
        for (var i = 0; i < texto.Length; i++)
        {
            var c = texto[i];
            if (entreComillas)
            {
                if (c == '"' && i + 1 < texto.Length && texto[i + 1] == '"') { actual.Append('"'); i++; }
                else if (c == '"') entreComillas = false;
                else actual.Append(c);
                continue;
            }
            switch (c)
            {
                case '"': entreComillas = true; break;
                case ',': campos.Add(actual.ToString()); actual.Clear(); break;
                case '\r': break;
                case '\n':
                    campos.Add(actual.ToString());
                    actual.Clear();
                    yield return campos;
                    campos = new List<string>();
                    break;
                default: actual.Append(c); break;
            }
        }
        if (actual.Length > 0 || campos.Count > 0)
        {
            campos.Add(actual.ToString());
            yield return campos;
        }
    }
}
