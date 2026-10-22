using System.Windows;
using System.Windows.Media.Animation;

namespace Lanzador.App.Tema;

/// <summary>
/// Curva de aceleración cubic-bezier(x1, y1, x2, y2), la misma que usan los tokens <c>ease-out</c> y <c>ease-in-out</c>
/// del Design System (WPF no la trae).
/// </summary>
public sealed class CurvaBezier : EasingFunctionBase
{
    public double X1 { get; set; }
    public double Y1 { get; set; }
    public double X2 { get; set; } = 1;
    public double Y2 { get; set; } = 1;

    public CurvaBezier() => EasingMode = EasingMode.EaseIn; // EaseIn = la curva tal cual

    protected override double EaseInCore(double tiempo)
    {
        if (tiempo <= 0) return 0;
        if (tiempo >= 1) return 1;
        // Busca el parámetro t cuyo x(t) es el tiempo (Newton con respaldo de bisección) y devuelve y(t).
        double t = tiempo;
        for (var i = 0; i < 8; i++)
        {
            var x = Bezier(t, X1, X2) - tiempo;
            var d = Derivada(t, X1, X2);
            if (Math.Abs(x) < 1e-5) return Bezier(t, Y1, Y2);
            if (Math.Abs(d) < 1e-6) break;
            t -= x / d;
        }
        double bajo = 0, alto = 1;
        t = tiempo;
        for (var i = 0; i < 20; i++)
        {
            var x = Bezier(t, X1, X2);
            if (Math.Abs(x - tiempo) < 1e-5) break;
            if (x < tiempo) bajo = t; else alto = t;
            t = (bajo + alto) / 2;
        }
        return Bezier(t, Y1, Y2);
    }

    private static double Bezier(double t, double p1, double p2) =>
        3 * (1 - t) * (1 - t) * t * p1 + 3 * (1 - t) * t * t * p2 + t * t * t;

    private static double Derivada(double t, double p1, double p2) =>
        3 * (1 - t) * (1 - t) * p1 + 6 * (1 - t) * t * (p2 - p1) + 3 * t * t * (1 - p2);

    protected override Freezable CreateInstanceCore() => new CurvaBezier();
}
