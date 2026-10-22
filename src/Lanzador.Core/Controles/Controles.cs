using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Lanzador.Core.Actualizaciones;
using Lanzador.Core.Trucos;

namespace Lanzador.Core.Controles;

/// <summary>Un jugador configurado en un emulador.</summary>
public sealed record JugadorControl(int Numero, bool Conectado, string Tipo, string Dispositivo, string? Guid, int Puerto)
{
    public bool EsMando => Guid != null;
}

public sealed record ResultadoControles(bool Exito, string Mensaje, bool Cambio = false);

/// <summary>
/// Lee y copia la configuración de mandos entre emuladores, para configurarlos una vez y tenerlos en todos:
/// <list type="bullet">
/// <item>Eden, Citron, Suyu (yuzu): claves player_N_* de [Controls] en user\config\qt-config.ini. Entre ellos se copian tal cual.</item>
/// <item>Ryujinx: input_config de portable\Config.json. Sus botones tienen nombre genérico (A, LeftShoulder…),
/// así que se reutiliza una asignación que ya funcione y solo se cambia el mando, el tipo y el jugador.</item>
/// </list>
/// Siempre con el emulador cerrado y guardando un respaldo del archivo que se modifica.
/// </summary>
public static partial class Controles
{
    public const int MaxJugadores = 8;

    internal static readonly string[] TiposYuzu = ["Pro Controller", "Joy-Con dobles", "Joy-Con izquierdo", "Joy-Con derecho", "Portátil", "GameCube"];

    public static string ArchivoConfig(EmuladorConfig emulador) => emulador.Tipo == TipoEmulador.Ryujinx
        ? Path.Combine(emulador.CarpetaDatos, "Config.json")
        : Path.Combine(emulador.CarpetaDatos, "config", "qt-config.ini");

    public static List<JugadorControl> Leer(EmuladorConfig emulador) =>
        emulador.Tipo == TipoEmulador.Ryujinx ? LeerRyujinx(emulador) : LeerYuzu(emulador);

    /// <summary>
    /// Copia los controles de <paramref name="origen"/> a <paramref name="destino"/>. <paramref name="todos"/> son los
    /// emuladores configurados: de ahí se sacan asignaciones ya hechas para un mando al copiar desde Ryujinx.
    /// </summary>
    public static ResultadoControles Copiar(EmuladorConfig origen, EmuladorConfig destino, IReadOnlyList<EmuladorConfig> todos)
    {
        if (origen.Id.Equals(destino.Id, StringComparison.OrdinalIgnoreCase))
            return new(true, "Es el mismo emulador.");
        if (Actualizador.Bloqueo(destino) is { } motivo)
            return new(false, motivo);
        if (!File.Exists(ArchivoConfig(origen)))
            return new(false, $"{origen.Nombre} todavía no tiene controles configurados (ábrelo una vez y configúralos).");
        try
        {
            return (origen.Tipo, destino.Tipo) switch
            {
                (TipoEmulador.Yuzu, TipoEmulador.Yuzu) => YuzuAYuzu(origen, destino),
                (TipoEmulador.Yuzu, TipoEmulador.Ryujinx) => YuzuARyujinx(origen, destino),
                (TipoEmulador.Ryujinx, TipoEmulador.Yuzu) => RyujinxAYuzu(origen, destino, todos),
                _ => new(false, "No se puede copiar entre esos emuladores."),
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return new(false, $"No se pudieron copiar los controles a {destino.Nombre}: {Actualizador.Explicar(ex)}");
        }
    }

    // ───────────── Lectura ─────────────

    private static List<JugadorControl> LeerYuzu(EmuladorConfig emulador)
    {
        var lista = new List<JugadorControl>();
        var ruta = ArchivoConfig(emulador);
        if (!File.Exists(ruta))
            return lista;
        var valores = Ini.Leer(ruta).Valores("Controls");
        for (var n = 0; n < MaxJugadores; n++)
        {
            var conectado = QtIni.Decodificar(valores.GetValueOrDefault($"player_{n}_connected", "false")) == "true";
            var tipo = int.TryParse(QtIni.Decodificar(valores.GetValueOrDefault($"player_{n}_type", "0")), out var t) && t >= 0 && t < TiposYuzu.Length ? TiposYuzu[t] : "Pro Controller";
            var boton = QtIni.Decodificar(valores.GetValueOrDefault($"player_{n}_button_a", ""));
            var (guid, puerto) = MandoDeYuzu(boton);
            var dispositivo = guid != null ? NombreMando(guid) : boton.Contains("engine:keyboard") ? "Teclado" : boton.Length == 0 || boton == "[empty]" ? "Sin asignar" : "Otro";
            lista.Add(new JugadorControl(n + 1, conectado, tipo, dispositivo, guid, puerto));
        }
        return lista;
    }

    private static List<JugadorControl> LeerRyujinx(EmuladorConfig emulador)
    {
        var lista = new List<JugadorControl>();
        var ruta = ArchivoConfig(emulador);
        if (!File.Exists(ruta) || JsonNode.Parse(File.ReadAllText(ruta)) is not JsonObject raiz || raiz["input_config"] is not JsonArray entradas)
            return lista;
        foreach (var e in entradas.OfType<JsonObject>())
        {
            var indice = e["player_index"]?.GetValue<string>() ?? "";
            if (!int.TryParse(indice.Replace("Player", ""), out var numero))
                continue;
            var id = e["id"]?.GetValue<string>() ?? "";
            var backend = e["backend"]?.GetValue<string>() ?? "";
            var (guid, puerto) = MandoDeRyujinx(id);
            var tipo = (e["controller_type"]?.GetValue<string>()) switch
            {
                "JoyconPair" => "Joy-Con dobles",
                "JoyconLeft" => "Joy-Con izquierdo",
                "JoyconRight" => "Joy-Con derecho",
                "Handheld" => "Portátil",
                _ => "Pro Controller",
            };
            var dispositivo = backend.Contains("Keyboard", StringComparison.OrdinalIgnoreCase) ? "Teclado"
                : guid != null ? NombreMando(guid) : e["name"]?.GetValue<string>() ?? "Otro";
            lista.Add(new JugadorControl(numero, true, tipo, dispositivo, guid, puerto));
        }
        return lista.OrderBy(j => j.Numero).ToList();
    }

    /// <summary>Nombre legible a partir del GUID de SDL (bytes 4-5 = fabricante, 8-9 = producto).</summary>
    public static string NombreMando(string guid)
    {
        if (guid.Length != 32)
            return "Mando";
        var bytes = Convert.FromHexString(guid);
        var vid = bytes[4] | bytes[5] << 8;
        var pid = bytes[8] | bytes[9] << 8;
        return (vid, pid) switch
        {
            (0x045e, 0x0b12 or 0x0b13 or 0x0b20 or 0x0b21 or 0x0b22) => "Mando Xbox Series",
            (0x045e, 0x02ea or 0x02fd or 0x02e0 or 0x0b00 or 0x0b05 or 0x0b0a) => "Mando Xbox One",
            (0x045e, 0x028e or 0x028f or 0x0719) => "Mando Xbox 360",
            (0x045e, _) => "Mando Xbox",
            (0x057e, 0x2009) => "Switch Pro Controller",
            (0x057e, 0x2006) => "Joy-Con izquierdo",
            (0x057e, 0x2007) => "Joy-Con derecho",
            (0x057e, 0x200e) => "Joy-Con (par)",
            (0x057e, _) => "Mando Nintendo",
            (0x054c, 0x05c4 or 0x09cc or 0x0ba0) => "DualShock 4",
            (0x054c, 0x0ce6 or 0x0df2) => "DualSense",
            (0x054c, _) => "Mando PlayStation",
            (0x28de, _) => "Mando de Steam",
            _ => $"Mando {vid:x4}:{pid:x4}",
        };
    }

    // ───────────── Eden/Citron/Suyu → Eden/Citron/Suyu ─────────────

    /// <summary>Claves que se copian: todo lo de cada jugador (botones, sticks, movimiento, vibración) y los servidores UDP de movimiento.</summary>
    private static bool EsClaveDeControles(string clave)
    {
        var baseClave = clave.EndsWith(@"\default") ? clave[..^8] : clave;
        return RxClaveJugador().IsMatch(baseClave) || baseClave == "udp_input_servers";
    }

    private static ResultadoControles YuzuAYuzu(EmuladorConfig origen, EmuladorConfig destino)
    {
        var desde = Ini.Leer(ArchivoConfig(origen));
        var claves = desde.Pares("Controls").Where(p => EsClaveDeControles(p.Clave)).ToList();
        if (!claves.Any(p => p.Clave.EndsWith("_connected")))
            return new(false, $"{origen.Nombre} no tiene jugadores configurados.");

        var rutaDestino = ArchivoConfig(destino);
        var hacia = Ini.Leer(rutaDestino);
        var antes = hacia.Pares("Controls").Where(p => EsClaveDeControles(p.Clave)).ToDictionary(p => p.Clave, p => p.Valor);
        if (claves.All(p => antes.TryGetValue(p.Clave, out var v) && v == p.Valor))
            return new(true, $"{destino.Nombre} ya tiene los mismos controles que {origen.Nombre}.");

        hacia.Fijar("Controls", claves);
        Guardar(destino, rutaDestino, () => hacia.Guardar(rutaDestino));
        return new(true, $"{destino.Nombre}: controles copiados de {origen.Nombre} ({Resumen(LeerYuzu(origen))}).", true);
    }

    // ───────────── Eden/Citron/Suyu → Ryujinx ─────────────

    private static ResultadoControles YuzuARyujinx(EmuladorConfig origen, EmuladorConfig destino)
    {
        var jugadores = LeerYuzu(origen).Where(j => j.Conectado && j.EsMando).ToList();
        if (jugadores.Count == 0)
            return new(false, $"{origen.Nombre} no tiene mandos conectados a ningún jugador.");

        var ruta = ArchivoConfig(destino);
        if (!File.Exists(ruta))
            return new(false, $"Abre {destino.Nombre} una vez para que cree su configuración.");
        var raiz = JsonNode.Parse(File.ReadAllText(ruta)) as JsonObject ?? throw new JsonException("Config.json de Ryujinx no es un objeto");
        var entradas = raiz["input_config"] as JsonArray ?? new JsonArray();
        var mandos = entradas.OfType<JsonObject>().Where(e => (e["backend"]?.GetValue<string>() ?? "").StartsWith("Gamepad", StringComparison.OrdinalIgnoreCase)).ToList();
        var plantilla = mandos.FirstOrDefault()
                        ?? throw new InvalidOperationException($"configura una vez cualquier mando en {destino.Nombre} para tomar su asignación de botones");

        var nuevas = new JsonArray();
        var tiposYuzuARyujinx = new Dictionary<string, string>
        {
            ["Pro Controller"] = "ProController",
            ["Joy-Con dobles"] = "JoyconPair",
            ["Joy-Con izquierdo"] = "JoyconLeft",
            ["Joy-Con derecho"] = "JoyconRight",
            ["Portátil"] = "Handheld",
        };
        foreach (var j in jugadores)
        {
            var id = IdRyujinx(j.Guid!, j.Puerto);
            // Si Ryujinx ya tiene ese mando configurado se conserva su asignación; si no, se usa la plantilla.
            var base_ = mandos.FirstOrDefault(m => string.Equals(m["id"]?.GetValue<string>(), id, StringComparison.OrdinalIgnoreCase)) ?? plantilla;
            var entrada = (JsonObject)base_.DeepClone();
            entrada["id"] = id;
            entrada["name"] = j.Dispositivo;
            entrada["player_index"] = j.Tipo == "Portátil" ? "Handheld" : $"Player{j.Numero}";
            entrada["controller_type"] = tiposYuzuARyujinx.GetValueOrDefault(j.Tipo, "ProController");
            nuevas.Add(entrada);
        }

        var antes = entradas.ToJsonString();
        if (antes == nuevas.ToJsonString())
            return new(true, $"{destino.Nombre} ya tiene los mismos controles que {origen.Nombre}.");
        raiz["input_config"] = nuevas;
        Guardar(destino, ruta, () => EscribirJson(ruta, raiz));
        return new(true, $"{destino.Nombre}: controles copiados de {origen.Nombre} ({Resumen(jugadores)}).", true);
    }

    // ───────────── Ryujinx → Eden/Citron/Suyu ─────────────

    /// <summary>
    /// En yuzu cada botón se guarda con el número de botón del mando, así que se reutiliza la asignación que ese mismo
    /// mando ya tenga en algún emulador de la familia (p. ej. Eden). Si nunca se configuró allí, ese jugador se omite.
    /// </summary>
    private static ResultadoControles RyujinxAYuzu(EmuladorConfig origen, EmuladorConfig destino, IReadOnlyList<EmuladorConfig> todos)
    {
        var jugadores = LeerRyujinx(origen).Where(j => j.EsMando && j.Numero <= MaxJugadores).ToList();
        if (jugadores.Count == 0)
            return new(false, $"{origen.Nombre} no tiene mandos asignados.");

        var conocidas = AsignacionesConocidas(destino, todos);

        var rutaDestino = ArchivoConfig(destino);
        var hacia = Ini.Leer(rutaDestino);
        // Cada valor va con su "\default=false": si no, yuzu ignora lo guardado y usa el valor por defecto.
        var nuevas = new List<(string Clave, string Valor)>();
        void Poner(string clave, string valor)
        {
            nuevas.RemoveAll(p => p.Clave == clave || p.Clave == clave + @"\default");
            nuevas.Add((clave + @"\default", "false"));
            nuevas.Add((clave, valor));
        }
        var omitidos = new List<string>();
        var copiados = new List<JugadorControl>();
        for (var n = 0; n < MaxJugadores; n++)
            Poner($"player_{n}_connected", "false");
        foreach (var j in jugadores)
        {
            if (!conocidas.TryGetValue(j.Guid!, out var asignacion))
            {
                omitidos.Add($"J{j.Numero} ({j.Dispositivo})");
                continue;
            }
            var n = j.Numero - 1;
            foreach (var (sufijo, valor) in asignacion.Where(a => !a.Clave.EndsWith(@"\default")))
                Poner($"player_{n}_{sufijo}", RxPuerto().Replace(valor, $"port:{j.Puerto}"));
            Poner($"player_{n}_connected", "true");
            Poner($"player_{n}_type", Array.IndexOf(TiposYuzu, j.Tipo) is >= 0 and var t ? t.ToString() : "0");
            copiados.Add(j);
        }
        if (copiados.Count == 0)
            return new(false, $"Ninguno de los mandos de {origen.Nombre} está configurado en Eden, Citron o Suyu todavía; configúralos una vez en uno de ellos.");

        hacia.Fijar("Controls", nuevas);
        Guardar(destino, rutaDestino, () => hacia.Guardar(rutaDestino));
        var nota = omitidos.Count > 0 ? $" Sin copiar (ese mando nunca se configuró en Eden/Citron/Suyu): {string.Join(", ", omitidos)}." : "";
        return new(true, $"{destino.Nombre}: controles copiados de {origen.Nombre} ({Resumen(copiados)}).{nota}", true);
    }

    /// <summary>
    /// Asignación de botones ya hecha para cada mando (GUID) en Eden, Citron o Suyu, empezando por <paramref name="destino"/>:
    /// sufijo de la clave (button_a, lstick…) → valor. Conserva calibraciones y cambios hechos a mano.
    /// </summary>
    internal static Dictionary<string, List<(string Clave, string Valor)>> AsignacionesConocidas(EmuladorConfig destino, IReadOnlyList<EmuladorConfig> todos)
    {
        var conocidas = new Dictionary<string, List<(string Clave, string Valor)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var archivo in ArchivosYuzuConocidos(destino, todos))
        {
            var pares = Ini.Leer(archivo).Pares("Controls").ToList();
            for (var n = 0; n < MaxJugadores; n++)
            {
                var prefijo = $"player_{n}_";
                var delJugador = pares.Where(p => p.Clave.StartsWith(prefijo)).ToList();
                var (guid, _) = MandoDeYuzu(QtIni.Decodificar(delJugador.FirstOrDefault(p => p.Clave == prefijo + "button_a").Valor ?? ""));
                if (guid != null && !conocidas.ContainsKey(guid))
                    conocidas[guid] = delJugador.Select(p => (p.Clave[prefijo.Length..], p.Valor)).ToList();
            }
        }
        return conocidas;
    }

    private static IEnumerable<string> ArchivosYuzuConocidos(EmuladorConfig destino, IReadOnlyList<EmuladorConfig> todos) =>
        // El propio destino primero, después los demás emuladores yuzu.
        new[] { destino }.Concat(todos.Where(e => e.Tipo == TipoEmulador.Yuzu))
            .Select(ArchivoConfig).Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase);

    // ───────────── Utilidades ─────────────

    /// <summary>"engine:sdl,guid:0300…,port:1,button:0" → (guid, 1).</summary>
    public static (string? Guid, int Puerto) MandoDeYuzu(string valor)
    {
        if (!valor.Contains("engine:sdl"))
            return (null, 0);
        var guid = RxGuid().Match(valor);
        var puerto = RxPuertoValor().Match(valor);
        return (guid.Success ? guid.Groups[1].Value.ToLowerInvariant() : null, puerto.Success ? int.Parse(puerto.Groups[1].Value) : 0);
    }

    /// <summary>"0-00000003-045e-0000-200b-000023057801" → ("030000005e040000200b000023057801", 0).</summary>
    public static (string? Guid, int Puerto) MandoDeRyujinx(string id)
    {
        var guion = id.IndexOf('-');
        if (guion < 1 || !int.TryParse(id[..guion], out var puerto) || !System.Guid.TryParse(id[(guion + 1)..], out var g))
            return (null, 0);
        return (Convert.ToHexStringLower(g.ToByteArray()), puerto);
    }

    /// <summary>El id que usa Ryujinx: índice entre mandos iguales + GUID de SDL con formato de Guid de .NET.</summary>
    public static string IdRyujinx(string guidSdl, int puerto) => $"{puerto}-{new Guid(Convert.FromHexString(guidSdl))}";

    private static string Resumen(IEnumerable<JugadorControl> jugadores) =>
        string.Join(", ", jugadores.Where(j => j.Conectado).Select(j => $"J{j.Numero}: {j.Dispositivo}"));

    internal static void Guardar(EmuladorConfig emulador, string ruta, Action escribir)
    {
        Respaldos.Copiar(ruta, $"{emulador.Id}-{Path.GetFileName(ruta)}");
        Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
        escribir();
    }

    internal static void EscribirJson(string ruta, JsonNode raiz)
    {
        var temporal = ruta + ".lanzador.tmp";
        File.WriteAllText(temporal, raiz.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        File.Move(temporal, ruta, true);
    }

    [GeneratedRegex(@"^player_\d+_")]
    private static partial Regex RxClaveJugador();

    [GeneratedRegex(@"guid:([0-9a-fA-F]{32})")]
    private static partial Regex RxGuid();

    [GeneratedRegex(@"port:(\d+)")]
    private static partial Regex RxPuertoValor();

    [GeneratedRegex(@"port:\d+")]
    private static partial Regex RxPuerto();
}

/// <summary>Archivo INI de QSettings: lee secciones y reemplaza valores conservando todo lo demás tal cual.</summary>
internal sealed class Ini
{
    private readonly List<string> _lineas;
    private readonly string _salto;

    private Ini(List<string> lineas, string salto)
    {
        _lineas = lineas;
        _salto = salto;
    }

    public static Ini Leer(string ruta)
    {
        var texto = File.Exists(ruta) ? File.ReadAllText(ruta, new UTF8Encoding(false)) : "";
        var salto = texto.Contains("\r\n") ? "\r\n" : "\n";
        var lineas = texto.Length == 0 ? new List<string>() : texto.Split(salto).ToList();
        if (lineas.Count > 0 && lineas[^1].Length == 0)
            lineas.RemoveAt(lineas.Count - 1);
        return new Ini(lineas, salto);
    }

    private (int Inicio, int Fin) Seccion(string nombre)
    {
        var inicio = _lineas.FindIndex(l => l.Trim() == $"[{nombre}]");
        if (inicio < 0)
            return (-1, -1);
        var fin = _lineas.Count;
        for (var i = inicio + 1; i < _lineas.Count; i++)
        {
            var l = _lineas[i].Trim();
            if (l.StartsWith('[') && l.EndsWith(']') && !l.Contains('='))
            {
                fin = i;
                break;
            }
        }
        return (inicio, fin);
    }

    public IEnumerable<(string Clave, string Valor)> Pares(string seccion)
    {
        var (inicio, fin) = Seccion(seccion);
        if (inicio < 0)
            yield break;
        for (var i = inicio + 1; i < fin; i++)
        {
            var igual = _lineas[i].IndexOf('=');
            if (igual > 0)
                yield return (_lineas[i][..igual], _lineas[i][(igual + 1)..]);
        }
    }

    public Dictionary<string, string> Valores(string seccion)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (c, v) in Pares(seccion))
            d[c] = v;
        return d;
    }

    /// <summary>Reemplaza el valor de cada clave en la sección (o la agrega al final de ella).</summary>
    public void Fijar(string seccion, IEnumerable<(string Clave, string Valor)> valores)
    {
        var (inicio, fin) = Seccion(seccion);
        if (inicio < 0)
        {
            if (_lineas.Count > 0 && _lineas[^1].Trim().Length > 0)
                _lineas.Add("");
            _lineas.Add($"[{seccion}]");
            inicio = _lineas.Count - 1;
            fin = _lineas.Count;
        }
        var indices = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = inicio + 1; i < fin; i++)
        {
            var igual = _lineas[i].IndexOf('=');
            if (igual > 0)
                indices[_lineas[i][..igual]] = i;
        }
        // Inserta antes de las líneas en blanco que cierran la sección.
        var insertar = fin;
        while (insertar - 1 > inicio && _lineas[insertar - 1].Trim().Length == 0)
            insertar--;
        foreach (var (clave, valor) in valores)
        {
            if (indices.TryGetValue(clave, out var i))
            {
                _lineas[i] = $"{clave}={valor}";
                continue;
            }
            _lineas.Insert(insertar, $"{clave}={valor}");
            foreach (var k in indices.Keys.ToList())
                if (indices[k] >= insertar)
                    indices[k]++;
            indices[clave] = insertar;
            insertar++;
        }
    }

    public void Guardar(string ruta)
    {
        var temporal = ruta + ".lanzador.tmp";
        File.WriteAllText(temporal, string.Join(_salto, _lineas) + _salto, new UTF8Encoding(false));
        File.Move(temporal, ruta, true);
    }
}
