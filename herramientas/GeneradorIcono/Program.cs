using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// Icono del lanzador: mando genérico blanco con un triángulo de «jugar» recortado, sobre un cuadrado
// redondeado con el azul del Design System (accent-500 / focus). En 16-24 px solo el triángulo.
// Uso: dotnet run --project herramientas\GeneradorIcono -- src\Lanzador.App\Icono\Lanzador.ico
// (deja también icono-<tamaño>.png junto al .ico para revisarlo).
static class Programa
{
    static readonly int[] Tamanos = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256];

    [STAThread]
    static int Main(string[] args)
    {
        var ico = args[0];
        var carpeta = Path.GetDirectoryName(Path.GetFullPath(ico))!;
        var imagenes = new List<(int Tamano, BitmapSource Imagen)>();
        foreach (var t in Tamanos)
        {
            var imagen = Dibujar(t);
            imagenes.Add((t, imagen));
            File.WriteAllBytes(Path.Combine(carpeta, $"icono-{t}.png"), Png(imagen));
        }
        EscribirIco(ico, imagenes);
        Console.WriteLine($"{ico}: {new FileInfo(ico).Length} bytes, {imagenes.Count} tamaños");
        return 0;
    }

    static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    static BitmapSource Dibujar(int tamano)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(tamano / 256.0, tamano / 256.0));
            var fondo = new LinearGradientBrush(C("#5CD0FF"), C("#1D7FD8"), new Point(0, 0), new Point(0.9, 1));
            var margen = tamano <= 24 ? 4 : 10;
            dc.DrawRoundedRectangle(fondo, null, new Rect(margen, margen, 256 - 2 * margen, 256 - 2 * margen), 58, 58);
            // Brillo suave arriba
            var brillo = new LinearGradientBrush(Color.FromArgb(60, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), new Point(0, 0), new Point(0, 0.55));
            dc.DrawRoundedRectangle(brillo, null, new Rect(margen, margen, 256 - 2 * margen, 256 - 2 * margen), 58, 58);

            var blanco = new SolidColorBrush(C("#F2F6FA"));
            if (tamano <= 24)
                dc.DrawGeometry(blanco, null, Triangulo(new Point(98, 70), 116, 26));
            else
            {
                var sombra = Mando(tamano >= 48);
                sombra.Transform = new TranslateTransform(0, 6);
                dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(55, 6, 30, 70)), null, sombra);
                dc.DrawGeometry(blanco, null, Mando(tamano >= 48));
            }
        }
        var bmp = new RenderTargetBitmap(tamano, tamano, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }

    /// <summary>Silueta de mando genérico; el triángulo de «jugar» va recortado en el centro y, en grande, cruceta y botones.</summary>
    static Geometry Mando(bool detalles)
    {
        Geometry cuerpo = Geometry.Parse("M128,76 L86,76 C58,76 44,86 38,110 C32,134 24,164 30,180 C36,196 60,200 72,188 C82,178 92,160 106,156 " +
                                         "L150,156 C164,160 174,178 184,188 C196,200 220,196 226,180 C232,164 224,134 218,110 C212,86 198,76 170,76 Z");
        cuerpo = Geometry.Combine(cuerpo, Triangulo(new Point(117, 95), 42, 7), GeometryCombineMode.Exclude, null);
        if (detalles)
        {
            var cruz = Geometry.Combine(new RectangleGeometry(new Rect(57, 110, 30, 10), 3, 3),
                                        new RectangleGeometry(new Rect(67, 100, 10, 30), 3, 3), GeometryCombineMode.Union, null);
            cuerpo = Geometry.Combine(cuerpo, cruz, GeometryCombineMode.Exclude, null);
            foreach (var (dx, dy) in new[] { (0, -11), (11, 0), (0, 11), (-11, 0) })
                cuerpo = Geometry.Combine(cuerpo, new EllipseGeometry(new Point(184 + dx, 115 + dy), 5.5, 5.5), GeometryCombineMode.Exclude, null);
        }
        return cuerpo;
    }

    /// <summary>Triángulo que apunta a la derecha, con esquinas redondeadas.</summary>
    static Geometry Triangulo(Point arribaIzquierda, double alto, double radio)
    {
        var (x, y) = (arribaIzquierda.X, arribaIzquierda.Y);
        var ancho = alto * 0.92;
        var figura = new PathFigure(new Point(x, y), [
            new LineSegment(new Point(x + ancho, y + alto / 2), true),
            new LineSegment(new Point(x, y + alto), true),
        ], true);
        var triangulo = new PathGeometry([figura]);
        var redondeo = triangulo.GetWidenedPathGeometry(new Pen(Brushes.Black, radio * 2) { LineJoin = PenLineJoin.Round });
        return Geometry.Combine(triangulo, redondeo, GeometryCombineMode.Union, null);
    }

    static byte[] Png(BitmapSource imagen)
    {
        var codificador = new PngBitmapEncoder();
        codificador.Frames.Add(BitmapFrame.Create(imagen));
        using var ms = new MemoryStream();
        codificador.Save(ms);
        return ms.ToArray();
    }

    /// <summary>ICO con 256 px en PNG y el resto en DIB de 32 bits (BGRA sin premultiplicar + máscara AND).</summary>
    static void EscribirIco(string ruta, List<(int Tamano, BitmapSource Imagen)> imagenes)
    {
        var datos = imagenes.Select(i => i.Tamano >= 256 ? Png(i.Imagen) : Dib(i.Imagen, i.Tamano)).ToList();
        using var f = new BinaryWriter(File.Create(ruta));
        f.Write((ushort)0);
        f.Write((ushort)1);
        f.Write((ushort)imagenes.Count);
        var desplazamiento = 6 + 16 * imagenes.Count;
        for (var i = 0; i < imagenes.Count; i++)
        {
            var t = imagenes[i].Tamano;
            f.Write((byte)(t >= 256 ? 0 : t));
            f.Write((byte)(t >= 256 ? 0 : t));
            f.Write((byte)0);
            f.Write((byte)0);
            f.Write((ushort)1);
            f.Write((ushort)32);
            f.Write(datos[i].Length);
            f.Write(desplazamiento);
            desplazamiento += datos[i].Length;
        }
        foreach (var d in datos)
            f.Write(d);
    }

    static byte[] Dib(BitmapSource imagen, int t)
    {
        var pixeles = new byte[t * t * 4];
        imagen.CopyPixels(pixeles, t * 4, 0);
        var filaMascara = (t + 31) / 32 * 4;
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(40);
        w.Write(t);
        w.Write(t * 2);
        w.Write((ushort)1);
        w.Write((ushort)32);
        w.Write(0);
        w.Write(t * t * 4 + filaMascara * t);
        w.Write(0);
        w.Write(0);
        w.Write(0);
        w.Write(0);
        for (var y = t - 1; y >= 0; y--)
            for (var x = 0; x < t; x++)
            {
                var i = (y * t + x) * 4;
                var a = pixeles[i + 3];
                byte Des(byte c) => a == 0 ? (byte)0 : (byte)Math.Min(255, c * 255 / a);
                w.Write(Des(pixeles[i]));
                w.Write(Des(pixeles[i + 1]));
                w.Write(Des(pixeles[i + 2]));
                w.Write(a);
            }
        for (var y = t - 1; y >= 0; y--)
        {
            var fila = new byte[filaMascara];
            for (var x = 0; x < t; x++)
                if (pixeles[(y * t + x) * 4 + 3] == 0)
                    fila[x / 8] |= (byte)(0x80 >> (x % 8));
            w.Write(fila);
        }
        return ms.ToArray();
    }
}
