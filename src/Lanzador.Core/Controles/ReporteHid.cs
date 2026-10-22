namespace Lanzador.Core.Controles;

/// <summary>Mandos que no son XInput y cuyos reportes HID se saben leer.</summary>
public enum MandoHid
{
    Ninguno,
    DualSense,
    DualShock4,
    ProController,
}

/// <summary>
/// Lee los reportes HID crudos (los mismos bytes que manda el mando al emulador) del DualSense, el DualShock 4 y el
/// Pro Controller, para saber si se mantiene la combinación de cerrar el juego. No se le envía nada al mando.
/// Formatos según la documentación de la comunidad (SDL, dekuNukem/Nintendo_Switch_Reverse_Engineering, dsremap).
/// </summary>
public static class ReporteHid
{
    /// <summary>Marcas que hacen mandos para Switch (PDP, Hori, PowerA, 8BitDo).</summary>
    private static readonly int[] MarcasSwitch = [0x0E6F, 0x0F0D, 0x20D6, 0x2DC8];

    public static MandoHid Reconocer(int vendor, int producto, bool bluetooth = false) => (vendor, producto) switch
    {
        (0x054C, 0x0CE6) or (0x054C, 0x0DF2) => MandoHid.DualSense, // DualSense y DualSense Edge
        (0x054C, 0x05C4) or (0x054C, 0x09CC) or (0x054C, 0x0BA0) => MandoHid.DualShock4,
        (0x057E, 0x2009) => MandoHid.ProController,
        // Un mando de otra marca que se conecta a la Switch por Bluetooth tiene que hablar como el Pro Controller
        // (por ejemplo el PDP 0E6F:018C, que SDL presenta como «Switch Pro Controller»).
        _ when bluetooth && MarcasSwitch.Contains(vendor) => MandoHid.ProController,
        _ => MandoHid.Ninguno,
    };

    /// <summary>La combinación en ese mando: los mismos botones que Vista + LB + RB en el de Xbox.</summary>
    public static string TextoCombo(MandoHid mando) => mando switch
    {
        MandoHid.DualSense => "Create + L1 + R1",
        MandoHid.DualShock4 => "Share + L1 + R1",
        MandoHid.ProController => "− + L + R",
        _ => "",
    };

    /// <summary>
    /// true si en este reporte están pulsados Create/Share/Menos + L1/L + R1/R; false si no; null si el reporte no
    /// trae botones (o no se reconoce su formato).
    /// </summary>
    public static bool? ComboCerrar(MandoHid mando, bool bluetooth, ReadOnlySpan<byte> reporte)
    {
        if (reporte.Length < 2)
            return null;
        var id = reporte[0];
        switch (mando)
        {
            case MandoHid.DualSense:
            {
                // USB 0x01: botones en 8..10. Bluetooth 0x31 (modo completo): igual pero un byte más adelante.
                // Bluetooth 0x01 (modo simple, antes de que el emulador lo cambie): como el DualShock 4.
                int? hombros = id switch
                {
                    0x01 when !bluetooth => 9,
                    0x31 when bluetooth => 10,
                    0x01 when bluetooth => 6,
                    _ => null,
                };
                return Sony(reporte, hombros);
            }
            case MandoHid.DualShock4:
            {
                int? hombros = id switch
                {
                    0x01 => 6,             // USB, o Bluetooth en modo simple
                    0x11 when bluetooth => 8,
                    _ => null,
                };
                return Sony(reporte, hombros);
            }
            case MandoHid.ProController:
            {
                // 0x30/0x31 (modo completo, el que usan los emuladores) y 0x21 (respuestas): derecha 3, compartidos 4, izquierda 5.
                // 0x3F (modo simple): botones 1 y 2.
                if (id is 0x30 or 0x31 or 0x21 && reporte.Length > 5)
                    return (reporte[4] & 0x01) != 0 && (reporte[5] & 0x40) != 0 && (reporte[3] & 0x40) != 0;
                if (id == 0x3F && reporte.Length > 2)
                    return (reporte[2] & 0x01) != 0 && (reporte[1] & 0x10) != 0 && (reporte[1] & 0x20) != 0;
                return null;
            }
            default:
                return null;
        }
    }

    /// <summary>Byte de hombros de Sony: L1 0x01, R1 0x02, Create/Share 0x10.</summary>
    private static bool? Sony(ReadOnlySpan<byte> reporte, int? posicion)
    {
        if (posicion is not { } p || p >= reporte.Length)
            return null;
        return (reporte[p] & 0x13) == 0x13;
    }
}
