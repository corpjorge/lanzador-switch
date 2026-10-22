using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Lanzador.App.Entrada;

namespace Lanzador.App.Tema;

/// <summary>
/// El tipo del último mando que se tocó (Xbox, PlayStation o Switch). Los glifos de la interfaz se dibujan con sus
/// botones: ✕ ○ □ △ en PlayStation, A B X Y y L R + − en Switch.
/// </summary>
public sealed class EstiloMando : INotifyPropertyChanged
{
    public static EstiloMando Actual { get; } = new();

    private FamiliaMando _familia = FamiliaMando.Xbox;

    public FamiliaMando Familia
    {
        get => _familia;
        set
        {
            if (_familia == value)
                return;
            _familia = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Familia)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Lo que se ve en el glifo: el botón lógico (A, B, X, Y, LB, RB, ☰, Vista) en el mando actual.</summary>
    public static string Etiqueta(string boton, FamiliaMando familia) => (familia, boton) switch
    {
        (FamiliaMando.PlayStation, "A") => "✕",
        (FamiliaMando.PlayStation, "B") => "○",
        (FamiliaMando.PlayStation, "X") => "◻", // ◻: el □ de la fuente sale muy chico
        (FamiliaMando.PlayStation, "Y") => "△",
        (FamiliaMando.PlayStation, "LB") => "L1",
        (FamiliaMando.PlayStation, "RB") => "R1",
        (FamiliaMando.PlayStation, "Vista") => "Create",
        (FamiliaMando.Nintendo, "LB") => "L",
        (FamiliaMando.Nintendo, "RB") => "R",
        (FamiliaMando.Nintendo, "☰") => "+",
        (FamiliaMando.Nintendo, "Vista") => "−",
        _ => boton,
    };

    /// <summary>Fondo del glifo: colores de Xbox; en PlayStation y Switch, gris como sus botones.</summary>
    public static string Fondo(string boton, FamiliaMando familia) => (familia, boton) switch
    {
        (FamiliaMando.Xbox, "A") => "pad-a",
        (FamiliaMando.Xbox, "B") => "pad-b",
        (FamiliaMando.Xbox, "X") => "pad-x",
        (FamiliaMando.Xbox, "Y") => "pad-y",
        _ => "pad-neutral",
    };

    /// <summary>Color de la letra o el símbolo: en PlayStation, cada figura con su color.</summary>
    public static string Tinta(string boton, FamiliaMando familia) => (familia, boton) switch
    {
        (FamiliaMando.Xbox, "Y") => "on-pad-y",
        (FamiliaMando.PlayStation, "A") => "ps-cruz",
        (FamiliaMando.PlayStation, "B") => "ps-circulo",
        (FamiliaMando.PlayStation, "X") => "ps-cuadrado",
        (FamiliaMando.PlayStation, "Y") => "ps-triangulo",
        _ => "ink-100",
    };
}

/// <summary>Convierte (botón lógico, familia) en la etiqueta, el fondo o la tinta del glifo.</summary>
public sealed class ConvertidorGlifo : IMultiValueConverter
{
    /// <summary>"Etiqueta", "Fondo", "Tinta" o "Tamano".</summary>
    public string Parte { get; set; } = "Etiqueta";

    public object Convert(object[] valores, Type destino, object parametro, CultureInfo cultura)
    {
        var boton = valores.Length > 0 ? valores[0] as string ?? "" : "";
        var familia = valores.Length > 1 && valores[1] is FamiliaMando f ? f : FamiliaMando.Xbox;
        return Parte switch
        {
            "Fondo" => Application.Current.FindResource(EstiloMando.Fondo(boton, familia)),
            "Tinta" => Application.Current.FindResource(EstiloMando.Tinta(boton, familia)),
            // Las figuras de PlayStation se ven chicas a 16 px: un poco más grandes.
            "Tamano" => familia == FamiliaMando.PlayStation && boton is "A" or "B" or "X" or "Y" ? 20.0 : 16.0,
            _ => EstiloMando.Etiqueta(boton, familia),
        };
    }

    public object[] ConvertBack(object valor, Type[] destinos, object parametro, CultureInfo cultura) => throw new NotSupportedException();
}
