using System.IO;
using Lanzador.Core;

namespace Lanzador.App.Audio;

internal static class VocesIntro
{
    public static (byte[]? Pcm, string? Nombre) Elegir()
    {
        var alicia = LeerLocal("intro-switch-alicia.pcm") ?? LeerIncluida("intro-switch-alicia.pcm");
        var george = LeerLocal("intro-switch-george.pcm") ?? LeerIncluida("intro-switch-george.pcm");

        if (alicia != null && george != null)
            return TurnoDeGeorge() ? (george, "George") : (alicia, "Alicia");
        if (alicia != null)
            return (alicia, "Alicia");
        if (george != null)
            return (george, "George");

        return (LeerLocal("intro-switch.pcm"), null);
    }

    private static byte[]? LeerLocal(string archivo)
    {
        try
        {
            using var entrada = new FileStream(Path.Combine(Rutas.Datos, archivo), FileMode.Open, FileAccess.Read, FileShare.Read);
            return Leer(entrada);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static byte[]? LeerIncluida(string archivo)
    {
        using var entrada = typeof(VocesIntro).Assembly.GetManifestResourceStream("Lanzador.App.Audio.Voces." + archivo);
        return entrada == null ? null : Leer(entrada);
    }

    private static byte[]? Leer(Stream entrada)
    {
        const int maximo = SalidaWave.Frecuencia * 3; // PCM mono de 16 bits, hasta 1,5 s.
        if (entrada.Length < 2 || entrada.Length > maximo || entrada.Length % 2 != 0)
            return null;

        var pcm = new byte[(int)entrada.Length];
        entrada.ReadExactly(pcm);
        return pcm;
    }

    private static bool TurnoDeGeorge()
    {
        var ruta = Path.Combine(Rutas.Datos, "intro-voz-siguiente.txt");
        for (var intento = 0; intento < 4; intento++)
        {
            try
            {
                // La apertura exclusiva mantiene el turno aunque dos lanzadores arranquen a la vez.
                using var turno = new FileStream(ruta, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                var george = turno.ReadByte() == 'G';
                turno.Position = 0;
                turno.WriteByte(george ? (byte)'A' : (byte)'G');
                turno.Flush(flushToDisk: true);
                return george;
            }
            catch (IOException ex) when ((ex.HResult & 0xFFFF) is 32 or 33)
            {
                if (intento < 3)
                    Thread.Sleep(15);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
        return false;
    }
}
