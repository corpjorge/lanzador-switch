using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lanzador.Core.Actualizaciones;

namespace Lanzador.Core.ClavesFirmware;

/// <summary>Claves y firmware que tiene un emulador y lo que hay para él en la carpeta elegida.</summary>
public sealed record EstadoClavesFirmware
{
    public bool HayClaves { get; init; }
    public string? VersionClaves { get; init; }
    public bool ClavesComoOrigen { get; init; }

    public int ArchivosFirmware { get; init; }
    public string? VersionFirmware { get; init; }
    public bool FirmwareComoOrigen { get; init; }

    public CopiaOrigen? ClavesOrigen { get; init; }
    public CopiaOrigen? FirmwareOrigen { get; init; }

    /// <summary>El emulador ya tiene todo lo que hay en la carpeta elegida.</summary>
    public bool AlDia => (ClavesOrigen != null || FirmwareOrigen != null)
                         && (ClavesOrigen == null || ClavesComoOrigen) && (FirmwareOrigen == null || FirmwareComoOrigen);

    /// <summary>La carpeta elegida tiene algo más nuevo que lo instalado (o al emulador le falta algo).</summary>
    public bool ClavesNuevas =>
        ClavesOrigen != null && !ClavesComoOrigen && (!HayClaves || InstaladorClavesFirmware.EsMasNueva(ClavesOrigen, VersionClaves));

    public bool FirmwareNuevo =>
        FirmwareOrigen != null && !FirmwareComoOrigen && (ArchivosFirmware == 0 || InstaladorClavesFirmware.EsMasNueva(FirmwareOrigen, VersionFirmware));

    public bool HayNuevas => ClavesNuevas || FirmwareNuevo;

    public string Resumen
    {
        get
        {
            var claves = !HayClaves ? "sin claves" : $"claves {VersionClaves ?? "de versión desconocida"}";
            var firmware = ArchivosFirmware == 0 ? "sin firmware" : $"firmware {VersionFirmware ?? "de versión desconocida"}";
            return $"{char.ToUpper(claves[0])}{claves[1..]} · {firmware}";
        }
    }
}

public sealed record ResultadoClavesFirmware(bool Exito, string Mensaje);

/// <summary>Lo que el lanzador instaló (app\datos\claves-firmware.json), para conocer la versión de lo instalado.</summary>
public sealed class RegistroClavesFirmware
{
    public string? VersionClaves { get; set; }
    public string? Sha256Claves { get; set; }
    public string? VersionFirmware { get; set; }
    public DateTime Fecha { get; set; }
}

/// <summary>
/// Instala en un emulador cerrado las claves y el firmware de la carpeta que eligió el usuario, con respaldo de lo anterior:
/// <list type="bullet">
/// <item>Eden, Citron, Suyu: user\keys\ y user\nand\system\Contents\registered\ (los .nca tal cual).</item>
/// <item>Ryujinx: portable\system\ y portable\bis\system\Contents\registered\&lt;id&gt;.nca\00.</item>
/// </list>
/// Solo escribe en esas dos carpetas; partidas, juegos instalados y configuración no se tocan.
/// </summary>
public static partial class InstaladorClavesFirmware
{
    private const string PrefijoRespaldo = "claves-firmware-";

    private static string ArchivoRegistro => Path.Combine(Rutas.Datos, "claves-firmware.json");

    public static string CarpetaClaves(EmuladorConfig emulador) =>
        emulador.Tipo == TipoEmulador.Ryujinx ? Path.Combine(emulador.CarpetaDatos, "system") : Path.Combine(emulador.CarpetaDatos, "keys");

    public static string CarpetaFirmware(EmuladorConfig emulador) =>
        emulador.Tipo == TipoEmulador.Ryujinx
            ? Path.Combine(emulador.CarpetaDatos, "bis", "system", "Contents", "registered")
            : Path.Combine(emulador.CarpetaDatos, "nand", "system", "Contents", "registered");

    public static EstadoClavesFirmware Leer(EmuladorConfig emulador, OrigenClavesFirmware? origen)
    {
        var claves = origen?.Claves;
        var firmware = origen?.Firmware;
        var registro = Registro().GetValueOrDefault(emulador.Id);

        // Claves: se comparan por sha256 con las de la carpeta elegida y con lo que instaló el lanzador.
        var prod = Path.Combine(CarpetaClaves(emulador), "prod.keys");
        var hayClaves = File.Exists(prod);
        var shaClaves = hayClaves ? Sha256(prod) : null;
        var shaOrigen = claves?.Archivos.GetValueOrDefault("prod.keys")?.Sha256;
        var clavesIguales = hayClaves && shaOrigen != null && shaOrigen.Equals(shaClaves, StringComparison.OrdinalIgnoreCase);
        var versionClaves = clavesIguales ? claves!.Version
            : registro?.Sha256Claves != null && registro.Sha256Claves.Equals(shaClaves, StringComparison.OrdinalIgnoreCase) ? registro.VersionClaves
            : null;

        // Firmware: está al día si cada .nca de la carpeta elegida ya está instalado con ese nombre.
        var carpeta = CarpetaFirmware(emulador);
        var instalados = NombresInstalados(emulador, carpeta);
        var firmwareIgual = false;
        if (firmware != null && instalados.Count > 0)
        {
            try
            {
                using var fuente = FuenteNca.Abrir(firmware);
                var esperados = fuente.Nombres.Select(n => NombreInstalado(emulador, n)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                firmwareIgual = esperados.Count > 0 && esperados.SetEquals(instalados);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException) { }
        }
        // Si no coincide: lo que anotó el emulador al abrirse por última vez y, si no, lo que instaló el lanzador.
        var versionFirmware = firmwareIgual ? firmware!.Version
            : instalados.Count > 0 ? VersionDelRegistroDelEmulador(emulador) ?? registro?.VersionFirmware
            : null;

        return new EstadoClavesFirmware
        {
            HayClaves = hayClaves,
            VersionClaves = versionClaves,
            ClavesComoOrigen = clavesIguales,
            ArchivosFirmware = instalados.Count,
            VersionFirmware = versionFirmware,
            FirmwareComoOrigen = firmwareIgual,
            ClavesOrigen = claves,
            FirmwareOrigen = firmware,
        };
    }

    /// <summary>
    /// Instala las claves y el firmware de la carpeta elegida que el emulador no tenga ya iguales. Respalda las claves
    /// y mueve el firmware anterior a app\datos\respaldos antes de reemplazarlo.
    /// </summary>
    public static ResultadoClavesFirmware Instalar(EmuladorConfig emulador, OrigenClavesFirmware origen, IProgress<string>? progreso = null, bool soloNuevos = false)
    {
        if (Actualizador.Bloqueo(emulador) is { } motivo)
            return new(false, motivo);
        if (string.IsNullOrWhiteSpace(emulador.CarpetaDatos))
            return new(false, $"{emulador.Nombre} no tiene carpeta de datos configurada.");

        var estado = Leer(emulador, origen);
        var instalarClaves = soloNuevos ? estado.ClavesNuevas : estado.ClavesOrigen != null && !estado.ClavesComoOrigen;
        var instalarFirmware = soloNuevos ? estado.FirmwareNuevo : estado.FirmwareOrigen != null && !estado.FirmwareComoOrigen;
        if (!instalarClaves && !instalarFirmware)
            return new(true, estado.ClavesOrigen == null && estado.FirmwareOrigen == null
                ? "La carpeta elegida no tiene claves ni firmware."
                : $"{emulador.Nombre} ya tiene las claves y el firmware de la carpeta elegida.");

        var respaldo = CarpetaRespaldoNueva(emulador.Id);
        var registro = Registro();
        var nuevo = registro.GetValueOrDefault(emulador.Id) ?? new RegistroClavesFirmware();
        var hechos = new List<string>();
        var claves = CarpetaClaves(emulador);
        var firmware = CarpetaFirmware(emulador);
        var firmwareMovido = false;
        try
        {
            Directory.CreateDirectory(respaldo);
            File.WriteAllText(Path.Combine(respaldo, "info.json"), JsonSerializer.Serialize(new
            {
                Emulador = emulador.Id,
                Fecha = DateTime.Now,
                ClavesAnteriores = estado.VersionClaves,
                FirmwareAnterior = estado.VersionFirmware,
            }, GestorConfiguracion.OpcionesJson));

            if (instalarClaves)
            {
                var descarga = estado.ClavesOrigen!;
                progreso?.Report("Instalando las claves…");
                Directory.CreateDirectory(claves);
                foreach (var (nombre, archivo) in descarga.Archivos.Where(a => a.Key.ToLowerInvariant() is "prod.keys" or "title.keys"))
                {
                    if (!File.Exists(archivo.Ruta))
                        throw new FileNotFoundException($"{nombre} ya no está en la carpeta elegida: {archivo.Ruta}");
                    var destino = Path.Combine(claves, nombre);
                    if (File.Exists(destino))
                    {
                        Directory.CreateDirectory(Path.Combine(respaldo, "claves"));
                        File.Copy(destino, Path.Combine(respaldo, "claves", nombre), true);
                    }
                    File.Copy(archivo.Ruta, destino + ".lanzador.tmp", true);
                    File.Move(destino + ".lanzador.tmp", destino, true);
                }
                nuevo.VersionClaves = descarga.Version;
                nuevo.Sha256Claves = Sha256(Path.Combine(claves, "prod.keys"));
                hechos.Add($"claves {descarga.Version}");
            }

            if (instalarFirmware)
            {
                var descarga = estado.FirmwareOrigen!;
                var temporal = firmware + ".lanzador-nuevo";
                if (Directory.Exists(temporal))
                    Directory.Delete(temporal, true);
                Directory.CreateDirectory(temporal);

                using (var fuente = FuenteNca.Abrir(descarga))
                {
                    var nombres = fuente.Nombres;
                    if (nombres.Count == 0)
                        throw new InvalidDataException("el firmware de la carpeta elegida no trae archivos .nca");
                    for (var i = 0; i < nombres.Count; i++)
                    {
                        if (i % 20 == 0)
                            progreso?.Report($"Copiando el firmware {descarga.Version}… {i * 100 / nombres.Count}%");
                        var nombre = NombreInstalado(emulador, nombres[i]);
                        var destino = emulador.Tipo == TipoEmulador.Ryujinx ? Path.Combine(temporal, nombre, "00") : Path.Combine(temporal, nombre);
                        Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
                        fuente.Copiar(i, destino);
                    }
                }

                progreso?.Report("Reemplazando el firmware…");
                if (Directory.Exists(firmware))
                {
                    MoverCarpeta(firmware, Path.Combine(respaldo, "firmware"));
                    firmwareMovido = true;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(firmware)!);
                Directory.Move(temporal, firmware);
                nuevo.VersionFirmware = descarga.Version;
                hechos.Add($"firmware {descarga.Version}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            // Deja todo como estaba.
            try
            {
                if (firmwareMovido && !Directory.Exists(firmware))
                    MoverCarpeta(Path.Combine(respaldo, "firmware"), firmware);
                var clavesRespaldadas = Path.Combine(respaldo, "claves");
                if (Directory.Exists(clavesRespaldadas))
                    foreach (var f in Directory.EnumerateFiles(clavesRespaldadas))
                        File.Copy(f, Path.Combine(claves, Path.GetFileName(f)), true);
                if (Directory.Exists(firmware + ".lanzador-nuevo"))
                    Directory.Delete(firmware + ".lanzador-nuevo", true);
            }
            catch (Exception ex2) when (ex2 is IOException or UnauthorizedAccessException) { }
            return new(false, $"No se pudieron instalar las claves y el firmware en {emulador.Nombre}: {Actualizador.Explicar(ex)}. Se dejó todo como estaba.");
        }

        nuevo.Fecha = DateTime.Now;
        registro[emulador.Id] = nuevo;
        GuardarRegistro(registro);
        LimpiarRespaldos(emulador.Id);
        return new(true, $"{emulador.Nombre}: se instalaron {string.Join(" y ", hechos)}. Lo anterior quedó respaldado.");
    }

    /// <summary>El respaldo más reciente de claves/firmware del emulador, si hay.</summary>
    public static string? UltimoRespaldo(string emuladorId) =>
        Directory.Exists(Rutas.Respaldos)
            ? Directory.EnumerateDirectories(Rutas.Respaldos, $"{PrefijoRespaldo}{emuladorId}-*")
                .Where(d => Directory.Exists(Path.Combine(d, "claves")) || Directory.Exists(Path.Combine(d, "firmware")))
                .OrderByDescending(d => d, StringComparer.Ordinal).FirstOrDefault()
            : null;

    /// <summary>Repone las claves y el firmware del último respaldo.</summary>
    public static ResultadoClavesFirmware VolverAnterior(EmuladorConfig emulador)
    {
        if (Actualizador.Bloqueo(emulador) is { } motivo)
            return new(false, motivo);
        var respaldo = UltimoRespaldo(emulador.Id);
        if (respaldo == null)
            return new(false, $"No hay respaldos de claves o firmware de {emulador.Nombre}.");
        try
        {
            var claves = Path.Combine(respaldo, "claves");
            if (Directory.Exists(claves))
                foreach (var f in Directory.EnumerateFiles(claves))
                    File.Copy(f, Path.Combine(CarpetaClaves(emulador), Path.GetFileName(f)), true);
            var firmware = Path.Combine(respaldo, "firmware");
            if (Directory.Exists(firmware))
            {
                var actual = CarpetaFirmware(emulador);
                var descarte = actual + ".lanzador-descarte";
                if (Directory.Exists(actual))
                    Directory.Move(actual, descarte);
                MoverCarpeta(firmware, actual);
                if (Directory.Exists(descarte))
                    Directory.Delete(descarte, true);
            }
            Directory.Delete(respaldo, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(false, $"No se pudo restaurar: {Actualizador.Explicar(ex)}");
        }

        var registro = Registro();
        registro.Remove(emulador.Id);
        GuardarRegistro(registro);
        return new(true, $"{emulador.Nombre} volvió a las claves y el firmware que tenía antes.");
    }

    /// <summary>
    /// Lo de la carpeta elegida es más nuevo que lo instalado. Una versión sacada de la fecha del archivo solo se
    /// compara con otra fecha (lo que el lanzador instaló antes), nunca con una versión de firmware como 20.1.0.
    /// </summary>
    public static bool EsMasNueva(CopiaOrigen disponible, string? instalada) =>
        instalada != null && disponible.VersionConocida != EsFecha(instalada) && EsMasNueva(disponible.Version, instalada);

    private static bool EsFecha(string version) => Numeros(version) is { Length: > 0 } n && n[0] >= 1000;

    /// <summary>Los .nca de un firmware, venga como zip o como carpeta.</summary>
    private sealed class FuenteNca : IDisposable
    {
        private ZipArchive? _zip;
        private List<ZipArchiveEntry>? _entradas;
        private List<string>? _archivos;

        public IReadOnlyList<string> Nombres { get; private set; } = [];

        public static FuenteNca Abrir(CopiaOrigen firmware)
        {
            var fuente = new FuenteNca();
            if (firmware.EsZip)
            {
                fuente._zip = ZipFile.OpenRead(firmware.Ruta);
                fuente._entradas = fuente._zip.Entries.Where(EsNca).ToList();
                fuente.Nombres = fuente._entradas.Select(e => e.Name).ToList();
            }
            else
            {
                fuente._archivos = Directory.EnumerateFiles(firmware.Ruta, "*.nca").ToList();
                fuente.Nombres = fuente._archivos.Select(Path.GetFileName).ToList()!;
            }
            return fuente;
        }

        public void Copiar(int indice, string destino)
        {
            if (_entradas != null)
                _entradas[indice].ExtractToFile(destino, true);
            else
                File.Copy(_archivos![indice], destino, true);
        }

        public void Dispose() => _zip?.Dispose();
    }

    /// <summary>Compara versiones por números: 22.10.0 &gt; 22.9.0. Si no se conoce la instalada, la disponible no cuenta como "más nueva".</summary>
    public static bool EsMasNueva(string disponible, string? instalada)
    {
        if (instalada == null)
            return false;
        var a = Numeros(disponible);
        var b = Numeros(instalada);
        for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            var x = i < a.Length ? a[i] : 0;
            var y = i < b.Length ? b[i] : 0;
            if (x != y)
                return x > y;
        }
        return false;
    }

    private static int[] Numeros(string v) => RxNumero().Matches(v).Select(m => int.TryParse(m.Value, out var n) ? n : 0).ToArray();

    internal static bool EsNca(ZipArchiveEntry e) =>
        e.Name.EndsWith(".nca", StringComparison.OrdinalIgnoreCase) && e.FullName == e.Name;

    /// <summary>Nombre que lleva cada .nca instalado: tal cual en yuzu; en Ryujinx una carpeta "&lt;id&gt;.nca" (sin ".cnmt").</summary>
    private static string NombreInstalado(EmuladorConfig emulador, string nombreZip) =>
        emulador.Tipo == TipoEmulador.Ryujinx && nombreZip.EndsWith(".cnmt.nca", StringComparison.OrdinalIgnoreCase)
            ? nombreZip[..^9] + ".nca"
            : nombreZip;

    private static HashSet<string> NombresInstalados(EmuladorConfig emulador, string carpeta)
    {
        if (!Directory.Exists(carpeta))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entradas = emulador.Tipo == TipoEmulador.Ryujinx
            ? Directory.EnumerateDirectories(carpeta, "*.nca").Where(d => File.Exists(Path.Combine(d, "00")))
            : Directory.EnumerateFiles(carpeta, "*.nca");
        return entradas.Select(Path.GetFileName).ToHashSet(StringComparer.OrdinalIgnoreCase)!;
    }

    /// <summary>Versión de firmware que anotó el propio emulador en su registro la última vez que se abrió.</summary>
    private static string? VersionDelRegistroDelEmulador(EmuladorConfig emulador)
    {
        try
        {
            if (emulador.Tipo == TipoEmulador.Ryujinx)
            {
                var logs = Path.Combine(emulador.CarpetaDatos, "Logs");
                if (!Directory.Exists(logs))
                    return null;
                foreach (var log in Directory.EnumerateFiles(logs, "*.log").OrderByDescending(File.GetLastWriteTimeUtc).Take(5))
                    if (BuscarEnTexto(log, RxFirmwareRyujinx()) is { } v)
                        return v;
                return null;
            }
            var carpetaLog = Path.Combine(emulador.CarpetaDatos, "log");
            if (!Directory.Exists(carpetaLog))
                return null;
            foreach (var log in Directory.EnumerateFiles(carpetaLog, "*.txt").OrderByDescending(File.GetLastWriteTimeUtc))
                if (BuscarEnTexto(log, RxFirmwareYuzu()) is { } v)
                    return v;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return null;
    }

    private static string? BuscarEnTexto(string archivo, Regex patron)
    {
        using var lector = new StreamReader(new FileStream(archivo, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete));
        for (var i = 0; i < 3000 && lector.ReadLine() is { } linea; i++)
            if (patron.Match(linea) is { Success: true } m)
                return m.Groups[1].Value;
        return null;
    }

    private static string? Sha256(string archivo)
    {
        try
        {
            using var flujo = new FileStream(archivo, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return Convert.ToHexStringLower(SHA256.HashData(flujo));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Mueve una carpeta; si está en otro disco, la copia y borra la original.</summary>
    private static void MoverCarpeta(string origen, string destino)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
        if (string.Equals(Path.GetPathRoot(Path.GetFullPath(origen)), Path.GetPathRoot(Path.GetFullPath(destino)), StringComparison.OrdinalIgnoreCase))
        {
            Directory.Move(origen, destino);
            return;
        }
        foreach (var archivo in Directory.EnumerateFiles(origen, "*", SearchOption.AllDirectories))
        {
            var relativo = Path.GetRelativePath(origen, archivo);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(destino, relativo))!);
            File.Copy(archivo, Path.Combine(destino, relativo), true);
        }
        Directory.Delete(origen, true);
    }

    public static Dictionary<string, RegistroClavesFirmware> Registro()
    {
        try
        {
            if (File.Exists(ArchivoRegistro))
                return new Dictionary<string, RegistroClavesFirmware>(
                    JsonSerializer.Deserialize<Dictionary<string, RegistroClavesFirmware>>(File.ReadAllText(ArchivoRegistro), GestorConfiguracion.OpcionesJson) ?? new(),
                    StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException) { }
        catch (IOException) { }
        return new Dictionary<string, RegistroClavesFirmware>(StringComparer.OrdinalIgnoreCase);
    }

    private static void GuardarRegistro(Dictionary<string, RegistroClavesFirmware> registro)
    {
        Directory.CreateDirectory(Rutas.Datos);
        File.WriteAllText(ArchivoRegistro + ".tmp", JsonSerializer.Serialize(registro, GestorConfiguracion.OpcionesJson));
        File.Move(ArchivoRegistro + ".tmp", ArchivoRegistro, true);
    }

    private static string CarpetaRespaldoNueva(string emuladorId)
    {
        var baseNombre = Path.Combine(Rutas.Respaldos, $"{PrefijoRespaldo}{emuladorId}-{DateTime.Now:yyyyMMdd-HHmmss}");
        var carpeta = baseNombre;
        for (var i = 2; Directory.Exists(carpeta); i++)
            carpeta = $"{baseNombre}-{i}";
        return carpeta;
    }

    /// <summary>El firmware ocupa cientos de MB: se conserva solo el respaldo más reciente por emulador.</summary>
    private static void LimpiarRespaldos(string emuladorId)
    {
        if (!Directory.Exists(Rutas.Respaldos))
            return;
        foreach (var viejo in Directory.EnumerateDirectories(Rutas.Respaldos, $"{PrefijoRespaldo}{emuladorId}-*")
                     .OrderByDescending(d => d, StringComparer.Ordinal).Skip(1))
            try
            {
                Directory.Delete(viejo, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    [GeneratedRegex(@"\d+")]
    private static partial Regex RxNumero();

    [GeneratedRegex(@"Installed firmware:\s*(\d+\.\d+\.\d+)")]
    private static partial Regex RxFirmwareYuzu();

    [GeneratedRegex(@"Using Firmware Version:\s*(\d+\.\d+\.\d+)")]
    private static partial Regex RxFirmwareRyujinx();
}
