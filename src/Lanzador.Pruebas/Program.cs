using Lanzador.Core;
using Lanzador.Core.IA;
using Lanzador.Core.Trucos;

// Pruebas del núcleo con los datos reales, sin modificar nada de los emuladores:
// todo lo que escribe va a una carpeta temporal con copias.
Console.OutputEncoding = System.Text.Encoding.UTF8;
var temporal = Path.Combine(Path.GetTempPath(), "lanzador-pruebas");
if (Directory.Exists(temporal))
    Directory.Delete(temporal, true);
Rutas.Configurar(Path.Combine(temporal, "datos"));
var fallos = 0;

void Comprobar(bool condicion, string descripcion)
{
    Console.WriteLine($"  [{(condicion ? "OK" : "FALLO")}] {descripcion}");
    if (!condicion)
        fallos++;
}

Console.WriteLine("== Configuración detectada");
// La carpeta app junto a Lanzador.slnx, como la del lanzador publicado (o LANZADOR_APP).
var carpetaApp = Environment.GetEnvironmentVariable("LANZADOR_APP") ?? CarpetaApp();
var config = GestorConfiguracion.Detectar(carpetaApp);
foreach (var e in config.Emuladores)
    Console.WriteLine($"  {e.Nombre}: {e.RutaExe} | datos={e.CarpetaDatos} | instalado={e.Instalado}");
Console.WriteLine($"  Carpetas: {string.Join(" ; ", config.CarpetasJuegos)}");
var eden = config.Emulador("eden")!;
var ryujinx = config.Emulador("ryujinx")!;
Comprobar(eden.Instalado && ryujinx.Instalado, "Eden y Ryujinx detectados");

Console.WriteLine("\n== Biblioteca");
var nombres = EscanerBiblioteca.NombresDeRyujinx(ryujinx.CarpetaDatos);
var juegos = EscanerBiblioteca.Escanear(config.CarpetasJuegos, nombres);
Console.WriteLine($"  {juegos.Count} juegos ({juegos.Count(j => j.TieneTitleId)} con Title ID)");
foreach (var j in juegos.Take(8))
    Console.WriteLine($"  {j.TitleId,-16} {j.Nombre} | incluida={j.VersionIncluida} {j.VersionTexto} | actualizaciones=[{string.Join(", ", j.Actualizaciones.Select(a => a.Version?.ToString() ?? a.VersionTexto))}]");
var mario = juegos.FirstOrDefault(j => j.TitleId == "010028600EBDA000");
Comprobar(mario != null, "Mario 3D World + Bowser's Fury está en la biblioteca");

Console.WriteLine("\n== Compatibilidad (Ryubing)");
var compat = await Compatibilidad.CargarRyubingAsync(false, CancellationToken.None);
Console.WriteLine($"  {compat.Count} entradas");
var conCompat = juegos.Where(j => compat.ContainsKey(j.TitleId)).ToList();
Console.WriteLine($"  {conCompat.Count} de tus juegos aparecen en la lista");
foreach (var j in conCompat.Take(5))
    Console.WriteLine($"  {j.Nombre}: {compat[j.TitleId].EstadoEs} ({compat[j.TitleId].EtiquetasEs})");
Comprobar(compat.Count > 1000, "La lista de compatibilidad se descargó");

Console.WriteLine("\n== Compatibilidad (Citron Neo y yuzu 2024)");
var formatoYuzu = Compatibilidad.ParsearYuzu("""
    [{"title":"Juego A","compatibility":0,"releases":[{"id":"0100000000010000"}]},
     {"directory":"Juego B","compatibility":3,"releases":[{"id":"0100152000022000"},{"id":"0100152000022800"}]},
     {"title":"Sin probar","compatibility":99,"releases":[{"id":"010028600EBDA000"}]},
     {"title":"Roto","compatibility":"x","releases":[]}]
    """);
Comprobar(formatoYuzu.Count == 4 && formatoYuzu["0100000000010000"].EstadoEs == "Perfecto" && formatoYuzu["0100152000022800"].Nombre == "Juego B"
          && formatoYuzu["0100152000022000"].Nivel == 2 && formatoYuzu["010028600EBDA000"].Nivel == -1, "Formato de yuzu: título o carpeta, varias versiones, sin probar");
var citron = await Compatibilidad.CargarCitronAsync(false, CancellationToken.None);
var yuzu = await Compatibilidad.CargarYuzuAsync(CancellationToken.None);
Console.WriteLine($"  Citron Neo: {citron.Count} entradas, {juegos.Count(j => citron.ContainsKey(j.TitleId))} de tus juegos");
Console.WriteLine($"  yuzu (2024): {yuzu.Count} entradas, {juegos.Count(j => yuzu.ContainsKey(j.TitleId))} de tus juegos");
Comprobar(citron.Count > 20 && yuzu.Count > 2000, "Se descargaron las listas de Citron Neo y de yuzu");

Console.WriteLine("\n== Botones");
Comprobar(LectorBotones.Describir(["80000002"]) == "mantener B", "80000002 = mantener B");
Comprobar(LectorBotones.Describir(["80000440"]) == "mantener L + Más", "80000440 = mantener L + Más");

Console.WriteLine("\n== Catálogo de trucos de Mario");
var catalogo = await FuentesTrucos.CargarAsync("010028600EBDA000", false, CancellationToken.None);
Console.WriteLine($"  Título: {catalogo.Titulo}");
Console.WriteLine($"  Versiones: {string.Join(", ", catalogo.Versiones.Select(v => $"{VersionDetectada.Texto(v.Key)}={v.Value}"))}");
foreach (var (build, lista) in catalogo.PorBuild)
    Console.WriteLine($"  {build}: {lista.Count} trucos ({lista.Count(t => t.Fuentes.Contains(FuentesTrucos.NombreSwitchCheatsDb))} scdb, {lista.Count(t => t.Fuentes.Contains(FuentesTrucos.NombreTinfoil))} tinfoil, {lista.Count(t => t.Fuentes.Count > 1)} en ambas)");
foreach (var aviso in catalogo.Avisos)
    Console.WriteLine($"  AVISO: {aviso}");
Comprobar(catalogo.BuildDeVersion(0) == "9F7EFC2FB9653E5C", "v0 = 9F7EFC2FB9653E5C");
Comprobar(catalogo.BuildDeVersion(0x40000) == "635FE25C7CB69847", "v4 = 635FE25C7CB69847");
Comprobar(catalogo.Para("9F7EFC2FB9653E5C").Count > 0, "Hay trucos para v0");
var ejemplo = catalogo.Para("9F7EFC2FB9653E5C").FirstOrDefault(t => t.Botones != null);
if (ejemplo != null)
    Console.WriteLine($"  Ejemplo con botones: {ejemplo.NombreOriginal} → {ejemplo.Botones}");

Console.WriteLine("\n== Versión detectada");
if (mario != null)
{
    var vEden = DetectorVersion.Detectar(mario, eden, catalogo);
    var vRyu = DetectorVersion.Detectar(mario, ryujinx, catalogo);
    Console.WriteLine($"  Eden: {VersionDetectada.Texto(vEden.Version)} ({vEden.Motivo})");
    Console.WriteLine($"  Ryujinx: {VersionDetectada.Texto(vRyu.Version)} ({vRyu.Motivo})");
    Comprobar(vEden.Version == 0, "En Eden usas la v0 (actualizaciones desactivadas)");
}
foreach (var j in juegos.Where(j => j.TieneTitleId).Take(12))
{
    var v = DetectorVersion.Detectar(j, eden, null);
    Console.WriteLine($"  {j.Nombre}: Eden {VersionDetectada.Texto(v.Version)}{(v.Aproximada ? "?" : "")} — {v.Motivo}");
}

Console.WriteLine("\n== Trucos instalados en Eden (solo lectura)");
var gestorEden = GestoresTrucos.Para(eden);
var instaladosV0 = gestorEden.Leer("010028600EBDA000", "9F7EFC2FB9653E5C");
var instaladosV4 = gestorEden.Leer("010028600EBDA000", "635FE25C7CB69847");
Console.WriteLine($"  v0: {instaladosV0.Count} ({instaladosV0.Count(t => t.Activo)} activos) | v4: {instaladosV4.Count} ({instaladosV4.Count(t => t.Activo)} activos)");
foreach (var t in instaladosV0.Take(4))
    Console.WriteLine($"  {(t.Activo ? "●" : "○")} {t.Nombre} {t.Botones}");
Comprobar(instaladosV4.Count > 0 && instaladosV4.All(t => !t.Activo), "Los trucos de v4 aparecen desactivados");

Console.WriteLine("\n== qt-config.ini (sobre una copia)");
var qtOriginal = Path.Combine(eden.CarpetaDatos, "config", "qt-config.ini");
var edenCopia = new EmuladorConfig { Id = "eden-prueba", Nombre = "Eden (copia)", Tipo = TipoEmulador.Yuzu, RutaExe = @"C:\no-existe\eden-prueba.exe", CarpetaDatos = Path.Combine(temporal, "eden-user") };
Directory.CreateDirectory(Path.Combine(edenCopia.CarpetaDatos, "config"));
var qtCopia = Path.Combine(edenCopia.CarpetaDatos, "config", "qt-config.ini");
File.Copy(qtOriginal, qtCopia);
var antes = QtIni.Leer(qtCopia).Deshabilitados("010028600EBDA000");
QtIni.Leer(qtCopia).Guardar(qtCopia);
var despues = QtIni.Leer(qtCopia).Deshabilitados("010028600EBDA000");
Comprobar(antes.SetEquals(despues), $"Reescribir sin cambios conserva los {antes.Count} desactivados");
var lineasOriginal = File.ReadAllLines(qtOriginal).Where(l => !EnSeccionAddOns(l)).ToList();
var lineasCopia = File.ReadAllLines(qtCopia).Where(l => !EnSeccionAddOns(l)).ToList();
Comprobar(lineasOriginal.SequenceEqual(lineasCopia), "El resto del archivo queda idéntico");

var nuevo = new TrucoNuevo { Nombre = "J1 - Velocidad al correr x2 (mantener B)", Lineas = ["80000002", "04000000 0033E894 1E201008", "20000000"] };
var cambios = new CambiosTrucos();
cambios.Instalar.Add(nuevo);
cambios.Instalar.Add(new TrucoNuevo { Nombre = "Tiempo infinito", Lineas = ["04000000 0019CD50 D65F03C0"], Activo = false });
var gestorCopia = GestoresTrucos.Para(edenCopia);
gestorCopia.Aplicar("0100AAAA00000000", "1234567890ABCDEF", cambios);
var leidos = gestorCopia.Leer("0100AAAA00000000", "1234567890ABCDEF");
Comprobar(leidos.Count == 2, "Se instalaron 2 trucos en la copia de Eden");
Comprobar(leidos.Single(t => t.Nombre.StartsWith("J1")).Activo, "El truco activo quedó activo");
Comprobar(!leidos.Single(t => t.Nombre == "Tiempo infinito").Activo, "El truco desactivado quedó en [DisabledAddOns]");
var cambio2 = new CambiosTrucos();
cambio2.Estados["Tiempo infinito"] = true;
gestorCopia.Aplicar("0100AAAA00000000", "1234567890ABCDEF", cambio2);
Comprobar(gestorCopia.Leer("0100AAAA00000000", "1234567890ABCDEF").All(t => t.Activo), "Activar después funciona");
Console.WriteLine("  Archivo escrito:");
foreach (var l in File.ReadAllLines(leidos.Single(t => t.Nombre.StartsWith("J1")).Archivo))
    Console.WriteLine($"    {l}");
Comprobar(QtIni.Leer(qtCopia).Deshabilitados("010028600EBDA000").SetEquals(antes), "Los desactivados de Mario no se tocaron");

Console.WriteLine("\n== Ryujinx (sobre una carpeta temporal)");
var ryuCopia = new EmuladorConfig { Id = "ryu-prueba", Nombre = "Ryujinx (copia)", Tipo = TipoEmulador.Ryujinx, RutaExe = @"C:\no-existe\ryu-prueba.exe", CarpetaDatos = Path.Combine(temporal, "ryu-portable") };
var gestorRyu = GestoresTrucos.Para(ryuCopia);
gestorRyu.Aplicar("010028600EBDA000", "9F7EFC2FB9653E5C", cambios);
var leidosRyu = gestorRyu.Leer("010028600EBDA000", "9F7EFC2FB9653E5C");
Comprobar(leidosRyu.Count == 2 && leidosRyu.Count(t => t.Activo) == 1, "Ryujinx: 2 instalados, 1 activo");
var habilitados = Path.Combine(ryuCopia.CarpetaDatos, "mods", "contents", "010028600ebda000", "cheats", "enabled.txt");
Console.WriteLine($"  enabled.txt: {File.ReadAllText(habilitados).Trim()}");
Comprobar(File.ReadAllText(habilitados).Trim() == "9F7EFC2FB9653E5C-<J1 - Velocidad al correr x2 (mantener B) Cheat>", "Formato de enabled.txt");

fallos += await PruebasActualizador.EjecutarAsync(temporal, eden, ryujinx);
fallos += PruebasControles.Ejecutar(temporal, config);

if (args.Contains("--ia"))
{
    Console.WriteLine("\n== Traducción con IA");
    foreach (var motor in new[] { "claude", "codex" })
    {
        try
        {
            var elementos = catalogo.Para("9F7EFC2FB9653E5C").Take(3).Select((t, i) => new ElementoATraducir($"t{i}", t.NombreOriginal, t.Botones)).ToList();
            var traducciones = await TraductorIA.TraducirAsync(motor, "", catalogo.Titulo ?? "", elementos, null, CancellationToken.None);
            foreach (var e in elementos)
                Console.WriteLine($"  {motor}: {e.Nombre} → {(traducciones.TryGetValue(e.Id, out var t) ? $"{t.Nombre} | {t.Descripcion} | {t.Categoria}" : "(sin traducción)")}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  {motor}: ERROR {ex.Message}");
        }
    }
}

Console.WriteLine(fallos == 0 ? "\nTodo correcto." : $"\n{fallos} comprobaciones fallaron.");
return fallos == 0 ? 0 : 1;

static string CarpetaApp()
{
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        if (File.Exists(Path.Combine(dir.FullName, "Lanzador.slnx")))
            return Path.Combine(dir.FullName, "app");
    return AppContext.BaseDirectory;
}

static bool EnSeccionAddOns(string linea) =>
    linea == "[DisabledAddOns]" || linea.StartsWith("size=") || System.Text.RegularExpressions.Regex.IsMatch(linea, @"^\d+\\(title_id|disabled)");
