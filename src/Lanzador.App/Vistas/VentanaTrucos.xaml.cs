using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace Lanzador.App.Vistas;

/// <summary>Un truco activo en la ventana flotante: su nombre y qué botones lo activan (si se saben).</summary>
public sealed record TrucoEnPantalla(string Nombre, string? Botones);

/// <summary>
/// Ventana flotante sobre el juego con los trucos activos y sus combinaciones. Se muestra y se oculta con una tecla
/// (F7 por defecto) mientras se juega; no toma el foco (el mando y el teclado siguen yendo al emulador) y los clics la
/// atraviesan. Se ve sobre el emulador en pantalla completa sin bordes (no sobre la pantalla completa exclusiva).
/// </summary>
public partial class VentanaTrucos : Window
{
    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x00000020;
    private const long WsExToolWindow = 0x00000080;
    private const long WsExNoActivate = 0x08000000;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr ventana, int indice);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr ventana, int indice, IntPtr valor);

    public VentanaTrucos()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var estilo = (long)GetWindowLongPtr(hwnd, GwlExStyle);
            SetWindowLongPtr(hwnd, GwlExStyle, (IntPtr)(estilo | WsExNoActivate | WsExToolWindow | WsExTransparent));
        };
    }

    public bool Visible { get; private set; }

    /// <summary>Pone el contenido: juego, detalle (emulador y versión) y trucos, o un mensaje si no hay lista.</summary>
    public void Fijar(string juego, string detalle, IReadOnlyList<TrucoEnPantalla>? trucos, string? mensaje, string tecla)
    {
        TextoJuego.Text = juego;
        TextoDetalle.Text = detalle;
        Lista.ItemsSource = trucos == null ? null : SinPrefijoComun(trucos);
        TextoMensaje.Text = mensaje ?? "";
        TextoMensaje.Visibility = string.IsNullOrEmpty(mensaje) ? Visibility.Collapsed : Visibility.Visible;
        TextoTecla.Text = tecla;
        TextoPie.Text = $"{tecla} para ocultar";
    }

    /// <summary>Si todos empiezan igual ("Bowsers Fury - …"), quita ese prefijo para que se lea más rápido.</summary>
    private static IReadOnlyList<TrucoEnPantalla> SinPrefijoComun(IReadOnlyList<TrucoEnPantalla> trucos)
    {
        if (trucos.Count < 2)
            return trucos;
        var primero = trucos[0].Nombre;
        var corte = primero.IndexOf(" - ", StringComparison.Ordinal);
        if (corte <= 0)
            return trucos;
        var prefijo = primero[..(corte + 3)];
        return trucos.All(t => t.Nombre.StartsWith(prefijo, StringComparison.Ordinal) && t.Nombre.Length > prefijo.Length)
            ? trucos.Select(t => t with { Nombre = t.Nombre[prefijo.Length..] }).ToList()
            : trucos;
    }

    public void Mostrar()
    {
        // Arriba a la derecha de la pantalla principal; si no cabe, el contenido se achica (Viewbox).
        var area = SystemParameters.WorkArea;
        Ajuste.MaxHeight = area.Height;
        Left = area.Right - Width;
        Top = area.Top;
        if (!IsVisible)
            Show();
        Topmost = false;
        Topmost = true; // por encima del emulador aunque acabe de ponerse en pantalla completa
        Visible = true;
        Panel.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(MainWindow.ReducirMovimiento ? 0 : 200)));
    }

    public void Ocultar()
    {
        if (!Visible)
            return;
        Visible = false;
        var fundido = new DoubleAnimation(0, TimeSpan.FromMilliseconds(MainWindow.ReducirMovimiento ? 0 : 160));
        fundido.Completed += (_, _) =>
        {
            if (!Visible)
                Hide();
        };
        Panel.BeginAnimation(OpacityProperty, fundido);
    }

    public void Alternar()
    {
        if (Visible)
            Ocultar();
        else
            Mostrar();
    }

    /// <summary>Modo de prueba: guarda cómo se ve el panel.</summary>
    public void GuardarCaptura(string archivo)
    {
        if (Content is not FrameworkElement raiz || raiz.ActualWidth <= 0)
            return;
        var dpi = VisualTreeHelper.GetDpi(this);
        var bitmap = new RenderTargetBitmap((int)(raiz.ActualWidth * dpi.DpiScaleX), (int)(raiz.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        var fondo = new DrawingVisual();
        using (var dc = fondo.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x30, 0x48, 0x60)), null, new Rect(0, 0, raiz.ActualWidth, raiz.ActualHeight));
        }
        bitmap.Render(fondo); // un fondo de color, como si hubiera un juego detrás
        bitmap.Render(raiz);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        using var salida = File.Create(archivo);
        png.Save(salida);
    }
}
