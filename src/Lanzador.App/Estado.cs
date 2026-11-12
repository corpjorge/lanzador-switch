using Lanzador.App.Modelos;
using Lanzador.Core;
using Lanzador.Core.ClavesFirmware;
using Lanzador.Core.IA;

namespace Lanzador.App;

/// <summary>Datos compartidos por todas las pantallas.</summary>
public static class Estado
{
    public static Configuracion Config { get; set; } = new();
    public static List<JuegoVM> Juegos { get; set; } = new();
    public static Dictionary<string, EntradaCompatibilidad> Compatibilidad { get; set; } = new();

    /// <summary>Lista de compatibilidad de Citron Neo, por TitleID.</summary>
    public static Dictionary<string, CompatibilidadYuzu> CompatibilidadCitron { get; set; } = new();

    /// <summary>Lista de yuzu (marzo de 2024), referencia para Eden y Suyu, por TitleID.</summary>
    public static Dictionary<string, CompatibilidadYuzu> CompatibilidadYuzu { get; set; } = new();
    public static CacheTraducciones Traducciones { get; set; } = null!;

    /// <summary>Último error de la IA; mientras exista no se reintenta sola (solo con Y o al guardar Configuración).</summary>
    public static string? ErrorIA { get; set; }

    /// <summary>Mientras se copian o restauran archivos de un emulador: no se deja cerrar el lanzador.</summary>
    public static string? CopiaEnCurso { get; set; }

    /// <summary>Lo que hay en la carpeta de claves y firmware elegida; null si no hay carpeta o no se ha leído.</summary>
    public static OrigenClavesFirmware? Origen { get; set; }

    public static EstadoFirma? Firma { get; set; }

    public static OrigenClavesFirmware? OrigenPara(EmuladorConfig emulador) =>
        Config.ClavesFirmware.UsarFirma ? Firma?.OrigenPara(Config.ClavesFirmware.Carpeta, emulador) : Origen;

    public static string ResumenFirma
    {
        get
        {
            if (ErrorOrigen is { } error)
                return error;
            if (LeyendoOrigen)
                return "Firma está buscando claves y firmware…";
            if (Firma == null)
                return "Firma está configurada. Pulsa X en Emuladores para consultar.";

            var copias = Firma.Elementos.Where(e => e.Descargado != null).ToList();
            var claves = copias.Count(e => e.EsClaves);
            var firmware = copias.Count(e => e.EsFirmware);
            return claves + firmware == 0
                ? "Firma todavía no tiene claves ni firmware descargados."
                : $"Firma tiene {claves} variantes de claves y {firmware} de firmware.";
        }
    }

    /// <summary>Último error del origen seleccionado.</summary>
    public static string? ErrorOrigen { get; set; }

    /// <summary>Hay una consulta en curso.</summary>
    public static bool LeyendoOrigen { get; set; }

    public static event Action? AlCambiarOrigen;

    public static void AvisarCambioOrigen() => AlCambiarOrigen?.Invoke();

    public static IEnumerable<EmuladorConfig> EmuladoresDisponibles => Config.Emuladores.Where(e => e.Disponible);

    public static void GuardarConfig() => GestorConfiguracion.Guardar(Config);

    public static string TextoCalificacion(Calificacion c) => c switch
    {
        Calificacion.Funciona => "Funciona",
        Calificacion.ConFallos => "Con fallos",
        Calificacion.NoArranca => "No arranca",
        _ => "Sin probar",
    };
}
