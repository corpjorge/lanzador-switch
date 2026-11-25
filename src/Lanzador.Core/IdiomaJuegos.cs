using System.Text.Json;
using System.Text.Json.Nodes;
using Lanzador.Core.Actualizaciones;
using Lanzador.Core.Controles;
using Lanzador.Core.Trucos;
using ConfigEmulador = Lanzador.Core.Controles.Controles;

namespace Lanzador.Core;

internal static class IdiomaJuegos
{
    private const string EspanolLatinoRyujinx = "LatinAmericanSpanish";
    private const string EspanolLatinoYuzu = "14";
    private const string SeccionSistema = "System";
    private const string ClaveIdioma = "language_index";

    internal static string Preparar(EmuladorConfig emulador, Juego juego, string argumentos)
    {
        if (string.IsNullOrWhiteSpace(emulador.CarpetaDatos))
            return argumentos;

        try
        {
            var ruta = ConfigEmulador.ArchivoConfig(emulador);
            var titleId = juego.TieneTitleId && ulong.TryParse(juego.TitleId,
                System.Globalization.NumberStyles.AllowHexSpecifier, null, out var id) ? id : (ulong?)null;

            if (emulador.Tipo == TipoEmulador.Ryujinx)
            {
                // Ryujinx da prioridad al Config.json que está junto al ejecutable.
                var local = Path.Combine(Path.GetDirectoryName(emulador.RutaExe)!, "Config.json");
                if (File.Exists(local))
                    ruta = local;
                if (titleId is { } idRyujinx)
                    AplicarRyujinx(emulador, Path.Combine(emulador.CarpetaDatos, "games", idRyujinx.ToString("x16"), "Config.json"),
                        $"{idRyujinx:x16}-");
                if (!File.Exists(ruta))
                    return $"--system-language {EspanolLatinoRyujinx} {argumentos}";

                AplicarRyujinx(emulador, ruta, "");
            }
            else
            {
                AplicarQt(emulador, ruta, [(ClaveIdioma, EspanolLatinoYuzu), (@"language_index\default", "false")]);
                if (titleId is { } idYuzu)
                {
                    var personalizado = Path.Combine(emulador.CarpetaDatos, "config", "custom", $"{idYuzu:X16}.ini");
                    if (File.Exists(personalizado))
                    {
                        var valores = Ini.Leer(personalizado).Valores(SeccionSistema);
                        const string usarGlobal = @"language_index\use_global";
                        if (valores.ContainsKey(usarGlobal) || valores.ContainsKey(ClaveIdioma))
                            AplicarQt(emulador, personalizado, [(usarGlobal, "true")]);
                    }
                }
            }
            return argumentos;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new InvalidOperationException(
                $"No se pudo configurar el español latinoamericano en {emulador.Nombre}. {ex.Message}", ex);
        }
    }

    private static void AplicarQt(EmuladorConfig emulador, string ruta, (string Clave, string Valor)[] valores)
    {
        var ini = Ini.Leer(ruta);
        var actuales = ini.Valores(SeccionSistema);
        if (valores.All(par => actuales.TryGetValue(par.Clave, out var valor) &&
            QtIni.Decodificar(valor) == par.Valor))
            return;

        ini.Fijar(SeccionSistema, valores);
        Guardar(emulador, ruta, "", () => ini.Guardar(ruta));
    }

    private static void AplicarRyujinx(EmuladorConfig emulador, string ruta, string prefijoRespaldo)
    {
        if (!File.Exists(ruta))
            return;

        var json = JsonNode.Parse(File.ReadAllText(ruta)) as JsonObject
            ?? throw new JsonException("Config.json de Ryujinx no es un objeto.");
        if (json["system_language"] is JsonValue idioma &&
            idioma.TryGetValue<string>(out var valor) && valor == EspanolLatinoRyujinx)
            return;

        json["system_language"] = EspanolLatinoRyujinx;
        Guardar(emulador, ruta, prefijoRespaldo, () => ConfigEmulador.EscribirJson(ruta, json));
    }

    private static void Guardar(EmuladorConfig emulador, string ruta, string prefijoRespaldo, Action escribir)
    {
        if (Actualizador.Bloqueo(emulador) is { } motivo)
            throw new InvalidOperationException(motivo);

        Respaldos.Copiar(ruta, $"{emulador.Id}-idioma-{prefijoRespaldo}{Path.GetFileName(ruta)}");
        Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
        escribir();
    }
}
