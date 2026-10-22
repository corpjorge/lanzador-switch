using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lanzador.Core.Trucos;

public sealed class TrucoDisponible
{
    public string BuildId { get; init; } = "";
    public string NombreOriginal { get; init; } = "";
    public bool EsMaestro { get; init; }
    public List<string> Lineas { get; init; } = new();
    public List<string> Fuentes { get; } = new();
    public string Codigo => string.Join("\n", Lineas);
    public string? Botones => LectorBotones.Describir(Lineas);
}

/// <summary>Trucos de un juego juntando switch-cheats-db y tinfoil.io, agrupados por Build ID.</summary>
public sealed class CatalogoTrucos
{
    public string TitleId { get; init; } = "";
    public string? Titulo { get; set; }

    /// <summary>Versión del juego (0, 65536, 131072…) → Build ID de 16 caracteres.</summary>
    public SortedDictionary<int, string> Versiones { get; } = new();

    public Dictionary<string, List<TrucoDisponible>> PorBuild { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Avisos { get; } = new();

    public IReadOnlyList<TrucoDisponible> Para(string buildId) =>
        PorBuild.TryGetValue(buildId, out var lista) ? lista : [];

    public string? BuildDeVersion(int version) => Versiones.TryGetValue(version, out var b) ? b : null;

    internal void AgregarVersion(int version, string buildId)
    {
        buildId = NormalizarBuild(buildId);
        if (buildId.Length == 16 && !Versiones.ContainsKey(version))
            Versiones[version] = buildId;
    }

    internal void Agregar(string buildId, SeccionTruco seccion, string fuente)
    {
        buildId = NormalizarBuild(buildId);
        if (buildId.Length != 16)
            return;
        if (!PorBuild.TryGetValue(buildId, out var lista))
            PorBuild[buildId] = lista = new List<TrucoDisponible>();

        var codigo = seccion.Codigo;
        var repetido = lista.FirstOrDefault(t => t.Codigo == codigo);
        if (repetido != null)
        {
            if (!repetido.Fuentes.Contains(fuente))
                repetido.Fuentes.Add(fuente);
            return;
        }
        var truco = new TrucoDisponible { BuildId = buildId, NombreOriginal = seccion.Nombre, EsMaestro = seccion.EsMaestro, Lineas = seccion.Lineas.ToList() };
        truco.Fuentes.Add(fuente);
        lista.Add(truco);
    }

    public static string NormalizarBuild(string buildId)
    {
        var b = buildId.Trim().ToUpperInvariant();
        return b.Length > 16 ? b[..16] : b;
    }
}

public static partial class FuentesTrucos
{
    public const string NombreSwitchCheatsDb = "switch-cheats-db";
    public const string NombreTinfoil = "tinfoil.io";

    private const string BaseSwitchCheatsDb = "https://raw.githubusercontent.com/HamletDuFromage/switch-cheats-db/master";
    private static readonly TimeSpan Vigencia = TimeSpan.FromDays(3);

    public static async Task<CatalogoTrucos> CargarAsync(string titleId, bool forzar, CancellationToken ct)
    {
        var catalogo = new CatalogoTrucos { TitleId = titleId };
        if (!Http.ContenidoEnLinea)
            catalogo.Avisos.Add("Las descargas están desactivadas en Configuración → Internet: solo se usa lo que ya se había descargado.");
        try
        {
            await CargarSwitchCheatsDbAsync(catalogo, forzar, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException && !ct.IsCancellationRequested)
        {
            catalogo.Avisos.Add($"No se pudo leer switch-cheats-db: {ex.Message}");
        }
        try
        {
            await CargarTinfoilAsync(catalogo, forzar, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            catalogo.Avisos.Add($"No se pudo leer tinfoil.io: {ex.Message}");
        }
        return catalogo;
    }

    private static async Task CargarSwitchCheatsDbAsync(CatalogoTrucos catalogo, bool forzar, CancellationToken ct)
    {
        var tid = catalogo.TitleId;
        var versiones = await Http.TextoConCacheAsync($"{BaseSwitchCheatsDb}/versions/{tid}.json", Path.Combine(Rutas.Trucos, $"{tid}.versiones.json"), Vigencia, forzar, ct);
        if (versiones.Length > 0)
            LeerVersionesSwitchCheatsDb(catalogo, versiones);

        var trucos = await Http.TextoConCacheAsync($"{BaseSwitchCheatsDb}/cheats/{tid}.json", Path.Combine(Rutas.Trucos, $"{tid}.trucos.json"), Vigencia, forzar, ct);
        if (trucos.Length > 0)
            LeerTrucosSwitchCheatsDb(catalogo, trucos);
    }

    public static void LeerVersionesSwitchCheatsDb(CatalogoTrucos catalogo, string json)
    {
        using var doc = JsonDocument.Parse(json);
        foreach (var p in doc.RootElement.EnumerateObject())
        {
            if (p.Name == "title" && p.Value.ValueKind == JsonValueKind.String)
                catalogo.Titulo ??= p.Value.GetString();
            else if (int.TryParse(p.Name, out var version) && p.Value.ValueKind == JsonValueKind.String)
                catalogo.AgregarVersion(version, p.Value.GetString()!);
        }
    }

    public static void LeerTrucosSwitchCheatsDb(CatalogoTrucos catalogo, string json)
    {
        using var doc = JsonDocument.Parse(json);
        foreach (var build in doc.RootElement.EnumerateObject())
        {
            if (build.Name == "attribution" || build.Value.ValueKind != JsonValueKind.Object)
                continue;
            foreach (var truco in build.Value.EnumerateObject())
            {
                if (truco.Value.ValueKind != JsonValueKind.String)
                    continue;
                var texto = truco.Value.GetString()!;
                var secciones = ParserAtmosphere.Parsear(texto);
                if (secciones.Count == 1 && secciones[0].Nombre == "Sin nombre")
                    secciones[0].Nombre = truco.Name.Trim('[', ']', '{', '}', ' ');
                foreach (var s in secciones)
                    catalogo.Agregar(build.Name, s, NombreSwitchCheatsDb);
            }
        }
    }

    private static async Task CargarTinfoilAsync(CatalogoTrucos catalogo, bool forzar, CancellationToken ct)
    {
        var tid = catalogo.TitleId;
        var html = await Http.TextoConCacheAsync($"https://tinfoil.io/Title/{tid}", Path.Combine(Rutas.Trucos, $"{tid}.tinfoil.html"), Vigencia, forzar, ct);
        if (html.Length > 0)
            LeerTinfoil(catalogo, html);
    }

    /// <summary>Lee la tabla "Build ID's" y la tabla "Cheats" de la página del juego en tinfoil.io.</summary>
    public static void LeerTinfoil(CatalogoTrucos catalogo, string html)
    {
        var titulo = RxTitulo().Match(html);
        if (titulo.Success)
            catalogo.Titulo ??= WebUtility.HtmlDecode(titulo.Groups[1].Value).Trim();

        var tablaBuilds = TablaDespuesDe(html, ">Build ID");
        if (tablaBuilds != null)
            foreach (Match fila in RxFilaBuild().Matches(tablaBuilds))
                catalogo.AgregarVersion(int.Parse(fila.Groups[2].Value) * 0x10000, fila.Groups[1].Value);

        var tablaTrucos = TablaDespuesDe(html, ">Cheats<");
        if (tablaTrucos == null)
            return;
        foreach (Match fila in RxFila().Matches(tablaTrucos))
        {
            var celdas = RxCelda().Matches(fila.Groups[1].Value).Select(c => c.Groups[1].Value).ToList();
            if (celdas.Count < 4)
                continue;
            var nombre = WebUtility.HtmlDecode(RxEtiqueta().Replace(celdas[0], "")).Trim();
            var parche = RxParche().Match(celdas[1]);
            var version = parche.Success ? int.Parse(parche.Groups[1].Value) * 0x10000 : 0;
            var buildId = catalogo.BuildDeVersion(version);
            if (buildId == null)
                continue;

            var lineas = RxLi().Matches(celdas[3]).Select(li => WebUtility.HtmlDecode(RxEtiqueta().Replace(li.Groups[1].Value, "")).Trim());
            var esMaestro = nombre.StartsWith('{');
            var nombreLimpio = nombre.Trim('[', ']', '{', '}', ' ');
            var texto = (esMaestro ? "{" : "[") + nombreLimpio + (esMaestro ? "}" : "]") + "\n" + string.Join("\n", lineas);
            foreach (var s in ParserAtmosphere.Parsear(texto))
                catalogo.Agregar(buildId, s, NombreTinfoil);
        }
    }

    private static string? TablaDespuesDe(string html, string marca)
    {
        var inicio = html.IndexOf(marca, StringComparison.OrdinalIgnoreCase);
        if (inicio < 0)
            return null;
        var tabla = html.IndexOf("<table", inicio, StringComparison.OrdinalIgnoreCase);
        var fin = tabla < 0 ? -1 : html.IndexOf("</table>", tabla, StringComparison.OrdinalIgnoreCase);
        return fin < 0 ? null : html[tabla..fin];
    }

    [GeneratedRegex(@"<title>(.*?)</title>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex RxTitulo();

    [GeneratedRegex(@"<td>\s*([0-9A-Fa-f]{16,64})\s*</td>\s*<td>\s*v(\d+)\s*</td>", RegexOptions.IgnoreCase)]
    private static partial Regex RxFilaBuild();

    [GeneratedRegex(@"<tr[^>]*>(.*?)</tr>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex RxFila();

    [GeneratedRegex(@"<td[^>]*>(.*?)</td>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex RxCelda();

    [GeneratedRegex(@"<li[^>]*>(.*?)</li>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex RxLi();

    [GeneratedRegex(@"v(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex RxParche();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex RxEtiqueta();
}
