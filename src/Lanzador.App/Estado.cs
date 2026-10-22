using Lanzador.App.Modelos;
using Lanzador.Core;
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
