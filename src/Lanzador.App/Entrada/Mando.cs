using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Lanzador.App.Entrada;

public enum Accion
{
    Arriba,
    Abajo,
    Izquierda,
    Derecha,
    Aceptar,
    Atras,
    Trucos,
    Opciones,
    Menu,
    Vista,
    AnteriorPestana,
    SiguientePestana,
}

/// <summary>Tipo de mando, para dibujar sus botones en la interfaz.</summary>
public enum FamiliaMando
{
    Xbox,
    PlayStation,
    Nintendo,
}

/// <summary>
/// Lee los mandos XInput (Xbox y cualquier mando que Steam Input presente como Xbox) y los convierte en acciones,
/// con repetición al mantener una dirección.
/// </summary>
public sealed class Mando : IDisposable
{
    private const int Desconectado = 1167;
    private const short UmbralStick = 16000;

    /// <summary>Vista + LB + RB: no choca con los atajos de Eden (Inicio + Menos con L/R) ni con trucos de botones.</summary>
    private const ushort ComboCerrar = 0x0020 | 0x0100 | 0x0200;
    public static readonly TimeSpan TiempoComboCerrar = TimeSpan.FromSeconds(1.5);
    public const string TextoComboCerrar = "Vista + LB + RB";

    /// <summary>La combinación en los tres mandos (los mismos botones en cada uno).</summary>
    public const string TextoComboTodos = "Vista + LB + RB (Xbox), Create + L1 + R1 (PS5) o − + L + R (Switch Pro)";

    /// <summary>Glifos de la combinación por mando, para mostrarla en pantalla.</summary>
    public static readonly (string Mando, string[] Glifos)[] CombosCerrar =
    [
        ("Xbox", ["Vista", "LB", "RB"]),
        ("PS5", ["Create", "L1", "R1"]),
        ("Switch Pro", ["−", "L", "R"]),
    ];
    private static readonly TimeSpan PrimeraRepeticion = TimeSpan.FromMilliseconds(380);
    private static readonly TimeSpan Repeticion = TimeSpan.FromMilliseconds(110);
    private static readonly TimeSpan ReintentoDesconectado = TimeSpan.FromSeconds(2);

    private readonly DispatcherTimer _temporizador;
    private readonly ushort[] _botonesPrevios = new ushort[4];
    private readonly DateTime[] _proximoIntento = new DateTime[4];
    private Accion? _direccion;
    private DateTime _proximaRepeticion;
    private DateTime? _comboDesde;
    private bool _comboDisparado;
    private bool _disponible = true;

    // Mandos que no son XInput (Pro Controller, PlayStation…), leídos con SDL3 solo mientras el lanzador está al frente:
    // así no se interfiere con los emuladores, que también los abren.
    private readonly Dictionary<uint, (IntPtr Mando, ushort Previos, FamiliaMando Familia)> _sdl = new();
    private FamiliaMando _familia = FamiliaMando.Xbox;
    private DateTime _proximaEnumeracionSdl;
    private bool _sdlActivo;

    /// <summary>Activa o suelta los mandos leídos con SDL3 (se activa cuando la ventana tiene el foco).</summary>
    public bool SdlActivo
    {
        get => _sdlActivo;
        set
        {
            if (_sdlActivo == value)
                return;
            _sdlActivo = value;
            if (!value)
                SoltarSdl();
            _proximaEnumeracionSdl = DateTime.MinValue;
        }
    }

    public event Action<Accion>? AlPulsar;

    /// <summary>Cambió el tipo del mando que se está usando (para dibujar sus botones).</summary>
    public event Action<FamiliaMando>? AlCambiarFamilia;

    public FamiliaMando Familia => _familia;

    private void Usar(FamiliaMando familia)
    {
        if (familia == _familia)
            return;
        _familia = familia;
        AlCambiarFamilia?.Invoke(familia);
    }

    /// <summary>PlayStation por Sony; Switch por Nintendo (SDL presenta así también los compatibles, como el PDP); el resto, Xbox.</summary>
    private static FamiliaMando FamiliaDe(Lanzador.Core.Controles.MandoConectado mando) => mando.Fabricante switch
    {
        0x054C => FamiliaMando.PlayStation,
        0x057E => FamiliaMando.Nintendo,
        _ => FamiliaMando.Xbox,
    };

    /// <summary>Se mantuvo la combinación para cerrar el juego. Se avisa aunque la ventana no tenga el foco.</summary>
    public event Action? AlMantenerComboCerrar;

    public Mando()
    {
        _temporizador = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(16) };
        _temporizador.Tick += (_, _) => Leer();
        _temporizador.Start();
    }

    private void Leer()
    {
        if (!_disponible)
            return;

        ushort botones = 0;
        short stickX = 0, stickY = 0;
        var ahora = DateTime.UtcNow;
        for (var i = 0; i < 4; i++)
        {
            if (ahora < _proximoIntento[i])
                continue;
            int resultado;
            XInputState estado;
            try
            {
                resultado = XInputGetState(i, out estado);
            }
            catch (DllNotFoundException)
            {
                _disponible = false;
                return;
            }
            if (resultado == Desconectado)
            {
                _proximoIntento[i] = ahora + ReintentoDesconectado;
                _botonesPrevios[i] = 0;
                continue;
            }
            if (resultado != 0)
                continue;

            var b = estado.Gamepad.Botones;
            var nuevos = (ushort)(b & ~_botonesPrevios[i]);
            _botonesPrevios[i] = b;
            if (nuevos != 0 || Math.Abs((int)estado.Gamepad.StickIzqX) > UmbralStick || Math.Abs((int)estado.Gamepad.StickIzqY) > UmbralStick)
                Usar(FamiliaMando.Xbox);
            botones |= b;
            if (Math.Abs((int)estado.Gamepad.StickIzqX) > Math.Abs((int)stickX)) stickX = estado.Gamepad.StickIzqX;
            if (Math.Abs((int)estado.Gamepad.StickIzqY) > Math.Abs((int)stickY)) stickY = estado.Gamepad.StickIzqY;

            Disparar(nuevos, 0x1000, Accion.Aceptar);
            Disparar(nuevos, 0x2000, Accion.Atras);
            Disparar(nuevos, 0x4000, Accion.Trucos);
            Disparar(nuevos, 0x8000, Accion.Opciones);
            Disparar(nuevos, 0x0010, Accion.Menu);
            Disparar(nuevos, 0x0020, Accion.Vista);
            Disparar(nuevos, 0x0100, Accion.AnteriorPestana);
            Disparar(nuevos, 0x0200, Accion.SiguientePestana);
        }

        var (botonesSdl, sdlX, sdlY) = LeerSdl(ahora);
        botones |= botonesSdl;
        if (Math.Abs((int)sdlX) > Math.Abs((int)stickX)) stickX = sdlX;
        if (Math.Abs((int)sdlY) > Math.Abs((int)stickY)) stickY = sdlY;

        if ((botones & ComboCerrar) == ComboCerrar)
        {
            _comboDesde ??= ahora;
            if (!_comboDisparado && ahora - _comboDesde >= TiempoComboCerrar)
            {
                _comboDisparado = true;
                AlMantenerComboCerrar?.Invoke();
            }
        }
        else
        {
            _comboDesde = null;
            _comboDisparado = false;
        }

        Accion? direccion =
            (botones & 0x0001) != 0 || stickY > UmbralStick ? Accion.Arriba :
            (botones & 0x0002) != 0 || stickY < -UmbralStick ? Accion.Abajo :
            (botones & 0x0004) != 0 || stickX < -UmbralStick ? Accion.Izquierda :
            (botones & 0x0008) != 0 || stickX > UmbralStick ? Accion.Derecha :
            null;

        if (direccion != _direccion)
        {
            _direccion = direccion;
            if (direccion is { } d)
            {
                AlPulsar?.Invoke(d);
                _proximaRepeticion = ahora + PrimeraRepeticion;
            }
        }
        else if (direccion is { } d && ahora >= _proximaRepeticion)
        {
            AlPulsar?.Invoke(d);
            _proximaRepeticion = ahora + Repeticion;
        }
    }

    /// <summary>
    /// Lee los mandos que no son XInput y devuelve sus botones con los mismos bits que XInput. Los botones van por
    /// posición, como en Xbox: el de abajo acepta y el de la derecha vuelve, en todos los mandos.
    /// </summary>
    private (ushort Botones, short StickX, short StickY) LeerSdl(DateTime ahora)
    {
        if (!_sdlActivo || !Lanzador.Core.Controles.Sdl3.Disponible)
            return (0, 0, 0);

        if (ahora >= _proximaEnumeracionSdl)
        {
            _proximaEnumeracionSdl = ahora + ReintentoDesconectado;
            var actuales = Lanzador.Core.Controles.Sdl3.Mandos().Where(m => !m.EsXInput).ToList();
            foreach (var id in _sdl.Keys.Where(id => actuales.All(m => m.IdSdl != id)).ToList())
            {
                Lanzador.Core.Controles.Sdl3.Cerrar(_sdl[id].Mando);
                _sdl.Remove(id);
            }
            foreach (var m in actuales.Where(m => !_sdl.ContainsKey(m.IdSdl)))
                if (Lanzador.Core.Controles.Sdl3.Abrir(m.IdSdl) is var abierto && abierto != IntPtr.Zero)
                    _sdl[m.IdSdl] = (abierto, 0, FamiliaDe(m));
        }
        if (_sdl.Count == 0)
            return (0, 0, 0);

        Lanzador.Core.Controles.Sdl3.Actualizar();
        // Botón de SDL3 (por posición) → bit de XInput. En PlayStation y Xbox el de abajo acepta y el de la derecha vuelve.
        (int Boton, ushort Bit)[] tabla =
        [
            (0, 0x1000), (1, 0x2000), (2, 0x4000), (3, 0x8000), (4, 0x0020), (6, 0x0010), (9, 0x0100), (10, 0x0200),
            (11, 0x0001), (12, 0x0002), (13, 0x0004), (14, 0x0008),
        ];
        // En los de Switch, como en la consola: A (derecha) acepta, B (abajo) vuelve, X (arriba) e Y (izquierda).
        (int Boton, ushort Bit)[] tablaNintendo =
        [
            (1, 0x1000), (0, 0x2000), (3, 0x4000), (2, 0x8000), (4, 0x0020), (6, 0x0010), (9, 0x0100), (10, 0x0200),
            (11, 0x0001), (12, 0x0002), (13, 0x0004), (14, 0x0008),
        ];
        ushort todos = 0;
        short x = 0, y = 0;
        foreach (var id in _sdl.Keys.ToList())
        {
            var (mando, previos, familia) = _sdl[id];
            ushort b = 0;
            foreach (var (boton, bit) in familia == FamiliaMando.Nintendo ? tablaNintendo : tabla)
                if (Lanzador.Core.Controles.Sdl3.Boton(mando, boton))
                    b |= bit;
            var nuevos = (ushort)(b & ~previos);
            _sdl[id] = (mando, b, familia);
            todos |= b;
            var ex = Lanzador.Core.Controles.Sdl3.Eje(mando, 0);
            var ey = (short)-Math.Clamp((int)Lanzador.Core.Controles.Sdl3.Eje(mando, 1), -32767, 32767); // en SDL abajo es positivo
            if (Math.Abs((int)ex) > Math.Abs((int)x)) x = ex;
            if (Math.Abs((int)ey) > Math.Abs((int)y)) y = ey;
            if (nuevos != 0 || Math.Abs((int)ex) > UmbralStick || Math.Abs((int)ey) > UmbralStick)
                Usar(familia);

            Disparar(nuevos, 0x1000, Accion.Aceptar);
            Disparar(nuevos, 0x2000, Accion.Atras);
            Disparar(nuevos, 0x4000, Accion.Trucos);
            Disparar(nuevos, 0x8000, Accion.Opciones);
            Disparar(nuevos, 0x0010, Accion.Menu);
            Disparar(nuevos, 0x0020, Accion.Vista);
            Disparar(nuevos, 0x0100, Accion.AnteriorPestana);
            Disparar(nuevos, 0x0200, Accion.SiguientePestana);
        }
        return (todos, x, y);
    }

    private void SoltarSdl()
    {
        foreach (var (mando, _, _) in _sdl.Values)
            Lanzador.Core.Controles.Sdl3.Cerrar(mando);
        _sdl.Clear();
    }

    private void Disparar(ushort nuevos, ushort mascara, Accion accion)
    {
        if ((nuevos & mascara) != 0)
            AlPulsar?.Invoke(accion);
    }

    /// <summary>Olvida lo que estaba pulsado (al volver del emulador, para no repetir pulsaciones).</summary>
    public void Reiniciar()
    {
        if (!_disponible)
            return;
        for (var i = 0; i < 4; i++)
        {
            if (XInputGetState(i, out var estado) == 0)
                _botonesPrevios[i] = estado.Gamepad.Botones;
        }
        _direccion = null;
    }

    public void Dispose()
    {
        _temporizador.Stop();
        SoltarSdl();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputGamepad
    {
        public ushort Botones;
        public byte GatilloIzq;
        public byte GatilloDer;
        public short StickIzqX;
        public short StickIzqY;
        public short StickDerX;
        public short StickDerY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputState
    {
        public uint Paquete;
        public XInputGamepad Gamepad;
    }

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static extern int XInputGetState(int indice, out XInputState estado);
}
