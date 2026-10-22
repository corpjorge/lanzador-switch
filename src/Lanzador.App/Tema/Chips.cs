using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Shapes;
using Lanzador.Core;

namespace Lanzador.App.Tema;

/// <summary>Tono de un Chip del Design System: el punto y el fondo repiten el estado; el texto lo dice.</summary>
public enum Tono
{
    Neutro,
    Ok,
    Aviso,
    Error,
    Info,
}

/// <summary>Crea los Chips del Design System desde el código (etiquetas de estado de un juego, de un emulador…).</summary>
public static class Chips
{
    public static Border Crear(string texto, Tono tono, bool estrella = false)
    {
        var app = Application.Current;
        var fila = new StackPanel { Orientation = Orientation.Horizontal };
        if (tono != Tono.Neutro)
        {
            fila.Children.Add(new Ellipse
            {
                Width = 10,
                Height = 10,
                Margin = new Thickness(0, 1, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Fill = (Brush)app.FindResource(tono switch
                {
                    Tono.Ok => "ok-500",
                    Tono.Aviso => "warn-500",
                    Tono.Error => "danger-500",
                    _ => "focus",
                }),
            });
        }
        var bloque = new TextBlock { Style = (Style)app.FindResource("chip"), VerticalAlignment = VerticalAlignment.Center };
        if (estrella)
            bloque.Inlines.Add(new Run("★ ") { Foreground = (Brush)app.FindResource("star-400") });
        bloque.Inlines.Add(new Run(texto));
        fila.Children.Add(bloque);

        var chip = new Border
        {
            Style = (Style)app.FindResource("Chip"),
            Margin = new Thickness(0, 0, 8, 8),
            Child = fila,
        };
        if (tono != Tono.Neutro)
        {
            chip.Background = (Brush)app.FindResource(tono switch
            {
                Tono.Ok => "ok-soft",
                Tono.Aviso => "warn-soft",
                Tono.Error => "danger-soft",
                _ => "accent-soft",
            });
        }
        return chip;
    }

    public static Tono DeCalificacion(Calificacion calificacion) => calificacion switch
    {
        Calificacion.Funciona => Tono.Ok,
        Calificacion.ConFallos => Tono.Aviso,
        Calificacion.NoArranca => Tono.Error,
        _ => Tono.Neutro,
    };

    /// <summary>Nivel de compatibilidad: 4 = jugable, 3 = con fallos, menos = no se puede jugar; -1 = sin probar.</summary>
    public static Tono DeCompatibilidad(int nivel) => nivel switch
    {
        >= 4 => Tono.Ok,
        3 => Tono.Aviso,
        < 0 => Tono.Neutro,
        _ => Tono.Error,
    };
}
