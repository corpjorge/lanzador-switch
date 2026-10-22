using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lanzador.Core;
using Lanzador.Core.Actualizaciones;

/// <summary>
/// Pruebas del actualizador de emuladores. Las instalaciones son carpetas temporales con carpetas user/portable falsas
/// y las descargas salen de un servidor simulado; de internet solo se consultan las versiones (no se descarga nada).
/// </summary>
static class PruebasActualizador
{
    private static int _fallos;

    private static void Comprobar(bool condicion, string descripcion)
    {
        Console.WriteLine($"  [{(condicion ? "OK" : "FALLO")}] {descripcion}");
        if (!condicion)
            _fallos++;
    }

    public static async Task<int> EjecutarAsync(string temporal, EmuladorConfig edenReal, EmuladorConfig ryujinxReal)
    {
        _fallos = 0;
        Fuentes();
        await FuentesRealesAsync();
        VersionesReales(edenReal, ryujinxReal);
        await ActualizacionEdenAsync(Path.Combine(temporal, "emus"));
        await ActualizacionRyujinxAsync(Path.Combine(temporal, "emus"));
        await InstalacionCitronAsync(Path.Combine(temporal, "emus"));
        await EmuladorAbiertoAsync();
        return _fallos;
    }

    private static void Fuentes()
    {
        Console.WriteLine("\n== Actualizador: lectura de las fuentes (ejemplos)");
        var nightly = JsonDocument.Parse("""
            {"tag_name":"v1792353251.de7e6e01d4","name":"Eden Nightly - Oct 18 2026","html_url":"https://git.eden-emu.dev/eden-ci/nightly/releases/tag/v1792353251.de7e6e01d4",
             "published_at":"2026-10-18T20:49:51+00:00","assets":[
              {"name":"Eden-Windows-de7e6e01d4-rog-ally-clang-pgo.zip","size":41417352,"digest":"sha256:bdbaee0950f9e6efaf41253ff71b6e07ca72e4ea2e2c36db6f8d8e7e0375f9f9","browser_download_url":"https://nightly.eden-emu.dev/x/Eden-Windows-de7e6e01d4-rog-ally-clang-pgo.zip"},
              {"name":"Eden-Linux-de7e6e01d4-amd64-clang-pgo.AppImage","size":71942640,"digest":"sha256:42d0b80e1a77a3829a51f932ff5e57cf8702aedc99cde87f0167d073380af948","browser_download_url":"https://nightly.eden-emu.dev/x/Eden-Linux-de7e6e01d4-amd64-clang-pgo.AppImage"},
              {"name":"Eden-Windows-de7e6e01d4-arm64-clang-pgo.zip","size":33486837,"digest":"sha256:678068ba980edee84747f660e009a00e190b11b0c217cc5546cfb59f5685d614","browser_download_url":"https://nightly.eden-emu.dev/x/Eden-Windows-de7e6e01d4-arm64-clang-pgo.zip"},
              {"name":"Eden-Windows-de7e6e01d4-amd64-clang-pgo.zip","size":41013717,"digest":"sha256:BD3F1A8E5660A7B03BC4DE9CBCB6CF9DD2F5DFF77B050D27C143DF1C7564592E","browser_download_url":"https://nightly.eden-emu.dev/x/Eden-Windows-de7e6e01d4-amd64-clang-pgo.zip"}]}
            """);
        var v = FuenteEdenNightly.Interpretar(nightly.RootElement);
        Comprobar(v.Archivo == "Eden-Windows-de7e6e01d4-amd64-clang-pgo.zip", "Eden Nightly: elige el zip amd64 clang PGO (no rog-ally ni arm64)");
        Comprobar(v.Version == "de7e6e01d4" && v.Sha256 == "bd3f1a8e5660a7b03bc4de9cbcb6cf9dd2f5dff77b050d27c143df1c7564592e" && v.Tamano == 41013717,
            "Eden Nightly: versión = hash del commit, sha256 en minúsculas y tamaño");

        var gitea = JsonDocument.Parse("""
            [{"tag_name":"v0.3.0-rc1","name":"borrador","draft":true,"prerelease":false,"assets":[]},
             {"tag_name":"v0.3.0-beta","name":"beta","draft":false,"prerelease":true,"assets":[]},
             {"tag_name":"v0.2.1","name":"Eden v0.2.1","draft":false,"prerelease":false,"html_url":"https://git.eden-emu.dev/eden-emu/eden/releases/tag/v0.2.1","assets":[
               {"name":"Eden-Windows-v0.2.1-amd64-msvc-standard.zip","size":0,"browser_download_url":"https://stable.eden-emu.dev/v0.2.1/Eden-Windows-v0.2.1-amd64-msvc-standard.zip"},
               {"name":"Eden-Windows-v0.2.1-amd64-clang-pgo.zip","size":0,"browser_download_url":"https://stable.eden-emu.dev/v0.2.1/Eden-Windows-v0.2.1-amd64-clang-pgo.zip"}]}]
            """);
        var conHashes = JsonDocument.Parse("""
            {"tag_name":"v0.2.1","name":"Eden v0.2.1","assets":[
              {"name":"Eden-Windows-v0.2.1-amd64-clang-pgo.zip","size":36943177,"digest":"sha256:6c1b53ce325170a026cc0f87098380027dc6170d94ba95f913aab7596fd097cb","browser_download_url":"https://stable.eden-emu.dev/v0.2.1/Eden-Windows-v0.2.1-amd64-clang-pgo.zip"}]}
            """);
        var release = FuenteEdenEstable.ElegirRelease(gitea.RootElement);
        var e = FuenteEdenEstable.Interpretar(release, conHashes.RootElement);
        Comprobar(e.Version == "v0.2.1", "Eden estable: salta borradores y prereleases");
        Comprobar(e.Sha256 == "6c1b53ce325170a026cc0f87098380027dc6170d94ba95f913aab7596fd097cb" && e.Tamano == 36943177,
            "Eden estable: el sha256 y el tamaño salen de stable.eden-emu.dev (Gitea no los publica)");
        Comprobar(FuenteEdenEstable.Interpretar(release, null).Sha256 == null, "Eden estable sin release.json: queda sin sha256 (no se instalará)");

        var citron = JsonDocument.Parse("""
            {"tag_name":"nightly-windows","name":"Continuous Build (Windows Nightly: 598b70088)","assets":[
              {"name":"Citron-windows-nightly-598b70088-x64-clang-cl.zip","size":41793296,"digest":"sha256:f72cf26bef45d7c82593b8109585d879b65493a4dd6abb0e2ad69066d5cab29f","browser_download_url":"https://github.com/citron-neo/CI/releases/download/nightly-windows/Citron-windows-nightly-598b70088-x64-clang-cl.zip"},
              {"name":"Citron-windows-nightly-598b70088-x64-clangtron.zip","size":65062937,"digest":"sha256:1a381aa3b41aa55a7dc00b0f4bdbf8501687ed5aff2c95cee1cbafc3e0b647ab","browser_download_url":"https://github.com/citron-neo/CI/releases/download/nightly-windows/Citron-windows-nightly-598b70088-x64-clangtron.zip"}]}
            """);
        var c1 = FuenteCitronNeo.Interpretar(citron.RootElement, "clang-cl");
        var c2 = FuenteCitronNeo.Interpretar(citron.RootElement, "clangtron");
        Comprobar(c1.Archivo!.EndsWith("-x64-clang-cl.zip") && c2.Archivo!.EndsWith("-x64-clangtron.zip"), "Citron Neo: se elige la variante configurada");
        Comprobar(c1.Version == "598b70088" && c2.Sha256 == "1a381aa3b41aa55a7dc00b0f4bdbf8501687ed5aff2c95cee1cbafc3e0b647ab", "Citron Neo: versión y sha256");

        Comprobar(VersionesInstaladas.MismaVersion("de7e6e01d4", "DE7E6E01D4") && VersionesInstaladas.MismaVersion("598b70088", "598b700884")
                  && VersionesInstaladas.MismaVersion("v0.2.1", "0.2.1"), "Comparar versiones: mismas");
        Comprobar(!VersionesInstaladas.MismaVersion("de7e6e01d4", "abc1234567") && !VersionesInstaladas.MismaVersion("1.3.351", "1.3.35")
                  && !VersionesInstaladas.MismaVersion(null, "v0.2.1"), "Comparar versiones: distintas");

        var opciones = new OpcionesActualizacion();
        var eden = new EmuladorConfig { Id = "eden" };
        Comprobar(FuentesActualizacion.Para(eden, opciones) is FuenteEdenNightly
                  && FuentesActualizacion.Para(eden, new OpcionesActualizacion { CanalEden = "estable" }) is FuenteEdenEstable
                  && FuentesActualizacion.Para(new EmuladorConfig { Id = "citron" }, new OpcionesActualizacion { VarianteCitron = "clangtron" }) is FuenteCitronNeo { Variante: "clangtron" }
                  && FuentesActualizacion.Para(new EmuladorConfig { Id = "ryujinx" }, opciones) is { PermiteDescarga: false },
            "Cada emulador usa la fuente y el canal de la configuración (Ryujinx solo abre la página)");
    }

    private static async Task FuentesRealesAsync()
    {
        Console.WriteLine("\n== Actualizador: versiones publicadas (solo consulta)");
        IFuenteActualizacion[] fuentes = [new FuenteEdenNightly(), new FuenteEdenEstable(), new FuenteCitronNeo("clang-cl"), new FuenteCitronNeo("clangtron"), new FuenteSuyu()];
        foreach (var fuente in fuentes)
        {
            try
            {
                var v = await fuente.ConsultarAsync(CancellationToken.None);
                Console.WriteLine($"  {fuente.Descripcion}: {v.Version} | {v.Archivo} | {v.Tamano / 1048576.0:0.0} MB | sha256 {v.Sha256?[..12]}…");
                Comprobar(v.Url != null && v.Sha256 != null, $"{fuente.Descripcion}: tiene archivo y sha256");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidDataException or JsonException)
            {
                Comprobar(false, $"{fuente.Descripcion}: {ex.Message}");
            }
        }
    }

    private static void VersionesReales(EmuladorConfig eden, EmuladorConfig ryujinx)
    {
        Console.WriteLine("\n== Actualizador: versiones instaladas (solo lectura)");
        var ve = VersionesInstaladas.Leer(eden);
        var vr = VersionesInstaladas.Leer(ryujinx);
        Console.WriteLine($"  Eden: {ve.Version ?? "?"} ({ve.Origen}) | Ryujinx: {vr.Version ?? "?"} ({vr.Origen})");
        Comprobar(ve.Version is { Length: >= 7 }, "Se lee la versión de Eden");
        Comprobar(vr.Version != null && System.Text.RegularExpressions.Regex.IsMatch(vr.Version, @"^\d+\.\d+\.\d+$"), "Se lee la versión de Ryujinx");
    }

    private static async Task ActualizacionEdenAsync(string raiz)
    {
        Console.WriteLine("\n== Actualizador: Eden simulado (descarga, sha256, respaldo, copia y vuelta atrás)");
        var carpeta = Path.Combine(raiz, "Eden");
        Escribir(carpeta, "eden.exe", "eden viejo");
        Escribir(carpeta, "eden-cli.exe", "cli viejo");
        Escribir(carpeta, "README.md", "léeme viejo");
        Escribir(carpeta, @"user\config\qt-config.ini", "[UI]\r\ntema=oscuro\r\n");
        Escribir(carpeta, @"user\nand\system\save\0000000000000001", "partida guardada");
        Escribir(carpeta, @"user\load\010028600EBDA000\J1 - Saltar alto\cheats\9F7EFC2FB9653E5C.txt", "[J1]\r\n04000000 0033E894 1E201008\r\n");
        Escribir(carpeta, @"user\keys\prod.keys", "claves");
        var emu = new EmuladorConfig { Id = "eden", Nombre = "Eden (prueba)", Tipo = TipoEmulador.Yuzu, RutaExe = Path.Combine(carpeta, "eden.exe"), CarpetaDatos = Path.Combine(carpeta, "user") };
        var user = Foto(Path.Combine(carpeta, "user"));
        var inicial = Foto(carpeta);

        var servidor = new ServidorFalso();
        var descargas = Path.Combine(raiz, "descargas");
        var actualizador = new Actualizador(new HttpClient(servidor)) { CarpetaTemporal = descargas };

        // Paquete con una subcarpeta raíz y una carpeta user que nunca debe copiarse.
        var zip = CrearZip(
            ("Eden-Windows-abc1234567-amd64-clang-pgo/eden.exe", "eden nuevo"),
            ("Eden-Windows-abc1234567-amd64-clang-pgo/eden-cli.exe", "cli nuevo"),
            ("Eden-Windows-abc1234567-amd64-clang-pgo/LICENSES/MIT.txt", "mit"),
            ("Eden-Windows-abc1234567-amd64-clang-pgo/user/README.txt", "no debe copiarse"),
            ("Eden-Windows-abc1234567-amd64-clang-pgo/user/config/qt-config.ini", "pisaría la configuración"));
        servidor.Archivos["https://prueba.local/eden.zip"] = zip;
        var version = new VersionDisponible { Version = "abc1234567", Canal = "nightly", Archivo = "eden.zip", Url = "https://prueba.local/eden.zip", Sha256 = Sha(zip), Tamano = zip.Length };

        // 1. sha256 incorrecto: no se toca nada.
        var mala = await actualizador.ActualizarAsync(emu, version with { Sha256 = new string('0', 64) }, null, CancellationToken.None);
        Comprobar(!mala.Exito && mala.Mensaje.Contains("sha256"), $"sha256 incorrecto se rechaza: {mala.Mensaje}");
        Comprobar(Iguales(inicial, Foto(carpeta)), "Con sha256 incorrecto la instalación queda idéntica");
        Comprobar(Actualizador.UltimoRespaldo("eden") == null, "Con sha256 incorrecto no se crea respaldo");

        // 2. Paquete sin eden.exe: no se instala.
        var sinExe = CrearZip(("otra-cosa/leeme.txt", "nada"));
        servidor.Archivos["https://prueba.local/sin-exe.zip"] = sinExe;
        var r2 = await actualizador.ActualizarAsync(emu, version with { Url = "https://prueba.local/sin-exe.zip", Sha256 = Sha(sinExe), Tamano = sinExe.Length }, null, CancellationToken.None);
        Comprobar(!r2.Exito && r2.Mensaje.Contains("eden.exe") && Iguales(inicial, Foto(carpeta)), "Un paquete sin eden.exe se rechaza sin tocar nada");

        // 3. Descarga 404.
        var r3 = await actualizador.ActualizarAsync(emu, version with { Url = "https://prueba.local/no-existe.zip" }, null, CancellationToken.None);
        Comprobar(!r3.Exito && Iguales(inicial, Foto(carpeta)), $"Descarga fallida no toca nada: {r3.Mensaje}");

        // 4. Copia que falla a medias (eden.exe bloqueado como si estuviera abierto): se restaura sola.
        using (new FileStream(emu.RutaExe, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var r4 = await actualizador.ActualizarAsync(emu, version, null, CancellationToken.None);
            Comprobar(!r4.Exito && r4.Mensaje.Contains("Se restauró"), $"Copia fallida se deshace: {r4.Mensaje}");
        }
        Comprobar(Iguales(inicial, Foto(carpeta)), "Tras la copia fallida todo queda como antes (eden-cli.exe repuesto, sin LICENSES)");
        Comprobar(Actualizador.UltimoRespaldo("eden") == null, "Tras restaurar sola no queda respaldo pendiente");

        // 5. Actualización correcta.
        var etapas = new List<EtapaActualizacion>();
        var progreso = new ProgresoSincrono(p => { if (!etapas.Contains(p.Etapa)) etapas.Add(p.Etapa); });
        var ok = await actualizador.ActualizarAsync(emu, version, progreso, CancellationToken.None);
        Console.WriteLine($"  Resultado: {ok.Mensaje}");
        Console.WriteLine($"  Etapas: {string.Join(" → ", etapas)}");
        Comprobar(ok.Exito, "La actualización termina bien");
        Comprobar(Leer(carpeta, "eden.exe") == "eden nuevo" && Leer(carpeta, "eden-cli.exe") == "cli nuevo" && Leer(carpeta, @"LICENSES\MIT.txt") == "mit",
            "Se copiaron los archivos nuevos");
        Comprobar(!Directory.Exists(Path.Combine(carpeta, "Eden-Windows-abc1234567-amd64-clang-pgo")), "Se copió el contenido de la subcarpeta raíz, no la subcarpeta");
        Comprobar(Iguales(user, Foto(Path.Combine(carpeta, "user"))), "La carpeta user quedó intacta (ni README.txt ni qt-config.ini del paquete)");
        Comprobar(Leer(carpeta, "README.md") == "léeme viejo", "Los archivos que el paquete no trae no se tocan");
        Comprobar(etapas.Contains(EtapaActualizacion.Descargando) && etapas.Contains(EtapaActualizacion.Verificando) && etapas.Contains(EtapaActualizacion.Copiando),
            "Se informó el progreso de descarga, verificación y copia");
        Comprobar(!Directory.Exists(descargas) || !Directory.EnumerateFileSystemEntries(descargas).Any(), "La carpeta temporal de descarga quedó vacía");

        var respaldo = Actualizador.UltimoRespaldo("eden");
        Comprobar(respaldo != null && respaldo.Completo && File.ReadAllText(Path.Combine(respaldo.Carpeta, "archivos", "eden.exe")) == "eden viejo",
            $"Respaldo en {respaldo?.Carpeta} con el eden.exe anterior");
        Comprobar(respaldo != null && Path.GetFileName(respaldo.Carpeta).StartsWith("eden-") && Path.GetDirectoryName(respaldo.Carpeta) == Rutas.Respaldos,
            "El respaldo está en datos\\respaldos\\eden-<fecha>");
        Comprobar(respaldo != null && respaldo.Nuevos.Contains(@"LICENSES\MIT.txt") && !respaldo.Reemplazados.Any(r => r.StartsWith("user", StringComparison.OrdinalIgnoreCase)),
            "El respaldo anota los archivos nuevos y nada de user");
        var instalada = VersionesInstaladas.Leer(emu);
        Comprobar(instalada.Version == "abc1234567" && instalada.Origen == VersionesInstaladas.OrigenLanzador, "La versión instalada quedó registrada en datos\\versiones.json");

        // 6. Volver a la versión anterior.
        var atras = Actualizador.VolverAnterior(emu, null);
        Console.WriteLine($"  Volver atrás: {atras.Mensaje}");
        Comprobar(atras.Exito, "Volver a la versión anterior funciona");
        Comprobar(Iguales(inicial, Foto(carpeta)), "La instalación quedó exactamente como antes (sin LICENSES y con user intacta)");
        Comprobar(Actualizador.UltimoRespaldo("eden") == null, "El respaldo usado se quitó");

        // 7. Solo se conservan los últimos respaldos.
        for (var i = 0; i < Actualizador.RespaldosPorEmulador + 2; i++)
        {
            var paquete = CrearZip(("eden.exe", $"eden {i}"));
            servidor.Archivos[$"https://prueba.local/eden{i}.zip"] = paquete;
            await actualizador.ActualizarAsync(emu, version with { Version = $"abc12345{i:00}", Url = $"https://prueba.local/eden{i}.zip", Sha256 = Sha(paquete), Tamano = paquete.Length }, null, CancellationToken.None);
            await Task.Delay(1100); // las carpetas llevan la hora con segundos
        }
        Comprobar(Actualizador.Respaldos("eden").Count == Actualizador.RespaldosPorEmulador, $"Se conservan {Actualizador.RespaldosPorEmulador} respaldos de Eden");
        Comprobar(Iguales(user, Foto(Path.Combine(carpeta, "user"))), "Tras varias actualizaciones user sigue intacta");
    }

    private static async Task ActualizacionRyujinxAsync(string raiz)
    {
        Console.WriteLine("\n== Actualizador: Ryujinx simulado (carpeta portable)");
        var carpeta = Path.Combine(raiz, "ryujinx");
        Escribir(carpeta, "Ryujinx.exe", "ryujinx viejo");
        Escribir(carpeta, "SDL3.dll", "sdl viejo");
        Escribir(carpeta, @"portable\Config.json", "{\"game_dirs\":[\"C:\\\\Juegos\"]}");
        Escribir(carpeta, @"portable\system\prod.keys", "claves");
        Escribir(carpeta, @"portable\mods\contents\010028600ebda000\cheats\enabled.txt", "9F7EFC2FB9653E5C-<J1 Cheat>");
        var emu = new EmuladorConfig { Id = "ryujinx-prueba", Nombre = "Ryujinx (prueba)", Tipo = TipoEmulador.Ryujinx, RutaExe = Path.Combine(carpeta, "Ryujinx.exe"), CarpetaDatos = Path.Combine(carpeta, "portable") };
        var portable = Foto(Path.Combine(carpeta, "portable"));

        var servidor = new ServidorFalso();
        var zip = CrearZip(("publish/Ryujinx.exe", "ryujinx nuevo"), ("publish/SDL3.dll", "sdl nuevo"), ("publish/portable/Config.json", "{}"));
        servidor.Archivos["https://prueba.local/ryujinx.zip"] = zip;
        var r = await new Actualizador(new HttpClient(servidor)) { CarpetaTemporal = Path.Combine(raiz, "descargas") }
            .ActualizarAsync(emu, new VersionDisponible { Version = "1.3.400", Canal = "stable", Url = "https://prueba.local/ryujinx.zip", Sha256 = Sha(zip), Tamano = zip.Length }, null, CancellationToken.None);
        Comprobar(r.Exito && Leer(carpeta, "Ryujinx.exe") == "ryujinx nuevo" && Leer(carpeta, "SDL3.dll") == "sdl nuevo", $"Ryujinx actualizado: {r.Mensaje}");
        Comprobar(Iguales(portable, Foto(Path.Combine(carpeta, "portable"))), "La carpeta portable quedó intacta");
        var atras = Actualizador.VolverAnterior(emu, null);
        Comprobar(atras.Exito && Leer(carpeta, "Ryujinx.exe") == "ryujinx viejo" && Iguales(portable, Foto(Path.Combine(carpeta, "portable"))),
            "Volver atrás repone Ryujinx.exe y no toca portable");
    }

    private static async Task InstalacionCitronAsync(string raiz)
    {
        Console.WriteLine("\n== Actualizador: Citron Neo simulado (instalación nueva)");
        var carpeta = Path.Combine(raiz, "Citron");
        var emu = new EmuladorConfig { Id = "citron-prueba", Nombre = "Citron (prueba)", Tipo = TipoEmulador.Yuzu, RutaExe = Path.Combine(carpeta, "citron.exe"), CarpetaDatos = Path.Combine(carpeta, "user") };
        var servidor = new ServidorFalso();
        var zip = CrearZip(("citron.exe", "citron"), ("Qt6Core.dll", "qt"), ("platforms/qwindows.dll", "qwindows"), ("user/README.txt", "carpeta portable"));
        servidor.Archivos["https://prueba.local/citron.zip"] = zip;
        var r = await new Actualizador(new HttpClient(servidor)) { CarpetaTemporal = Path.Combine(raiz, "descargas") }
            .ActualizarAsync(emu, new VersionDisponible { Version = "598b70088", Canal = "clang-cl", Url = "https://prueba.local/citron.zip", Sha256 = Sha(zip), Tamano = zip.Length }, null, CancellationToken.None);
        Comprobar(r.Exito && File.Exists(emu.RutaExe) && Leer(carpeta, @"platforms\qwindows.dll") == "qwindows", $"Citron instalado: {r.Mensaje}");
        Comprobar(Directory.Exists(emu.CarpetaDatos) && !Directory.EnumerateFileSystemEntries(emu.CarpetaDatos).Any(),
            "Se creó la carpeta user vacía (modo portable) sin copiar nada dentro");
        Comprobar(Actualizador.UltimoRespaldo("citron-prueba") == null, "En una instalación nueva no queda respaldo");
        Comprobar(VersionesInstaladas.Leer(emu).Version == "598b70088", "Se registró la versión de Citron");

        // Una actualización posterior con la carpeta user ya en uso.
        Escribir(carpeta, @"user\config\qt-config.ini", "[UI]\r\n");
        var user = Foto(emu.CarpetaDatos);
        var zip2 = CrearZip(("citron.exe", "citron 2"), ("user/README.txt", "carpeta portable"));
        servidor.Archivos["https://prueba.local/citron2.zip"] = zip2;
        var r2 = await new Actualizador(new HttpClient(servidor)) { CarpetaTemporal = Path.Combine(raiz, "descargas") }
            .ActualizarAsync(emu, new VersionDisponible { Version = "1234567ab", Canal = "clang-cl", Url = "https://prueba.local/citron2.zip", Sha256 = Sha(zip2), Tamano = zip2.Length }, null, CancellationToken.None);
        Comprobar(r2.Exito && Leer(carpeta, "citron.exe") == "citron 2" && Iguales(user, Foto(emu.CarpetaDatos)), "Al actualizar Citron su carpeta user no se toca");
    }

    private static async Task EmuladorAbiertoAsync()
    {
        Console.WriteLine("\n== Actualizador: emulador abierto");
        // Este mismo programa de pruebas hace de "emulador abierto"; la URL no existe, así que nunca se descarga nada.
        var exe = Environment.ProcessPath!;
        var emu = new EmuladorConfig { Id = "abierto-prueba", Nombre = "Programa abierto", RutaExe = exe, CarpetaDatos = Path.Combine(Path.GetDirectoryName(exe)!, "user") };
        Comprobar(Actualizador.EmuladorAbierto(emu) != null, $"Detecta que {Path.GetFileName(exe)} está abierto");
        var servidor = new ServidorFalso();
        var r = await new Actualizador(new HttpClient(servidor)).ActualizarAsync(emu,
            new VersionDisponible { Version = "x", Url = "https://prueba.local/no-existe.zip", Sha256 = new string('a', 64) }, null, CancellationToken.None);
        Comprobar(!r.Exito && r.Mensaje.StartsWith("Cierra") && servidor.Peticiones == 0, $"No actualiza un emulador abierto: {r.Mensaje}");
        Comprobar(!Actualizador.VolverAnterior(emu, null).Exito, "Tampoco vuelve atrás con el emulador abierto");
    }

    // ---- utilidades ----

    private sealed class ServidorFalso : HttpMessageHandler
    {
        public Dictionary<string, byte[]> Archivos { get; } = new();
        public int Peticiones { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage peticion, CancellationToken ct)
        {
            Peticiones++;
            if (!Archivos.TryGetValue(peticion.RequestUri!.ToString(), out var datos))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            var respuesta = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(datos) };
            respuesta.Content.Headers.ContentLength = datos.Length;
            return Task.FromResult(respuesta);
        }
    }

    /// <summary>IProgress que avisa en el acto (Progress&lt;T&gt; lo haría en otro hilo).</summary>
    private sealed class ProgresoSincrono(Action<ProgresoActualizacion> accion) : IProgress<ProgresoActualizacion>
    {
        public void Report(ProgresoActualizacion valor) => accion(valor);
    }

    private static byte[] CrearZip(params (string Ruta, string Contenido)[] entradas)
    {
        using var memoria = new MemoryStream();
        using (var zip = new ZipArchive(memoria, ZipArchiveMode.Create, true))
            foreach (var (ruta, contenido) in entradas)
            {
                using var escritor = new StreamWriter(zip.CreateEntry(ruta).Open(), new UTF8Encoding(false));
                escritor.Write(contenido);
            }
        return memoria.ToArray();
    }

    private static string Sha(byte[] datos) => Convert.ToHexStringLower(SHA256.HashData(datos));

    private static void Escribir(string carpeta, string relativa, string contenido)
    {
        var ruta = Path.Combine(carpeta, relativa);
        Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
        File.WriteAllText(ruta, contenido);
    }

    private static string? Leer(string carpeta, string relativa)
    {
        var ruta = Path.Combine(carpeta, relativa);
        return File.Exists(ruta) ? File.ReadAllText(ruta) : null;
    }

    /// <summary>Ruta relativa → sha256 + fecha de cada archivo, y las carpetas (también las vacías).</summary>
    private static SortedDictionary<string, string> Foto(string carpeta)
    {
        var foto = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(carpeta))
            return foto;
        foreach (var dir in Directory.EnumerateDirectories(carpeta, "*", SearchOption.AllDirectories))
            foto[Path.GetRelativePath(carpeta, dir) + "\\"] = "carpeta";
        foreach (var archivo in Directory.EnumerateFiles(carpeta, "*", SearchOption.AllDirectories))
            foto[Path.GetRelativePath(carpeta, archivo)] = $"{Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(archivo)))} {File.GetLastWriteTimeUtc(archivo):O}";
        return foto;
    }

    private static bool Iguales(SortedDictionary<string, string> a, SortedDictionary<string, string> b)
    {
        if (a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out var v) && v == kv.Value))
            return true;
        foreach (var k in a.Keys.Union(b.Keys).Where(k => !a.TryGetValue(k, out var va) || !b.TryGetValue(k, out var vb) || va != vb))
            Console.WriteLine($"      diferencia: {k}");
        return false;
    }
}
