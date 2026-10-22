using System.Diagnostics;

namespace Lanzador.Core;

public static class Lanzamiento
{
    public static Process Iniciar(EmuladorConfig emulador, Juego juego)
    {
        if (!File.Exists(emulador.RutaExe))
            throw new FileNotFoundException($"No encuentro {emulador.Nombre} en {emulador.RutaExe}. Revisa la ruta en Configuración.");
        if (!File.Exists(juego.RutaArchivo))
            throw new FileNotFoundException($"No encuentro el archivo del juego: {juego.RutaArchivo}");

        var argumentos = emulador.Argumentos.Contains("{juego}")
            ? emulador.Argumentos.Replace("{juego}", juego.RutaArchivo)
            : $"{emulador.Argumentos} \"{juego.RutaArchivo}\"".Trim();

        var psi = new ProcessStartInfo(emulador.RutaExe, argumentos)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(emulador.RutaExe)!,
        };
        return Process.Start(psi) ?? throw new InvalidOperationException($"No se pudo iniciar {emulador.Nombre}.");
    }
}
