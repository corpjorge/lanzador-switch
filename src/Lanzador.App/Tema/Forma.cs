using System.Windows;
using System.Windows.Controls;

namespace Lanzador.App.Tema;

/// <summary>
/// <c>radius-pill</c> en WPF: un CornerRadius de 999 se deforma en bordes anchos (WPF reparte el radio entre los lados),
/// así que <c>tema:Forma.Pastilla="True"</c> redondea el Border con la mitad de su alto real.
/// </summary>
public static class Forma
{
    public static readonly DependencyProperty PastillaProperty = DependencyProperty.RegisterAttached(
        "Pastilla", typeof(bool), typeof(Forma), new PropertyMetadata(false, AlCambiarPastilla));

    public static bool GetPastilla(DependencyObject elemento) => (bool)elemento.GetValue(PastillaProperty);

    public static void SetPastilla(DependencyObject elemento, bool valor) => elemento.SetValue(PastillaProperty, valor);

    private static void AlCambiarPastilla(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Border borde)
            return;
        borde.SizeChanged -= AlCambiarTamano;
        if ((bool)e.NewValue)
        {
            borde.SizeChanged += AlCambiarTamano;
            Redondear(borde);
        }
        else
            borde.InvalidateProperty(Border.CornerRadiusProperty); // vuelve al radio del estilo o del disparador
    }

    private static void AlCambiarTamano(object sender, SizeChangedEventArgs e) => Redondear((Border)sender);

    private static void Redondear(Border borde)
    {
        if (borde.ActualHeight > 0)
            borde.SetCurrentValue(Border.CornerRadiusProperty, new CornerRadius(borde.ActualHeight / 2));
    }
}
