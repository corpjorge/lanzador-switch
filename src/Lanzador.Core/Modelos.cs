using System.Text.Json.Serialization;

namespace Lanzador.Core;

/// <summary>Familia del emulador: decide dónde guarda trucos y configuración.</summary>
public enum TipoEmulador
{
    /// <summary>Eden, Citron, Suyu y demás derivados de yuzu.</summary>
    Yuzu,
    Ryujinx,
}

public enum Calificacion
{
    SinProbar,
    Funciona,
    ConFallos,
    NoArranca,
}

public sealed class EmuladorConfig
{
    public string Id { get; set; } = "";
    public string Nombre { get; set; } = "";
    public TipoEmulador Tipo { get; set; }
    public string RutaExe { get; set; } = "";

    /// <summary>Argumentos para abrir un juego; <c>{juego}</c> se reemplaza por la ruta del archivo.</summary>
    public string Argumentos { get; set; } = "";

    /// <summary>Carpeta de datos del emulador: <c>user</c> en Eden, <c>portable</c> en Ryujinx.</summary>
    public string CarpetaDatos { get; set; } = "";

    public bool Activo { get; set; } = true;

    [JsonIgnore] public bool Instalado => File.Exists(RutaExe);
    [JsonIgnore] public bool Disponible => Activo && Instalado;
    [JsonIgnore] public string NombreProceso => Path.GetFileNameWithoutExtension(RutaExe);
}

public sealed class PreferenciasJuego
{
    public string? EmuladorPreferido { get; set; }
    public Dictionary<string, Calificacion> Calificaciones { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Cuándo se abrió por última vez desde el lanzador (para «Continuar jugando»).</summary>
    public DateTimeOffset? UltimaVez { get; set; }

    /// <summary>Último emulador con que se abrió.</summary>
    public string? UltimoEmulador { get; set; }

    public bool Favorito { get; set; }

    /// <summary>
    /// Trucos que estaban activos al pulsar «Apagar todos», por "emulador|BuildID" → claves de los trucos, para
    /// volver a encender exactamente esos.
    /// </summary>
    public Dictionary<string, List<string>> TrucosGuardados { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Calificacion CalificacionDe(string emuladorId) =>
        Calificaciones.TryGetValue(emuladorId, out var c) ? c : Calificacion.SinProbar;
}

public sealed class Configuracion
{
    public List<EmuladorConfig> Emuladores { get; set; } = new();
    public List<string> CarpetasJuegos { get; set; } = new();

    /// <summary>"claude", "codex" o "ninguno".</summary>
    public string MotorIA { get; set; } = "claude";

    /// <summary>Modelo a usar; vacío = el predeterminado de cada CLI.</summary>
    public string ModeloIA { get; set; } = "";

    public bool PantallaCompleta { get; set; } = true;

    /// <summary>Sonidos de la interfaz al moverse y elegir.</summary>
    public bool SonidosActivados { get; set; } = true;

    /// <summary>Volumen de los sonidos de la interfaz, de 0 a 100.</summary>
    public int VolumenSonido { get; set; } = 60;

    /// <summary>Sin animaciones (para quien se marea o prefiere una interfaz quieta).</summary>
    public bool ReducirMovimiento { get; set; }

    /// <summary>Intro animada «Switch» al abrir el lanzador.</summary>
    public bool MostrarIntro { get; set; } = true;

    /// <summary>Tecla que muestra u oculta la ventana de trucos durante el juego ("F7"; vacía = desactivada).</summary>
    public string TeclaTrucos { get; set; } = "F7";

    public OpcionesActualizacion Actualizaciones { get; set; } = new();

    /// <summary>Emulador donde se configuran los mandos; desde él se copian a los demás (y a los que se instalen).</summary>
    public string OrigenControles { get; set; } = "eden";

    /// <summary>Mando de cada jugador elegido en el lanzador; se escribe en todos los emuladores.</summary>
    public List<AsignacionJugador> Jugadores { get; set; } = new();

    /// <summary>Mandos que el lanzador ya vio (o que tenía algún emulador): se pueden elegir aunque estén apagados.</summary>
    public List<MandoConocido> MandosConocidos { get; set; } = new();

    public Dictionary<string, PreferenciasJuego> Juegos { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public PreferenciasJuego PreferenciasDe(string clave)
    {
        if (!Juegos.TryGetValue(clave, out var p))
        {
            p = new PreferenciasJuego();
            Juegos[clave] = p;
        }
        return p;
    }

    public EmuladorConfig? Emulador(string? id) =>
        Emuladores.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Qué mando usa un jugador: GUID de SDL (sin CRC) y su orden entre mandos iguales.</summary>
/// <summary>Un mando ya visto, para poder asignarlo a un jugador aunque ahora esté apagado.</summary>
public sealed class MandoConocido
{
    public string Guid { get; set; } = "";
    public int Puerto { get; set; }
    public string Nombre { get; set; } = "";
    public string? Mapeo { get; set; }
    public DateTimeOffset? VistoUltimaVez { get; set; }
}

public sealed class AsignacionJugador
{
    /// <summary>1 a 8.</summary>
    public int Jugador { get; set; }
    public string Guid { get; set; } = "";
    public int Puerto { get; set; }
    public string Tipo { get; set; } = "Pro Controller";
    public string Nombre { get; set; } = "";

    /// <summary>Mapeo de SDL del mando (solo hace falta para mandos que no son XInput ni HIDAPI).</summary>
    public string? Mapeo { get; set; }
}

/// <summary>De dónde se descargan las actualizaciones de cada emulador.</summary>
public sealed class OpcionesActualizacion
{
    /// <summary>"nightly" o "estable".</summary>
    public string CanalEden { get; set; } = "nightly";

    /// <summary>"clang-cl" o "clangtron".</summary>
    public string VarianteCitron { get; set; } = "clang-cl";
}

public sealed record ArchivoActualizacion(string Ruta, int? Version, string? VersionTexto);

public sealed class Juego
{
    /// <summary>Title ID del juego base en mayúsculas; vacío si el archivo no lo indica.</summary>
    public string TitleId { get; init; } = "";
    public string Nombre { get; set; } = "";
    public string RutaArchivo { get; init; } = "";

    /// <summary>Versión incluida en el propio archivo (p. ej. <c>[v983040]</c> en un archivo con la actualización integrada).</summary>
    public int? VersionIncluida { get; init; }

    /// <summary>Versión en texto si el archivo la indica así (p. ej. <c>[v1.3.0]</c>).</summary>
    public string? VersionTexto { get; init; }

    public List<ArchivoActualizacion> Actualizaciones { get; } = new();

    public bool TieneTitleId => TitleId.Length == 16;

    /// <summary>Clave para guardar preferencias: el Title ID o, si no hay, la ruta.</summary>
    public string Clave => TieneTitleId ? TitleId : RutaArchivo;
}
