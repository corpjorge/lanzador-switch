using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace Lanzador.Core.Actualizaciones;

public enum EtapaActualizacion
{
    Descargando,
    Verificando,
    Extrayendo,
    Respaldando,
    Copiando,
    Restaurando,
}

public sealed record ProgresoActualizacion(EtapaActualizacion Etapa, double Fraccion, string Texto);

public sealed record ResultadoActualizacion(bool Exito, string Mensaje, int ArchivosCopiados = 0, string? CarpetaRespaldo = null);

/// <summary>Respaldo de los archivos que reemplazó una actualización (app\datos\respaldos\&lt;emu&gt;-&lt;fecha&gt;).</summary>
public sealed class InfoRespaldo
{
    public string Emulador { get; set; } = "";
    public DateTime Fecha { get; set; }
    public string? VersionAnterior { get; set; }
    public string? CanalAnterior { get; set; }
    public string VersionNueva { get; set; } = "";

    /// <summary>Rutas relativas que existían y se reemplazaron; la copia está en "archivos".</summary>
    public List<string> Reemplazados { get; set; } = new();

    /// <summary>Rutas relativas que la actualización creó (se borran al volver atrás).</summary>
    public List<string> Nuevos { get; set; } = new();

    public bool Completo { get; set; }

    [System.Text.Json.Serialization.JsonIgnore] public string Carpeta { get; set; } = "";
}

/// <summary>
/// Actualiza un emulador en su sitio: descarga a una carpeta temporal, verifica el sha256, extrae, respalda los archivos que
/// va a reemplazar y copia encima. Nunca escribe, mueve ni borra nada dentro de las carpetas de datos (user, portable).
/// </summary>
public sealed class Actualizador(HttpClient? cliente = null)
{
    /// <summary>Respaldos que se conservan por emulador.</summary>
    public const int RespaldosPorEmulador = 3;

    private static readonly TimeSpan SinDatosMaximo = TimeSpan.FromSeconds(60);
    private readonly HttpClient _cliente = cliente ?? Http.Cliente;

    /// <summary>Dónde se descarga y extrae; se borra al terminar.</summary>
    public string CarpetaTemporal { get; init; } = Path.Combine(Path.GetTempPath(), "LanzadorSwitch");

    public static string CarpetaInstalacion(EmuladorConfig emulador) => Path.GetDirectoryName(Path.GetFullPath(emulador.RutaExe))!;

    /// <summary>Carpeta que activa el modo portable: user (Eden, Citron, Suyu) o portable (Ryujinx), junto al ejecutable.</summary>
    public static string CarpetaPortable(EmuladorConfig emulador) =>
        Path.Combine(CarpetaInstalacion(emulador), emulador.Tipo == TipoEmulador.Ryujinx ? "portable" : "user");

    /// <summary>Nombres de carpeta (primer nivel de la instalación) que nunca se tocan.</summary>
    public static HashSet<string> CarpetasProtegidas(EmuladorConfig emulador)
    {
        var protegidas = new HashSet<string>(["user", "portable"], StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(emulador.CarpetaDatos))
        {
            var relativa = Path.GetRelativePath(CarpetaInstalacion(emulador), Path.GetFullPath(emulador.CarpetaDatos));
            var primera = relativa.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
            if (!Path.IsPathRooted(relativa) && primera is not (".." or "." or ""))
                protegidas.Add(primera);
        }
        return protegidas;
    }

    /// <summary>Nombre del programa abierto desde la carpeta del emulador, o null si está cerrado.</summary>
    public static string? EmuladorAbierto(EmuladorConfig emulador)
    {
        var carpeta = CarpetaInstalacion(emulador);
        var nombres = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { emulador.NombreProceso };
        if (Directory.Exists(carpeta))
            foreach (var exe in Directory.EnumerateFiles(carpeta, "*.exe"))
                nombres.Add(Path.GetFileNameWithoutExtension(exe));

        foreach (var nombre in nombres)
            foreach (var proceso in Process.GetProcessesByName(nombre))
                using (proceso)
                {
                    string? ruta = null;
                    try
                    {
                        ruta = proceso.MainModule?.FileName;
                    }
                    catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException) { }
                    // Si no se puede leer su ruta se asume que es el emulador: mejor no actualizar que dañarlo.
                    if (ruta == null || EstaDentro(ruta, carpeta))
                        return nombre + ".exe";
                }
        return null;
    }

    public static string? Bloqueo(EmuladorConfig emulador) =>
        EmuladorAbierto(emulador) is { } abierto ? $"Cierra {emulador.Nombre} antes de continuar ({abierto} está abierto)." : null;

    public async Task<ResultadoActualizacion> ActualizarAsync(EmuladorConfig emulador, VersionDisponible version,
        IProgress<ProgresoActualizacion>? progreso, CancellationToken ct)
    {
        if (version.Url == null)
            return new(false, "Esta fuente no tiene un archivo para descargar.");
        if (version.Sha256 == null)
            return new(false, "La fuente no publica el sha256 del archivo; no se instala sin poder verificarlo.");
        if (Bloqueo(emulador) is { } motivo)
            return new(false, motivo);

        var instalacion = CarpetaInstalacion(emulador);
        LimpiarTemporalesViejos();
        var trabajo = Path.Combine(CarpetaTemporal, $"{emulador.Id}-{DateTime.Now:yyyyMMdd-HHmmss}-{Random.Shared.Next(1000, 10000)}");
        Directory.CreateDirectory(trabajo);
        try
        {
            if (version.Tamano is { } tamano && Directory.Exists(Path.GetPathRoot(instalacion))
                && new DriveInfo(Path.GetPathRoot(instalacion)!).AvailableFreeSpace < tamano * 4)
                return new(false, $"No hay espacio suficiente en {Path.GetPathRoot(instalacion)} (hacen falta unos {tamano * 4 / 1048576} MB).");

            // 1. Descarga y verificación.
            var zip = Path.Combine(trabajo, "descarga.zip");
            var hash = await DescargarAsync(version.Url, zip, version.Tamano, progreso, ct);
            progreso?.Report(new(EtapaActualizacion.Verificando, 1, "Verificando el sha256…"));
            if (!hash.Equals(version.Sha256, StringComparison.OrdinalIgnoreCase))
                return new(false, $"El archivo descargado no coincide con el sha256 publicado (esperado {version.Sha256[..12]}…, llegó {hash[..12]}…). No se instaló nada.");
            if (version.Tamano is { } esperado && new FileInfo(zip).Length != esperado)
                return new(false, "El archivo descargado no tiene el tamaño publicado. No se instaló nada.");

            // 2. Extracción en otra carpeta temporal.
            progreso?.Report(new(EtapaActualizacion.Extrayendo, 0, "Extrayendo…"));
            var extraido = Path.Combine(trabajo, "extraido");
            await Task.Run(() => ZipFile.ExtractToDirectory(zip, extraido), ct);
            var raiz = RaizDelPaquete(extraido);
            var exe = Path.GetFileName(emulador.RutaExe);
            if (!File.Exists(Path.Combine(raiz, exe)))
                return new(false, $"El paquete descargado no contiene {exe}; no se instaló nada.");
            ct.ThrowIfCancellationRequested();

            // 3. Respaldo y copia (a partir de aquí ya no se cancela).
            return await Task.Run(() => Instalar(emulador, version, raiz, progreso), CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
            return new(false, "Actualización cancelada. No se instaló nada.");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return new(false, $"No se pudo actualizar: {Explicar(ex)}");
        }
        finally
        {
            try
            {
                Directory.Delete(trabajo, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>Borra descargas de actualizaciones que quedaron a medias (p. ej. si se cerró el lanzador mientras descargaba).</summary>
    private void LimpiarTemporalesViejos()
    {
        if (!Directory.Exists(CarpetaTemporal))
            return;
        foreach (var dir in Directory.EnumerateDirectories(CarpetaTemporal))
            try
            {
                if (DateTime.Now - Directory.GetCreationTime(dir) > TimeSpan.FromHours(1))
                    Directory.Delete(dir, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Copia un paquete ya extraído y verificado sobre la instalación, respaldando lo que reemplaza.</summary>
    private ResultadoActualizacion Instalar(EmuladorConfig emulador, VersionDisponible version, string raizPaquete, IProgress<ProgresoActualizacion>? progreso)
    {
        var instalacion = CarpetaInstalacion(emulador);
        var protegidas = CarpetasProtegidas(emulador);
        var instalacionNueva = !Directory.Exists(instalacion) || !File.Exists(emulador.RutaExe);

        var archivos = Directory.EnumerateFiles(raizPaquete, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(raizPaquete, f))
            .Where(r => !EsProtegida(r, protegidas))
            .ToList();
        var datosEnPaquete = Directory.EnumerateDirectories(raizPaquete).Select(Path.GetFileName).Where(n => protegidas.Contains(n!)).ToList();

        var anterior = VersionesInstaladas.Leer(emulador);
        var respaldo = new InfoRespaldo
        {
            Emulador = emulador.Id,
            Fecha = DateTime.Now,
            VersionAnterior = anterior.Version,
            CanalAnterior = anterior.Canal,
            VersionNueva = version.Version,
            Carpeta = CarpetaRespaldoNueva(emulador.Id),
        };
        foreach (var r in archivos)
            (File.Exists(Path.Combine(instalacion, r)) ? respaldo.Reemplazados : respaldo.Nuevos).Add(r);

        // Respaldo de lo que se va a reemplazar, antes de tocar nada.
        try
        {
            Directory.CreateDirectory(respaldo.Carpeta);
            for (var i = 0; i < respaldo.Reemplazados.Count; i++)
            {
                var r = respaldo.Reemplazados[i];
                progreso?.Report(new(EtapaActualizacion.Respaldando, (double)i / respaldo.Reemplazados.Count, $"Guardando respaldo: {r}"));
                var destino = Path.Combine(respaldo.Carpeta, "archivos", r);
                Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
                File.Copy(Path.Combine(instalacion, r), destino, true);
            }
            GuardarInfo(respaldo);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            EliminarCarpeta(respaldo.Carpeta);
            return new(false, $"No se pudo guardar el respaldo ({Explicar(ex)}). No se instaló nada.");
        }

        var copiados = 0;
        var tocados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var r in archivos)
            {
                progreso?.Report(new(EtapaActualizacion.Copiando, (double)copiados / archivos.Count, $"Copiando {r}"));
                var destino = RutaSegura(instalacion, r, protegidas);
                tocados.Add(r);
                Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
                File.Copy(Path.Combine(raizPaquete, r), destino, true);
                copiados++;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            progreso?.Report(new(EtapaActualizacion.Restaurando, 0, "Falló la copia; restaurando la versión anterior…"));
            var restaurado = Restaurar(emulador, respaldo, null, tocados);
            if (restaurado)
                EliminarCarpeta(respaldo.Carpeta);
            return new(false, $"No se pudo copiar ({Explicar(ex)}). " +
                              (restaurado ? "Se restauró la versión anterior." : $"Quedó a medias: usa \"Volver a la versión anterior\" (respaldo en {respaldo.Carpeta})."));
        }

        // En una instalación nueva se crea siempre la carpeta de datos (user o portable) junto al ejecutable:
        // así el emulador arranca en modo portable y nada se va a AppData.
        if (instalacionNueva)
        {
            foreach (var nombre in datosEnPaquete)
                Directory.CreateDirectory(Path.Combine(instalacion, nombre!));
            Directory.CreateDirectory(CarpetaPortable(emulador));
        }

        respaldo.Completo = true;
        GuardarInfo(respaldo);
        VersionesInstaladas.Registrar(emulador, version.Version, version.Canal, version.Archivo);
        if (respaldo.Reemplazados.Count == 0)
            EliminarCarpeta(respaldo.Carpeta); // instalación nueva: no hay nada a qué volver
        LimpiarRespaldos(emulador.Id);

        var datos = Path.GetFileName(Path.TrimEndingDirectorySeparator(emulador.CarpetaDatos));
        var nota = instalacionNueva || !protegidas.Contains(datos) ? "" : $" Tu carpeta {datos} no se tocó.";
        return new(true, $"{emulador.Nombre} quedó en la versión {version.Version} ({Plural(copiados, "archivo copiado", "archivos copiados")}).{nota}", copiados,
            respaldo.Reemplazados.Count > 0 ? respaldo.Carpeta : null);
    }

    /// <summary>El respaldo más reciente del emulador al que se puede volver.</summary>
    public static InfoRespaldo? UltimoRespaldo(string emuladorId) => Respaldos(emuladorId).FirstOrDefault();

    public static List<InfoRespaldo> Respaldos(string emuladorId)
    {
        var lista = new List<InfoRespaldo>();
        if (!Directory.Exists(Rutas.Respaldos))
            return lista;
        foreach (var carpeta in Directory.EnumerateDirectories(Rutas.Respaldos, emuladorId + "-*").OrderByDescending(d => d, StringComparer.Ordinal))
        {
            var archivo = Path.Combine(carpeta, "respaldo.json");
            if (!File.Exists(archivo))
                continue;
            try
            {
                var info = JsonSerializer.Deserialize<InfoRespaldo>(File.ReadAllText(archivo), GestorConfiguracion.OpcionesJson);
                if (info == null || !info.Emulador.Equals(emuladorId, StringComparison.OrdinalIgnoreCase))
                    continue;
                info.Carpeta = carpeta;
                lista.Add(info);
            }
            catch (JsonException) { }
            catch (IOException) { }
        }
        return lista;
    }

    /// <summary>Deja la instalación como antes de la última actualización: borra lo que agregó y repone lo que reemplazó.</summary>
    public static ResultadoActualizacion VolverAnterior(EmuladorConfig emulador, IProgress<ProgresoActualizacion>? progreso)
    {
        if (Bloqueo(emulador) is { } motivo)
            return new(false, motivo);
        var respaldo = UltimoRespaldo(emulador.Id);
        if (respaldo == null)
            return new(false, $"No hay respaldos de {emulador.Nombre}.");
        try
        {
            if (!Restaurar(emulador, respaldo, progreso))
                return new(false, "No se pudo restaurar todo (¿hay algún archivo abierto?). El respaldo se conserva para intentarlo de nuevo.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(false, $"No se pudo restaurar: {Explicar(ex)}");
        }

        if (respaldo.VersionAnterior != null)
            VersionesInstaladas.Registrar(emulador, respaldo.VersionAnterior, respaldo.CanalAnterior ?? "", null);
        else
            VersionesInstaladas.Olvidar(emulador.Id);
        EliminarCarpeta(respaldo.Carpeta);
        var version = respaldo.VersionAnterior ?? "anterior";
        var detalle = Plural(respaldo.Reemplazados.Count, "archivo restaurado", "archivos restaurados")
                      + (respaldo.Nuevos.Count > 0 ? $" y {Plural(respaldo.Nuevos.Count, "quitado", "quitados")}" : "");
        return new(true, $"{emulador.Nombre} volvió a la versión {version} ({detalle}).",
            respaldo.Reemplazados.Count);
    }

    /// <summary>
    /// Borra lo que agregó la actualización y repone lo que reemplazó. Con <paramref name="soloEstos"/> se limita
    /// a esos archivos (los que alcanzó a tocar una copia que falló). Devuelve false si algo quedó sin restaurar.
    /// </summary>
    private static bool Restaurar(EmuladorConfig emulador, InfoRespaldo respaldo, IProgress<ProgresoActualizacion>? progreso, ISet<string>? soloEstos = null)
    {
        var instalacion = CarpetaInstalacion(emulador);
        var protegidas = CarpetasProtegidas(emulador);
        var completo = true;
        foreach (var r in respaldo.Nuevos.Where(r => soloEstos == null || soloEstos.Contains(r)))
        {
            var ruta = RutaSegura(instalacion, r, protegidas);
            try
            {
                if (File.Exists(ruta))
                    File.Delete(ruta);
                BorrarCarpetasVacias(Path.GetDirectoryName(ruta)!, instalacion);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                completo = false;
            }
        }
        var reponer = respaldo.Reemplazados.Where(r => soloEstos == null || soloEstos.Contains(r)).ToList();
        for (var i = 0; i < reponer.Count; i++)
        {
            var r = reponer[i];
            progreso?.Report(new(EtapaActualizacion.Restaurando, (double)i / reponer.Count, $"Restaurando {r}"));
            var origen = Path.Combine(respaldo.Carpeta, "archivos", r);
            if (!File.Exists(origen))
            {
                completo = false;
                continue;
            }
            var destino = RutaSegura(instalacion, r, protegidas);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
                File.Copy(origen, destino, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Si no se pudo escribir porque está bloqueado, puede que nunca se haya llegado a cambiar.
                if (!MismoContenido(origen, destino))
                    completo = false;
            }
        }
        return completo;
    }

    private static bool MismoContenido(string a, string b)
    {
        try
        {
            using var fa = new FileStream(a, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var fb = new FileStream(b, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (fa.Length != fb.Length)
                return false;
            var ba = new byte[1 << 16];
            var bb = new byte[1 << 16];
            int n;
            while ((n = fa.ReadAtLeast(ba, ba.Length, false)) > 0)
                if (fb.ReadAtLeast(bb, n, false) != n || !ba.AsSpan(0, n).SequenceEqual(bb.AsSpan(0, n)))
                    return false;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Si el zip trae todo dentro de una sola carpeta (p. ej. "publish\"), la raíz es esa carpeta.</summary>
    public static string RaizDelPaquete(string extraido)
    {
        var raiz = extraido;
        for (var nivel = 0; nivel < 3; nivel++)
        {
            var carpetas = Directory.GetDirectories(raiz);
            if (carpetas.Length != 1 || Directory.EnumerateFiles(raiz).Any())
                break;
            var nombre = Path.GetFileName(carpetas[0]);
            if (nombre.Equals("user", StringComparison.OrdinalIgnoreCase) || nombre.Equals("portable", StringComparison.OrdinalIgnoreCase))
                break;
            raiz = carpetas[0];
        }
        return raiz;
    }

    private async Task<string> DescargarAsync(string url, string destino, long? tamanoEsperado, IProgress<ProgresoActualizacion>? progreso, CancellationToken ct)
    {
        using var vigilancia = CancellationTokenSource.CreateLinkedTokenSource(ct);
        vigilancia.CancelAfter(SinDatosMaximo);
        HttpResponseMessage respuesta;
        try
        {
            respuesta = await _cliente.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, vigilancia.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new HttpRequestException("El servidor no respondió.");
        }
        using (respuesta)
        {
            if (!respuesta.IsSuccessStatusCode)
                throw new HttpRequestException($"el servidor respondió {(int)respuesta.StatusCode} al descargar {Path.GetFileName(new Uri(url).AbsolutePath)}");
            var total = respuesta.Content.Headers.ContentLength ?? tamanoEsperado;
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using var origen = await respuesta.Content.ReadAsStreamAsync(vigilancia.Token);
            await using (var archivo = new FileStream(destino, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, true))
            {
                var buffer = new byte[1 << 16];
                long recibidos = 0;
                var ultimoAviso = DateTime.MinValue;
                while (true)
                {
                    vigilancia.CancelAfter(SinDatosMaximo);
                    int leidos;
                    try
                    {
                        leidos = await origen.ReadAsync(buffer, vigilancia.Token);
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        throw new HttpRequestException($"La descarga se detuvo: {SinDatosMaximo.TotalSeconds:0} segundos sin recibir datos.");
                    }
                    if (leidos == 0)
                        break;
                    sha.AppendData(buffer, 0, leidos);
                    await archivo.WriteAsync(buffer.AsMemory(0, leidos), ct);
                    recibidos += leidos;
                    if (DateTime.UtcNow - ultimoAviso > TimeSpan.FromMilliseconds(150))
                    {
                        ultimoAviso = DateTime.UtcNow;
                        var fraccion = total is > 0 ? Math.Min(1, (double)recibidos / total.Value) : 0;
                        var texto = total is > 0
                            ? $"Descargando… {recibidos / 1048576.0:0.0} de {total.Value / 1048576.0:0.0} MB"
                            : $"Descargando… {recibidos / 1048576.0:0.0} MB";
                        progreso?.Report(new(EtapaActualizacion.Descargando, fraccion, texto));
                    }
                }
            }
            return Convert.ToHexStringLower(sha.GetHashAndReset());
        }
    }

    private static string Plural(int n, string uno, string varios) => $"{n} {(n == 1 ? uno : varios)}";

    /// <summary>Mensaje en español para los errores más comunes (los de .NET y Windows llegan en inglés).</summary>
    public static string Explicar(Exception ex)
    {
        static string Nombre(string mensaje) =>
            System.Text.RegularExpressions.Regex.Match(mensaje, "'([^']+)'") is { Success: true } m ? Path.GetFileName(m.Groups[1].Value) : "un archivo";
        return ex switch
        {
            IOException when (ex.HResult & 0xFFFF) is 32 or 33 => $"{Nombre(ex.Message)} está en uso por otro programa",
            IOException when (ex.HResult & 0xFFFF) is 112 or 39 => "el disco está lleno",
            UnauthorizedAccessException => $"Windows no dio permiso para escribir {Nombre(ex.Message)}",
            HttpRequestException { HttpRequestError: HttpRequestError.NameResolutionError } => "no se encontró el servidor (¿hay internet?)",
            HttpRequestException { HttpRequestError: HttpRequestError.ConnectionError } => "no se pudo conectar con el servidor",
            HttpRequestException { HttpRequestError: HttpRequestError.SecureConnectionError } => "falló la conexión segura con el servidor",
            HttpRequestException { StatusCode: { } codigo } => $"el servidor respondió {(int)codigo}",
            InvalidDataException => $"el archivo descargado está dañado ({ex.Message})",
            _ => ex.Message,
        };
    }

    private static bool EsProtegida(string relativa, HashSet<string> protegidas)
    {
        var primera = relativa.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
        return protegidas.Contains(primera);
    }

    /// <summary>Ruta dentro de la instalación; falla si saldría de ella o caería en una carpeta de datos.</summary>
    private static string RutaSegura(string instalacion, string relativa, HashSet<string> protegidas)
    {
        var ruta = Path.GetFullPath(Path.Combine(instalacion, relativa));
        if (!EstaDentro(ruta, instalacion))
            throw new IOException($"Ruta fuera de la instalación: {relativa}");
        if (EsProtegida(Path.GetRelativePath(instalacion, ruta), protegidas))
            throw new IOException($"Se intentó escribir en una carpeta de datos protegida: {relativa}");
        return ruta;
    }

    private static bool EstaDentro(string ruta, string carpeta)
    {
        var base_ = Path.GetFullPath(carpeta).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(ruta).StartsWith(base_, StringComparison.OrdinalIgnoreCase);
    }

    private static void BorrarCarpetasVacias(string carpeta, string hasta)
    {
        var limite = Path.GetFullPath(hasta).TrimEnd(Path.DirectorySeparatorChar);
        for (var dir = new DirectoryInfo(carpeta); dir != null && EstaDentro(dir.FullName, limite); dir = dir.Parent)
        {
            if (!dir.Exists || dir.EnumerateFileSystemInfos().Any())
                break;
            dir.Delete();
        }
    }

    private static string CarpetaRespaldoNueva(string emuladorId)
    {
        var baseNombre = Path.Combine(Rutas.Respaldos, $"{emuladorId}-{DateTime.Now:yyyyMMdd-HHmmss}");
        var carpeta = baseNombre;
        for (var i = 2; Directory.Exists(carpeta); i++)
            carpeta = $"{baseNombre}-{i}";
        return carpeta;
    }

    private static void GuardarInfo(InfoRespaldo respaldo)
    {
        var archivo = Path.Combine(respaldo.Carpeta, "respaldo.json");
        File.WriteAllText(archivo + ".tmp", JsonSerializer.Serialize(respaldo, GestorConfiguracion.OpcionesJson));
        File.Move(archivo + ".tmp", archivo, true);
    }

    private static void LimpiarRespaldos(string emuladorId)
    {
        foreach (var viejo in Respaldos(emuladorId).Skip(RespaldosPorEmulador))
            EliminarCarpeta(viejo.Carpeta);
    }

    private static void EliminarCarpeta(string carpeta)
    {
        // Solo carpetas de app\datos\respaldos: nunca nada del emulador.
        if (!EstaDentro(carpeta, Rutas.Respaldos) || !Directory.Exists(carpeta))
            return;
        try
        {
            Directory.Delete(carpeta, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
