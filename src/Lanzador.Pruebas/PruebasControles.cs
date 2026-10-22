using System.Text.Json.Nodes;
using Lanzador.Core;
using Lanzador.Core.Controles;

/// <summary>Pruebas de la copia de controles. Lo real solo se lee; las copias se hacen sobre duplicados en una carpeta temporal.</summary>
static class PruebasControles
{
    private static int _fallos;

    private static void Comprobar(bool condicion, string descripcion)
    {
        Console.WriteLine($"  [{(condicion ? "OK" : "FALLO")}] {descripcion}");
        if (!condicion)
            _fallos++;
    }

    public static int Ejecutar(string temporal, Configuracion config)
    {
        _fallos = 0;
        Console.WriteLine("\n== Controles: combinación para cerrar el juego con PS5 y Pro Controller (reportes HID)");
        Comprobar(ReporteHid.Reconocer(0x054C, 0x0CE6) == MandoHid.DualSense && ReporteHid.Reconocer(0x057E, 0x2009) == MandoHid.ProController
                  && ReporteHid.Reconocer(0x045E, 0x0B12) == MandoHid.Ninguno, "Se reconocen DualSense y Pro Controller (y el de Xbox no)");
        byte[] Reporte(int largo, params (int Posicion, byte Valor)[] bytes)
        {
            var r = new byte[largo];
            foreach (var (p, v) in bytes)
                r[p] = v;
            return r;
        }
        Comprobar(ReporteHid.Reconocer(0x0E6F, 0x018C, bluetooth: true) == MandoHid.ProController && ReporteHid.Reconocer(0x0E6F, 0x018C) == MandoHid.Ninguno,
            "Un mando PDP por Bluetooth se lee como Pro Controller (por cable no)");
        Comprobar(ReporteHid.ComboCerrar(MandoHid.DualSense, false, Reporte(64, (0, 0x01), (9, 0x13))) == true, "DualSense por cable: Create + L1 + R1");
        Comprobar(ReporteHid.ComboCerrar(MandoHid.DualSense, false, Reporte(64, (0, 0x01), (9, 0x03))) == false, "DualSense por cable: sin Create no cierra");
        Comprobar(ReporteHid.ComboCerrar(MandoHid.DualSense, true, Reporte(78, (0, 0x31), (10, 0x13))) == true, "DualSense por Bluetooth (modo completo 0x31)");
        Comprobar(ReporteHid.ComboCerrar(MandoHid.DualSense, true, Reporte(78, (0, 0x31), (9, 0x13))) == false, "DualSense por Bluetooth: el byte de al lado no cuenta");
        Comprobar(ReporteHid.ComboCerrar(MandoHid.DualSense, true, Reporte(78, (0, 0x01), (6, 0x13))) == true, "DualSense por Bluetooth (modo simple 0x01)");
        Comprobar(ReporteHid.ComboCerrar(MandoHid.DualSense, false, Reporte(64, (0, 0x05))) == null, "Un reporte sin botones se ignora");
        Comprobar(ReporteHid.ComboCerrar(MandoHid.ProController, true, Reporte(49, (0, 0x30), (3, 0x40), (4, 0x01), (5, 0x40))) == true, "Pro Controller (0x30): − + L + R");
        Comprobar(ReporteHid.ComboCerrar(MandoHid.ProController, true, Reporte(49, (0, 0x30), (4, 0x01), (5, 0x40))) == false, "Pro Controller: sin R no cierra");
        Comprobar(ReporteHid.ComboCerrar(MandoHid.ProController, true, Reporte(49, (0, 0x30), (4, 0x11), (5, 0x40))) == false, "Pro Controller: L + Inicio + − (detener en Eden) no cierra");
        Comprobar(ReporteHid.ComboCerrar(MandoHid.ProController, true, Reporte(12, (0, 0x3F), (1, 0x30), (2, 0x01))) == true, "Pro Controller en modo simple (0x3F)");

        Console.WriteLine("\n== Controles: identificación de mandos");
        Comprobar(Controles.IdRyujinx("030000005e040000200b000023057801", 0) == "0-00000003-045e-0000-200b-000023057801", "GUID de SDL → id de Ryujinx");
        Comprobar(Controles.MandoDeRyujinx("1-00000005-057e-0000-0920-000000006806") == ("050000007e0500000920000000006806", 1), "id de Ryujinx → GUID de SDL y puerto");
        Comprobar(Controles.NombreMando("030000005e040000200b000023057801") == "Mando Xbox Series", "045e:0b20 = Mando Xbox Series");
        Comprobar(Controles.NombreMando("050000007e0500000920000000006806") == "Switch Pro Controller", "057e:2009 = Switch Pro Controller");

        Console.WriteLine("\n== Controles: mandos conectados (SDL3)");
        var conectados = Sdl3.Mandos();
        Console.WriteLine(Sdl3.Disponible ? $"  {conectados.Count} mando(s)" : $"  SDL3 no disponible: {Sdl3.Error}");
        var guidsEden = Controles.Leer(config.Emulador("eden")!).Where(j => j.EsMando).Select(j => j.Guid).ToHashSet();
        foreach (var m in conectados)
            Console.WriteLine($"  {m.NombreLegible} | SDL: {m.Nombre} | {m.Guid} | puerto {m.Puerto} | controlador '{m.Controlador}' | {(guidsEden.Contains(m.Guid) ? "mismo GUID que en Eden" : "no está en Eden")}");
        Comprobar(Sdl3.Disponible, "SDL3 se carga");
        Comprobar(conectados.Count == 0 || conectados.Any(m => guidsEden.Contains(m.Guid)), "Los GUID de SDL3 coinciden con los que guarda Eden (si hay mandos conectados)");

        Console.WriteLine("\n== Controles: configuración real (solo lectura)");
        foreach (var emu in config.Emuladores.Where(e => e.Instalado))
        {
            var jugadores = Controles.Leer(emu).Where(j => j.Conectado).ToList();
            Console.WriteLine($"  {emu.Nombre}: {(jugadores.Count == 0 ? "sin jugadores" : string.Join(" | ", jugadores.Select(j => $"J{j.Numero} {j.Dispositivo} ({j.Tipo}{(j.Puerto > 0 ? $", puerto {j.Puerto}" : "")})")))}");
        }
        var edenReal = config.Emulador("eden")!;
        var ryuReal = config.Emulador("ryujinx")!;
        var citronReal = config.Emulador("citron")!;
        // Estas pruebas leen la configuración real, que cambia cuando configuras otros mandos: se adaptan a lo que haya.
        var mandosEden = Controles.Leer(edenReal).Where(j => j.Conectado && j.EsMando).ToList();
        Comprobar(mandosEden.Count >= 1, $"Eden: {mandosEden.Count} jugadores con mando");

        // Duplicados de las configuraciones para modificarlos sin tocar los reales.
        var eden = Duplicar(edenReal, Path.Combine(temporal, "controles", "Eden"));
        var citron = Duplicar(citronReal, Path.Combine(temporal, "controles", "Citron"));
        var ryujinx = Duplicar(ryuReal, Path.Combine(temporal, "controles", "ryujinx"));
        var todos = new List<EmuladorConfig> { eden, ryujinx, citron };

        Console.WriteLine("\n== Controles: Eden → Citron (sobre copias)");
        var lineasAntes = File.ReadAllLines(Controles.ArchivoConfig(citron));
        var r = Controles.Copiar(eden, citron, todos);
        Console.WriteLine($"  {r.Mensaje}");
        var deEden = Controles.Leer(eden);
        var deCitron = Controles.Leer(citron);
        Comprobar(r.Exito && r.Cambio, "La copia termina bien");
        Comprobar(deCitron.Where(j => j.Conectado).Select(j => (j.Numero, j.Guid, j.Tipo)).SequenceEqual(deEden.Where(j => j.Conectado).Select(j => (j.Numero, j.Guid, j.Tipo))),
            "Citron queda con los mismos jugadores, mandos y tipos que Eden");
        var lineasDespues = File.ReadAllLines(Controles.ArchivoConfig(citron));
        Comprobar(lineasDespues.Contains("player_0_body_color\\default=true") || lineasDespues.Any(l => l.StartsWith("player_0_body_color")), "Se conservan las claves propias de Citron");
        var fueraDeControles = (string[] l) => l.Where(x => !x.StartsWith("player_") && !x.StartsWith("udp_input_servers")).ToList();
        Comprobar(fueraDeControles(lineasAntes).SequenceEqual(fueraDeControles(lineasDespues)), "El resto del qt-config.ini de Citron queda idéntico");
        Comprobar(Controles.Copiar(eden, citron, todos).Mensaje.Contains("ya tiene"), "Copiar otra vez no cambia nada");

        Console.WriteLine("\n== Controles: Eden → Ryujinx (sobre copias)");
        var configAntes = JsonNode.Parse(File.ReadAllText(Controles.ArchivoConfig(ryujinx)))!.AsObject();
        var r2 = Controles.Copiar(eden, ryujinx, todos);
        Console.WriteLine($"  {r2.Mensaje}");
        var deRyu = Controles.Leer(ryujinx);
        Comprobar(r2.Exito && deRyu.Select(j => (j.Numero, j.Guid)).SequenceEqual(deEden.Where(j => j.Conectado && j.EsMando).Select(j => (j.Numero, j.Guid))),
            "Ryujinx queda con los mismos jugadores y mandos que Eden (solo los conectados)");
        var configDespues = JsonNode.Parse(File.ReadAllText(Controles.ArchivoConfig(ryujinx)))!.AsObject();
        configAntes.Remove("input_config");
        configDespues.Remove("input_config");
        Comprobar(configAntes.ToJsonString() == configDespues.ToJsonString(), "El resto del Config.json de Ryujinx queda igual");

        Console.WriteLine("\n== Controles: asignación por defecto generada por el lanzador vs. la que hizo Eden");
        var paresEden = File.ReadAllLines(Controles.ArchivoConfig(edenReal))
            .Where(l => l.StartsWith("player_") && l.Contains('=') && !l.Contains("\\default"))
            .Select(l => (Clave: l[..l.IndexOf('=')], Valor: Lanzador.Core.QtIni.Decodificar(l[(l.IndexOf('=') + 1)..])))
            .ToDictionary(p => p.Clave, p => p.Valor);
        foreach (var (guid, nombre) in new[] { ("030000005e040000200b000023057801", "Xbox (XInput)"), ("050000007e0500000920000000006806", "Pro Controller (HIDAPI)") })
        {
            // El jugador que tiene ese mando en el Eden real (puede haber cambiado de posición).
            var n = Enumerable.Range(0, 8).Cast<int?>().FirstOrDefault(i =>
                paresEden.TryGetValue($"player_{i}_connected", out var c) && c == "true"
                && paresEden.TryGetValue($"player_{i}_button_a", out var a) && a.Contains($"guid:{guid}", StringComparison.OrdinalIgnoreCase));
            if (n == null)
            {
                Console.WriteLine($"  (se omite {nombre}: el Eden real no tiene ese mando conectado ahora)");
                continue;
            }
            var plantilla = GeneradorControles.PlantillaYuzu(guid, 0, null);
            var botones = plantilla.Where(p => p.Sufijo.StartsWith("button_")).ToList();
            var iguales = botones.Count(p => paresEden.TryGetValue($"player_{n.Value}_{p.Sufijo}", out var v) && Partes(v).SetEquals(Partes(p.Valor)));
            var distintos = botones.Where(p => !(paresEden.TryGetValue($"player_{n.Value}_{p.Sufijo}", out var v) && Partes(v).SetEquals(Partes(p.Valor)))).Select(p => p.Sufijo).ToList();
            Console.WriteLine($"  {nombre}: {iguales}/{botones.Count} botones iguales a Eden{(distintos.Count > 0 ? $" (distintos: {string.Join(", ", distintos)})" : "")}");
            Comprobar(iguales == botones.Count, $"{nombre}: los botones generados son los mismos que puso Eden");
            var stick = Partes(plantilla.First(p => p.Sufijo == "rstick").Valor);
            var stickEden = Partes(paresEden[$"player_{n.Value}_rstick"]);
            Comprobar(stick.Where(s => s.StartsWith("axis_")).ToHashSet().SetEquals(stickEden.Where(s => s.StartsWith("axis_"))), $"{nombre}: el stick derecho usa los mismos ejes");
        }

        Console.WriteLine("\n== Controles: configurar desde el lanzador (sobre copias)");
        var asignacion = new List<AsignacionJugador>
        {
            new() { Jugador = 1, Guid = "030000005e040000200b000023057801", Puerto = 0, Tipo = "Pro Controller", Nombre = "Mando Xbox Series" },
            new() { Jugador = 2, Guid = "050000007e0500000920000000006806", Puerto = 0, Tipo = "Pro Controller", Nombre = "Switch Pro Controller" },
        };
        var citron2 = Duplicar(citronReal, Path.Combine(temporal, "controles", "Citron2"));
        var ryujinx2 = Duplicar(ryuReal, Path.Combine(temporal, "controles", "ryujinx2"));
        var suyuNuevo = new EmuladorConfig { Id = "suyu-copia", Nombre = "Suyu nuevo (copia)", Tipo = TipoEmulador.Yuzu, RutaExe = Path.Combine(temporal, "controles", "Suyu", "no-existe.exe"), CarpetaDatos = Path.Combine(temporal, "controles", "Suyu", "user") };
        var todos2 = new List<EmuladorConfig> { eden, ryujinx2, citron2, suyuNuevo };
        var asignacionEden = mandosEden.Select(j => new AsignacionJugador { Jugador = j.Numero, Guid = j.Guid ?? "", Puerto = j.Puerto, Tipo = j.Tipo, Nombre = j.Dispositivo }).ToList();
        Comprobar(GeneradorControles.Coincide(eden, asignacionEden), "Lo que se lee de Eden coincide consigo mismo (leer y comparar usan el mismo formato)");
        foreach (var destino in new[] { citron2, ryujinx2 })
        {
            var ra = GeneradorControles.Aplicar(destino, asignacion, todos2);
            Console.WriteLine($"  {ra.Mensaje}");
            Comprobar(ra.Exito && GeneradorControles.Coincide(destino, asignacion), $"{destino.Nombre}: queda con J1 Xbox y J2 Pro");
        }
        var paresCitron = File.ReadAllLines(Controles.ArchivoConfig(citron2)).Where(l => l.StartsWith("player_0_lstick=")).ToList();
        // La calibración del stick izquierdo del Xbox que tiene ahora el Eden real (cambia si se recalibra).
        var jugadorXbox = Enumerable.Range(0, 8).Cast<int?>().FirstOrDefault(i =>
            paresEden.TryGetValue($"player_{i}_button_a", out var a) && a.Contains("guid:030000005e040000200b000023057801", StringComparison.OrdinalIgnoreCase));
        var calibracionEden = jugadorXbox is { } jx && paresEden.TryGetValue($"player_{jx}_lstick", out var stickXbox)
            ? stickXbox.Split(',').FirstOrDefault(p => p.StartsWith("offset_x:")) : null;
        if (calibracionEden == null)
            Console.WriteLine("  (se omite la calibración: el Eden real no tiene el mando Xbox con calibración)");
        else
            Comprobar(paresCitron.Count == 1 && QtIni.Decodificar(paresCitron[0][(paresCitron[0].IndexOf('=') + 1)..]).Split(',').Contains(calibracionEden),
                $"Citron reutiliza la asignación calibrada que ya tenía Eden para ese mando ({calibracionEden})");
        // Suyu recién instalado sin configuración y sin asignaciones conocidas: se genera la de por defecto.
        var rs = GeneradorControles.Aplicar(suyuNuevo, asignacion, [suyuNuevo]);
        Console.WriteLine($"  {rs.Mensaje}");
        var leidosSuyu = Controles.Leer(suyuNuevo).Where(j => j.Conectado).ToList();
        Comprobar(rs.Exito && leidosSuyu.Count == 2 && leidosSuyu[0].Dispositivo == "Mando Xbox Series" && leidosSuyu[1].Dispositivo == "Switch Pro Controller",
            "Suyu sin configuración recibe un qt-config.ini nuevo con los dos mandos");
        Comprobar(File.ReadAllLines(Controles.ArchivoConfig(suyuNuevo)).Contains("player_0_connected\\default=false"), "Cada valor lleva su \\default=false (si no, yuzu lo ignoraría)");

        // El mismo mando en dos jugadores (se permite, como en los emuladores): queda en los dos.
        var compartido = new List<AsignacionJugador>
        {
            new() { Jugador = 1, Guid = "030000005e040000200b000023057801", Puerto = 0, Tipo = "Pro Controller", Nombre = "Mando Xbox Series" },
            new() { Jugador = 2, Guid = "030000005e040000200b000023057801", Puerto = 0, Tipo = "Pro Controller", Nombre = "Mando Xbox Series" },
        };
        var suyuCompartido = new EmuladorConfig { Id = "suyu-compartido", Nombre = "Suyu (mismo mando en J1 y J2)", Tipo = TipoEmulador.Yuzu, RutaExe = Path.Combine(temporal, "controles", "SuyuCompartido", "no-existe.exe"), CarpetaDatos = Path.Combine(temporal, "controles", "SuyuCompartido", "user") };
        var rc = GeneradorControles.Aplicar(suyuCompartido, compartido, [suyuCompartido]);
        var leidosCompartido = Controles.Leer(suyuCompartido).Where(j => j.Conectado).ToList();
        Comprobar(rc.Exito && leidosCompartido.Count == 2 && leidosCompartido.All(j => j.Dispositivo == "Mando Xbox Series") && GeneradorControles.Coincide(suyuCompartido, compartido),
            "El mismo mando en J1 y J2 queda en los dos jugadores (yuzu)");
        var ryujinxCompartido = Duplicar(ryuReal, Path.Combine(temporal, "controles", "ryujinx-compartido"));
        var rr = GeneradorControles.Aplicar(ryujinxCompartido, compartido, [ryujinxCompartido]);
        Comprobar(rr.Exito && GeneradorControles.Coincide(ryujinxCompartido, compartido), "El mismo mando en J1 y J2 queda en los dos jugadores (Ryujinx)");

        Console.WriteLine("\n== Controles: Ryujinx real → Citron (sobre copias)");
        var ryuOriginal = Duplicar(ryuReal, Path.Combine(temporal, "controles", "ryujinx-original"));
        var r3 = Controles.Copiar(ryuOriginal, citron, [eden, ryuOriginal, citron]);
        Console.WriteLine($"  {r3.Mensaje}");
        var deCitron2 = Controles.Leer(citron).Where(j => j.Conectado).ToList();
        var deRyuOriginal = Controles.Leer(ryuOriginal).Where(j => j.Conectado).ToList();
        Comprobar(r3.Exito && deCitron2.Count > 0
                  && deCitron2.All(c => deRyuOriginal.Any(o => o.Numero == c.Numero && string.Equals(o.Guid, c.Guid, StringComparison.OrdinalIgnoreCase))),
            "Los mandos que Eden ya conoce se copian con su asignación (mismo jugador y mando que en Ryujinx)");
        // Depende de la configuración real: solo se puede comprobar si el Ryujinx real tiene un J3 con un mando que Eden no conoce.
        var guidsDeEden = mandosEden.Select(j => j.Guid).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (deRyuOriginal.Any(j => j.Numero == 3 && !guidsDeEden.Contains(j.Guid ?? "")))
            Comprobar(r3.Mensaje.Contains("Sin copiar") && deCitron2.All(j => j.Numero != 3), "El mando que nunca se configuró en Eden se informa y no se inventa");
        else
            Console.WriteLine("  (se omite «el mando que nunca se configuró…»: el Ryujinx real ya no tiene un J3 desconocido para Eden)");
        return _fallos;
    }

    /// <summary>"engine:sdl,guid:…,port:0,button:1" → conjunto de pares, para comparar sin importar el orden.</summary>
    private static HashSet<string> Partes(string valor) => valor.Split(',').Select(p => p.Trim()).ToHashSet();

    private static EmuladorConfig Duplicar(EmuladorConfig real, string carpeta)
    {
        var copia = new EmuladorConfig
        {
            Id = real.Id + "-copia",
            Nombre = real.Nombre + " (copia)",
            Tipo = real.Tipo,
            RutaExe = Path.Combine(carpeta, "no-existe.exe"),
            CarpetaDatos = Path.Combine(carpeta, real.Tipo == TipoEmulador.Ryujinx ? "portable" : "user"),
        };
        var origen = Controles.ArchivoConfig(real);
        var destino = Controles.ArchivoConfig(copia);
        Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
        File.Copy(origen, destino, true);
        return copia;
    }
}
