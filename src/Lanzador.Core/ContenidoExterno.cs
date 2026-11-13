using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Lanzador.Core.Actualizaciones;
using Lanzador.Core.Controles;
using Lanzador.Core.Trucos;

namespace Lanzador.Core;

public sealed record ResultadoContenido(bool Exito, string Mensaje, bool Cambio = false);

/// <summary>
/// Carpetas de actualizaciones y DLC que los emuladores cargan solos, sin instalar nada:
/// <list type="bullet">
/// <item>Eden y Citron: <c>Paths\external_content_dirs</c> en [UI] de <c>qt-config.ini</c> («carpetas de contenido externo»).</item>
/// <item>Ryujinx (Ryubing): <c>autoload_dirs</c> de <c>Config.json</c> («carpetas de carga automática de actualizaciones y DLC»).</item>
/// <item>Suyu no tiene nada parecido: ahí las actualizaciones se instalan desde el propio emulador.</item>
/// </list>
/// Solo se agregan carpetas; nunca se quita una que el emulador ya tuviera. Con el emulador cerrado y con respaldo.
/// </summary>
public static partial class ContenidoExterno
{
    private const string SeccionYuzu = "UI";
    private const string ClaveTamano = @"Paths\external_content_dirs\size";

    /// <summary>"Juego", "Actualización" o "DLC" según el Title ID del nombre del archivo; null si no lo trae.</summary>
    public static string? TipoDeArchivo(string ruta)
    {
        if (RxTitleId().Match(Path.GetFileNameWithoutExtension(ruta)) is not { Success: true } m)
            return null;
        return (Convert.ToUInt64(m.Groups[1].Value, 16) & 0xFFF) switch
        {
            0 => "Juego",
            0x800 => "Actualización",
            _ => "DLC",
        };
    }

    /// <summary>True si el emulador sabe cargar actualizaciones y DLC desde carpetas.</summary>
    public static bool Admite(EmuladorConfig emulador)
    {
        try
        {
            var ruta = Controles.Controles.ArchivoConfig(emulador);
            if (!File.Exists(ruta))
                return false;
            return emulador.Tipo == TipoEmulador.Ryujinx
                ? JsonNode.Parse(File.ReadAllText(ruta)) is JsonObject raiz && raiz.ContainsKey("autoload_dirs")
                : Ini.Leer(ruta).Valores(SeccionYuzu).ContainsKey(ClaveTamano);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    /// <summary>Las carpetas de contenido que el emulador tiene configuradas.</summary>
    public static List<string> Leer(EmuladorConfig emulador)
    {
        var ruta = Controles.Controles.ArchivoConfig(emulador);
        if (!File.Exists(ruta))
            return [];
        if (emulador.Tipo == TipoEmulador.Ryujinx)
            return JsonNode.Parse(File.ReadAllText(ruta)) is JsonObject raiz && raiz["autoload_dirs"] is JsonArray dirs
                ? dirs.Select(d => d?.GetValue<string>() ?? "").Where(d => d.Length > 0).ToList()
                : [];
        return Ini.Leer(ruta).Pares(SeccionYuzu)
            .Where(p => RxEntrada().IsMatch(p.Clave))
            .Select(p => QtIni.Decodificar(p.Valor))
            .Where(v => v.Length > 0)
            .ToList();
    }

    /// <summary>Agrega al emulador las carpetas que le falten.</summary>
    public static ResultadoContenido Agregar(EmuladorConfig emulador, IEnumerable<string> carpetas)
    {
        try
        {
            if (!Admite(emulador))
                return new(true, $"{emulador.Nombre} no carga actualizaciones ni DLC desde carpetas: se instalan desde el propio emulador.");
            var actuales = Leer(emulador).Select(Normalizar).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var nuevas = carpetas.Where(c => !string.IsNullOrWhiteSpace(c) && Directory.Exists(c))
                .Where(c => !actuales.Contains(Normalizar(c)))
                .DistinctBy(Normalizar, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (nuevas.Count == 0)
                return new(true, $"{emulador.Nombre} ya tenía esas carpetas.");
            if (Actualizador.Bloqueo(emulador) is { } motivo)
                return new(false, motivo);

            var ruta = Controles.Controles.ArchivoConfig(emulador);
            Respaldos.Copiar(ruta, $"{emulador.Id}-{Path.GetFileName(ruta)}");
            if (emulador.Tipo == TipoEmulador.Ryujinx)
            {
                var raiz = JsonNode.Parse(File.ReadAllText(ruta)) as JsonObject ?? throw new JsonException("Config.json de Ryujinx no es un objeto");
                var dirs = raiz["autoload_dirs"] as JsonArray ?? new JsonArray();
                foreach (var c in nuevas)
                    dirs.Add(Path.GetFullPath(c));
                if (dirs.Parent == null)
                    raiz["autoload_dirs"] = dirs;
                Controles.Controles.EscribirJson(ruta, raiz);
            }
            else
            {
                var ini = Ini.Leer(ruta);
                var pares = ini.Pares(SeccionYuzu).ToList();
                // QSettings lee hasta "size": se numera después de la entrada más alta que haya, por si quedaron restos.
                var ultimo = pares.Select(p => RxEntrada().Match(p.Clave)).Where(m => m.Success).Select(m => int.Parse(m.Groups[1].Value)).DefaultIfEmpty(0).Max();
                var tamano = int.TryParse(pares.LastOrDefault(p => p.Clave == ClaveTamano).Valor, out var t) ? t : 0;
                var indice = Math.Max(ultimo, tamano);
                var valores = new List<(string, string)>();
                foreach (var c in nuevas)
                    valores.Add(($@"Paths\external_content_dirs\{++indice}\path", QtIni.Codificar(Path.GetFullPath(c).Replace('\\', '/').TrimEnd('/') + "/")));
                valores.Add((ClaveTamano, indice.ToString()));
                ini.Fijar(SeccionYuzu, valores);
                ini.Guardar(ruta);
            }
            return new(true, $"{emulador.Nombre}: {(nuevas.Count == 1 ? "carpeta agregada" : $"{nuevas.Count} carpetas agregadas")}.", true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return new(false, $"No se pudieron agregar las carpetas a {emulador.Nombre}: {Actualizador.Explicar(ex)}");
        }
    }

    /// <summary>Misma carpeta aunque cambien las barras o la barra final.</summary>
    public static string Normalizar(string carpeta) => carpeta.Trim().Replace('/', '\\').TrimEnd('\\');

    [GeneratedRegex(@"^Paths\\external_content_dirs\\(\d+)\\path$")]
    private static partial Regex RxEntrada();

    [GeneratedRegex(@"\[([0-9A-Fa-f]{16})\]")]
    private static partial Regex RxTitleId();
}
