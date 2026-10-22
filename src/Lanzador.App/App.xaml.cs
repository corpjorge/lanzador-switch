using System.IO;
using System.Windows;
using System.Windows.Threading;
using Lanzador.Core;

namespace Lanzador.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += AlFallar;

        var carpetaDatos = Environment.GetEnvironmentVariable("LANZADOR_DATOS");
        if (!string.IsNullOrWhiteSpace(carpetaDatos))
            Rutas.Configurar(carpetaDatos);

        var ventana = new MainWindow();
        MainWindow = ventana;

        var prueba = Environment.GetEnvironmentVariable("LANZADOR_PRUEBA");
        var capturas = Environment.GetEnvironmentVariable("LANZADOR_CAPTURAS");
        if (prueba != null && capturas != null)
        {
            ventana.PonerPantallaCompleta(false);
            ventana.Width = 1600;
            ventana.Height = 900;
            ventana.Loaded += (_, _) => _ = ventana.EjecutarPruebaAsync(prueba.Split(',', StringSplitOptions.RemoveEmptyEntries), capturas);
        }
        ventana.Show();
    }

    private void AlFallar(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            File.AppendAllText(Path.Combine(Rutas.Datos, "errores.log"), $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {e.Exception}\r\n\r\n");
        }
        catch (IOException) { }

        if (MainWindow is MainWindow ventana)
        {
            ventana.Aviso("Ocurrió un error: " + e.Exception.Message, true);
            e.Handled = true;
        }
    }
}
