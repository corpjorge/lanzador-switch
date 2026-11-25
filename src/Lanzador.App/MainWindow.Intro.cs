using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Lanzador.App.Audio;

namespace Lanzador.App;

public partial class MainWindow
{
    private const double InicioSonidoIntro = 0.16;
    private const double FinIntro = 5.1;
    private bool _introVisible;
    private Storyboard? _historiaIntro;
    private DispatcherTimer? _sonidoIntro;
    private CancellationTokenSource? _audioIntro;

    public void MostrarIntro()
    {
        _historiaIntro?.Remove(this);
        CancelarAudioIntro();
        Intro.BeginAnimation(OpacityProperty, null);
        Intro.Opacity = 1;
        Intro.Visibility = Visibility.Visible;
        _introVisible = true;

        var salida = Curva("ease-out");
        var suave = Curva("ease-in-out");
        var historia = new Storyboard();

        void Animar(DependencyObject objetivo, DependencyProperty propiedad, IEasingFunction curva,
            params (double Segundo, double Valor)[] puntos)
        {
            var pista = new DoubleAnimationUsingKeyFrames();
            foreach (var (segundo, valor) in puntos)
                pista.KeyFrames.Add(new EasingDoubleKeyFrame(valor, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(segundo)), curva));
            Storyboard.SetTarget(pista, objetivo);
            Storyboard.SetTargetProperty(pista, new PropertyPath(propiedad));
            historia.Children.Add(pista);
        }

        void Escalar(ScaleTransform objetivo, IEasingFunction curva, params (double Segundo, double Valor)[] puntos)
        {
            Animar(objetivo, ScaleTransform.ScaleXProperty, curva, puntos);
            Animar(objetivo, ScaleTransform.ScaleYProperty, curva, puntos);
        }

        // Entrada de dos líneas y anillos que se abren alrededor del centro.
        Animar(HaloIntro, OpacityProperty, suave, (0, 0), (0.95, 0.72), (2.12, 1), (3.75, 0.66), (4.72, 0));
        Animar(RielSuperiorIntro, OpacityProperty, suave, (0, 0), (0.29, 0), (0.76, 0.9), (1.44, 0.7), (1.98, 0));
        Animar(RielInferiorIntro, OpacityProperty, suave, (0, 0), (0.49, 0), (0.96, 0.78), (1.58, 0.6), (2.04, 0));
        Animar(DesplazarRielSuperiorIntro, TranslateTransform.XProperty, salida, (0, -610), (0.29, -610), (1.42, -6), (2.02, 92));
        Animar(DesplazarRielInferiorIntro, TranslateTransform.XProperty, salida, (0, 610), (0.49, 610), (1.57, 6), (2.07, -92));
        Animar(AnillosIntro, OpacityProperty, suave, (0, 0), (0.53, 0), (1.72, 0.77), (2.48, 0.65), (4.44, 0));
        Escalar(EscalaAnillosIntro, salida, (0, 0.31), (0.53, 0.31), (1.82, 0.83), (4.44, 1.24));

        // El pulso coincide con el golpe, luego habla la voz sobre la palabra.
        Animar(PulsoIntro, OpacityProperty, suave, (0, 0), (1.6, 0), (1.82, 0.7), (3.08, 0));
        Escalar(EscalaPulsoIntro, salida, (0, 0.55), (1.6, 0.55), (3.08, 1.74));
        Animar(LogoIntro, OpacityProperty, salida, (0, 0), (1.5, 0), (2.1, 1));
        Escalar(EscalaIntro, salida, (0, 0.88), (1.5, 0.88), (2.21, 1), (3.85, 1), (FinIntro, 1.06));
        Animar(DesplazarLogoIntro, TranslateTransform.YProperty, salida, (0, 21), (1.5, 21), (2.21, 0), (3.85, 0), (FinIntro, -14));
        Animar(ResplandorIntro, OpacityProperty, suave, (0, 0), (1.55, 0), (2.18, 0.85), (3.3, 0.36), (4.65, 0.1));
        Animar(DestelloIntro, TranslateTransform.XProperty, suave, (0, -0.85), (2.12, -0.85), (3.12, 0.85));
        Animar(LineaIntro, WidthProperty, salida, (0, 0), (2.21, 0), (2.97, 340), (3.84, 340), (FinIntro, 440));
        Animar(LineaIntro, OpacityProperty, suave, (0, 0), (2.21, 0), (2.97, 0.9), (3.84, 0.78), (FinIntro, 0));

        Animar(Intro, OpacityProperty, suave, (0, 1), (4.2, 1), (FinIntro, 0));
        Animar(Contenido, OpacityProperty, salida, (0, 1), (4.2, 1), (4.21, 0), (5, 1));
        Animar(DesplazamientoContenido, TranslateTransform.YProperty, salida, (0, 24), (4.2, 24), (5, 0));

        historia.Completed += (_, _) =>
        {
            if (_historiaIntro != historia)
                return;
            _introVisible = false;
            _historiaIntro = null;
            Intro.Visibility = Visibility.Collapsed;
            historia.Remove(this);
            CancelarAudioIntro();
        };
        _historiaIntro = historia;
        historia.Begin(this, isControllable: true);

        if (Sonidos.Activados && Sonidos.Volumen > 0)
        {
            Sonidos.MantenerSalida(true);
            _audioIntro = new CancellationTokenSource();
            var token = _audioIntro.Token;
            var temporizador = new DispatcherTimer { Interval = TimeSpan.FromSeconds(InicioSonidoIntro) };
            _sonidoIntro = temporizador;
            temporizador.Tick += (_, _) =>
            {
                temporizador.Stop();
                if (!token.IsCancellationRequested)
                    Sonidos.Tocar(Sonido.Arranque, token);
            };
            temporizador.Start();
        }
    }

    private void CancelarAudioIntro()
    {
        _sonidoIntro?.Stop();
        _sonidoIntro = null;
        _audioIntro?.Cancel();
        _audioIntro?.Dispose();
        _audioIntro = null;
    }

    private void SaltarIntro()
    {
        if (!_introVisible)
            return;
        CancelarAudioIntro();
        var historia = _historiaIntro;
        historia?.Pause(this);
        _introVisible = false;
        var fundido = new DoubleAnimation(Intro.Opacity, 0, TimeSpan.FromMilliseconds(180));
        fundido.Completed += (_, _) =>
        {
            if (_historiaIntro != historia || _introVisible)
                return;
            _historiaIntro = null;
            Intro.Visibility = Visibility.Collapsed;
            historia?.Remove(this);
            EntrarContenido();
        };
        Intro.BeginAnimation(OpacityProperty, fundido, HandoffBehavior.SnapshotAndReplace);
    }

    private void EntrarContenido()
    {
        var duracion = Duracion("dur-page");
        var curva = Curva("ease-out");
        Contenido.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duracion) { EasingFunction = curva });
        DesplazamientoContenido.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(24, 0, TimeSpan.FromMilliseconds(320)) { EasingFunction = curva });
    }

    private void AlPulsarIntro(object sender, MouseButtonEventArgs e) => SaltarIntro();
}
