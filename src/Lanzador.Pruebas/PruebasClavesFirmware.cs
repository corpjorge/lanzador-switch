using System.IO.Compression;
using Lanzador.Core;
using Lanzador.Core.ClavesFirmware;

/// <summary>
/// Pruebas de claves y firmware. Todo es sintético: una carpeta de origen falsa y emuladores falsos
/// en una carpeta temporal. No se lee ni se toca ningún emulador real.
/// </summary>
static class PruebasClavesFirmware
{
    private static int _fallos;

    private static void Comprobar(bool condicion, string descripcion)
    {
        Console.WriteLine($"  [{(condicion ? "OK" : "FALLO")}] {descripcion}");
        if (!condicion)
            _fallos++;
    }

    public static int Ejecutar(string temporal)
    {
        _fallos = 0;
        var carpeta = Path.Combine(temporal, "claves-firmware");
        if (Directory.Exists(carpeta))
            Directory.Delete(carpeta, true);
        Directory.CreateDirectory(carpeta);
        Versiones();
        CarpetaElegida(Path.Combine(carpeta, "origenes"));
        OrigenFirma(Path.Combine(carpeta, "firma"));
        Instalacion(carpeta);
        return _fallos;
    }

    private static void Versiones()
    {
        Console.WriteLine("\n== Claves y firmware: comparación de versiones");
        Comprobar(InstaladorClavesFirmware.EsMasNueva("23.0.0", "22.5.0"), "23.0.0 es más nueva que 22.5.0");
        Comprobar(InstaladorClavesFirmware.EsMasNueva("22.10.0", "22.9.1"), "22.10.0 es más nueva que 22.9.1 (por números, no texto)");
        Comprobar(!InstaladorClavesFirmware.EsMasNueva("22.5.0", "22.5.0"), "Misma versión no cuenta como nueva");
        Comprobar(!InstaladorClavesFirmware.EsMasNueva("23.0.0", null), "Si no se sabe la instalada, no se da por vieja");

        var conVersion = new CopiaOrigen("23.0.0", true, "x", new Dictionary<string, ArchivoOrigen>());
        var conFecha = new CopiaOrigen("2026.11.05", false, "x", new Dictionary<string, ArchivoOrigen>());
        Comprobar(InstaladorClavesFirmware.EsMasNueva(conVersion, "22.0.0") && !InstaladorClavesFirmware.EsMasNueva(conVersion, "2026.01.01"), "Una versión del nombre solo se compara con otra versión");
        Comprobar(InstaladorClavesFirmware.EsMasNueva(conFecha, "2026.10.20") && !InstaladorClavesFirmware.EsMasNueva(conFecha, "22.0.0"), "Una fecha de archivo nunca le gana a un firmware 22.0.0 instalado");
    }

    private static void CarpetaElegida(string raiz)
    {
        Console.WriteLine("\n== Claves y firmware: lectura de la carpeta elegida");
        var vacia = Path.Combine(raiz, "vacia");
        Directory.CreateDirectory(vacia);
        var nada = OrigenClavesFirmware.Leer(vacia);
        Comprobar(nada.Claves == null && nada.Firmware == null, "Una carpeta vacía no tiene claves ni firmware");
        try
        {
            OrigenClavesFirmware.Leer(Path.Combine(raiz, "no-existe"));
            Comprobar(false, "Una carpeta que no existe da error");
        }
        catch (DirectoryNotFoundException)
        {
            Comprobar(true, "Una carpeta que no existe da error");
        }

        // Dos versiones en subcarpetas con nombre de versión: gana la mayor.
        var versiones = Path.Combine(raiz, "versiones");
        Escribir(Path.Combine(versiones, "19.0.1", "prod.keys"), "vieja");
        Escribir(Path.Combine(versiones, "20.1.0", "prod.keys"), "nueva");
        Escribir(Path.Combine(versiones, "20.1.0", "title.keys"), "titulos");
        CrearZip(Path.Combine(versiones, "Firmware 19.0.1.zip"), 3);
        CrearZip(Path.Combine(versiones, "Firmware 20.1.0.zip"), 3);
        using (var zip = ZipFile.Open(Path.Combine(versiones, "otra-cosa 21.0.0.zip"), ZipArchiveMode.Create))
            using (var s = new StreamWriter(zip.CreateEntry("leeme.txt").Open()))
                s.Write("no es firmware");
        var o = OrigenClavesFirmware.Leer(versiones);
        Comprobar(o.Claves is { Version: "20.1.0", VersionConocida: true } && o.Claves.Archivos.ContainsKey("title.keys")
                  && File.ReadAllText(o.Claves.Archivos["prod.keys"].Ruta) == "nueva", "Claves: se toman las de la subcarpeta 20.1.0, con su title.keys");
        Comprobar(o.Firmware is { Version: "20.1.0", EsZip: true }, "Firmware: gana el zip 20.1.0 y se ignora un zip sin .nca");

        // Sin versión en los nombres: se usa la fecha del archivo. El firmware viene como carpeta de .nca.
        var sueltos = Path.Combine(raiz, "sueltos");
        Escribir(Path.Combine(sueltos, "prod.keys"), "claves");
        File.SetLastWriteTime(Path.Combine(sueltos, "prod.keys"), new DateTime(2026, 10, 20, 12, 0, 0));
        for (var i = 0; i < 25; i++)
            Escribir(Path.Combine(sueltos, "volcado", $"{i:x32}.nca"), "nca " + i);
        var s2 = OrigenClavesFirmware.Leer(sueltos);
        Comprobar(s2.Claves is { Version: "2026.10.20", VersionConocida: false }, "Claves sin versión en el nombre: se usa la fecha del archivo");
        Comprobar(s2.Firmware is { EsZip: false, VersionConocida: false } && s2.Firmware.Ruta.EndsWith("volcado"), "Firmware: también vale una carpeta con los .nca");
    }

    private static void OrigenFirma(string carpeta)
    {
        Console.WriteLine("\n== Claves y firmware: variantes descargadas por Firma");
        var prodEden = Path.Combine(carpeta, "eden", "prod.keys");
        var prodRyu = Path.Combine(carpeta, "ryujinx", "prod.keys");
        var zip = Path.Combine(carpeta, "archive.zip");
        Escribir(prodEden, "eden");
        Escribir(prodRyu, "ryujinx");
        CrearZip(zip, 3);

        using var documento = System.Text.Json.JsonDocument.Parse(
            System.Text.Json.JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                revision = 3,
                isChecking = false,
                items = new[]
                {
                    Variante("keys", "eden", "china", "24.0.0", new { archivePath = zip, version = "24.0.0", files = Archivos(prodRyu) }),
                    Variante("keys", "eden", "global", "24.0.0", new { archivePath = zip, version = "23.0.0", files = Archivos(prodEden) }),
                    Variante("keys", "ryujinx", "global", "22.0.0", new { archivePath = zip, version = "22.0.0", files = Archivos(prodRyu) }),
                    Variante("firmware", "yuzu", "global", "23.0.0", null),
                    Variante("firmware", "switch", "global", "22.0.0", new { archivePath = zip, version = "22.0.0" }),
                },
            }));
        var estado = EstadoFirma.Leer(documento.RootElement);
        var eden = new EmuladorConfig { Id = "eden", Tipo = TipoEmulador.Yuzu };
        var ryujinx = new EmuladorConfig { Id = "ryujinx", Tipo = TipoEmulador.Ryujinx };
        var origenEden = estado.OrigenPara(carpeta, eden);
        var origenRyu = estado.OrigenPara(carpeta, ryujinx);
        Comprobar(origenEden.Claves is { Version: "23.0.0" } && origenEden.Claves.Archivos["prod.keys"].Ruta == prodEden,
            "Eden usa su variante global y la versión descargada");
        Comprobar(origenRyu.Claves?.Archivos["prod.keys"].Ruta == prodRyu, "Ryujinx usa su propia variante");
        Comprobar(origenEden.Firmware?.Ruta == zip && origenEden.Firmware.Version == "22.0.0",
            "Firma usa el firmware común cuando la variante específica está pendiente");
        File.Delete(prodEden);
        Comprobar(estado.OrigenPara(carpeta, eden).Claves == null, "Se descarta una copia cuyo archivo ya no existe");

        static object Variante(string kind, string variant, string region, string availableVersion, object? downloaded) =>
            new { id = $"{kind}/{variant}/{region}", kind, variant, region, availableVersion, status = "ready", downloaded };

        static Dictionary<string, object> Archivos(string path) => new() { ["prod.keys"] = new { path } };
    }

    private static void Instalacion(string carpeta)
    {
        Console.WriteLine("\n== Claves y firmware: instalación sobre emuladores falsos");
        Rutas.Configurar(Path.Combine(carpeta, "datos-lanzador"));

        // Carpeta del usuario sintética: claves y un zip con 2 .nca y 1 .cnmt.nca.
        var mia = Path.Combine(carpeta, "mi-carpeta", "23.0.0");
        Escribir(Path.Combine(mia, "prod.keys"), "header_key = 00112233\n");
        Escribir(Path.Combine(mia, "title.keys"), "0100000000000000 = AABB\n");
        using (var zip = ZipFile.Open(Path.Combine(mia, "firmware.zip"), ZipArchiveMode.Create))
            foreach (var nombre in new[] { "aaaa.nca", "bbbb.cnmt.nca", "cccc.nca" })
                using (var s = new StreamWriter(zip.CreateEntry(nombre).Open()))
                    s.Write("contenido de " + nombre);
        var origen = OrigenClavesFirmware.Leer(Path.Combine(carpeta, "mi-carpeta"));
        Comprobar(origen.Claves?.Version == "23.0.0" && origen.Firmware?.Version == "23.0.0", "La carpeta sintética se lee como 23.0.0");

        // Eden falso con claves y firmware viejos, partidas y un juego instalado en la NAND de usuario.
        var eden = new EmuladorConfig { Id = "eden-falso", Nombre = "Eden falso", Tipo = TipoEmulador.Yuzu, RutaExe = Path.Combine(carpeta, "Eden", "eden-falso.exe"), CarpetaDatos = Path.Combine(carpeta, "Eden", "user") };
        Escribir(Path.Combine(eden.CarpetaDatos, "keys", "prod.keys"), "header_key = VIEJA\n");
        Escribir(Path.Combine(eden.CarpetaDatos, "nand", "system", "Contents", "registered", "vieja.nca"), "firmware viejo");
        Escribir(Path.Combine(eden.CarpetaDatos, "nand", "user", "save", "partida.bin"), "partida");
        Escribir(Path.Combine(eden.CarpetaDatos, "nand", "user", "Contents", "registered", "juego.nca"), "juego instalado");
        Escribir(Path.Combine(eden.CarpetaDatos, "keys", "title.keys_autogenerated"), "autogeneradas");

        var antes = InstaladorClavesFirmware.Leer(eden, origen);
        Comprobar(antes.HayClaves && !antes.ClavesComoOrigen && !antes.FirmwareComoOrigen && antes.ArchivosFirmware == 1, "Antes: claves y firmware distintos a los de la carpeta");
        Comprobar(!antes.HayNuevas, "Antes: como no se sabe qué versión tiene, no se reemplaza solo (queda para Opciones)");
        Comprobar(!InstaladorClavesFirmware.Leer(eden, null).AlDia, "Sin carpeta elegida nunca está «al día»");

        var r = InstaladorClavesFirmware.Instalar(eden, origen);
        Console.WriteLine($"  {r.Mensaje}");
        var registrado = Path.Combine(eden.CarpetaDatos, "nand", "system", "Contents", "registered");
        Comprobar(r.Exito, "Eden: la instalación termina bien");
        Comprobar(File.ReadAllText(Path.Combine(eden.CarpetaDatos, "keys", "prod.keys")) == "header_key = 00112233\n"
                  && File.Exists(Path.Combine(eden.CarpetaDatos, "keys", "title.keys")), "Eden: prod.keys y title.keys en user\\keys");
        Comprobar(Directory.GetFiles(registrado).Select(Path.GetFileName).Order().SequenceEqual(["aaaa.nca", "bbbb.cnmt.nca", "cccc.nca"]), "Eden: los .nca quedan sueltos en nand\\system\\Contents\\registered (sin el viejo)");
        Comprobar(File.ReadAllText(Path.Combine(eden.CarpetaDatos, "nand", "user", "save", "partida.bin")) == "partida"
                  && File.Exists(Path.Combine(eden.CarpetaDatos, "nand", "user", "Contents", "registered", "juego.nca"))
                  && File.Exists(Path.Combine(eden.CarpetaDatos, "keys", "title.keys_autogenerated")), "Eden: partidas, juegos instalados y otras claves intactos");
        var despues = InstaladorClavesFirmware.Leer(eden, origen);
        Comprobar(despues.AlDia && despues.VersionClaves == "23.0.0" && despues.VersionFirmware == "23.0.0", "Eden: después queda al día (23.0.0)");
        Comprobar(InstaladorClavesFirmware.Instalar(eden, origen).Mensaje.Contains("ya tiene"), "Eden: instalar otra vez no repite nada");

        // Llega una versión más nueva a la carpeta: ahora sí se sabe que la instalada (23.0.0) es más vieja.
        var nueva = Path.Combine(carpeta, "mi-carpeta", "24.0.0");
        Escribir(Path.Combine(nueva, "prod.keys"), "header_key = 44556677\n");
        var origen24 = OrigenClavesFirmware.Leer(Path.Combine(carpeta, "mi-carpeta"));
        Comprobar(origen24.Claves?.Version == "24.0.0" && InstaladorClavesFirmware.Leer(eden, origen24).HayNuevas, "Eden: con claves 24.0.0 en la carpeta, hay nuevas para instalar solas");

        var vuelta = InstaladorClavesFirmware.VolverAnterior(eden);
        Comprobar(vuelta.Exito && File.ReadAllText(Path.Combine(eden.CarpetaDatos, "keys", "prod.keys")) == "header_key = VIEJA\n"
                  && Directory.GetFiles(registrado).Select(Path.GetFileName).SequenceEqual(["vieja.nca"]), "Eden: volver atrás repone las claves y el firmware viejos");

        // Ryujinx falso sin nada instalado (como recién descargado), con el firmware como carpeta de .nca.
        var volcado = Path.Combine(carpeta, "mi-volcado");
        Escribir(Path.Combine(volcado, "prod.keys"), "header_key = 00112233\n");
        for (var i = 0; i < 24; i++)
            Escribir(Path.Combine(volcado, "firmware", $"{i:x4}.nca"), "nca " + i);
        Escribir(Path.Combine(volcado, "firmware", "bbbb.cnmt.nca"), "meta");
        var origenCarpeta = OrigenClavesFirmware.Leer(volcado);
        var ryujinx = new EmuladorConfig { Id = "ryu-falso", Nombre = "Ryujinx falso", Tipo = TipoEmulador.Ryujinx, RutaExe = Path.Combine(carpeta, "ryujinx", "ryu-falso.exe"), CarpetaDatos = Path.Combine(carpeta, "ryujinx", "portable") };
        Directory.CreateDirectory(ryujinx.CarpetaDatos);
        Comprobar(InstaladorClavesFirmware.Leer(ryujinx, origenCarpeta).HayNuevas, "Ryujinx: sin claves ni firmware, lo de la carpeta se instala solo");
        var r2 = InstaladorClavesFirmware.Instalar(ryujinx, origenCarpeta);
        var otroFirmware = Path.Combine(carpeta, "Firmware 20.0.0.zip");
        CrearZip(otroFirmware, 1);
        var origenMixto = origen24 with { Firmware = new("20.0.0", true, otroFirmware, new Dictionary<string, ArchivoOrigen>()) };
        var prodInstalado = Path.Combine(eden.CarpetaDatos, "keys", "prod.keys");
        File.Delete(prodInstalado);
        var resultadoMixto = InstaladorClavesFirmware.Instalar(eden, origenMixto, soloNuevos: true);
        Comprobar(resultadoMixto.Exito && File.Exists(Path.Combine(registrado, "vieja.nca"))
            && File.ReadAllText(prodInstalado) == "header_key = 44556677\n",
            "La instalación automática agrega las claves que faltan y conserva el firmware de versión desconocida");
        Console.WriteLine($"  {r2.Mensaje}");
        var registradoRyu = Path.Combine(ryujinx.CarpetaDatos, "bis", "system", "Contents", "registered");
        Comprobar(r2.Exito && File.Exists(Path.Combine(ryujinx.CarpetaDatos, "system", "prod.keys")), "Ryujinx: prod.keys en portable\\system");
        Comprobar(Directory.GetDirectories(registradoRyu).Length == 25
                  && File.ReadAllText(Path.Combine(registradoRyu, "bbbb.nca", "00")) == "meta", "Ryujinx: cada .nca en su carpeta con un archivo 00 (los .cnmt sin \".cnmt\")");
        Comprobar(InstaladorClavesFirmware.Leer(ryujinx, origenCarpeta).AlDia, "Ryujinx: después queda al día");
        Comprobar(Directory.GetFiles(Path.Combine(volcado, "firmware")).Length == 25 && File.Exists(Path.Combine(volcado, "prod.keys")), "La carpeta del usuario queda como estaba (solo se copia)");
    }

    private static void CrearZip(string ruta, int archivos)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
        using var zip = ZipFile.Open(ruta, ZipArchiveMode.Create);
        for (var i = 0; i < archivos; i++)
            using (var s = new StreamWriter(zip.CreateEntry($"{i:x8}.nca").Open()))
                s.Write("nca " + i);
    }

    private static void Escribir(string ruta, string texto)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
        File.WriteAllText(ruta, texto);
    }
}
