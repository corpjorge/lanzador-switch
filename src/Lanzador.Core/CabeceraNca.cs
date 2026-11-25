using System.Buffers.Binary;
using System.Security.Cryptography;
using Lanzador.Core.ClavesFirmware;

namespace Lanzador.Core;

/// <summary>Lee el identificador en la cabecera de los NCA de metadatos sin abrir sus secciones.</summary>
internal sealed class CabeceraNca : IDisposable
{
    private readonly Aes _datos = Aes.Create();
    private readonly byte[] _primerVector = new byte[16];
    private readonly byte[] _segundoVector = new byte[16];

    private CabeceraNca(byte[] clave)
    {
        _datos.Key = clave[..16];
        using var cifradorVector = Aes.Create();
        cifradorVector.Key = clave[16..];

        // NCA usa sectores de 0x200 y el contador XTS en orden big-endian.
        Span<byte> sector = stackalloc byte[16];
        sector.Clear();
        sector[^1] = 1;
        cifradorVector.EncryptEcb(sector, _primerVector, PaddingMode.None);
        var acarreo = 0;
        for (var i = 0; i < _primerVector.Length; i++)
        {
            _segundoVector[i] = (byte)((_primerVector[i] << 1) | acarreo);
            acarreo = _primerVector[i] >> 7;
        }
        if (acarreo != 0)
            _segundoVector[0] ^= 0x87;
    }

    public static CabeceraNca? Abrir(IEnumerable<EmuladorConfig> emuladores)
    {
        foreach (var emulador in emuladores.Where(e => !string.IsNullOrWhiteSpace(e.CarpetaDatos)))
        {
            var ruta = Path.Combine(InstaladorClavesFirmware.CarpetaClaves(emulador), "prod.keys");
            try
            {
                if (!File.Exists(ruta))
                    continue;
                foreach (var linea in File.ReadLines(ruta))
                {
                    var igual = linea.IndexOf('=');
                    if (igual < 0 || !linea[..igual].Trim().Equals("header_key", StringComparison.OrdinalIgnoreCase))
                        continue;
                    var texto = linea[(igual + 1)..].Trim();
                    if (texto.Length != 64 || !texto.All(char.IsAsciiHexDigit))
                        break;
                    var clave = Convert.FromHexString(texto);
                    try { return new CabeceraNca(clave); }
                    finally { CryptographicOperations.ZeroMemory(clave); }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return null;
    }

    public ulong? LeerTitleId(Stream archivo, long inicio = 0)
    {
        Span<byte> cifrado = stackalloc byte[32];
        Span<byte> claro = stackalloc byte[16];
        archivo.Position = inicio + 0x200;
        if (archivo.ReadAtLeast(cifrado, cifrado.Length, throwOnEndOfStream: false) != cifrado.Length)
            return null;
        DescifrarBloque(cifrado[..16], claro, _primerVector);
        if ((!claro[..4].SequenceEqual("NCA3"u8) && !claro[..4].SequenceEqual("NCA2"u8)) || claro[5] != 1)
            return null;
        DescifrarBloque(cifrado[16..], claro, _segundoVector);
        return BinaryPrimitives.ReadUInt64LittleEndian(claro);
    }

    private void DescifrarBloque(Span<byte> bloque, Span<byte> salida, byte[] vector)
    {
        for (var i = 0; i < bloque.Length; i++)
            bloque[i] ^= vector[i];
        _datos.DecryptEcb(bloque, salida, PaddingMode.None);
        for (var i = 0; i < salida.Length; i++)
            salida[i] ^= vector[i];
    }

    public void Dispose() => _datos.Dispose();
}
