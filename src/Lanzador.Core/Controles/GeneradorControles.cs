using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Lanzador.Core.Actualizaciones;

namespace Lanzador.Core.Controles;

/// <summary>
/// Escribe en cada emulador los mandos elegidos en el lanzador, con la asignación de botones que el propio emulador
/// pondría por defecto al elegir ese mando en su pantalla de controles:
/// <list type="bullet">
/// <item>Eden, Citron, Suyu: si ese mando ya está configurado en alguno de ellos se reutiliza su asignación (con su
/// calibración); si no, se genera según el controlador de SDL: XInput (Xbox), HIDAPI (Pro Controller, PlayStation)
/// o el mapeo de SDL para otros mandos. Por posición, como en Switch: la A es el botón de la derecha.</item>
/// <item>Ryujinx: sus botones tienen nombre genérico; se usa la asignación que ya tiene (o la suya por defecto).</item>
/// </list>
/// </summary>
public static partial class GeneradorControles
{
    public static readonly string[] Tipos = ["Pro Controller", "Joy-Con dobles", "Joy-Con izquierdo", "Joy-Con derecho", "Portátil"];

    /// <summary>Asignación por defecto de un mando de Ryujinx (la que crea Ryujinx al elegir un mando).</summary>
    private const string PlantillaRyujinx = """
        {"left_joycon_stick":{"joystick":"Left","invert_stick_x":false,"invert_stick_y":false,"rotate90_cw":false,"stick_button":"LeftStick"},
         "right_joycon_stick":{"joystick":"Right","invert_stick_x":false,"invert_stick_y":false,"rotate90_cw":false,"stick_button":"RightStick"},
         "deadzone_left":0.1,"deadzone_right":0.1,"range_left":1,"range_right":1,"trigger_threshold":0.5,
         "motion":{"motion_backend":"GamepadDriver","sensitivity":100,"gyro_deadzone":1,"enable_motion":true},
         "rumble":{"strong_rumble":1,"weak_rumble":1,"enable_rumble":true,"use_hdrumble":false},
         "led":{"enable_led":false,"turn_off_led":false,"use_rainbow":false,"led_color":0},
         "left_joycon":{"button_minus":"Back","button_l":"LeftShoulder","button_zl":"LeftTrigger","button_sl":"SingleLeftTrigger0","button_sr":"SingleRightTrigger0","dpad_up":"DpadUp","dpad_down":"DpadDown","dpad_left":"DpadLeft","dpad_right":"DpadRight"},
         "right_joycon":{"button_plus":"Start","button_r":"RightShoulder","button_zr":"RightTrigger","button_sl":"SingleLeftTrigger1","button_sr":"SingleRightTrigger1","button_x":"X","button_b":"B","button_y":"Y","button_a":"A"},
         "version":1,"backend":"GamepadSDL3","id":"","name":"","controller_type":"ProController","player_index":"Player1","enable_dynamic_gamepad_swap":false}
        """;

    /// <summary>Tipo de mando que corresponde por defecto a un dispositivo (Joy-Con sueltos o en par).</summary>
    public static string TipoPorDefecto(string guid) => Controles.NombreMando(guid) switch
    {
        "Joy-Con izquierdo" => "Joy-Con izquierdo",
        "Joy-Con derecho" => "Joy-Con derecho",
        "Joy-Con (par)" => "Joy-Con dobles",
        _ => "Pro Controller",
    };

    /// <summary>Jugadores con mando que tiene configurados un emulador, como asignaciones del lanzador.</summary>
    public static List<AsignacionJugador> DesdeEmulador(EmuladorConfig emulador) =>
        Controles.Leer(emulador)
            .Where(j => j.Conectado && j.EsMando)
            .Select(j => new AsignacionJugador { Jugador = j.Numero, Guid = j.Guid!, Puerto = j.Puerto, Tipo = j.Tipo, Nombre = j.Dispositivo })
            .ToList();

    /// <summary>True si el emulador ya tiene exactamente esos jugadores, mandos y tipos.</summary>
    public static bool Coincide(EmuladorConfig emulador, IReadOnlyList<AsignacionJugador> jugadores)
    {
        var actual = Controles.Leer(emulador).Where(j => j.Conectado).Select(j => (j.Numero, j.Guid ?? "", j.Puerto, j.Tipo)).ToHashSet();
        var deseado = jugadores.Select(j => (j.Jugador, j.Guid.ToLowerInvariant(), j.Puerto, j.Tipo)).ToHashSet();
        return actual.SetEquals(deseado);
    }

    public static ResultadoControles Aplicar(EmuladorConfig destino, IReadOnlyList<AsignacionJugador> jugadores, IReadOnlyList<EmuladorConfig> todos)
    {
        if (Actualizador.Bloqueo(destino) is { } motivo)
            return new(false, motivo);
        if (string.IsNullOrWhiteSpace(destino.CarpetaDatos))
            return new(false, $"{destino.Nombre} no tiene carpeta de datos configurada.");
        var lista = jugadores.Where(j => j.Jugador is >= 1 and <= Controles.MaxJugadores && j.Guid.Length == 32)
            .GroupBy(j => j.Jugador).Select(g => g.Last()).OrderBy(j => j.Jugador).ToList();
        try
        {
            return destino.Tipo == TipoEmulador.Ryujinx ? AplicarRyujinx(destino, lista) : AplicarYuzu(destino, lista, todos);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new(false, $"No se pudieron escribir los controles de {destino.Nombre}: {Actualizador.Explicar(ex)}");
        }
    }

    // ───────────── Eden, Citron, Suyu ─────────────

    private static ResultadoControles AplicarYuzu(EmuladorConfig destino, List<AsignacionJugador> jugadores, IReadOnlyList<EmuladorConfig> todos)
    {
        if (Coincide(destino, jugadores))
            return new(true, $"{destino.Nombre} ya tenía estos mandos.");

        var conocidas = Controles.AsignacionesConocidas(destino, todos);
        var valores = new List<(string Clave, string Valor)>();
        void Poner(string clave, string valor)
        {
            valores.RemoveAll(p => p.Clave == clave || p.Clave == clave + @"\default");
            valores.Add((clave + @"\default", "false"));
            valores.Add((clave, valor));
        }

        var generados = 0;
        for (var n = 0; n < Controles.MaxJugadores; n++)
        {
            var j = jugadores.FirstOrDefault(x => x.Jugador == n + 1);
            if (j == null)
            {
                Poner($"player_{n}_connected", "false");
                continue;
            }
            var guid = j.Guid.ToLowerInvariant();
            IEnumerable<(string Sufijo, string Valor)> asignacion;
            if (conocidas.TryGetValue(guid, out var conocida))
            {
                asignacion = conocida
                    .Where(a => !a.Clave.EndsWith(@"\default") && EsClaveDeMapeo(a.Clave))
                    .Select(a => (a.Clave, RxPuerto().Replace(a.Valor, $"port:{j.Puerto}")));
            }
            else
            {
                asignacion = PlantillaYuzu(guid, j.Puerto, j.Mapeo).Select(a => (a.Sufijo, QtIni.Codificar(a.Valor)));
                generados++;
            }
            foreach (var (sufijo, valor) in asignacion)
                Poner($"player_{n}_{sufijo}", valor);
            Poner($"player_{n}_connected", "true");
            Poner($"player_{n}_type", Math.Max(0, Array.IndexOf(Controles.TiposYuzu, j.Tipo)).ToString());
        }

        var ruta = Controles.ArchivoConfig(destino);
        var ini = Ini.Leer(ruta);
        ini.Fijar("Controls", valores);
        Controles.Guardar(destino, ruta, () => ini.Guardar(ruta));
        var nota = generados > 0 ? $" ({generados} con la asignación por defecto del emulador)" : "";
        return new(true, $"{destino.Nombre}: {Resumen(jugadores)}{nota}.", true);
    }

    private static bool EsClaveDeMapeo(string sufijo) =>
        sufijo.StartsWith("button_") || sufijo is "lstick" or "rstick" or "motionleft" or "motionright";

    /// <summary>Asignación por defecto de yuzu para un mando, según su controlador de SDL (byte 14 del GUID).</summary>
    public static List<(string Sufijo, string Valor)> PlantillaYuzu(string guid, int puerto, string? mapeoSdl)
    {
        var bytes = Convert.FromHexString(guid);
        var fabricante = bytes[4] | bytes[5] << 8;
        var controlador = (char)bytes[14];

        string B(int n) => $"engine:sdl,guid:{guid},port:{puerto},button:{n}";
        string G(int n) => $"engine:sdl,guid:{guid},threshold:0.500000,port:{puerto},axis:{n},invert:+";
        string H(string d) => $"engine:sdl,guid:{guid},hat:0,port:{puerto},direction:{d}";
        string S(int x, int y) => $"engine:sdl,guid:{guid},axis_x:{x},offset_x:0.000000,invert_y:+,deadzone:0.150000,port:{puerto},axis_y:{y},offset_y:0.000000,invert_x:+";
        var movimiento = $"engine:sdl,motion:0,port:{puerto},guid:{guid}";
        const string Vacio = "[empty]";

        if (controlador != 'x' && controlador != 'h' && mapeoSdl != null && DesdeMapeoSdl(guid, puerto, mapeoSdl) is { Count: > 0 } generico)
            return generico;

        if (controlador == 'x')
            // XInput (Xbox): botones A0 B1 X2 Y3 LB4 RB5 Back6 Start7 LS8 RS9 Guide10; ejes LX0 LY1 LT2 RX3 RY4 RT5; cruceta en hat 0.
            return
            [
                ("button_a", B(1)), ("button_b", B(0)), ("button_x", B(3)), ("button_y", B(2)),
                ("button_lstick", B(8)), ("button_rstick", B(9)), ("button_l", B(4)), ("button_r", B(5)),
                ("button_zl", G(2)), ("button_zr", G(5)), ("button_plus", B(7)), ("button_minus", B(6)),
                ("button_dleft", H("left")), ("button_dup", H("up")), ("button_dright", H("right")), ("button_ddown", H("down")),
                ("button_slleft", B(4)), ("button_srleft", B(5)), ("button_slright", B(4)), ("button_srright", B(5)),
                ("button_home", B(10)), ("button_screenshot", Vacio),
                ("lstick", S(0, 1)), ("rstick", S(3, 4)), ("motionleft", Vacio), ("motionright", Vacio),
            ];

        // HIDAPI (Pro Controller, Joy-Con, PlayStation): orden de botones de SDL: A0 B1 X2 Y3 Back4 Guide5 Start6 LS7 RS8 LB9 RB10 Misc11;
        // ejes LX0 LY1 RX2 RY3 LT4 RT5. Con giroscopio en los de Nintendo y PlayStation.
        var conGiroscopio = fabricante is 0x057e or 0x054c;
        return
        [
            ("button_a", B(1)), ("button_b", B(0)), ("button_x", B(3)), ("button_y", B(2)),
            ("button_lstick", B(7)), ("button_rstick", B(8)), ("button_l", B(9)), ("button_r", B(10)),
            ("button_zl", G(4)), ("button_zr", G(5)), ("button_plus", B(6)), ("button_minus", B(4)),
            ("button_dleft", H("left")), ("button_dup", H("up")), ("button_dright", H("right")), ("button_ddown", H("down")),
            ("button_slleft", B(9)), ("button_srleft", B(10)), ("button_slright", B(9)), ("button_srright", B(10)),
            ("button_home", B(5)), ("button_screenshot", fabricante == 0x057e ? B(11) : Vacio),
            ("lstick", S(0, 1)), ("rstick", S(2, 3)),
            ("motionleft", conGiroscopio ? movimiento : Vacio), ("motionright", conGiroscopio ? movimiento : Vacio),
        ];
    }

    /// <summary>Para otros mandos: se traduce el mapeo de SDL ("a:b0,b:b1,dpup:h0.1,leftx:a0,lefttrigger:a2…").</summary>
    private static List<(string Sufijo, string Valor)> DesdeMapeoSdl(string guid, int puerto, string mapeo)
    {
        var partes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in mapeo.Split(','))
        {
            var dos = p.IndexOf(':');
            if (dos > 0)
                partes[p[..dos]] = p[(dos + 1)..];
        }
        string Boton(string clave)
        {
            if (!partes.TryGetValue(clave, out var v))
                return "[empty]";
            if (RxBoton().Match(v) is { Success: true } b)
                return $"engine:sdl,guid:{guid},port:{puerto},button:{b.Groups[1].Value}";
            if (RxHat().Match(v) is { Success: true } h)
            {
                var direccion = int.Parse(h.Groups[2].Value) switch { 1 => "up", 2 => "right", 4 => "down", _ => "left" };
                return $"engine:sdl,guid:{guid},hat:{h.Groups[1].Value},port:{puerto},direction:{direccion}";
            }
            if (RxEje().Match(v) is { Success: true } e)
                return $"engine:sdl,guid:{guid},threshold:0.500000,port:{puerto},axis:{e.Groups[1].Value},invert:+";
            return "[empty]";
        }
        int Eje(string clave, int predeterminado) =>
            partes.TryGetValue(clave, out var v) && RxEje().Match(v) is { Success: true } e ? int.Parse(e.Groups[1].Value) : predeterminado;
        string Stick(string x, string y, int dx, int dy) =>
            $"engine:sdl,guid:{guid},axis_x:{Eje(x, dx)},offset_x:0.000000,invert_y:+,deadzone:0.150000,port:{puerto},axis_y:{Eje(y, dy)},offset_y:0.000000,invert_x:+";

        if (!partes.ContainsKey("a") || !partes.ContainsKey("b"))
            return [];
        // En SDL3 "a" es el botón de abajo y "b" el de la derecha: la A de Switch (derecha) es "b".
        return
        [
            ("button_a", Boton("b")), ("button_b", Boton("a")), ("button_x", Boton("y")), ("button_y", Boton("x")),
            ("button_lstick", Boton("leftstick")), ("button_rstick", Boton("rightstick")),
            ("button_l", Boton("leftshoulder")), ("button_r", Boton("rightshoulder")),
            ("button_zl", Boton("lefttrigger")), ("button_zr", Boton("righttrigger")),
            ("button_plus", Boton("start")), ("button_minus", Boton("back")),
            ("button_dleft", Boton("dpleft")), ("button_dup", Boton("dpup")), ("button_dright", Boton("dpright")), ("button_ddown", Boton("dpdown")),
            ("button_slleft", Boton("leftshoulder")), ("button_srleft", Boton("rightshoulder")),
            ("button_slright", Boton("leftshoulder")), ("button_srright", Boton("rightshoulder")),
            ("button_home", Boton("guide")), ("button_screenshot", Boton("misc1")),
            ("lstick", Stick("leftx", "lefty", 0, 1)), ("rstick", Stick("rightx", "righty", 2, 3)),
            ("motionleft", "[empty]"), ("motionright", "[empty]"),
        ];
    }

    // ───────────── Ryujinx ─────────────

    private static ResultadoControles AplicarRyujinx(EmuladorConfig destino, List<AsignacionJugador> jugadores)
    {
        var ruta = Controles.ArchivoConfig(destino);
        if (!File.Exists(ruta))
            return new(false, $"Abre {destino.Nombre} una vez para que cree su configuración.");
        if (Coincide(destino, jugadores))
            return new(true, $"{destino.Nombre} ya tenía estos mandos.");

        var raiz = JsonNode.Parse(File.ReadAllText(ruta)) as JsonObject ?? throw new JsonException("Config.json de Ryujinx no es un objeto");
        var existentes = (raiz["input_config"] as JsonArray ?? new JsonArray()).OfType<JsonObject>()
            .Where(e => (e["backend"]?.GetValue<string>() ?? "").StartsWith("Gamepad", StringComparison.OrdinalIgnoreCase)).ToList();
        var plantilla = existentes.FirstOrDefault() ?? (JsonObject)JsonNode.Parse(PlantillaRyujinx)!;

        var nuevas = new JsonArray();
        foreach (var j in jugadores)
        {
            var id = Controles.IdRyujinx(j.Guid.ToLowerInvariant(), j.Puerto);
            var base_ = existentes.FirstOrDefault(e => string.Equals(e["id"]?.GetValue<string>(), id, StringComparison.OrdinalIgnoreCase)) ?? plantilla;
            var entrada = (JsonObject)base_.DeepClone();
            entrada["id"] = id;
            entrada["name"] = j.Nombre.Length > 0 ? j.Nombre : Controles.NombreMando(j.Guid);
            entrada["player_index"] = j.Tipo == "Portátil" ? "Handheld" : $"Player{j.Jugador}";
            entrada["controller_type"] = j.Tipo switch
            {
                "Joy-Con dobles" => "JoyconPair",
                "Joy-Con izquierdo" => "JoyconLeft",
                "Joy-Con derecho" => "JoyconRight",
                "Portátil" => "Handheld",
                _ => "ProController",
            };
            nuevas.Add(entrada);
        }
        raiz["input_config"] = nuevas;
        Controles.Guardar(destino, ruta, () => Controles.EscribirJson(ruta, raiz));
        return new(true, $"{destino.Nombre}: {Resumen(jugadores)}.", true);
    }

    private static string Resumen(IEnumerable<AsignacionJugador> jugadores)
    {
        var lista = jugadores.Select(j => $"J{j.Jugador} {(j.Nombre.Length > 0 ? j.Nombre : Controles.NombreMando(j.Guid))}").ToList();
        return lista.Count == 0 ? "sin jugadores" : string.Join(", ", lista);
    }

    [GeneratedRegex(@"port:\d+")]
    private static partial Regex RxPuerto();

    [GeneratedRegex(@"^b(\d+)$")]
    private static partial Regex RxBoton();

    [GeneratedRegex(@"^h(\d+)\.(\d+)$")]
    private static partial Regex RxHat();

    [GeneratedRegex(@"^[+-]?a(\d+)~?$")]
    private static partial Regex RxEje();
}
