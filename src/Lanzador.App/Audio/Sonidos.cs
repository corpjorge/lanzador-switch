using System.IO;

namespace Lanzador.App.Audio;

/// <summary>Los sonidos de la interfaz del Design System.</summary>
public enum Sonido
{
    /// <summary>Mover el foco una posición.</summary>
    Paso,
    /// <summary>Intentar moverse más allá del borde.</summary>
    Borde,
    /// <summary>Elegir, abrir una pantalla o un diálogo.</summary>
    Elegir,
    /// <summary>Volver o cerrar sin hacer nada.</summary>
    Volver,
    /// <summary>Encender un interruptor (truco activado).</summary>
    Encender,
    /// <summary>Apagar un interruptor.</summary>
    Apagar,
    /// <summary>Algo se guardó o se aplicó.</summary>
    Aplicado,
    /// <summary>Algo falló.</summary>
    Error,
    /// <summary>Abrir un juego (con fundido a negro).</summary>
    Lanzar,
    /// <summary>La intro al abrir el lanzador.</summary>
    Arranque,
}

/// <summary>
/// Genera los sonidos en el momento (osciladores y ruido, sin archivos de terceros) y los toca con el volumen
/// de la configuración. Paso suena como mucho cada 90 ms para que mantener una dirección no sature.
/// </summary>
public static class Sonidos
{
    private static readonly Dictionary<Sonido, float[]> Generados = new();
    private static readonly Dictionary<Sonido, DateTime> UltimaVez = new();

    /// <summary>0 a 1; 0,6 por defecto.</summary>
    public static double Volumen { get; set; } = 0.6;

    public static bool Activados { get; set; } = true;

    /// <summary>Si existe la variable LANZADOR_AUDIO_LOG, cada sonido se anota en ese archivo (para diagnosticar).</summary>
    private static readonly string? Bitacora = Environment.GetEnvironmentVariable("LANZADOR_AUDIO_LOG");
    private static bool _falloAnotado;

    public static void Tocar(Sonido sonido)
    {
        if (!Activados || Volumen <= 0)
        {
            Anotar($"{sonido}: no suena (activados={Activados}, volumen={Volumen})");
            return;
        }
        var ahora = DateTime.UtcNow;
        var separacion = sonido == Sonido.Paso ? 90 : 50; // el mismo sonido dos veces seguidas suena una
        if (UltimaVez.TryGetValue(sonido, out var antes) && (ahora - antes).TotalMilliseconds < separacion)
            return;
        UltimaVez[sonido] = ahora;
        if (!Generados.TryGetValue(sonido, out var muestras))
            Generados[sonido] = muestras = Generar(sonido);
        SalidaWave.AlInformar ??= texto => Anotar("Audio: " + texto);
        SalidaWave.AlFallar ??= fallo =>
        {
            Anotar("Salida de audio: " + fallo);
            AnotarFalloUnaVez("No se pudieron tocar los sonidos: " + fallo);
        };
        SalidaWave.Tocar(muestras, Volumen);
        Anotar($"{sonido}: a la mezcla ({muestras.Length * 1000 / SalidaWave.Frecuencia} ms, volumen {Volumen:0.00}, salida {(SalidaWave.Abierta ? "abierta" : "abriéndose")})");
    }

    /// <summary>
    /// Con el lanzador al frente la salida queda abierta tocando silencio (las salidas HDMI se duermen y se comerían
    /// los sonidos cortos); al pasar al juego se cierra.
    /// </summary>
    public static void MantenerSalida(bool abierta)
    {
        SalidaWave.AlInformar ??= texto => Anotar("Audio: " + texto);
        if (abierta && Activados && Volumen > 0)
            SalidaWave.Iniciar();
        else
            SalidaWave.Detener();
    }

    private static readonly object Cerrojo = new();

    private static void Anotar(string texto)
    {
        if (Bitacora == null)
            return;
        lock (Cerrojo)
        {
            try
            {
                File.AppendAllText(Bitacora, $"[{DateTime.Now:HH:mm:ss.fff}] {texto}\r\n");
            }
            catch (IOException) { }
        }
    }

    /// <summary>El primer fallo de audio de la sesión queda en errores.log (los siguientes no, para no llenarlo).</summary>
    private static void AnotarFalloUnaVez(string texto)
    {
        lock (Cerrojo)
        {
            if (_falloAnotado)
                return;
            _falloAnotado = true;
            try
            {
                File.AppendAllText(Path.Combine(Lanzador.Core.Rutas.Datos, "errores.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {texto}\r\n\r\n");
            }
            catch (IOException) { }
        }
    }

    /// <summary>Guarda el sonido como WAV (modo de prueba), con el volumen actual.</summary>
    public static void GuardarWav(Sonido sonido, string ruta)
    {
        var muestras = Generar(sonido);
        using var archivo = new BinaryWriter(File.Create(ruta));
        archivo.Write("RIFF"u8);
        archivo.Write(36 + muestras.Length * 2);
        archivo.Write("WAVEfmt "u8);
        archivo.Write(16);
        archivo.Write((short)1);
        archivo.Write((short)1);
        archivo.Write(SalidaWave.Frecuencia);
        archivo.Write(SalidaWave.Frecuencia * 2);
        archivo.Write((short)2);
        archivo.Write((short)16);
        archivo.Write("data"u8);
        archivo.Write(muestras.Length * 2);
        foreach (var m in muestras)
            archivo.Write((short)Math.Clamp(m * Volumen * short.MaxValue, short.MinValue, short.MaxValue));
    }

    /// <summary>Paso si el foco se movió; Borde si ya estaba en el extremo.</summary>
    public static void Moverse(bool seMovio) => Tocar(seMovio ? Sonido.Paso : Sonido.Borde);

    // ───── Síntesis ─────

    private enum Onda { Seno, Triangulo, Cuadrada }

    private sealed record Nota(double Desde, double Hasta, double Segundos, Onda Onda, double Volumen, double Inicio);

    private static float[] Generar(Sonido sonido) => sonido switch
    {
        // Tick agudo y muy corto (1,6 kHz, 18 ms).
        Sonido.Paso => Notas(new Nota(1600, 1600, 0.022, Onda.Seno, 0.42, 0)),
        // Golpe sordo (180 Hz, 40 ms).
        Sonido.Borde => Notas(new Nota(180, 150, 0.05, Onda.Triangulo, 0.75, 0)),
        // Dos notas que suben (660 → 990 Hz, 90 ms).
        Sonido.Elegir => Notas(new Nota(660, 660, 0.07, Onda.Seno, 0.55, 0), new Nota(990, 990, 0.09, Onda.Seno, 0.55, 0.056)),
        // Dos notas que bajan (520 → 390 Hz, 80 ms).
        Sonido.Volver => Notas(new Nota(520, 520, 0.05, Onda.Seno, 0.52, 0), new Nota(390, 390, 0.07, Onda.Seno, 0.52, 0.04)),
        Sonido.Encender => Notas(new Nota(1200, 1200, 0.035, Onda.Seno, 0.5, 0)),
        Sonido.Apagar => Notas(new Nota(900, 900, 0.035, Onda.Seno, 0.5, 0)),
        // Acorde corto que sube (do-mi-sol-do, 220 ms).
        Sonido.Aplicado => Notas(
            new Nota(523.25, 523.25, 0.12, Onda.Seno, 0.4, 0),
            new Nota(659.25, 659.25, 0.12, Onda.Seno, 0.4, 0.04),
            new Nota(783.99, 783.99, 0.14, Onda.Seno, 0.4, 0.08),
            new Nota(1046.5, 1046.5, 0.14, Onda.Seno, 0.34, 0.12)),
        // Zumbido grave que baja (cuadrada 220 → 160 Hz, 140 ms), suave porque la cuadrada suena fuerte.
        Sonido.Error => Notas(new Nota(220, 160, 0.14, Onda.Cuadrada, 0.32, 0)),
        Sonido.Lanzar => Lanzar(),
        Sonido.Arranque => Arranque(),
        _ => [],
    };

    private static float[] Notas(params Nota[] notas)
    {
        var total = notas.Max(n => n.Inicio + n.Segundos) + 0.01;
        var muestras = new float[(int)(total * SalidaWave.Frecuencia)];
        foreach (var nota in notas)
            Sumar(muestras, nota);
        return muestras;
    }

    /// <summary>Suma una nota con barrido de frecuencia, ataque de 4 ms y caída exponencial (como un rampa de WebAudio).</summary>
    private static void Sumar(float[] destino, Nota nota)
    {
        var inicio = (int)(nota.Inicio * SalidaWave.Frecuencia);
        var largo = (int)(nota.Segundos * SalidaWave.Frecuencia);
        var ataque = 0.004 * SalidaWave.Frecuencia;
        var fase = 0.0;
        for (var i = 0; i < largo && inicio + i < destino.Length; i++)
        {
            var t = (double)i / largo;
            var frecuencia = nota.Desde * Math.Pow(nota.Hasta / nota.Desde, t);
            fase += 2 * Math.PI * frecuencia / SalidaWave.Frecuencia;
            var onda = nota.Onda switch
            {
                Onda.Triangulo => 2 / Math.PI * Math.Asin(Math.Sin(fase)),
                Onda.Cuadrada => Math.Sign(Math.Sin(fase)),
                _ => Math.Sin(fase),
            };
            var envolvente = Math.Min(1, i / ataque) * Math.Exp(-7 * Math.Max(0, i - ataque) / Math.Max(1, largo - ataque));
            destino[inicio + i] += (float)(onda * envolvente * nota.Volumen);
        }
    }

    /// <summary>
    /// Intro: un soplo de ruido que sube (0,5 s) y desemboca en un acorde luminoso de mi mayor que entra nota a nota
    /// y se apaga despacio, con un grave suave debajo (≈ 1,6 s).
    /// </summary>
    private static float[] Arranque()
    {
        const double segundos = 1.7;
        var muestras = new float[(int)(segundos * SalidaWave.Frecuencia)];
        var azar = new Random(11);
        double filtrado = 0, filtrado2 = 0;
        var largo = (int)(0.55 * SalidaWave.Frecuencia);
        for (var i = 0; i < largo; i++)
        {
            var t = (double)i / largo;
            var corte = 200 * Math.Pow(6000 / 200.0, t);
            var alfa = 1 - Math.Exp(-2 * Math.PI * corte / SalidaWave.Frecuencia);
            filtrado += alfa * (azar.NextDouble() * 2 - 1 - filtrado);
            filtrado2 += alfa * (filtrado - filtrado2);
            var envolvente = Math.Pow(t, 1.5) * (t < 0.85 ? 1 : (1 - t) / 0.15);
            muestras[i] += (float)(filtrado2 * envolvente * 1.0);
        }
        // Acorde (mi - sol# - si - mi) a partir de 0,42 s, una nota cada 70 ms.
        double[] acorde = [329.63, 415.30, 493.88, 659.26];
        for (var n = 0; n < acorde.Length; n++)
            Sumar(muestras, new Nota(acorde[n], acorde[n], 1.15, Onda.Seno, 0.2, 0.42 + n * 0.07));
        Sumar(muestras, new Nota(1318.5, 1318.5, 0.9, Onda.Seno, 0.07, 0.62)); // brillo agudo
        Sumar(muestras, new Nota(82.41, 82.41, 0.9, Onda.Triangulo, 0.28, 0.42)); // grave
        return muestras;
    }

    /// <summary>Barrido de ruido que sube (400 ms) con tres notas ascendentes encima.</summary>
    private static float[] Lanzar()
    {
        const double segundos = 0.45;
        var muestras = new float[(int)(segundos * SalidaWave.Frecuencia)];
        var azar = new Random(7);
        double filtrado = 0, filtrado2 = 0;
        var largo = (int)(0.4 * SalidaWave.Frecuencia);
        for (var i = 0; i < largo; i++)
        {
            var t = (double)i / largo;
            // Paso bajo de dos polos cuyo corte sube de 300 Hz a 5 kHz: suena a "whoosh".
            var corte = 300 * Math.Pow(5000 / 300.0, t);
            var alfa = 1 - Math.Exp(-2 * Math.PI * corte / SalidaWave.Frecuencia);
            filtrado += alfa * (azar.NextDouble() * 2 - 1 - filtrado);
            filtrado2 += alfa * (filtrado - filtrado2);
            var envolvente = Math.Sin(Math.PI * Math.Min(1, t * 1.15)) * (0.4 + 0.6 * t);
            muestras[i] += (float)(filtrado2 * envolvente * 2.0);
        }
        Sumar(muestras, new Nota(220, 220, 0.06, Onda.Triangulo, 0.28, 0));
        Sumar(muestras, new Nota(440, 440, 0.09, Onda.Triangulo, 0.32, 0.05));
        Sumar(muestras, new Nota(880, 880, 0.24, Onda.Seno, 0.38, 0.12));
        return muestras;
    }
}
