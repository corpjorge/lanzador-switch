using System.Text.Json.Nodes;
using Lanzador.Core;

/// <summary>Pruebas de las carpetas de actualizaciones y DLC, sobre copias de la configuración real de los emuladores.</summary>
static class PruebasContenido
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
        Console.WriteLine("\n== Carpetas de actualizaciones y DLC (sobre copias)");
        Comprobar(ContenidoExterno.TipoDeArchivo(@"C:\x\Juego [0100AAAA00001000][v0].nsp") == "Juego"
                  && ContenidoExterno.TipoDeArchivo(@"C:\x\Juego [0100AAAA00001800][v65536].nsz") == "Actualización"
                  && ContenidoExterno.TipoDeArchivo(@"C:\x\Juego [0100AAAA00002001][v0].nsp") == "DLC"
                  && ContenidoExterno.TipoDeArchivo(@"C:\x\sin id.nsp") == null, "El Title ID del nombre dice si es juego, actualización o DLC");

        var raiz = Path.Combine(temporal, "contenido");
        var nuevaA = Directory.CreateDirectory(Path.Combine(raiz, "mis updates")).FullName;
        var nuevaB = Directory.CreateDirectory(Path.Combine(raiz, "mis dlc")).FullName;

        foreach (var real in config.Emuladores.Where(e => e.Instalado))
        {
            var origen = Lanzador.Core.Controles.Controles.ArchivoConfig(real);
            if (!File.Exists(origen))
                continue;
            var datos = Path.Combine(raiz, real.Id, real.Tipo == TipoEmulador.Ryujinx ? "portable" : "user");
            var copia = new EmuladorConfig { Id = real.Id + "-contenido", Nombre = real.Nombre, Tipo = real.Tipo, RutaExe = Path.Combine(raiz, real.Id, "no-existe.exe"), CarpetaDatos = datos };
            var destino = Lanzador.Core.Controles.Controles.ArchivoConfig(copia);
            Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
            File.Copy(origen, destino, true);

            var antes = ContenidoExterno.Leer(copia);
            Console.WriteLine($"  {real.Nombre}: admite={ContenidoExterno.Admite(copia)} · {antes.Count} carpetas: {string.Join(" ; ", antes)}");
            if (!ContenidoExterno.Admite(copia))
            {
                var nada = ContenidoExterno.Agregar(copia, [nuevaA]);
                Comprobar(nada is { Exito: true, Cambio: false } && File.ReadAllText(destino) == File.ReadAllText(origen), $"{real.Nombre}: no admite carpetas de contenido y su configuración no se toca");
                continue;
            }

            var lineasAntes = File.ReadAllLines(destino);
            var r = ContenidoExterno.Agregar(copia, [nuevaA, nuevaB, nuevaA + "\\", Path.Combine(raiz, "no existe")]);
            var despues = ContenidoExterno.Leer(copia).Select(ContenidoExterno.Normalizar).ToList();
            Comprobar(r is { Exito: true, Cambio: true } && despues.Count == antes.Count + 2 && despues.Contains(nuevaA) && despues.Contains(nuevaB)
                      && antes.All(a => despues.Contains(ContenidoExterno.Normalizar(a))), $"{real.Nombre}: se agregan las dos carpetas nuevas y se conservan las {antes.Count} que tenía ({r.Mensaje})");
            Comprobar(ContenidoExterno.Agregar(copia, [nuevaA, nuevaB]) is { Exito: true, Cambio: false } && ContenidoExterno.Leer(copia).Count == despues.Count,
                $"{real.Nombre}: agregarlas otra vez no duplica nada");

            if (real.Tipo == TipoEmulador.Ryujinx)
            {
                var a = (JsonObject)JsonNode.Parse(File.ReadAllText(origen))!;
                var b = (JsonObject)JsonNode.Parse(File.ReadAllText(destino))!;
                a.Remove("autoload_dirs");
                b.Remove("autoload_dirs");
                Comprobar(a.ToJsonString() == b.ToJsonString(), $"{real.Nombre}: el resto de Config.json queda igual");
            }
            else
            {
                static bool DeContenido(string l) => l.StartsWith(@"Paths\external_content_dirs\");
                var lineasDespues = File.ReadAllLines(destino);
                Comprobar(lineasAntes.Where(l => !DeContenido(l)).SequenceEqual(lineasDespues.Where(l => !DeContenido(l))), $"{real.Nombre}: el resto de qt-config.ini queda idéntico");
                var entradas = lineasDespues.Where(l => DeContenido(l) && l.Contains(@"\path=")).ToList();
                var tamano = lineasDespues.Single(l => l.StartsWith(@"Paths\external_content_dirs\size=")).Split('=')[1];
                var indices = entradas.Select(l => int.Parse(l.Split('\\')[2])).ToList();
                Comprobar(int.Parse(tamano) == indices.Max() && indices.Distinct().Count() == indices.Count && entradas.Count(l => l.EndsWith("mis updates/")) == 1,
                    $"{real.Nombre}: «size» cubre todas las entradas ({tamano}), sin índices repetidos y con barras de Qt");
            }
        }
        ComprobarSincronizacion(raiz);
        ComprobarFicha(raiz);
        return _fallos;
    }

    private static void ComprobarSincronizacion(string raiz)
    {
        var biblioteca = Directory.CreateDirectory(Path.Combine(raiz, "biblioteca")).FullName;
        var updates = Directory.CreateDirectory(Path.Combine(biblioteca, "updates")).FullName;
        var dlc = Directory.CreateDirectory(Path.Combine(biblioteca, "dlc")).FullName;
        var config = new Configuracion { CarpetasJuegos = [biblioteca] };
        var carpetas = ContenidoExterno.CarpetasDeBiblioteca(config);
        Comprobar(carpetas.Count == 2 && carpetas.Contains(updates) && carpetas.Contains(dlc),
            "Carga automática: descubre las carpetas de contenido de la biblioteca sin agregarlas a mano");

        var datos = Directory.CreateDirectory(Path.Combine(raiz, "qt-con-restos", "config")).Parent!.FullName;
        var emu = new EmuladorConfig { Id = "qt-con-restos", CarpetaDatos = datos, RutaExe = Path.Combine(datos, "emulador.exe") };
        var ini = Path.Combine(datos, "config", "qt-config.ini");
        File.WriteAllText(ini, $"""
            [UI]
            Paths\external_content_dirs\size=1
            Paths\external_content_dirs\1\path={updates.Replace('\\', '/')}
            Paths\external_content_dirs\4\path={dlc.Replace('\\', '/')}
            """);
        Comprobar(ContenidoExterno.Leer(emu).Count == 1 && ContenidoExterno.Agregar(emu, carpetas).Cambio &&
                  ContenidoExterno.Leer(emu).Select(ContenidoExterno.Normalizar).SequenceEqual(new[] { updates, dlc }) &&
                  !ContenidoExterno.Agregar(emu, carpetas).Cambio,
            "Carga automática: respeta size de Qt, registra rutas fuera del tamaño y evita duplicados");
    }

    private static void ComprobarFicha(string raiz)
    {
        var archivos = Directory.CreateDirectory(Path.Combine(raiz, "ficha")).FullName;
        var juego = new Juego { TitleId = "0100AAAA00001000" };
        var update = Path.Combine(archivos, "Parche [0100AAAA00001800].nsp");
        var dlc = Path.Combine(archivos, "Complemento [0100AAAA00002001].nsp");
        foreach (var archivo in new[] { update, Path.ChangeExtension(update, ".nsz"), dlc,
            Path.Combine(archivos, "Otro juego [0100AAAA00002800].nsp") })
            File.WriteAllText(archivo, "");

        var paquetes = ContenidoJuego.Buscar(juego, [archivos]);
        Comprobar(paquetes.Count == 2 && paquetes.Any(p => p.Ruta == update && p.Convertido) && paquetes.Any(p => p.Ruta == dlc),
            "Ficha: asocia actualización y DLC al juego, agrupa la conversión y excluye el juego adyacente");

        var emu = new EmuladorConfig { Id = "ryujinx-ficha", Tipo = TipoEmulador.Ryujinx, CarpetaDatos = archivos };
        var registro = Directory.CreateDirectory(Path.Combine(archivos, "games", juego.TitleId.ToLowerInvariant())).FullName;
        var actualizaciones = new JsonObject { ["selected"] = update, ["paths"] = new JsonArray(JsonValue.Create(update)) };
        File.WriteAllText(Path.Combine(registro, "updates.json"), actualizaciones.ToJsonString());
        var complementos = new JsonArray(new JsonObject { ["path"] = dlc, ["dlc_nca_list"] = new JsonArray(
            new JsonObject { ["is_enabled"] = false }) });
        File.WriteAllText(Path.Combine(registro, "dlc.json"), complementos.ToJsonString());
        var filas = ContenidoJuego.Leer(juego, emu, paquetes);
        Comprobar(filas.Count == 2 && filas.Any(f => f.Ruta == update && f.Activo) &&
                  filas.Any(f => f.Ruta == dlc && !f.Activo && f.Estado == "Desactivado"),
            "Ficha: Ryujinx indica la actualización activa y el DLC desactivado sin duplicar los archivos");
    }
}
