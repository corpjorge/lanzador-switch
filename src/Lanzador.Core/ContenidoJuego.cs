using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Lanzador.Core.Conversion;
using Lanzador.Core.Trucos;

namespace Lanzador.Core;

public enum TipoContenidoJuego { Actualizacion, Dlc, Mod }

public sealed record PaqueteJuego(TipoContenidoJuego Tipo, ulong TitleId, string Ruta, ArchivoComprimido? Comprimido,
    bool Convertido = false);

public sealed record EntradaContenidoJuego(TipoContenidoJuego Tipo, string Nombre, string Estado,
    string? Ruta = null, string? Detalle = null, bool Activo = false, bool Convertible = false);

public static partial class ContenidoJuego
{
    public static IReadOnlyList<PaqueteJuego> Buscar(Juego juego, IEnumerable<string> carpetas,
        IEnumerable<EmuladorConfig>? emuladores = null)
    {
        if (!ulong.TryParse(juego.TitleId, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id) ||
            !juego.TieneTitleId || id == 0 || id > ulong.MaxValue - 0x17FF)
            return [];

        using var cabecera = emuladores == null ? null : CabeceraNca.Abrir(emuladores);
        var encontrados = new Dictionary<string, PaqueteJuego>(StringComparer.OrdinalIgnoreCase);
        foreach (var ruta in EscanerBiblioteca.Archivos(carpetas))
        {
            var extension = Path.GetExtension(ruta);
            var idNombre = EscanerBiblioteca.LeerTitleId(Path.GetFileNameWithoutExtension(ruta));
            var titulo = idNombre.GetValueOrDefault();
            if (idNombre == null && cabecera != null &&
                (extension.Equals(".nsp", StringComparison.OrdinalIgnoreCase) || extension.Equals(".nsz", StringComparison.OrdinalIgnoreCase)))
                titulo = ConversorNsz.LeerTitleIds(ruta, cabecera).FirstOrDefault(t => TipoDeTitleId(id, t) != null);
            if (TipoDeTitleId(id, titulo) is not { } tipo)
                continue;

            var comprimido = extension.Equals(".nsz", StringComparison.OrdinalIgnoreCase) ||
                             extension.Equals(".xcz", StringComparison.OrdinalIgnoreCase);
            if (comprimido)
            {
                var destino = Path.ChangeExtension(ruta, extension.Equals(".nsz", StringComparison.OrdinalIgnoreCase) ? ".nsp" : ".xci");
                if (File.Exists(destino))
                {
                    encontrados[destino] = new(tipo, titulo, destino, null, Convertido: true);
                    continue;
                }
            }
            encontrados.TryAdd(ruta, new(tipo, titulo, ruta, comprimido ? ConversorNsz.Examinar(ruta) : null));
        }
        return encontrados.Values.ToList();
    }

    private static TipoContenidoJuego? TipoDeTitleId(ulong idJuego, ulong id) =>
        id == idJuego + 0x800 ? TipoContenidoJuego.Actualizacion
        : id > idJuego + 0x1000 && id <= idJuego + 0x17FF ? TipoContenidoJuego.Dlc
        : null;

    public static IReadOnlyList<EntradaContenidoJuego> Leer(Juego juego, EmuladorConfig? emulador, IReadOnlyList<PaqueteJuego> paquetes)
    {
        if (!juego.TieneTitleId ||
            !ulong.TryParse(juego.TitleId, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id) ||
            id == 0 || id > ulong.MaxValue - 0x17FF)
            return [];

        var filas = paquetes.Select(p => CrearFila(p, emulador)).ToList();
        var hayIncluida = juego.VersionIncluida is > 0 || juego.VersionTexto != null;
        if (hayIncluida && !juego.Comprimido)
        {
            var version = juego.VersionTexto ?? VersionDetectada.Texto(juego.VersionIncluida!.Value);
            filas.Add(new(TipoContenidoJuego.Actualizacion, $"Actualización {version}",
                "Incluida en el juego", juego.RutaArchivo));
        }

        if (emulador != null && !string.IsNullOrWhiteSpace(emulador.CarpetaDatos))
        {
            if (emulador.Tipo == TipoEmulador.Ryujinx)
                LeerRyujinx(emulador, id, filas);
            else
                LeerYuzu(emulador, id, paquetes, filas);
        }

        return filas.OrderBy(f => f.Tipo).ThenByDescending(f => f.Convertible)
            .ThenByDescending(f => f.Activo).ThenBy(f => f.Nombre, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static EntradaContenidoJuego CrearFila(PaqueteJuego paquete, EmuladorConfig? emulador)
    {
        var estado = paquete.Comprimido?.Estado switch
        {
            EstadoComprimido.Convertible => "Pendiente de convertir",
            EstadoComprimido.Danado => "Archivo dañado o ilegible",
            EstadoComprimido.NoCompatible => "Conversión no disponible",
            _ => emulador == null ? "Disponible en disco" : "Pendiente de cargar o instalar",
        };
        return new(paquete.Tipo, Path.GetFileNameWithoutExtension(paquete.Ruta), estado, paquete.Ruta,
            paquete.Convertido ? "Convertido" : paquete.Comprimido?.Detalle,
            Convertible: paquete.Comprimido?.Estado == EstadoComprimido.Convertible);
    }

    private static void LeerRyujinx(EmuladorConfig emulador, ulong id, List<EntradaContenidoJuego> filas)
    {
        var datos = emulador.CarpetaDatos;
        var juego = Path.Combine(datos, "games", $"{id:x16}");
        if (LeerJson(Path.Combine(juego, "updates.json")) is JsonObject actualizaciones)
        {
            var seleccionada = Texto(actualizaciones["selected"]);
            foreach (var archivo in Elementos(actualizaciones["paths"]).Select(Texto).Append(seleccionada)
                .OfType<string>().Where(p => p.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var activa = archivo.Equals(seleccionada, StringComparison.OrdinalIgnoreCase);
                Registrar(filas, TipoContenidoJuego.Actualizacion, archivo,
                    activa ? "Activa" : "Registrada · sin seleccionar", activa);
            }
        }

        foreach (var paquete in Elementos(LeerJson(Path.Combine(juego, "dlc.json"))).OfType<JsonObject>())
        {
            if (Texto(paquete["path"]) is not { Length: > 0 } ruta)
                continue;
            var dlc = Elementos(paquete["dlc_nca_list"]).OfType<JsonObject>().ToArray();
            var activos = dlc.Count(d => Booleano(d["is_enabled"]) == true);
            var estado = activos == 0 ? "Desactivado" : activos == dlc.Length ? "Activo" : $"Activos {activos} de {dlc.Length}";
            Registrar(filas, TipoContenidoJuego.Dlc, ruta, dlc.Length > 0 ? estado : "Registrado", activos > 0,
                dlc.Length > 1 ? $"{dlc.Length} complementos en el paquete" : null);
        }

        var modsJson = LeerJson(Path.Combine(juego, "mods.json"));
        var definiciones = Elementos((modsJson as JsonObject)?["mods"] ?? modsJson).OfType<JsonObject>()
            .Where(m => Texto(m["path"]) is { Length: > 0 })
            .GroupBy(m => Texto(m["path"])!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => Booleano(g.Last()["enabled"]), StringComparer.OrdinalIgnoreCase);
        var raices = new[]
        {
            Path.Combine(datos, "mods", "contents", $"{id:x16}"),
            Path.Combine(datos, "sdcard", "atmosphere", "contents", $"{id:x16}"),
        };
        foreach (var raiz in raices.Where(Directory.Exists))
        {
            foreach (var mod in Directorios(raiz).Where(EsCarpetaMod))
                AgregarMod(filas, mod, definiciones.GetValueOrDefault(mod) ?? true);
            foreach (var nombre in new[] { "romfs.bin", "exefs.nsp" })
            {
                var ruta = Path.Combine(raiz, nombre);
                if (File.Exists(ruta))
                    AgregarMod(filas, ruta, definiciones.GetValueOrDefault(ruta) ?? true);
            }
        }
        foreach (var (ruta, habilitado) in definiciones)
        {
            if ((Directory.Exists(ruta) || File.Exists(ruta)) &&
                !filas.Any(f => f.Tipo == TipoContenidoJuego.Mod && string.Equals(f.Ruta, ruta, StringComparison.OrdinalIgnoreCase)))
                AgregarMod(filas, ruta, habilitado);
        }

        MarcarCarpetasDeCarga(emulador, filas);
    }

    private static void LeerYuzu(EmuladorConfig emulador, ulong id, IReadOnlyList<PaqueteJuego> paquetes,
        List<EntradaContenidoJuego> filas)
    {
        var datos = emulador.CarpetaDatos;
        var deshabilitados = QtIni.Leer(Path.Combine(datos, "config", "qt-config.ini")).Deshabilitados($"{id:X16}");
        var cache = Path.Combine(datos, "cache", "game_list", $"{id:X16}.pv.txt");
        var instalados = LeerInstaladosYuzu(emulador, id);
        var dlcVistos = new HashSet<int>();
        foreach (var linea in File.Exists(cache) ? File.ReadAllLines(cache).Select(l => l.Trim()) : [])
        {
            var desactivada = linea.StartsWith("[D] ", StringComparison.OrdinalIgnoreCase);
            var etiqueta = desactivada ? linea[4..] : linea;
            if (etiqueta.StartsWith("Update (", StringComparison.OrdinalIgnoreCase))
            {
                var version = RxVersionCache().Match(etiqueta).Groups[1].Value;
                var fuente = etiqueta.Contains("(NAND)", StringComparison.OrdinalIgnoreCase) ? "NAND del emulador" : "Caché del emulador";
                if (fuente == "NAND del emulador")
                    instalados.Remove(id + 0x800);
                var estado = desactivada ? "Desactivada" : "Habilitada";
                var candidatos = filas.Where(f => f.Tipo == TipoContenidoJuego.Actualizacion && f.Ruta != null &&
                    !EsComprimido(f.Ruta) && EscanerBiblioteca.LeerVersion(Path.GetFileNameWithoutExtension(f.Ruta)).Texto == version)
                    .ToList();
                if (version.Length > 0 && candidatos.Count == 1 && fuente != "NAND del emulador")
                {
                    var candidata = candidatos[0];
                    filas[filas.IndexOf(candidata)] = candidata with { Estado = estado, Detalle = fuente };
                }
                else
                    filas.Add(new(TipoContenidoJuego.Actualizacion, "Actualización" + (version.Length > 0 ? $" {version}" : ""), estado, Detalle: fuente));
            }
            else if (etiqueta.StartsWith("DLC (", StringComparison.OrdinalIgnoreCase))
            {
                var indices = RxNumero().Matches(etiqueta)
                    .Select(m => int.TryParse(m.Value, CultureInfo.InvariantCulture, out var n) ? n : -1)
                    .Where(n => n > 0 && n <= 0x7FF && dlcVistos.Add(n));
                foreach (var indice in indices)
                {
                    var instalado = instalados.Remove(id + 0x1000 + (ulong)indice, out var origen);
                    var activo = !desactivada && !deshabilitados.Contains("DLC");
                    var asociados = paquetes.Where(p => p.Tipo == TipoContenidoJuego.Dlc && !EsComprimido(p.Ruta) &&
                        (p.TitleId & 0x7FFUL) == (ulong)indice).ToList();
                    if (asociados.Count == 1)
                        Registrar(filas, TipoContenidoJuego.Dlc, asociados[0].Ruta, activo ? "Activo" : "Desactivado", activo, origen);
                    else
                        filas.Add(new(TipoContenidoJuego.Dlc, $"DLC #{indice}", activo ? "Activo" : "Desactivado",
                            Detalle: instalado ? origen : "Caché del emulador", Activo: activo));
                }
            }
        }

        foreach (var (titulo, origen) in instalados)
        {
            if (titulo == id + 0x800)
            {
                filas.Add(new(TipoContenidoJuego.Actualizacion, "Actualización instalada",
                    deshabilitados.Contains("Update (NAND)") ? "Desactivada" : "Instalada",
                    Detalle: origen + " · versión sin confirmar"));
                continue;
            }

            var activo = !deshabilitados.Contains("DLC");
            var archivos = paquetes.Where(p => p.TitleId == titulo && !EsComprimido(p.Ruta)).ToList();
            if (archivos.Count == 1)
                Registrar(filas, TipoContenidoJuego.Dlc, archivos[0].Ruta, activo ? "Activo" : "Desactivado", activo, origen);
            else
                filas.Add(new(TipoContenidoJuego.Dlc, $"DLC #{titulo - id - 0x1000}", activo ? "Activo" : "Desactivado",
                    Detalle: origen, Activo: activo));
        }

        if (deshabilitados.Contains("Update"))
        {
            var incluida = filas.FindIndex(f => f.Estado == "Incluida en el juego");
            if (incluida >= 0)
                filas[incluida] = filas[incluida] with { Estado = "Incluida · desactivada" };
        }
        foreach (var paquete in paquetes.Where(p => p.Tipo == TipoContenidoJuego.Actualizacion && !EsComprimido(p.Ruta)))
        {
            if (EscanerBiblioteca.LeerVersion(Path.GetFileNameWithoutExtension(paquete.Ruta)).Version is { } version &&
                deshabilitados.Contains($"Update@{version}"))
                Registrar(filas, paquete.Tipo, paquete.Ruta, "Desactivada", false);
        }
        foreach (var mod in Directorios(Path.Combine(datos, "load", $"{id:X16}")).Where(EsCarpetaMod))
            AgregarMod(filas, mod, !deshabilitados.Contains(Path.GetFileName(mod)));

        MarcarCarpetasDeCarga(emulador, filas);
    }

    private static Dictionary<ulong, string> LeerInstaladosYuzu(EmuladorConfig emulador, ulong id)
    {
        var instalados = new Dictionary<ulong, string>();
        var raices = new[]
        {
            (Path.Combine(emulador.CarpetaDatos, "nand", "user", "Contents", "registered"), "NAND del emulador"),
            (Path.Combine(emulador.CarpetaDatos, "sdmc", "Nintendo", "Contents", "registered"), "Tarjeta SD del emulador"),
        };
        if (!raices.Any(r => Directory.Exists(r.Item1)))
            return instalados;

        using var cabecera = CabeceraNca.Abrir([emulador]);
        if (cabecera == null)
            return instalados;

        foreach (var (ruta, origen) in raices.Where(r => Directory.Exists(r.Item1)))
        {
            foreach (var archivo in Directory.EnumerateFiles(ruta, "*.nca", new EnumerationOptions
                { RecurseSubdirectories = true, IgnoreInaccessible = true }))
            {
                try
                {
                    using var nca = new FileStream(archivo, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    if (cabecera.LeerTitleId(nca) is { } titulo && TipoDeTitleId(id, titulo) != null)
                        instalados.TryAdd(titulo, origen);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        return instalados;
    }

    private static void MarcarCarpetasDeCarga(EmuladorConfig emulador, List<EntradaContenidoJuego> filas)
    {
        var carpetas = ContenidoExterno.Leer(emulador).Where(Path.IsPathFullyQualified)
            .Select(c => Path.GetFullPath(c).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar).ToArray();
        for (var i = 0; i < filas.Count; i++)
        {
            if (filas[i] is not { Estado: "Pendiente de cargar o instalar", Ruta: { } ruta } fila)
                continue;

            var archivo = Path.GetFullPath(ruta);
            var estado = carpetas.Any(c => archivo.StartsWith(c, StringComparison.OrdinalIgnoreCase))
                ? "Configurada para carga automática"
                : emulador.Id == "suyu" ? "En disco · instalación manual" : "En disco · carga sin confirmar";
            filas[i] = fila with { Estado = estado };
        }
    }

    private static void Registrar(List<EntradaContenidoJuego> filas, TipoContenidoJuego tipo, string ruta, string estado,
        bool activo, string? detalle = null)
    {
        var posicion = filas.FindIndex(f => f.Tipo == tipo && string.Equals(f.Ruta, ruta, StringComparison.OrdinalIgnoreCase));
        var disponible = File.Exists(ruta) && !EsComprimido(ruta);
        var fila = posicion >= 0 ? filas[posicion] : new EntradaContenidoJuego(tipo, Path.GetFileNameWithoutExtension(ruta), "", ruta);
        if (fila.Convertible)
            return;
        fila = fila with
        {
            Estado = disponible ? estado : "Archivo registrado no disponible",
            Activo = activo && disponible,
            Detalle = detalle ?? fila.Detalle,
        };
        if (posicion >= 0)
            filas[posicion] = fila;
        else
            filas.Add(fila);
    }

    private static void AgregarMod(List<EntradaContenidoJuego> filas, string ruta, bool? activo) =>
        filas.Add(new(TipoContenidoJuego.Mod, Path.GetFileName(ruta), activo switch
        {
            true => "Activo",
            false => "Desactivado",
            null => "Detectado",
        }, ruta, Activo: activo == true));

    private static bool EsCarpetaMod(string ruta) =>
        new[] { "romfs", "romfs_ext", "exefs" }.Any(tipo => Directory.Exists(Path.Combine(ruta, tipo)));

    private static IEnumerable<string> Directorios(string ruta) =>
        Directory.Exists(ruta) ? Directory.EnumerateDirectories(ruta) : [];

    private static bool EsComprimido(string ruta) =>
        Path.GetExtension(ruta) is var extension && (extension.Equals(".nsz", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".xcz", StringComparison.OrdinalIgnoreCase));

    private static JsonNode? LeerJson(string ruta)
    {
        try { return File.Exists(ruta) ? JsonNode.Parse(File.ReadAllText(ruta)) : null; }
        catch (System.Text.Json.JsonException) { return null; }
    }

    private static IEnumerable<JsonNode?> Elementos(JsonNode? nodo) => nodo is JsonArray lista ? lista : [];
    private static string? Texto(JsonNode? nodo) => nodo is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
    private static bool? Booleano(JsonNode? nodo) => nodo is JsonValue v && v.TryGetValue<bool>(out var b) ? b : null;

    [GeneratedRegex(@"\((\d+(?:\.\d+)+)\)$")]
    private static partial Regex RxVersionCache();

    [GeneratedRegex(@"\d+")]
    private static partial Regex RxNumero();
}
