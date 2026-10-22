using System.Runtime.InteropServices;

namespace Lanzador.Core.Controles;

/// <summary>Un mando conectado, con el GUID de SDL tal como lo guardan Eden, Citron, Suyu y Ryujinx.</summary>
public sealed record MandoConectado(uint IdSdl, string Guid, int Puerto, string Nombre, ushort Fabricante, ushort Producto, string? Mapeo)
{
    /// <summary>Byte 14 del GUID: 'x' = XInput, 'h' = HIDAPI, 'r' = RawInput, 'w' = WGI.</summary>
    public char Controlador => Guid.Length == 32 ? (char)Convert.ToByte(Guid.Substring(28, 2), 16) : '\0';

    public bool EsXInput => Controlador == 'x';
    public string NombreLegible => Controles.NombreMando(Guid);
}

/// <summary>
/// Acceso mínimo a SDL3 (SDL3.dll junto al lanzador, la misma librería que usa Ryujinx) para enumerar mandos y leer sus
/// botones. Se configura igual que los emuladores para que los GUID coincidan: Xbox por XInput y el resto por HIDAPI.
/// Todas las llamadas deben hacerse desde el mismo hilo (el de la interfaz).
/// </summary>
public static class Sdl3
{
    private const string Dll = "SDL3";
    private const uint InitGamepad = 0x00002000;
    private static bool? _iniciado;

    public static bool Disponible => _iniciado ??= Iniciar();

    /// <summary>Por qué no se pudo usar SDL3, si falló.</summary>
    public static string? Error { get; private set; }

    private static bool Iniciar()
    {
        try
        {
            SDL_SetHint("SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS", "1");
            SDL_SetHint("SDL_JOYSTICK_RAWINPUT", "0");
            SDL_SetHint("SDL_JOYSTICK_WGI", "0");
            SDL_SetHint("SDL_XINPUT_ENABLED", "1");
            SDL_SetHint("SDL_JOYSTICK_HIDAPI", "1");
            SDL_SetHint("SDL_JOYSTICK_HIDAPI_XBOX", "0");
            // No tocar luces ni modos de los mandos de Nintendo: los emuladores también los usan.
            SDL_SetHint("SDL_JOYSTICK_HIDAPI_SWITCH_PLAYER_LED", "0");
            SDL_SetHint("SDL_JOYSTICK_HIDAPI_SWITCH_HOME_LED", "0");
            if (!SDL_Init(InitGamepad))
            {
                Error = Marshal.PtrToStringUTF8(SDL_GetError()) ?? "SDL_Init falló";
                return false;
            }
            return true;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            Error = $"No se pudo cargar SDL3.dll: {ex.Message}";
            return false;
        }
    }

    /// <summary>Procesa conexiones y desconexiones y descarta la cola de eventos (no se usan).</summary>
    public static void Actualizar()
    {
        if (!Disponible)
            return;
        SDL_PumpEvents();
        SDL_UpdateGamepads();
        SDL_FlushEvents(0, 0xFFFF);
    }

    public static List<MandoConectado> Mandos()
    {
        var lista = new List<MandoConectado>();
        if (!Disponible)
            return lista;
        Actualizar();
        var arreglo = SDL_GetGamepads(out var cantidad);
        if (arreglo == IntPtr.Zero)
            return lista;
        try
        {
            var vistos = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < cantidad; i++)
            {
                var id = (uint)Marshal.ReadInt32(arreglo, i * 4);
                var g = SDL_GetJoystickGUIDForID(id);
                var bytes = BitConverter.GetBytes(g.A).Concat(BitConverter.GetBytes(g.B)).ToArray();
                bytes[2] = bytes[3] = 0; // Eden y Ryujinx guardan el GUID sin el CRC del nombre
                var guid = Convert.ToHexStringLower(bytes);
                var puerto = vistos.TryGetValue(guid, out var n) ? n + 1 : 0;
                vistos[guid] = puerto;
                string? mapeo = null;
                var p = SDL_GetGamepadMappingForID(id);
                if (p != IntPtr.Zero)
                {
                    mapeo = Marshal.PtrToStringUTF8(p);
                    SDL_free(p);
                }
                lista.Add(new MandoConectado(id, guid, puerto, Marshal.PtrToStringUTF8(SDL_GetGamepadNameForID(id)) ?? "Mando",
                    SDL_GetJoystickVendorForID(id), SDL_GetJoystickProductForID(id), mapeo));
            }
        }
        finally
        {
            SDL_free(arreglo);
        }
        return lista;
    }

    public static IntPtr Abrir(uint id) => Disponible ? SDL_OpenGamepad(id) : IntPtr.Zero;

    public static void Cerrar(IntPtr mando)
    {
        if (mando != IntPtr.Zero)
            SDL_CloseGamepad(mando);
    }

    /// <summary>Botones por posición (SDL3): 0 abajo, 1 derecha, 2 izquierda, 3 arriba, 4 Back, 5 Guide, 6 Start, 9 LB, 10 RB, 11-14 cruceta.</summary>
    public static bool Boton(IntPtr mando, int boton) => mando != IntPtr.Zero && SDL_GetGamepadButton(mando, boton);

    /// <summary>Eje del mando: 0 stick izq X, 1 stick izq Y (abajo positivo), 4 y 5 gatillos.</summary>
    public static short Eje(IntPtr mando, int eje) => mando != IntPtr.Zero ? SDL_GetGamepadAxis(mando, eje) : (short)0;

    [StructLayout(LayoutKind.Sequential)]
    private struct SdlGuid
    {
        public ulong A;
        public ulong B;
    }

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool SDL_Init(uint flags);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool SDL_SetHint([MarshalAs(UnmanagedType.LPUTF8Str)] string nombre, [MarshalAs(UnmanagedType.LPUTF8Str)] string valor);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr SDL_GetError();

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern void SDL_PumpEvents();

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern void SDL_FlushEvents(uint minimo, uint maximo);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern void SDL_UpdateGamepads();

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr SDL_GetGamepads(out int cantidad);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern SdlGuid SDL_GetJoystickGUIDForID(uint id);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr SDL_GetGamepadNameForID(uint id);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr SDL_GetGamepadMappingForID(uint id);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern ushort SDL_GetJoystickVendorForID(uint id);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern ushort SDL_GetJoystickProductForID(uint id);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr SDL_OpenGamepad(uint id);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern void SDL_CloseGamepad(IntPtr mando);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool SDL_GetGamepadButton(IntPtr mando, int boton);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern short SDL_GetGamepadAxis(IntPtr mando, int eje);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern void SDL_free(IntPtr memoria);
}
