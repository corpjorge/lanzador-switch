using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Lanzador.App.Audio;

/// <summary>
/// Salida de audio con waveOut de Windows: un mezclador con una sola salida que se queda abierta (tocando silencio)
/// mientras el lanzador está al frente. Así los sonidos cortos no se pierden en salidas HDMI, que se duermen con el
/// silencio y tardan en despertar, y suenan casi sin demora. Varios sonidos a la vez se mezclan.
/// </summary>
internal static class SalidaWave
{
    public const int Frecuencia = 44100;

    private const int WaveMapper = -1;
    private const int CallbackEvent = 0x00050000;
    private const int WhdrDone = 0x1;
    private const int MmsyserrNoerror = 0;
    private const int MuestrasPorBloque = 512; // 11,6 ms
    private const int Bloques = 4;             // ~46 ms en cola

    [StructLayout(LayoutKind.Sequential)]
    private struct FormatoWave
    {
        public ushort wFormatTag;
        public ushort nChannels;
        public uint nSamplesPerSec;
        public uint nAvgBytesPerSec;
        public ushort nBlockAlign;
        public ushort wBitsPerSample;
        public ushort cbSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CabeceraWave
    {
        public IntPtr lpData;
        public uint dwBufferLength;
        public uint dwBytesRecorded;
        public IntPtr dwUser;
        public uint dwFlags;
        public uint dwLoops;
        public IntPtr lpNext;
        public IntPtr reserved;
    }

    [DllImport("winmm.dll")]
    private static extern int waveOutOpen(out IntPtr salida, int dispositivo, ref FormatoWave formato, IntPtr callback, IntPtr instancia, int opciones);

    [DllImport("winmm.dll")]
    private static extern int waveOutPrepareHeader(IntPtr salida, IntPtr cabecera, int tamano);

    [DllImport("winmm.dll")]
    private static extern int waveOutWrite(IntPtr salida, IntPtr cabecera, int tamano);

    [DllImport("winmm.dll")]
    private static extern int waveOutUnprepareHeader(IntPtr salida, IntPtr cabecera, int tamano);

    [DllImport("winmm.dll")]
    private static extern int waveOutReset(IntPtr salida);

    [DllImport("winmm.dll")]
    private static extern int waveOutClose(IntPtr salida);

    private static readonly int TamanoCabecera = Marshal.SizeOf<CabeceraWave>();
    private static readonly int PosicionFlags = (int)Marshal.OffsetOf<CabeceraWave>(nameof(CabeceraWave.dwFlags));

    private sealed class Voz(float[] muestras, double volumen)
    {
        public float[] Muestras { get; } = muestras;
        public double Volumen { get; } = volumen;
        public int Posicion { get; set; }
    }

    private static readonly ConcurrentQueue<Voz> Nuevas = new();
    private static readonly object Cerrojo = new();
    private static Thread? _hilo;
    private static bool _mantener;

    /// <summary>Se llama con el motivo cuando la salida no se puede abrir o falla (para anotarlo).</summary>
    public static Action<string>? AlFallar { get; set; }

    /// <summary>Se llama al abrir y cerrar la salida (para la bitácora de diagnóstico).</summary>
    public static Action<string>? AlInformar { get; set; }

    public static bool Abierta
    {
        get { lock (Cerrojo) return _hilo != null; }
    }

    /// <summary>Abre la salida y la deja tocando silencio (lanzador al frente).</summary>
    public static void Iniciar()
    {
        lock (Cerrojo)
        {
            _mantener = true;
            AbrirSiHaceFalta();
        }
    }

    /// <summary>Cierra la salida en cuanto terminen los sonidos que estén sonando.</summary>
    public static void Detener()
    {
        lock (Cerrojo)
            _mantener = false;
    }

    /// <summary>Agrega un sonido a la mezcla. Si la salida estaba cerrada la abre, y se vuelve a cerrar al terminar.</summary>
    public static void Tocar(float[] muestras, double volumen)
    {
        lock (Cerrojo)
        {
            Nuevas.Enqueue(new Voz(muestras, volumen));
            AbrirSiHaceFalta();
        }
    }

    private static void AbrirSiHaceFalta()
    {
        if (_hilo != null)
            return;
        _hilo = new Thread(Mezclar) { IsBackground = true, Name = "Mezclador de sonidos", Priority = ThreadPriority.AboveNormal };
        _hilo.Start();
    }

    private static void Mezclar()
    {
        try
        {
            MezclarHastaTerminar();
        }
        catch (Exception ex)
        {
            AlFallar?.Invoke(ex.GetType().Name + ": " + ex.Message);
            Nuevas.Clear();
        }
        finally
        {
            // Si llegó un sonido mientras se cerraba (o se volvió al frente), se abre de nuevo.
            lock (Cerrojo)
            {
                _hilo = null;
                if (!Nuevas.IsEmpty || _mantener)
                    AbrirSiHaceFalta();
            }
        }
    }

    private static void MezclarHastaTerminar()
    {
        using var evento = new AutoResetEvent(false);
        var formato = new FormatoWave
        {
            wFormatTag = 1, // PCM
            nChannels = 1,
            nSamplesPerSec = Frecuencia,
            wBitsPerSample = 16,
            nBlockAlign = 2,
            nAvgBytesPerSec = Frecuencia * 2,
        };
        var abrir = waveOutOpen(out var salida, WaveMapper, ref formato, evento.SafeWaitHandle.DangerousGetHandle(), IntPtr.Zero, CallbackEvent);
        if (abrir != MmsyserrNoerror)
        {
            AlFallar?.Invoke($"waveOutOpen devolvió {abrir}");
            lock (Cerrojo)
            {
                Nuevas.Clear();
                _mantener = false; // no reintentar en bucle
            }
            return;
        }

        AlInformar?.Invoke("salida abierta");
        var cabeceras = new IntPtr[Bloques];
        var datos = new IntPtr[Bloques];
        var mezcla = new float[MuestrasPorBloque];
        var pcm = new short[MuestrasPorBloque];
        var voces = new List<Voz>();
        try
        {
            for (var i = 0; i < Bloques; i++)
            {
                datos[i] = Marshal.AllocHGlobal(MuestrasPorBloque * 2);
                cabeceras[i] = Marshal.AllocHGlobal(TamanoCabecera);
                Marshal.StructureToPtr(new CabeceraWave { lpData = datos[i], dwBufferLength = MuestrasPorBloque * 2 }, cabeceras[i], false);
                var preparar = waveOutPrepareHeader(salida, cabeceras[i], TamanoCabecera);
                if (preparar != MmsyserrNoerror)
                {
                    AlFallar?.Invoke($"waveOutPrepareHeader devolvió {preparar}");
                    lock (Cerrojo)
                    {
                        Nuevas.Clear();
                        _mantener = false;
                    }
                    return;
                }
                Llenar(i);
            }

            while (true)
            {
                evento.WaitOne(100);
                for (var i = 0; i < Bloques; i++)
                    if ((Marshal.ReadInt32(cabeceras[i], PosicionFlags) & WhdrDone) != 0)
                        Llenar(i);
                lock (Cerrojo)
                    if (!_mantener && voces.Count == 0 && Nuevas.IsEmpty)
                        break;
            }
        }
        finally
        {
            waveOutReset(salida);
            for (var i = 0; i < Bloques; i++)
            {
                if (cabeceras[i] != IntPtr.Zero)
                {
                    waveOutUnprepareHeader(salida, cabeceras[i], TamanoCabecera);
                    Marshal.FreeHGlobal(cabeceras[i]);
                }
                if (datos[i] != IntPtr.Zero)
                    Marshal.FreeHGlobal(datos[i]);
            }
            waveOutClose(salida);
            AlInformar?.Invoke("salida cerrada");
        }

        // Mezcla las voces activas en el bloque i y lo manda a la cola de la tarjeta.
        void Llenar(int i)
        {
            while (Nuevas.TryDequeue(out var nueva))
                voces.Add(nueva);
            Array.Clear(mezcla);
            foreach (var voz in voces)
            {
                var n = Math.Min(MuestrasPorBloque, voz.Muestras.Length - voz.Posicion);
                for (var k = 0; k < n; k++)
                    mezcla[k] += (float)(voz.Muestras[voz.Posicion + k] * voz.Volumen);
                voz.Posicion += n;
            }
            voces.RemoveAll(v => v.Posicion >= v.Muestras.Length);
            for (var k = 0; k < MuestrasPorBloque; k++)
                pcm[k] = (short)Math.Clamp(mezcla[k] * short.MaxValue, short.MinValue, short.MaxValue);
            Marshal.Copy(pcm, 0, datos[i], MuestrasPorBloque);
            var escribir = waveOutWrite(salida, cabeceras[i], TamanoCabecera);
            if (escribir != MmsyserrNoerror)
                AlFallar?.Invoke($"waveOutWrite devolvió {escribir}");
        }
    }
}
