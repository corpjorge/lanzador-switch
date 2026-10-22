using System.Text.Json;

namespace Lanzador.Core.Trucos;

public sealed record VersionDetectada(int Version, bool Aproximada, string Motivo)
{
    public static string Texto(int version) => $"v{version / 0x10000}";
}

/// <summary>Averigua qué versión del juego (y por tanto qué Build ID) usa cada emulador.</summary>
public static class DetectorVersion
{
    public static VersionDetectada Detectar(Juego juego, EmuladorConfig emulador, CatalogoTrucos? catalogo)
    {
        var resultado = emulador.Tipo == TipoEmulador.Yuzu ? EnYuzu(juego, emulador) : EnRyujinx(juego, emulador);
        if (resultado.Version >= 0)
            return resultado;

        var ultima = catalogo is { Versiones.Count: > 0 } ? catalogo.Versiones.Keys.Max() : 0;
        return new VersionDetectada(ultima, true, resultado.Motivo + "; se usa la más reciente conocida");
    }

    private static VersionDetectada EnYuzu(Juego juego, EmuladorConfig emulador)
    {
        var versionBase = juego.VersionIncluida ?? 0;
        var deshabilitados = QtIni.Leer(Path.Combine(emulador.CarpetaDatos, "config", "qt-config.ini")).Deshabilitados(juego.TitleId);

        // Eden guarda en caché la columna "Complementos" de su lista: "[D]" delante = desactivado.
        bool? hayActualizacionActiva = null;
        var cacheLista = Path.Combine(emulador.CarpetaDatos, "cache", "game_list", juego.TitleId + ".pv.txt");
        if (File.Exists(cacheLista))
        {
            var actualizaciones = File.ReadAllLines(cacheLista)
                .Select(l => l.Trim())
                .Where(l => l.StartsWith("Update", StringComparison.OrdinalIgnoreCase) || l.StartsWith("[D] Update", StringComparison.OrdinalIgnoreCase))
                .ToList();
            hayActualizacionActiva = actualizaciones.Any(l => !l.StartsWith("[D]"));
        }

        // Con todas las actualizaciones desactivadas, Eden ejecuta la versión base aunque el archivo
        // traiga una actualización integrada ("1G+1U"): esa también aparece como complemento "Update".
        if (hayActualizacionActiva == false)
            return new VersionDetectada(0, false, $"Actualizaciones desactivadas en {emulador.Nombre} (versión base)");

        var activas = juego.Actualizaciones
            .Where(a => a.Version is > 0 && !deshabilitados.Contains($"Update@{a.Version}"))
            .Select(a => a.Version!.Value)
            .ToList();
        var candidata = Math.Max(activas.DefaultIfEmpty(0).Max(), versionBase);
        if (candidata > 0)
            return new VersionDetectada(candidata, false, candidata > versionBase || versionBase == 0
                ? $"Actualización activa en {emulador.Nombre}"
                : "Versión incluida en el archivo del juego");

        if (hayActualizacionActiva == true)
            return new VersionDetectada(-1, true, $"{emulador.Nombre} tiene una actualización instalada en su NAND");

        return new VersionDetectada(0, false, "Sin actualizaciones (versión base)");
    }

    private static VersionDetectada EnRyujinx(Juego juego, EmuladorConfig emulador)
    {
        var versionBase = juego.VersionIncluida ?? 0;
        var ruta = Path.Combine(emulador.CarpetaDatos, "games", juego.TitleId.ToLowerInvariant(), "updates.json");
        string? seleccionada = null;
        if (File.Exists(ruta))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(ruta));
                if (doc.RootElement.TryGetProperty("selected", out var s) && s.ValueKind == JsonValueKind.String)
                    seleccionada = s.GetString();
            }
            catch (JsonException) { }
        }

        if (string.IsNullOrWhiteSpace(seleccionada))
            return new VersionDetectada(versionBase, false, versionBase > 0 ? "Versión incluida en el archivo del juego" : "Sin actualización seleccionada en Ryujinx");

        var archivo = juego.Actualizaciones.FirstOrDefault(a => a.Ruta.Equals(seleccionada, StringComparison.OrdinalIgnoreCase));
        var version = archivo?.Version ?? EscanerBiblioteca.LeerVersion(Path.GetFileNameWithoutExtension(seleccionada)).Version;
        if (version is > 0)
            return new VersionDetectada(version.Value, false, "Actualización seleccionada en Ryujinx");
        if (version == 0)
            return new VersionDetectada(versionBase, false, "Sin actualizaciones (versión base)");

        return new VersionDetectada(-1, true, "La versión de la actualización de Ryujinx no viene en el nombre del archivo");
    }
}
