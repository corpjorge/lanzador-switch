using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lanzador.Core;
using Lanzador.Core.Actualizaciones;
using Lanzador.Core.IA;
using Lanzador.Core.Trucos;

namespace Lanzador.App.Modelos;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Fijar<T>(ref T campo, T valor, [CallerMemberName] string? propiedad = null)
    {
        if (EqualityComparer<T>.Default.Equals(campo, valor))
            return false;
        campo = valor;
        Avisar(propiedad);
        return true;
    }

    protected void Avisar([CallerMemberName] string? propiedad = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propiedad));
}

public sealed class JuegoVM(Juego juego) : Observable
{
    private ImageSource? _icono;
    private bool _seleccionado;
    private EntradaCompatibilidad? _compat;

    public Juego Juego { get; } = juego;
    public string Nombre => Juego.Nombre;
    public string Iniciales => string.Concat(Juego.Nombre.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(p => char.ToUpperInvariant(p[0])));

    public ImageSource? Icono { get => _icono; set { if (Fijar(ref _icono, value)) Avisar(nameof(SinIcono)); } }
    public bool SinIcono => _icono == null;

    /// <summary>El archivo es .nsz o .xcz: no se puede jugar hasta convertirlo.</summary>
    public bool Comprimido => Juego.Comprimido;

    /// <summary>"NSZ" o "XCZ", para la etiqueta de la carátula.</summary>
    public string Formato => Path.GetExtension(Juego.RutaArchivo).TrimStart('.').ToUpperInvariant();
    public bool Seleccionado { get => _seleccionado; set => Fijar(ref _seleccionado, value); }
    public EntradaCompatibilidad? Compat { get => _compat; set => Fijar(ref _compat, value); }

    /// <summary>Lista de Citron Neo.</summary>
    public CompatibilidadYuzu? CompatCitron { get; set; }

    /// <summary>Lista de yuzu (2024), referencia para Eden y Suyu.</summary>
    public CompatibilidadYuzu? CompatYuzu { get; set; }

    public void Refrescar() => Avisar(string.Empty);
}

public sealed class TrucoVM : Observable
{
    private string _nombre = "";
    private string? _descripcion;
    private string? _categoria;
    private bool _activoDeseado;
    private bool _seleccionado;

    public bool EsEncabezado { get; init; }
    public string Titulo { get; init; } = "";

    public TrucoInstalado? Instalado { get; init; }
    public TrucoDisponible? Disponible { get; init; }

    public string NombreOriginal => Instalado?.Nombre ?? Disponible?.NombreOriginal ?? "";
    public IReadOnlyList<string> Lineas => (IReadOnlyList<string>?)Instalado?.Lineas ?? Disponible?.Lineas ?? [];
    public string? Botones => Instalado?.Botones ?? Disponible?.Botones;
    public bool EsMaestro => Disponible?.EsMaestro ?? false;

    public string Nombre { get => _nombre; set => Fijar(ref _nombre, value); }
    public string? Descripcion { get => _descripcion; set { if (Fijar(ref _descripcion, value)) Avisar(nameof(TieneDescripcion)); } }
    public bool TieneDescripcion => !string.IsNullOrWhiteSpace(_descripcion);
    public string? Categoria { get => _categoria; set => Fijar(ref _categoria, value); }
    public bool Traducido { get; set; }

    public string Fuentes => Instalado != null ? "Instalado" : string.Join(" · ", Disponible?.Fuentes ?? []);
    public bool ActivoActual => Instalado?.Activo ?? false;

    public bool ActivoDeseado
    {
        get => _activoDeseado;
        set { if (Fijar(ref _activoDeseado, value)) Avisar(nameof(Pendiente)); }
    }

    public bool Pendiente => ActivoDeseado != ActivoActual;
    public bool Seleccionado { get => _seleccionado; set => Fijar(ref _seleccionado, value); }

    /// <summary>Nombre con que se instala: traducido si se pudo, con los botones al final si el nombre no los menciona.</summary>
    public string NombreParaInstalar()
    {
        var nombre = Traducido ? Nombre : TraductorIA.LimpiarNombre(NombreOriginal);
        if (EsMaestro)
            nombre = "Código maestro" + (nombre.Length > 0 && nombre != "Código maestro" ? $" - {nombre}" : "");
        if (Botones != null && !nombre.Contains("mantener", StringComparison.OrdinalIgnoreCase) && !nombre.Contains("mantén", StringComparison.OrdinalIgnoreCase))
            nombre += $" ({Botones})";
        return nombre;
    }
}

/// <summary>Un emulador en la pantalla Emuladores: versión instalada, la publicada y el progreso de la actualización.</summary>
public sealed class EmuladorVM(EmuladorConfig emulador, IFuenteActualizacion? fuente) : Observable
{
    private bool _seleccionado;
    private double _progreso;
    private string? _textoProgreso;

    public EmuladorConfig Emulador { get; } = emulador;
    public IFuenteActualizacion? Fuente { get; set; } = fuente;
    public string Nombre => Emulador.Nombre;

    /// <summary>Icono sacado del ejecutable (null si no está instalado: se muestra la inicial).</summary>
    public ImageSource? Icono => Lanzador.App.Tema.IconosEmulador.De(Emulador);
    public bool SinIcono => Icono == null;
    public string Inicial => Nombre.Length > 0 ? Nombre[..1] : "?";

    public VersionLocal? Instalada { get; set; }
    public VersionDisponible? Disponible { get; set; }
    public string? ErrorConsulta { get; set; }
    public bool Consultando { get; set; }
    public bool Ocupado { get; set; }
    public EtapaActualizacion? Etapa { get; set; }
    public InfoRespaldo? Respaldo { get; set; }
    public string? Resultado { get; set; }
    public bool ResultadoError { get; set; }

    public bool Seleccionado { get => _seleccionado; set => Fijar(ref _seleccionado, value); }
    public double Progreso { get => _progreso; set => Fijar(ref _progreso, value); }
    public string? TextoProgreso { get => _textoProgreso; set => Fijar(ref _textoProgreso, value); }

    public bool Instalado => Emulador.Instalado;
    public bool SoloPagina => Fuente is { PermiteDescarga: false };
    public bool AlDia => Instalado && Disponible != null && VersionesInstaladas.MismaVersion(Instalada?.Version, Disponible.Version);
    public bool HayActualizacion => Disponible != null && !AlDia;
    public bool PuedeDescargar => Fuente is { PermiteDescarga: true } && Disponible is { Url: not null };
    public bool TieneResultado => Resultado != null;

    public string TextoFuente => Fuente == null ? "Sin actualizador" : $"Fuente: {Fuente.Descripcion}";

    public string TextoInstalada => !Instalado ? "No instalado"
        : Instalada?.Version is { } v ? v + (Instalada.Canal is { Length: > 0 } c ? $" · {NombreCanal(c)}" : "")
        : Instalada == null ? "Leyendo…" : "Versión desconocida";

    public string DetalleInstalada => !Instalado ? Path.GetDirectoryName(Emulador.RutaExe) ?? "" : Instalada?.Origen ?? "";

    public string TextoDisponible => SoloPagina ? "En su página"
        : Fuente == null ? "—"
        : Consultando ? "Buscando…"
        : ErrorConsulta != null ? "No se pudo consultar"
        : Disponible?.Version ?? "—";

    public string DetalleDisponible
    {
        get
        {
            if (SoloPagina)
                return "Se actualiza desde su página de descargas";
            if (ErrorConsulta != null)
                return ErrorConsulta;
            if (Disponible == null)
                return "";
            var partes = new List<string> { Disponible.Titulo };
            if (Disponible.Tamano is { } t)
                partes.Add($"{t / 1048576.0:0.0} MB");
            if (Disponible.Fecha is { } f)
                partes.Add(f.LocalDateTime.ToString("dd/MM/yyyy HH:mm"));
            return string.Join(" · ", partes.Where(p => p.Length > 0));
        }
    }

    /// <summary>Lo publicado, en una línea bajo la versión instalada.</summary>
    public string TextoDisponibleCompleto => "Disponible: " + TextoDisponible + (DetalleDisponible.Length > 0 ? $" · {DetalleDisponible}" : "");

    /// <summary>Etiqueta de estado y su color: ok, nuevo, aviso, error o neutro.</summary>
    public string TextoEstado => Ocupado ? "Actualizando…"
        : Consultando ? "Buscando…"
        : SoloPagina ? "Desde su página"
        : Fuente == null ? "Sin actualizador"
        : ErrorConsulta != null ? "Sin conexión"
        : !Instalado ? "No instalado"
        : AlDia ? "Al día"
        : Instalada?.Version == null ? "Versión desconocida"
        : HayActualizacion ? "Actualización disponible"
        : "—";

    public string NivelEstado => Ocupado ? "nuevo"
        : Consultando || SoloPagina || Fuente == null || !Instalado && ErrorConsulta == null ? "neutro"
        : ErrorConsulta != null ? "error"
        : AlDia ? "ok"
        : Instalada?.Version == null ? "aviso"
        : "nuevo";

    /// <summary>Lo que hace A sobre este emulador.</summary>
    public string TextoAccion => Ocupado ? ""
        : SoloPagina ? "Abrir la página de descargas"
        : Fuente == null || Consultando ? ""
        : ErrorConsulta != null ? "Reintentar"
        : Disponible == null ? ""
        : !Instalado ? $"Instalar {Disponible.Version}"
        : AlDia ? "Reinstalar"
        : $"Actualizar a {Disponible.Version}";

    public bool TieneAccion => TextoAccion.Length > 0;

    /// <summary>Claves y firmware instalados y lo que hay en la carpeta elegida; null mientras se lee.</summary>
    public Lanzador.Core.ClavesFirmware.EstadoClavesFirmware? ClavesFirmware { get; set; }

    public string TextoClavesFirmware => !Instalado ? "Se instalarán al descargar el emulador"
        : ClavesFirmware == null ? "Leyendo…"
        : ClavesFirmware.Resumen;

    public string DetalleClavesFirmware
    {
        get
        {
            if (!Instalado || ClavesFirmware is not { } c)
                return "";
            var version = c.FirmwareOrigen?.Version ?? c.ClavesOrigen?.Version;
            if (version == null)
                return Estado.Origen == null ? "Elige tu carpeta en Configuración → Claves y firmware" : "Tu carpeta no tiene claves ni firmware";
            if (c.AlDia)
                return $"Iguales a los de tu carpeta ({version})";
            if (c.HayNuevas)
                return $"Tu carpeta tiene {version}: se instalan con el emulador cerrado";
            return $"Distintos a los de tu carpeta ({version}); se pueden instalar desde Opciones (Y)";
        }
    }

    public string NivelClaves => !Instalado || ClavesFirmware == null ? "neutro"
        : ClavesFirmware.AlDia ? "ok"
        : ClavesFirmware.HayNuevas || !ClavesFirmware.HayClaves || ClavesFirmware.ArchivosFirmware == 0 ? "nuevo"
        : "aviso";

    public static string NombreCanal(string canal) => canal switch
    {
        "nightly" => "Nightly",
        "estable" => "Estable",
        "canary" => "Canary",
        _ => canal,
    };

    public void Refrescar() => Avisar(string.Empty);
}

/// <summary>Un jugador en la pantalla Controles: qué mando y qué tipo de mando usa.</summary>
public sealed class JugadorVM(int numero) : Observable
{
    private bool _seleccionado;

    public int Numero { get; } = numero;
    public AsignacionJugador? Asignacion { get; set; }
    public bool MandoConectado { get; set; }
    public bool Seleccionado { get => _seleccionado; set => Fijar(ref _seleccionado, value); }

    public string Titulo => $"Jugador {Numero}";
    public string TextoMando => Asignacion == null ? "Ninguno"
        : (Asignacion.Nombre.Length > 0 ? Asignacion.Nombre : Lanzador.Core.Controles.Controles.NombreMando(Asignacion.Guid))
          + (Asignacion.Puerto > 0 ? $" ({Asignacion.Puerto + 1}º)" : "");
    public string TextoTipo => Asignacion?.Tipo ?? "";
    public bool TieneMando => Asignacion != null;
    public string TextoConexion => Asignacion == null ? "" : MandoConectado ? "conectado" : "apagado";

    /// <summary>"También en J1" si otro jugador usa el mismo mando (se permite, como en los emuladores).</summary>
    public string Compartido { get; set; } = "";
    public bool TieneCompartido => Compartido.Length > 0;

    public void Refrescar() => Avisar(string.Empty);
}

/// <summary>Estado de un emulador frente a los mandos elegidos en el lanzador.</summary>
public sealed class EstadoControlesVM(EmuladorConfig emulador) : Observable
{
    public EmuladorConfig Emulador { get; } = emulador;
    public string Texto { get; set; } = "";
    public string Nivel { get; set; } = "neutro";

    public void Refrescar() => Avisar(string.Empty);
}

/// <summary>Un juego comprimido (.nsz o .xcz) en la pantalla de conversión.</summary>
public sealed class ConversionVM(Lanzador.Core.Conversion.ArchivoComprimido archivo) : Observable
{
    private bool _seleccionado;
    private double _progreso;
    private string? _textoProgreso;

    public Lanzador.Core.Conversion.ArchivoComprimido Archivo { get; set; } = archivo;
    public bool Ocupado { get; set; }

    /// <summary>En cola al convertir todos.</summary>
    public bool EnCola { get; set; }

    /// <summary>El .nsp existe y se comprobó el sha256 de todos sus NCA en esta sesión: se puede borrar el .nsz.</summary>
    public bool Verificado { get; set; }

    /// <summary>El .nsz original ya se envió a la papelera.</summary>
    public bool Borrado { get; set; }
    public string? Resultado { get; set; }
    public bool ResultadoError { get; set; }

    public bool Seleccionado { get => _seleccionado; set => Fijar(ref _seleccionado, value); }
    public double Progreso { get => _progreso; set => Fijar(ref _progreso, value); }
    public string? TextoProgreso { get => _textoProgreso; set => Fijar(ref _textoProgreso, value); }

    public string Nombre => Archivo.Nombre;
    public string Carpeta => Path.GetDirectoryName(Archivo.Ruta) ?? "";
    public Lanzador.Core.Conversion.EstadoComprimido Estado => Archivo.Estado;
    public bool Convertible => !Borrado && !Verificado && Estado == Lanzador.Core.Conversion.EstadoComprimido.Convertible;
    public bool PorVerificar => !Borrado && !Verificado && Estado == Lanzador.Core.Conversion.EstadoComprimido.YaConvertido;
    public bool PuedeBorrar => Verificado && !Borrado && !Ocupado;
    public bool TieneResultado => Resultado != null && !Ocupado;

    /// <summary>"Juego", "Actualización" o "DLC" (por el Title ID del nombre); "Archivo" si no lo trae.</summary>
    public string Tipo => Lanzador.Core.ContenidoExterno.TipoDeArchivo(Archivo.Ruta) ?? "Archivo";

    /// <summary>Es una actualización o un DLC: los emuladores lo cargan desde sus carpetas de contenido.</summary>
    public bool EsContenido => Tipo is "Actualización" or "DLC";

    public string TextoTamano
    {
        get
        {
            var extension = Path.GetExtension(Archivo.Ruta).TrimStart('.').ToUpperInvariant();
            var origen = $"{Tipo}  ·  {extension} {Lanzador.Core.Conversion.ConversorNsz.Tamano(Archivo.Tamano)}";
            return Archivo.TamanoDestino is { } destino ? $"{origen}  →  NSP {Lanzador.Core.Conversion.ConversorNsz.Tamano(destino)}" : origen;
        }
    }

    public string TextoEstado => Borrado ? "Original borrado"
        : Ocupado ? Verificado || PorVerificar ? "Verificando…" : "Convirtiendo…"
        : Verificado ? "Convertido y verificado"
        : EnCola ? "En cola"
        : ResultadoError ? "Falló"
        : Estado switch
        {
            Lanzador.Core.Conversion.EstadoComprimido.Convertible => "Se puede convertir",
            Lanzador.Core.Conversion.EstadoComprimido.YaConvertido => "Ya tiene su NSP",
            Lanzador.Core.Conversion.EstadoComprimido.NoCompatible => "No compatible",
            _ => "No se pudo leer",
        };

    /// <summary>Color de la etiqueta: ok, nuevo, aviso, error o neutro.</summary>
    public string Nivel => Borrado ? "neutro"
        : Ocupado || EnCola ? "nuevo"
        : Verificado ? "ok"
        : ResultadoError ? "error"
        : Estado switch
        {
            Lanzador.Core.Conversion.EstadoComprimido.Convertible => "nuevo",
            Lanzador.Core.Conversion.EstadoComprimido.YaConvertido => "aviso",
            Lanzador.Core.Conversion.EstadoComprimido.Danado => "error",
            _ => "neutro",
        };

    /// <summary>Lo que hace A sobre este archivo.</summary>
    public string TextoAccion => Borrado || Ocupado || EnCola ? ""
        : Verificado ? "Borrar el NSZ"
        : Convertible ? "Convertir"
        : PorVerificar ? "Verificar el NSP"
        : "";

    public bool TieneAccion => TextoAccion.Length > 0;
    public string Detalle => TieneResultado ? Resultado! : Archivo.Detalle;

    public void Refrescar() => Avisar(string.Empty);
}

public static class Imagen
{
    public static ImageSource? Cargar(string? ruta, int ancho)
    {
        if (ruta == null || !File.Exists(ruta))
            return null;
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.DecodePixelWidth = ancho;
            bitmap.UriSource = new Uri(ruta);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }
}
