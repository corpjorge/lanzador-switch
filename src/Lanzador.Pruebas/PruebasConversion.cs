using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Lanzador.Core.Conversion;

/// <summary>
/// Pruebas del conversor NSZ → NSP: con un NSZ armado aquí mismo (de corrido y por bloques) y, si están, con los .nsz
/// reales de las carpetas de juegos, siempre sobre copias en la carpeta temporal.
/// </summary>
static class PruebasConversion
{
    private static int _fallos;

    private static void Comprobar(bool condicion, string descripcion)
    {
        Console.WriteLine($"  [{(condicion ? "OK" : "FALLO")}] {descripcion}");
        if (!condicion)
            _fallos++;
    }

    public static int Ejecutar(string temporal, IEnumerable<string> carpetasJuegos)
    {
        _fallos = 0;
        var carpeta = Path.Combine(temporal, "conversion");
        Directory.CreateDirectory(carpeta);

        Console.WriteLine("\n== Conversión NSZ → NSP: archivo armado aquí (zstd de corrido y por bloques)");
        var azar = new Random(7);
        // Un «NCA» con cabecera, un hueco sin cifrar, una sección AES-CTR, otra sin cifrar y otra AES-CTR (como un BKTR).
        var nca = new byte[0x4000 + 0x800 + 700_000 + 0x1234 + 300_001];
        azar.NextBytes(nca.AsSpan(0, 0x4000));
        for (var i = 0x4000; i < nca.Length; i++)
            nca[i] = (byte)(i / 977 + i % 13); // se comprime bien
        azar.NextBytes(nca.AsSpan(0x4000 + 0x800 + 650_000, 50_000)); // y un tramo que no
        (long Inicio, long Tamano, long Cifrado, byte[] Clave, byte[] Contador)[] secciones =
        [
            (0x4800, 700_000, 3, Bytes(azar, 16), Bytes(azar, 16)),
            (0x4800 + 700_000, 0x1234, 1, new byte[16], new byte[16]),
            (0x4800 + 700_000 + 0x1234, 300_001, 4, Bytes(azar, 16), Bytes(azar, 16)),
        ];
        var cifrado = (byte[])nca.Clone();
        foreach (var s in secciones.Where(s => s.Cifrado is 3 or 4))
            CifrarCtr(cifrado.AsSpan((int)s.Inicio, (int)s.Tamano), s.Clave, s.Contador, s.Inicio);
        var id = Convert.ToHexStringLower(SHA256.HashData(cifrado))[..32];
        var cnmt = Bytes(azar, 5000);
        var idCnmt = Convert.ToHexStringLower(SHA256.HashData(cnmt))[..32];
        var tik = Bytes(azar, 0x2C0);

        foreach (var porBloques in new[] { false, true })
        {
            var modo = porBloques ? "por bloques" : "de corrido";
            var nsz = Path.Combine(carpeta, $"Juego de prueba [0100000000010000][v0] {(porBloques ? "bloques" : "corrido")}.nsz");
            File.WriteAllBytes(nsz, Pfs0(($"{id}.ncz", Ncz(nca, secciones, porBloques)), ($"{idCnmt}.cnmt.nca", cnmt), ("0100000000010000000000000000000a.tik", tik)));

            var examen = ConversorNsz.Examinar(nsz);
            Comprobar(examen.Estado == EstadoComprimido.Convertible && examen.TamanoDestino > nca.Length, $"{modo}: se reconoce como convertible y se calcula su tamaño");
            var r = ConversorNsz.Convertir(nsz, null, CancellationToken.None);
            Comprobar(r is { Exito: true, Verificado: true, NcaVerificados: 2 } && File.Exists(r.Destino), $"{modo}: {r.Mensaje}");
            if (r.Exito)
            {
                var archivos = LeerPfs0(File.ReadAllBytes(r.Destino!));
                Comprobar(archivos.Count == 3 && archivos[$"{id}.nca"].AsSpan().SequenceEqual(cifrado) && archivos[$"{idCnmt}.cnmt.nca"].AsSpan().SequenceEqual(cnmt)
                          && archivos["0100000000010000000000000000000a.tik"].AsSpan().SequenceEqual(tik), $"{modo}: el NSP trae el NCA cifrado de nuevo, byte a byte, y los demás archivos iguales");
                Comprobar(examen.TamanoDestino == new FileInfo(r.Destino!).Length, $"{modo}: el tamaño calculado antes es el real");
                Comprobar(ConversorNsz.Verificar(r.Destino!, nsz, null, CancellationToken.None) is { Exito: true, Verificado: true }, $"{modo}: el NSP ya convertido se verifica contra su NSZ");
                Comprobar(ConversorNsz.Examinar(nsz).Estado == EstadoComprimido.YaConvertido && !ConversorNsz.Convertir(nsz, null, CancellationToken.None).Exito,
                    $"{modo}: con el .nsp al lado figura como ya convertido y no se reemplaza");

                // Un NSP con un byte cambiado no pasa la verificación.
                var danado = Path.Combine(carpeta, $"danado-{porBloques}.nsp");
                var bytes = File.ReadAllBytes(r.Destino!);
                bytes[^1000] ^= 0x01;
                File.WriteAllBytes(danado, bytes);
                Comprobar(!ConversorNsz.Verificar(danado, null, null, CancellationToken.None).Exito, $"{modo}: un NSP con un byte cambiado no pasa la verificación");
            }
        }

        // Un NSZ dañado (un byte del zstd cambiado) no deja ningún archivo.
        var roto = Path.Combine(carpeta, "roto.nsz");
        var contenido = Pfs0(($"{id}.ncz", Ncz(nca, secciones, false)));
        contenido[^5000] ^= 0x40;
        File.WriteAllBytes(roto, contenido);
        var fallido = ConversorNsz.Convertir(roto, null, CancellationToken.None);
        Comprobar(!fallido.Exito && !File.Exists(Path.ChangeExtension(roto, ".nsp")) && !File.Exists(Path.ChangeExtension(roto, ".nsp") + ".parcial"),
            $"Un NSZ dañado no se convierte ni deja archivos a medias: {fallido.Mensaje}");
        var cancelado = new CancellationTokenSource();
        cancelado.Cancel();
        var otro = Path.Combine(carpeta, "cancelado.nsz");
        File.WriteAllBytes(otro, Pfs0(($"{id}.ncz", Ncz(nca, secciones, true))));
        Comprobar(!ConversorNsz.Convertir(otro, null, cancelado.Token).Exito && Directory.GetFiles(carpeta, "cancelado.nsp*").Length == 0, "Cancelar no deja archivos a medias");
        File.WriteAllBytes(Path.Combine(carpeta, "juego.xcz"), [1, 2, 3]);
        File.WriteAllText(Path.Combine(carpeta, "no-es.nsz"), "esto no es un NSZ");
        var encontrados = ConversorNsz.Buscar([carpeta]);
        Comprobar(encontrados.Count(a => a.Estado == EstadoComprimido.YaConvertido) == 2 && encontrados.Single(a => a.Ruta.EndsWith(".xcz")).Estado == EstadoComprimido.NoCompatible
                  && encontrados.Single(a => a.Ruta.EndsWith("no-es.nsz")).Estado == EstadoComprimido.Danado && encontrados.Any(a => a.Estado == EstadoComprimido.Convertible),
            "Buscar: lista los convertibles, los ya convertidos, los .xcz (no compatibles) y los dañados");

        Console.WriteLine("\n== Conversión NSZ → NSP: tus .nsz reales (sobre copias, los originales solo se leen)");
        var reales = ConversorNsz.Buscar(carpetasJuegos).Where(a => a.Ruta.EndsWith(".nsz", StringComparison.OrdinalIgnoreCase)).OrderBy(a => a.Tamano).Take(2).ToList();
        if (reales.Count == 0)
            Console.WriteLine("  (no hay archivos .nsz en las carpetas de juegos)");
        foreach (var real in reales)
        {
            var copia = Path.Combine(carpeta, Path.GetFileName(real.Ruta));
            File.Copy(real.Ruta, copia, true);
            var reloj = System.Diagnostics.Stopwatch.StartNew();
            var r = ConversorNsz.Convertir(copia, null, CancellationToken.None);
            Console.WriteLine($"  {Path.GetFileName(real.Ruta)}: {ConversorNsz.Tamano(real.Tamano)} → {(r.Destino != null && File.Exists(r.Destino) ? ConversorNsz.Tamano(new FileInfo(r.Destino).Length) : "—")} en {reloj.Elapsed.TotalSeconds:0.0} s");
            Comprobar(r is { Exito: true, Verificado: true }, r.Mensaje);
        }
        return _fallos;
    }

    private static byte[] Bytes(Random azar, int cantidad)
    {
        var b = new byte[cantidad];
        azar.NextBytes(b);
        return b;
    }

    /// <summary>AES-CTR escrito aparte del conversor, bloque a bloque, para comparar.</summary>
    private static void CifrarCtr(Span<byte> datos, byte[] clave, byte[] contador, long posicion)
    {
        using var aes = Aes.Create();
        aes.Key = clave;
        var c = new byte[16];
        var flujo = new byte[16];
        for (var i = 0; i < datos.Length; i++)
        {
            var absoluta = posicion + i;
            if (i == 0 || absoluta % 16 == 0)
            {
                contador.AsSpan(0, 8).CopyTo(c);
                BinaryPrimitives.WriteUInt64BigEndian(c.AsSpan(8), (ulong)(absoluta / 16));
                aes.EncryptEcb(c, flujo, PaddingMode.None);
            }
            datos[i] ^= flujo[absoluta % 16];
        }
    }

    private static byte[] Ncz(byte[] ncaDescifrado, (long Inicio, long Tamano, long Cifrado, byte[] Clave, byte[] Contador)[] secciones, bool porBloques)
    {
        using var salida = new MemoryStream();
        using var w = new BinaryWriter(salida);
        w.Write(ncaDescifrado, 0, 0x4000);
        w.Write("NCZSECTN"u8);
        w.Write((long)secciones.Length);
        foreach (var s in secciones)
        {
            w.Write(s.Inicio);
            w.Write(s.Tamano);
            w.Write(s.Cifrado);
            w.Write(0L);
            w.Write(s.Clave);
            w.Write(s.Contador);
        }
        var cuerpo = ncaDescifrado.AsSpan(0x4000);
        using var zstd = new ZstdSharp.Compressor(3);
        if (!porBloques)
        {
            w.Write(zstd.Wrap(cuerpo));
            return salida.ToArray();
        }
        const int exponente = 16;
        var bloque = 1 << exponente;
        var bloques = new List<byte[]>();
        for (var i = 0; i < cuerpo.Length; i += bloque)
        {
            var trozo = cuerpo.Slice(i, Math.Min(bloque, cuerpo.Length - i));
            var comprimido = zstd.Wrap(trozo).ToArray();
            bloques.Add(comprimido.Length < trozo.Length ? comprimido : trozo.ToArray()); // si no gana nada, va tal cual
        }
        w.Write("NCZBLOCK"u8);
        w.Write((byte)2);
        w.Write((byte)1);
        w.Write((byte)0);
        w.Write((byte)exponente);
        w.Write((uint)bloques.Count);
        w.Write((long)cuerpo.Length);
        foreach (var b in bloques)
            w.Write(b.Length);
        foreach (var b in bloques)
            w.Write(b);
        return salida.ToArray();
    }

    private static byte[] Pfs0(params (string Nombre, byte[] Datos)[] archivos)
    {
        var nombres = new MemoryStream();
        var posiciones = new List<int>();
        foreach (var (nombre, _) in archivos)
        {
            posiciones.Add((int)nombres.Length);
            nombres.Write(Encoding.UTF8.GetBytes(nombre + "\0"));
        }
        using var salida = new MemoryStream();
        using var w = new BinaryWriter(salida);
        w.Write("PFS0"u8);
        w.Write(archivos.Length);
        w.Write((int)nombres.Length);
        w.Write(0);
        long posicion = 0;
        for (var i = 0; i < archivos.Length; i++)
        {
            w.Write(posicion);
            w.Write((long)archivos[i].Datos.Length);
            w.Write(posiciones[i]);
            w.Write(0);
            posicion += archivos[i].Datos.Length;
        }
        w.Write(nombres.ToArray());
        foreach (var (_, datos) in archivos)
            w.Write(datos);
        return salida.ToArray();
    }

    private static Dictionary<string, byte[]> LeerPfs0(byte[] archivo)
    {
        var cantidad = BitConverter.ToInt32(archivo, 4);
        var tamanoNombres = BitConverter.ToInt32(archivo, 8);
        var nombres = 16 + cantidad * 0x18;
        var datos = nombres + tamanoNombres;
        var resultado = new Dictionary<string, byte[]>();
        for (var i = 0; i < cantidad; i++)
        {
            var e = 16 + i * 0x18;
            var posicion = BitConverter.ToInt64(archivo, e);
            var tamano = BitConverter.ToInt64(archivo, e + 8);
            var nombreEn = nombres + BitConverter.ToInt32(archivo, e + 16);
            var fin = Array.IndexOf(archivo, (byte)0, nombreEn);
            resultado[Encoding.UTF8.GetString(archivo, nombreEn, fin - nombreEn)] = archivo.AsSpan((int)(datos + posicion), (int)tamano).ToArray();
        }
        return resultado;
    }
}
