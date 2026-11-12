using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using ZstdSharp;

namespace Lanzador.Core.Conversion;

public enum EstadoComprimido
{
    /// <summary>Un .nsz que se puede convertir a .nsp.</summary>
    Convertible,

    /// <summary>Ya hay un .nsp con el mismo nombre al lado: se puede verificar y borrar el .nsz.</summary>
    YaConvertido,

    /// <summary>Formato que el lanzador no convierte (.xcz).</summary>
    NoCompatible,

    /// <summary>No se pudo leer (archivo incompleto o dañado).</summary>
    Danado,
}

/// <summary>Un juego comprimido encontrado en las carpetas de juegos.</summary>
public sealed record ArchivoComprimido(string Ruta, long Tamano, string Destino, long? TamanoDestino, EstadoComprimido Estado, string Detalle)
{
    public string Nombre => Path.GetFileNameWithoutExtension(Ruta);
}

public sealed record ProgresoConversion(double Fraccion, string Texto);

/// <summary><see cref="Verificado"/> solo es true si se comprobó el sha256 de todos los NCA: entonces es seguro borrar el original.</summary>
public sealed record ResultadoConversion(bool Exito, string Mensaje, string? Destino = null, bool Verificado = false, int NcaVerificados = 0);

/// <summary>
/// Convierte juegos NSZ (NSP comprimido con zstd, el formato de la herramienta nsz) a NSP, sin programas externos ni claves.
/// <para>
/// Un NSZ es un NSP (contenedor PFS0) cuyos .nca van como .ncz: los primeros 0x4000 bytes del NCA tal cual, una tabla
/// de secciones (<c>NCZSECTN</c>) con la clave y el contador AES-CTR de cada una, y el resto del NCA descifrado y
/// comprimido con zstd, de corrido o por bloques (<c>NCZBLOCK</c>). Para volver al NCA se descomprime y se cifra de nuevo
/// cada sección.
/// </para>
/// <para>
/// El nombre de cada NCA son los primeros 16 bytes de su sha256, así que al terminar se comprueba cada uno: si todos
/// coinciden, el NSP es idéntico, NCA por NCA, al que se comprimió.
/// </para>
/// </summary>
public static class ConversorNsz
{
    private const int CabeceraNca = 0x4000;
    private const int Bloque = 1 << 20;
    private const int MaxArchivos = 4096;

    /// <summary>Espacio que se deja libre en el disco además de lo que ocupa el NSP.</summary>
    public const long MargenDisco = 512L << 20;

    private static readonly EnumerationOptions Recorrer = new() { RecurseSubdirectories = true, IgnoreInaccessible = true };

    /// <summary>Los .nsz y .xcz de esas carpetas (y sus subcarpetas: juegos, actualizaciones y DLC), con lo que se puede hacer con cada uno.</summary>
    public static List<ArchivoComprimido> Buscar(IEnumerable<string> carpetas, CancellationToken ct = default)
    {
        var lista = new List<ArchivoComprimido>();
        foreach (var carpeta in carpetas.Where(Directory.Exists))
            foreach (var ruta in Directory.EnumerateFiles(carpeta, "*", Recorrer))
            {
                ct.ThrowIfCancellationRequested();
                var extension = Path.GetExtension(ruta);
                if (!extension.Equals(".nsz", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".xcz", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (lista.Any(a => a.Ruta.Equals(ruta, StringComparison.OrdinalIgnoreCase)))
                    continue;
                lista.Add(Examinar(ruta));
            }
        return lista.OrderBy(a => a.Nombre, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Lee las cabeceras de un archivo comprimido para saber si se puede convertir y cuánto ocupará.</summary>
    public static ArchivoComprimido Examinar(string ruta)
    {
        long tamano = 0;
        var esXcz = Path.GetExtension(ruta).Equals(".xcz", StringComparison.OrdinalIgnoreCase);
        var destino = Path.ChangeExtension(ruta, esXcz ? ".xci" : ".nsp");
        try
        {
            tamano = new FileInfo(ruta).Length;
            if (esXcz)
                return new(ruta, tamano, destino, null, EstadoComprimido.NoCompatible,
                    File.Exists(destino) ? "Ya tienes el .xci al lado" : "Los .xcz todavía no se convierten aquí: usa la herramienta nsz");

            using var entrada = Abrir(ruta);
            var archivos = LeerPfs0(entrada);
            if (!archivos.Any(a => EsNcz(a.Nombre)))
                return new(ruta, tamano, destino, null, EstadoComprimido.Danado, "No trae ningún .ncz: no parece un NSZ");
            long total = TamanoCabecera(archivos.Select(a => NombreNsp(a.Nombre)));
            foreach (var a in archivos)
                total += EsNcz(a.Nombre) ? LeerNcz(entrada, a).TamanoNca : a.Tamano;

            return File.Exists(destino)
                ? new(ruta, tamano, destino, new FileInfo(destino).Length, EstadoComprimido.YaConvertido, "Ya tienes el .nsp al lado: verifícalo para borrar el .nsz")
                : new(ruta, tamano, destino, total, EstadoComprimido.Convertible, "Se puede convertir");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new(ruta, tamano, destino, null, EstadoComprimido.Danado, $"No se pudo leer: {ex.Message}");
        }
    }

    /// <summary>
    /// Convierte un .nsz al .nsp del mismo nombre, en la misma carpeta, y verifica el sha256 de cada NCA. Escribe en un
    /// archivo <c>.parcial</c> y solo le pone el nombre definitivo si todo coincide. El .nsz no se modifica ni se borra.
    /// </summary>
    public static ResultadoConversion Convertir(string rutaNsz, IProgress<ProgresoConversion>? progreso, CancellationToken ct)
    {
        var destino = Path.ChangeExtension(rutaNsz, ".nsp");
        var parcial = destino + ".parcial";
        if (File.Exists(destino))
            return new(false, $"Ya existe {Path.GetFileName(destino)}; no se reemplaza.", destino);
        try
        {
            int verificados;
            using (var entrada = Abrir(rutaNsz))
            {
                var archivos = LeerPfs0(entrada);
                if (!archivos.Any(a => EsNcz(a.Nombre)))
                    return new(false, "El archivo no trae ningún .ncz: no parece un NSZ.");
                var nombres = archivos.Select(a => NombreNsp(a.Nombre)).ToList();
                var cabecera = TamanoCabecera(nombres);
                var estimado = cabecera + archivos.Sum(a => EsNcz(a.Nombre) ? LeerNcz(entrada, a).TamanoNca : a.Tamano);
                if (Path.GetPathRoot(Path.GetFullPath(destino)) is { Length: > 0 } raiz && new DriveInfo(raiz) is { IsReady: true } disco
                    && disco.AvailableFreeSpace < estimado + MargenDisco)
                    return new(false, $"No hay espacio en {raiz}: el NSP ocupa {Tamano(estimado)} y quedan {Tamano(disco.AvailableFreeSpace)} libres.");

                using var salida = new FileStream(parcial, FileMode.Create, FileAccess.ReadWrite, FileShare.None, Bloque);
                salida.SetLength(cabecera);
                salida.Position = cabecera;
                var tamanos = new long[archivos.Count];
                long escritos = 0;
                verificados = 0;
                void Avance(long bytes, string nombre)
                {
                    escritos += bytes;
                    progreso?.Report(new(Math.Min(1, (double)(cabecera + escritos) / Math.Max(1, estimado)),
                        $"{Tamano(cabecera + escritos)} de {Tamano(estimado)} · {nombre}"));
                }

                for (var i = 0; i < archivos.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var a = archivos[i];
                    var inicio = salida.Position;
                    using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    if (EsNcz(a.Nombre))
                        DescomprimirNcz(entrada, a, salida, sha, bytes => Avance(bytes, nombres[i]), ct);
                    else
                        Copiar(entrada, a, salida, sha, bytes => Avance(bytes, nombres[i]), ct);
                    tamanos[i] = salida.Position - inicio;

                    if (IdNca(nombres[i]) is { } id)
                    {
                        var hash = Convert.ToHexStringLower(sha.GetHashAndReset());
                        if (!hash.StartsWith(id, StringComparison.Ordinal))
                            throw new InvalidDataException($"{nombres[i]} no coincide con su sha256 tras descomprimirlo (salió {hash[..32]})");
                        verificados++;
                    }
                }
                if (verificados == 0)
                    throw new InvalidDataException("ningún NCA tiene su identificador en el nombre, así que no se puede verificar");

                salida.Position = 0;
                salida.Write(CrearCabecera(nombres, tamanos));
                salida.Flush(true);
            }
            File.Move(parcial, destino);
            return new(true, $"Convertido y verificado: {Path.GetFileName(destino)} ({Tamano(new FileInfo(destino).Length)}, {verificados} NCA comprobados con su sha256).",
                destino, true, verificados);
        }
        catch (OperationCanceledException)
        {
            Borrar(parcial);
            return new(false, "Conversión cancelada. No quedó ningún archivo a medias.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ZstdException or CryptographicException)
        {
            Borrar(parcial);
            var motivo = ex is IOException && (ex.HResult & 0xFFFF) is 112 or 39 ? "el disco se llenó" : ex.Message;
            return new(false, $"No se pudo convertir {Path.GetFileName(rutaNsz)}: {motivo}. El .nsz quedó intacto y no se creó el .nsp.");
        }
    }

    /// <summary>
    /// Comprueba un .nsp ya existente: el sha256 de cada NCA contra su nombre y, si se da el .nsz del que salió, que
    /// traiga exactamente los mismos archivos. Solo lee.
    /// </summary>
    public static ResultadoConversion Verificar(string rutaNsp, string? rutaNsz, IProgress<ProgresoConversion>? progreso, CancellationToken ct)
    {
        try
        {
            using var entrada = Abrir(rutaNsp);
            var archivos = LeerPfs0(entrada);
            if (rutaNsz != null)
            {
                using var original = Abrir(rutaNsz);
                var esperados = LeerPfs0(original).Select(a => NombreNsp(a.Nombre)).Order(StringComparer.OrdinalIgnoreCase).ToList();
                var presentes = archivos.Select(a => a.Nombre).Order(StringComparer.OrdinalIgnoreCase).ToList();
                if (!esperados.SequenceEqual(presentes, StringComparer.OrdinalIgnoreCase))
                    return new(false, $"{Path.GetFileName(rutaNsp)} no trae los mismos archivos que el .nsz ({presentes.Count} frente a {esperados.Count}): no es su conversión.", rutaNsp);
            }

            var total = Math.Max(1, archivos.Sum(a => a.Tamano));
            long leidos = 0;
            var verificados = 0;
            foreach (var a in archivos)
            {
                ct.ThrowIfCancellationRequested();
                using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                Copiar(entrada, a, Stream.Null, sha, bytes =>
                {
                    leidos += bytes;
                    progreso?.Report(new((double)leidos / total, $"Verificando {Tamano(leidos)} de {Tamano(total)} · {a.Nombre}"));
                }, ct);
                if (IdNca(a.Nombre) is not { } id)
                    continue;
                var hash = Convert.ToHexStringLower(sha.GetHashAndReset());
                if (!hash.StartsWith(id, StringComparison.Ordinal))
                    return new(false, $"{Path.GetFileName(rutaNsp)} está dañado: {a.Nombre} no coincide con su sha256.", rutaNsp);
                verificados++;
            }
            return verificados == 0
                ? new(false, $"{Path.GetFileName(rutaNsp)} no trae NCA que se puedan verificar.", rutaNsp)
                : new(true, $"{Path.GetFileName(rutaNsp)} está bien: {verificados} NCA comprobados con su sha256.", rutaNsp, true, verificados);
        }
        catch (OperationCanceledException)
        {
            return new(false, "Verificación cancelada.", rutaNsp);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new(false, $"No se pudo verificar {Path.GetFileName(rutaNsp)}: {ex.Message}", rutaNsp);
        }
    }

    public static string Tamano(long bytes) => bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.00} GB" : $"{bytes / (double)(1 << 20):0.0} MB";

    // ───────────── PFS0 (el contenedor de NSP y NSZ) ─────────────

    private sealed record Entrada(string Nombre, long Posicion, long Tamano);

    private static FileStream Abrir(string ruta) => new(ruta, FileMode.Open, FileAccess.Read, FileShare.Read, Bloque, FileOptions.SequentialScan);

    private static bool EsNcz(string nombre) => nombre.EndsWith(".ncz", StringComparison.OrdinalIgnoreCase);

    private static string NombreNsp(string nombre) => EsNcz(nombre) ? nombre[..^4] + ".nca" : nombre;

    /// <summary>"3c7e…f2.nca" o "3c7e…f2.cnmt.nca" → los 32 caracteres del identificador (los 16 primeros bytes del sha256).</summary>
    private static string? IdNca(string nombre)
    {
        if (!nombre.EndsWith(".nca", StringComparison.OrdinalIgnoreCase))
            return null;
        var id = nombre[..nombre.IndexOf('.')].ToLowerInvariant();
        return id.Length == 32 && id.All(char.IsAsciiHexDigit) ? id : null;
    }

    private static List<Entrada> LeerPfs0(Stream archivo)
    {
        Span<byte> inicio = stackalloc byte[16];
        archivo.Position = 0;
        if (archivo.ReadAtLeast(inicio, 16, false) < 16 || !inicio[..4].SequenceEqual("PFS0"u8))
            throw new InvalidDataException("no es un NSP ni un NSZ (falta la cabecera PFS0)");
        var cantidad = BinaryPrimitives.ReadUInt32LittleEndian(inicio[4..]);
        var tamanoNombres = BinaryPrimitives.ReadUInt32LittleEndian(inicio[8..]);
        if (cantidad is 0 or > MaxArchivos || tamanoNombres > 1 << 20)
            throw new InvalidDataException("la cabecera del archivo no es válida");

        var tabla = new byte[cantidad * 0x18 + tamanoNombres];
        if (archivo.ReadAtLeast(tabla, tabla.Length, false) < tabla.Length)
            throw new InvalidDataException("el archivo está incompleto");
        var datos = 16L + tabla.Length;
        var nombres = tabla.AsSpan((int)cantidad * 0x18);
        var lista = new List<Entrada>((int)cantidad);
        for (var i = 0; i < cantidad; i++)
        {
            var e = tabla.AsSpan(i * 0x18, 0x18);
            var posicion = BinaryPrimitives.ReadInt64LittleEndian(e);
            var tamano = BinaryPrimitives.ReadInt64LittleEndian(e[8..]);
            var nombreEn = (int)BinaryPrimitives.ReadUInt32LittleEndian(e[16..]);
            if (posicion < 0 || tamano < 0 || nombreEn >= nombres.Length || datos + posicion + tamano > archivo.Length)
                throw new InvalidDataException("el archivo está incompleto o dañado");
            var fin = nombres[nombreEn..].IndexOf((byte)0);
            var nombre = Encoding.UTF8.GetString(fin < 0 ? nombres[nombreEn..] : nombres.Slice(nombreEn, fin));
            if (nombre.Length == 0 || nombre.IndexOfAny(['/', '\\']) >= 0)
                throw new InvalidDataException("el archivo trae un nombre no válido");
            lista.Add(new Entrada(nombre, datos + posicion, tamano));
        }
        return lista;
    }

    /// <summary>Tamaño de la cabecera PFS0 para esos nombres, alineada a 0x20 como la que escribe nxdumptool.</summary>
    private static int TamanoCabecera(IEnumerable<string> nombres)
    {
        var lista = nombres.ToList();
        var bruto = 16 + lista.Count * 0x18 + lista.Sum(n => Encoding.UTF8.GetByteCount(n) + 1);
        return (bruto + 0x1F) & ~0x1F;
    }

    private static byte[] CrearCabecera(IReadOnlyList<string> nombres, IReadOnlyList<long> tamanos)
    {
        var cabecera = new byte[TamanoCabecera(nombres)];
        var tabla = 16 + nombres.Count * 0x18;
        "PFS0"u8.CopyTo(cabecera);
        BinaryPrimitives.WriteUInt32LittleEndian(cabecera.AsSpan(4), (uint)nombres.Count);
        BinaryPrimitives.WriteUInt32LittleEndian(cabecera.AsSpan(8), (uint)(cabecera.Length - tabla));
        long posicion = 0;
        var nombreEn = 0;
        for (var i = 0; i < nombres.Count; i++)
        {
            var e = cabecera.AsSpan(16 + i * 0x18, 0x18);
            BinaryPrimitives.WriteInt64LittleEndian(e, posicion);
            BinaryPrimitives.WriteInt64LittleEndian(e[8..], tamanos[i]);
            BinaryPrimitives.WriteUInt32LittleEndian(e[16..], (uint)nombreEn);
            nombreEn += Encoding.UTF8.GetBytes(nombres[i], cabecera.AsSpan(tabla + nombreEn)) + 1;
            posicion += tamanos[i];
        }
        return cabecera;
    }

    private static void Copiar(Stream archivo, Entrada entrada, Stream salida, IncrementalHash sha, Action<long> avance, CancellationToken ct)
    {
        var buffer = new byte[Bloque];
        archivo.Position = entrada.Posicion;
        for (var faltan = entrada.Tamano; faltan > 0;)
        {
            ct.ThrowIfCancellationRequested();
            var leidos = archivo.Read(buffer, 0, (int)Math.Min(buffer.Length, faltan));
            if (leidos == 0)
                throw new InvalidDataException("el archivo está incompleto");
            sha.AppendData(buffer, 0, leidos);
            salida.Write(buffer, 0, leidos);
            faltan -= leidos;
            avance(leidos);
        }
    }

    // ───────────── NCZ ─────────────

    /// <summary>Una sección del NCA: de dónde a dónde va y cómo se vuelve a cifrar.</summary>
    private sealed record Seccion(long Inicio, long Fin, long Cifrado, byte[] Clave, byte[] Contador)
    {
        /// <summary>3 = AES-CTR, 4 = BKTR (también AES-CTR). El resto va sin cifrar.</summary>
        public bool Cifrada => Cifrado is 3 or 4;
    }

    private sealed record Ncz(List<Seccion> Secciones, long TamanoNca, long PosicionDatos);

    /// <summary>Lee la tabla de secciones de un .ncz y deja anotado dónde empiezan los datos comprimidos.</summary>
    private static Ncz LeerNcz(Stream archivo, Entrada entrada)
    {
        if (entrada.Tamano < CabeceraNca + 16)
            throw new InvalidDataException($"{entrada.Nombre} está incompleto");
        archivo.Position = entrada.Posicion + CabeceraNca;
        Span<byte> inicio = stackalloc byte[16];
        archivo.ReadExactly(inicio);
        if (!inicio[..8].SequenceEqual("NCZSECTN"u8))
            throw new InvalidDataException($"{entrada.Nombre} no tiene la tabla de secciones NCZSECTN");
        var cantidad = BinaryPrimitives.ReadInt64LittleEndian(inicio[8..]);
        if (cantidad is < 1 or > 65536 || CabeceraNca + 16 + cantidad * 0x40 > entrada.Tamano)
            throw new InvalidDataException($"{entrada.Nombre} tiene una tabla de secciones no válida");

        var tabla = new byte[cantidad * 0x40];
        archivo.ReadExactly(tabla);
        var secciones = new List<Seccion>((int)cantidad + 1);
        for (var i = 0; i < cantidad; i++)
        {
            var s = tabla.AsSpan(i * 0x40, 0x40);
            var posicion = BinaryPrimitives.ReadInt64LittleEndian(s);
            var tamano = BinaryPrimitives.ReadInt64LittleEndian(s[8..]);
            if (posicion < 0 || tamano < 0)
                throw new InvalidDataException($"{entrada.Nombre} tiene una sección no válida");
            var inicioSeccion = posicion;
            if (i == 0)
            {
                // Lo que la primera sección tenga dentro de los primeros 0x4000 bytes ya va en la cabecera sin comprimir;
                // y si empieza más allá, el hueco hasta ella va comprimido sin cifrar.
                if (posicion > CabeceraNca)
                    secciones.Add(new Seccion(CabeceraNca, posicion, 0, [], []));
                inicioSeccion = Math.Max(posicion, CabeceraNca);
            }
            secciones.Add(new Seccion(inicioSeccion, Math.Max(inicioSeccion, posicion + tamano),
                BinaryPrimitives.ReadInt64LittleEndian(s[16..]), s.Slice(32, 16).ToArray(), s.Slice(48, 16).ToArray()));
        }
        var tamanoNca = CabeceraNca + secciones.Sum(s => s.Fin - s.Inicio);
        return new Ncz(secciones, tamanoNca, archivo.Position);
    }

    private static void DescomprimirNcz(Stream archivo, Entrada entrada, Stream salida, IncrementalHash sha, Action<long> avance, CancellationToken ct)
    {
        var ncz = LeerNcz(archivo, entrada);
        var buffer = new byte[Bloque];

        // Los primeros 0x4000 bytes del NCA van tal cual.
        archivo.Position = entrada.Posicion;
        archivo.ReadExactly(buffer, 0, CabeceraNca);
        salida.Write(buffer, 0, CabeceraNca);
        sha.AppendData(buffer, 0, CabeceraNca);
        avance(CabeceraNca);

        var fin = entrada.Posicion + entrada.Tamano;
        archivo.Position = ncz.PosicionDatos;
        Span<byte> magia = stackalloc byte[8];
        var porBloques = fin - ncz.PosicionDatos >= 8 && archivo.ReadAtLeast(magia, 8, false) == 8 && magia.SequenceEqual("NCZBLOCK"u8);
        archivo.Position = ncz.PosicionDatos;

        using Stream datos = porBloques
            ? new LectorBloques(archivo, fin, entrada.Nombre)
            : new DecompressionStream(new Tramo(archivo, ncz.PosicionDatos, fin - ncz.PosicionDatos), Bloque);
        using var aes = Aes.Create();
        var contadores = new byte[Bloque + 16];
        var flujoClave = new byte[Bloque + 16];

        foreach (var s in ncz.Secciones)
        {
            if (s.Cifrada)
            {
                if (s.Clave.Length != 16 || s.Contador.Length != 16)
                    throw new InvalidDataException($"{entrada.Nombre} tiene una sección cifrada sin clave");
                aes.Key = s.Clave;
            }
            for (var posicion = s.Inicio; posicion < s.Fin;)
            {
                ct.ThrowIfCancellationRequested();
                var pedir = (int)Math.Min(Bloque, s.Fin - posicion);
                var leidos = datos.ReadAtLeast(buffer.AsSpan(0, pedir), pedir, false);
                if (leidos == 0)
                    throw new InvalidDataException($"{entrada.Nombre} está incompleto: los datos comprimidos se acaban antes de tiempo");
                if (s.Cifrada)
                    CifrarCtr(aes, s.Contador, posicion, buffer.AsSpan(0, leidos), contadores, flujoClave);
                salida.Write(buffer, 0, leidos);
                sha.AppendData(buffer, 0, leidos);
                posicion += leidos;
                avance(leidos);
            }
        }
    }

    /// <summary>
    /// AES-128-CTR como en los NCA: el contador de 16 bytes son los 8 primeros de la sección y, en los 8 últimos, la
    /// posición dentro del NCA dividida entre 16, en big-endian.
    /// </summary>
    private static void CifrarCtr(Aes aes, byte[] contadorBase, long posicion, Span<byte> datos, byte[] contadores, byte[] flujoClave)
    {
        var salto = (int)(posicion & 15);
        var bloques = (salto + datos.Length + 15) / 16;
        var numero = (ulong)(posicion >> 4);
        for (var b = 0; b < bloques; b++)
        {
            var c = contadores.AsSpan(b * 16, 16);
            contadorBase.AsSpan(0, 8).CopyTo(c);
            BinaryPrimitives.WriteUInt64BigEndian(c[8..], numero + (ulong)b);
        }
        aes.EncryptEcb(contadores.AsSpan(0, bloques * 16), flujoClave, PaddingMode.None);
        var clave = flujoClave.AsSpan(salto, datos.Length);
        for (var i = 0; i < datos.Length; i++)
            datos[i] ^= clave[i];
    }

    private static void Borrar(string archivo)
    {
        try
        {
            if (File.Exists(archivo))
                File.Delete(archivo);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Un trozo de otro flujo, solo de lectura: el zstd de un .ncz dentro del NSZ.</summary>
    private sealed class Tramo(Stream origen, long inicio, long largo) : Stream
    {
        private long _leidos;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => largo;
        public override long Position { get => _leidos; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            var pedir = (int)Math.Min(buffer.Length, largo - _leidos);
            if (pedir <= 0)
                return 0;
            origen.Position = inicio + _leidos;
            var leidos = origen.Read(buffer[..pedir]);
            _leidos += leidos;
            return leidos;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>
    /// Compresión por bloques (<c>NCZBLOCK</c>): versión, tipo, exponente del tamaño de bloque, cantidad de bloques,
    /// tamaño descomprimido y el tamaño comprimido de cada bloque. Un bloque que no se pudo comprimir va tal cual.
    /// </summary>
    private sealed class LectorBloques : Stream
    {
        private readonly Stream _archivo;
        private readonly string _nombre;
        private readonly Decompressor _zstd = new();
        private readonly int[] _tamanos;
        private readonly int _tamanoBloque;
        private readonly long _total;
        private readonly byte[] _comprimido;
        private readonly byte[] _bloque;
        private long _posicionArchivo;
        private long _entregados;
        private int _siguiente;
        private int _enBloque;
        private int _usados;

        public LectorBloques(Stream archivo, long fin, string nombre)
        {
            _archivo = archivo;
            _nombre = nombre;
            Span<byte> cabecera = stackalloc byte[24];
            archivo.ReadExactly(cabecera);
            var exponente = cabecera[11];
            var cantidad = BinaryPrimitives.ReadUInt32LittleEndian(cabecera[12..]);
            _total = BinaryPrimitives.ReadInt64LittleEndian(cabecera[16..]);
            if (exponente is < 14 or > 30 || _total < 0 || archivo.Position + cantidad * 4L > fin)
                throw new InvalidDataException($"{nombre} tiene una cabecera de bloques no válida");
            _tamanoBloque = 1 << exponente;
            if (cantidad != (_total + _tamanoBloque - 1) / _tamanoBloque)
                throw new InvalidDataException($"{nombre} no trae la cantidad de bloques que dice");

            var lista = new byte[cantidad * 4];
            archivo.ReadExactly(lista);
            _tamanos = new int[cantidad];
            long suma = 0;
            for (var i = 0; i < cantidad; i++)
            {
                _tamanos[i] = BinaryPrimitives.ReadInt32LittleEndian(lista.AsSpan(i * 4));
                if (_tamanos[i] <= 0 || _tamanos[i] > _tamanoBloque)
                    throw new InvalidDataException($"{nombre} tiene un bloque de tamaño no válido");
                suma += _tamanos[i];
            }
            _posicionArchivo = archivo.Position;
            if (_posicionArchivo + suma > fin)
                throw new InvalidDataException($"{nombre} está incompleto");
            _comprimido = new byte[_tamanoBloque];
            _bloque = new byte[_tamanoBloque];
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (_usados == _enBloque && !Siguiente())
                return 0;
            var dar = Math.Min(buffer.Length, _enBloque - _usados);
            _bloque.AsSpan(_usados, dar).CopyTo(buffer);
            _usados += dar;
            return dar;
        }

        private bool Siguiente()
        {
            if (_siguiente >= _tamanos.Length)
                return false;
            var comprimido = _tamanos[_siguiente++];
            var esperado = (int)Math.Min(_tamanoBloque, _total - _entregados);
            _archivo.Position = _posicionArchivo;
            if (comprimido < esperado)
            {
                _archivo.ReadExactly(_comprimido, 0, comprimido);
                if (_zstd.Unwrap(_comprimido.AsSpan(0, comprimido), _bloque) != esperado)
                    throw new InvalidDataException($"{_nombre} tiene un bloque que no se descomprime al tamaño esperado");
            }
            else
            {
                _archivo.ReadExactly(_bloque, 0, esperado);
            }
            _posicionArchivo += comprimido;
            _entregados += esperado;
            _enBloque = esperado;
            _usados = 0;
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _zstd.Dispose();
            base.Dispose(disposing);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _total;
        public override long Position { get => _entregados - (_enBloque - _usados); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
