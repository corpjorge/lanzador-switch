using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lanzador.Core;

namespace Lanzador.App.Tema;

/// <summary>
/// El icono de cada emulador, sacado de su propio ejecutable en alta resolución (256 px si lo trae), para el diálogo
/// de elegir emulador, la sección Emuladores y la página del juego. Si no está instalado, no hay icono.
/// </summary>
public static class IconosEmulador
{
    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHDefExtractIcon(string archivo, int indice, uint opciones, out IntPtr grande, out IntPtr pequeno, uint tamanos);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icono);

    public static ImageSource? De(EmuladorConfig emulador)
    {
        var ruta = emulador.RutaExe;
        if (string.IsNullOrWhiteSpace(ruta))
            return null;
        if (Cache.TryGetValue(ruta, out var guardado))
            return guardado;
        ImageSource? imagen = null;
        if (File.Exists(ruta))
        {
            // Pide 256 px; si el ejecutable solo trae tamaños menores, Windows escala el más grande.
            if (SHDefExtractIcon(ruta, 0, 0, out var grande, out var pequeno, 256 | (16u << 16)) == 0 && grande != IntPtr.Zero)
            {
                try
                {
                    var bitmap = Imaging.CreateBitmapSourceFromHIcon(grande, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    bitmap.Freeze();
                    imagen = bitmap;
                }
                catch (Exception ex) when (ex is COMException or ArgumentException)
                {
                    imagen = null;
                }
                finally
                {
                    DestroyIcon(grande);
                    if (pequeno != IntPtr.Zero)
                        DestroyIcon(pequeno);
                }
            }
        }
        if (imagen != null)
            Cache[ruta] = imagen; // los que faltan se vuelven a buscar (p. ej. recién instalado)
        return imagen;
    }

    /// <summary>Al instalar o actualizar un emulador su icono puede cambiar.</summary>
    public static void Olvidar() => Cache.Clear();
}
