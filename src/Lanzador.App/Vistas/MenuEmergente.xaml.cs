using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Lanzador.App.Entrada;
using Lanzador.App.Modelos;
using Lanzador.App.Audio;

namespace Lanzador.App.Vistas;

public sealed class OpcionMenu(string texto, string? detalle, Action? accion, bool marcada = false, bool deshabilitada = false,
    System.Windows.Media.ImageSource? icono = null) : Observable
{
    private bool _seleccionada;

    public string Texto { get; } = texto;

    /// <summary>Icono a la izquierda (p. ej. el del emulador), o null.</summary>
    public System.Windows.Media.ImageSource? Icono { get; } = icono;
    public string? Detalle { get; } = detalle;
    public Action? Accion { get; } = accion;
    public bool Marcada { get; } = marcada;
    public bool Deshabilitada { get; } = deshabilitada;
    public bool Seleccionada { get => _seleccionada; set => Fijar(ref _seleccionada, value); }
}

public partial class MenuEmergente : UserControl, IPantalla
{
    private readonly List<OpcionMenu> _opciones;
    private readonly Action? _alCancelar;
    private int _indice;

    public MenuEmergente(string titulo, string? subtitulo, IEnumerable<OpcionMenu> opciones, Action? alCancelar = null)
    {
        InitializeComponent();
        TextoTitulo.Text = titulo;
        TextoSubtitulo.Text = subtitulo ?? "";
        TextoSubtitulo.Visibility = string.IsNullOrEmpty(subtitulo) ? Visibility.Collapsed : Visibility.Visible;
        _opciones = opciones.ToList();
        _alCancelar = alCancelar;
        ListaOpciones.ItemsSource = _opciones;
        _indice = Math.Max(0, _opciones.FindIndex(o => !o.Deshabilitada));
        Seleccionar(_indice);
        Loaded += (_, _) => Aparecer();
    }

    /// <summary>Entra con fundido y escala 0.96 → 1 (dur-page, ease-out), como pide el MenuDialog.</summary>
    private void Aparecer()
    {
        var duracion = MainWindow.Duracion("dur-page");
        var curva = MainWindow.Curva("ease-out");
        Atenuado.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duracion));
        Caja.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duracion) { EasingFunction = curva });
        var escala = new DoubleAnimation(0.96, 1, duracion) { EasingFunction = curva };
        EscalaCaja.BeginAnimation(ScaleTransform.ScaleXProperty, escala);
        EscalaCaja.BeginAnimation(ScaleTransform.ScaleYProperty, escala);
    }

    public IReadOnlyList<Ayuda> Ayudas { get; } = [new("A", "Elegir", "Enter"), new("B", "Volver", "Esc")];

    public void Ejecutar(Accion accion)
    {
        switch (accion)
        {
            case Accion.Arriba: Mover(-1); break;
            case Accion.Abajo: Mover(1); break;
            case Accion.Aceptar: Elegir(_indice); break;
            case Accion.Atras: Cancelar(); break;
        }
    }

    private void Mover(int paso)
    {
        if (_opciones.Count == 0)
            return;
        var i = _indice;
        for (var n = 0; n < _opciones.Count; n++)
        {
            i = (i + paso + _opciones.Count) % _opciones.Count;
            if (!_opciones[i].Deshabilitada)
                break;
        }
        Sonidos.Moverse(i != _indice);
        Seleccionar(i);
    }

    private void Seleccionar(int indice)
    {
        if (_opciones.Count == 0)
            return;
        _opciones[_indice].Seleccionada = false;
        _indice = indice;
        _opciones[_indice].Seleccionada = true;
        Dispatcher.BeginInvoke(() =>
        {
            if (ListaOpciones.ItemContainerGenerator.ContainerFromIndex(_indice) is FrameworkElement contenedor)
                contenedor.BringIntoView();
        });
    }

    private void Elegir(int indice)
    {
        if (indice < 0 || indice >= _opciones.Count || _opciones[indice].Deshabilitada)
            return;
        Sonidos.Tocar(Sonido.Elegir);
        // Primero la acción (puede abrir otro menú encima) y después se cierra este,
        // así la pantalla de abajo se refresca con los cambios ya guardados.
        _opciones[indice].Accion?.Invoke();
        MainWindow.Actual.CerrarMenu(this);
    }

    private void Cancelar()
    {
        Sonidos.Tocar(Sonido.Volver);
        MainWindow.Actual.CerrarMenu(this);
        _alCancelar?.Invoke();
    }

    private void AlPulsarOpcion(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is OpcionMenu opcion)
            Elegir(_opciones.IndexOf(opcion));
    }

    private void AlPulsarFondo(object sender, MouseButtonEventArgs e) => Cancelar();
}
