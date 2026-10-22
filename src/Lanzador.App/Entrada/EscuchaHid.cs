using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Lanzador.Core.Controles;

namespace Lanzador.App.Entrada;

/// <summary>
/// Escucha el DualSense, el DualShock 4 y el Pro Controller con Raw Input de Windows mientras se juega: recibe una
/// copia de lo que el mando manda al emulador, aunque el lanzador esté minimizado, sin abrir el mando ni enviarle
/// nada (el emulador sigue siendo su dueño). Solo busca la combinación de cerrar el juego.
/// </summary>
public sealed class EscuchaHid
{
    private const int WmInput = 0x00FF;
    private const uint RidInput = 0x10000003;
    private const uint RidiDeviceName = 0x20000007;
    private const uint RidiDeviceInfo = 0x2000000b;
    private const uint RimTypeHid = 2;
    private const uint RideVInputSink = 0x00000100;
    private const uint RideVRemove = 0x00000001;

    [StructLayout(LayoutKind.Sequential)]
    private struct DispositivoRaw
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Opciones;
        public IntPtr Ventana;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices(DispositivoRaw[] dispositivos, uint cantidad, uint tamano);

    [DllImport("user32.dll")]
    private static extern uint GetRawInputData(IntPtr entrada, uint comando, IntPtr datos, ref uint tamano, uint tamanoCabecera);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint GetRawInputDeviceInfo(IntPtr dispositivo, uint comando, IntPtr datos, ref uint tamano);

    private static readonly uint TamanoCabecera = (uint)(8 + 2 * IntPtr.Size); // RAWINPUTHEADER

    private sealed class Estado
    {
        public MandoHid Mando;
        public bool Bluetooth;
        public bool Anotado;
        public DateTime? Desde;
        public bool Disparado;
    }

    private readonly Dictionary<IntPtr, Estado> _dispositivos = new();
    private readonly string? _bitacora = Environment.GetEnvironmentVariable("LANZADOR_MANDOS_LOG");
    private HwndSource? _fuente;
    private bool _activa;

    /// <summary>Se mantuvo la combinación 1,5 s en un mando; trae el texto de la combinación usada.</summary>
    public event Action<string>? AlMantenerComboCerrar;

    /// <summary>Se llama cuando la ventana ya tiene identificador (SourceInitialized).</summary>
    public void Conectar(Window ventana)
    {
        _fuente = HwndSource.FromHwnd(new WindowInteropHelper(ventana).Handle);
        _fuente?.AddHook(Procesar);
    }

    /// <summary>Empieza a escuchar mandos de juego (gamepad y joystick) aunque la ventana no esté al frente.</summary>
    public void Activar()
    {
        if (_activa || _fuente == null)
            return;
        DispositivoRaw[] lista =
        [
            new() { UsagePage = 0x01, Usage = 0x05, Opciones = RideVInputSink, Ventana = _fuente.Handle },
            new() { UsagePage = 0x01, Usage = 0x04, Opciones = RideVInputSink, Ventana = _fuente.Handle },
        ];
        _activa = RegisterRawInputDevices(lista, (uint)lista.Length, (uint)Marshal.SizeOf<DispositivoRaw>());
        Anotar(_activa ? "escucha activada" : $"no se pudo activar la escucha (error {Marshal.GetLastWin32Error()})");
    }

    /// <summary>Deja de escuchar (al volver al lanzador).</summary>
    public void Desactivar()
    {
        if (!_activa)
            return;
        DispositivoRaw[] lista =
        [
            new() { UsagePage = 0x01, Usage = 0x05, Opciones = RideVRemove, Ventana = IntPtr.Zero },
            new() { UsagePage = 0x01, Usage = 0x04, Opciones = RideVRemove, Ventana = IntPtr.Zero },
        ];
        RegisterRawInputDevices(lista, (uint)lista.Length, (uint)Marshal.SizeOf<DispositivoRaw>());
        _activa = false;
        foreach (var estado in _dispositivos.Values)
        {
            estado.Desde = null;
            estado.Disparado = false;
        }
        Anotar("escucha desactivada");
    }

    private IntPtr Procesar(IntPtr ventana, int mensaje, IntPtr wParam, IntPtr lParam, ref bool manejado)
    {
        if (mensaje == WmInput && _activa)
            Leer(lParam);
        return IntPtr.Zero; // DefWindowProc limpia el mensaje
    }

    private void Leer(IntPtr entrada)
    {
        uint tamano = 0;
        GetRawInputData(entrada, RidInput, IntPtr.Zero, ref tamano, TamanoCabecera);
        if (tamano == 0 || tamano > 64 * 1024)
            return;
        var memoria = Marshal.AllocHGlobal((int)tamano);
        try
        {
            if (GetRawInputData(entrada, RidInput, memoria, ref tamano, TamanoCabecera) != tamano)
                return;
            if ((uint)Marshal.ReadInt32(memoria, 0) != RimTypeHid)
                return;
            var dispositivo = Marshal.ReadIntPtr(memoria, 8);
            var estado = EstadoDe(dispositivo);
            if (estado.Mando == MandoHid.Ninguno)
                return;

            // RAWHID: dwSizeHid, dwCount y luego dwCount reportes de dwSizeHid bytes.
            var inicio = (int)TamanoCabecera;
            var largo = Marshal.ReadInt32(memoria, inicio);
            var cantidad = Marshal.ReadInt32(memoria, inicio + 4);
            if (largo <= 0 || cantidad <= 0 || inicio + 8 + (long)largo * cantidad > tamano)
                return;
            var reporte = new byte[largo];
            for (var n = 0; n < cantidad; n++)
            {
                Marshal.Copy(memoria + inicio + 8 + n * largo, reporte, 0, largo);
                Evaluar(estado, reporte);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(memoria);
        }
    }

    private void Evaluar(Estado estado, byte[] reporte)
    {
        if (!estado.Anotado)
        {
            estado.Anotado = true;
            Anotar($"{estado.Mando} ({(estado.Bluetooth ? "Bluetooth" : "cable")}): primer reporte {Convert.ToHexString(reporte, 0, Math.Min(16, reporte.Length))}… ({reporte.Length} bytes)");
        }
        var combo = ReporteHid.ComboCerrar(estado.Mando, estado.Bluetooth, reporte);
        if (combo == null)
            return;
        var ahora = DateTime.UtcNow;
        if (combo == true)
        {
            if (estado.Desde == null)
                Anotar($"{estado.Mando}: combinación pulsada");
            estado.Desde ??= ahora;
            if (!estado.Disparado && ahora - estado.Desde >= Mando.TiempoComboCerrar)
            {
                estado.Disparado = true;
                Anotar($"{estado.Mando}: combinación mantenida, se cierra el juego");
                AlMantenerComboCerrar?.Invoke(ReporteHid.TextoCombo(estado.Mando));
            }
        }
        else
        {
            estado.Desde = null;
            estado.Disparado = false;
        }
    }

    /// <summary>Qué mando es (por vendedor y producto) y si va por Bluetooth (por la ruta del dispositivo).</summary>
    private Estado EstadoDe(IntPtr dispositivo)
    {
        if (_dispositivos.TryGetValue(dispositivo, out var conocido))
            return conocido;
        var estado = new Estado();
        _dispositivos[dispositivo] = estado;

        uint tamano = 32; // RID_DEVICE_INFO
        int vendor = 0, producto = 0;
        var info = Marshal.AllocHGlobal(32);
        try
        {
            Marshal.WriteInt32(info, 0, 32);
            if (GetRawInputDeviceInfo(dispositivo, RidiDeviceInfo, info, ref tamano) > 0 && (uint)Marshal.ReadInt32(info, 4) == RimTypeHid)
            {
                vendor = Marshal.ReadInt32(info, 8);
                producto = Marshal.ReadInt32(info, 12);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(info);
        }

        uint caracteres = 0;
        GetRawInputDeviceInfo(dispositivo, RidiDeviceName, IntPtr.Zero, ref caracteres);
        if (caracteres is > 0 and < 2048)
        {
            var nombre = Marshal.AllocHGlobal((int)caracteres * 2 + 2);
            try
            {
                if (GetRawInputDeviceInfo(dispositivo, RidiDeviceName, nombre, ref caracteres) > 0)
                {
                    var ruta = Marshal.PtrToStringUni(nombre) ?? "";
                    // HID por Bluetooth clásico: {00001124-0000-1000-8000-00805f9b34fb}.
                    estado.Bluetooth = ruta.Contains("00001124-0000-1000-8000-00805F9B34FB", StringComparison.OrdinalIgnoreCase);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(nombre);
            }
        }
        estado.Mando = ReporteHid.Reconocer(vendor, producto, estado.Bluetooth);
        Anotar(estado.Mando == MandoHid.Ninguno
            ? $"mando no reconocido: {vendor:X4}:{producto:X4} por {(estado.Bluetooth ? "Bluetooth" : "cable")} (se ignora)"
            : $"mando detectado: {estado.Mando} ({vendor:X4}:{producto:X4}) por {(estado.Bluetooth ? "Bluetooth" : "cable")}");
        return estado;
    }

    private void Anotar(string texto)
    {
        if (_bitacora == null)
            return;
        try
        {
            File.AppendAllText(_bitacora, $"[{DateTime.Now:HH:mm:ss.fff}] {texto}\r\n");
        }
        catch (IOException) { }
    }
}
