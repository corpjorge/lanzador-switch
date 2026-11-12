using System.Text.Json;
using Lanzador.Core.Actualizaciones;

/// <summary>Pruebas de la actualización del propio lanzador: lectura de un release de GitHub, sin descargar nada.</summary>
static class PruebasLanzador
{
    private static int _fallos;

    private static void Comprobar(bool condicion, string descripcion)
    {
        Console.WriteLine($"  [{(condicion ? "OK" : "FALLO")}] {descripcion}");
        if (!condicion)
            _fallos++;
    }

    public static int Ejecutar()
    {
        _fallos = 0;
        Console.WriteLine("\n== Actualización del lanzador: lectura del release");
        Comprobar(ActualizacionLanzador.LeerVersion("v1.2.0") == new Version(1, 2, 0) && ActualizacionLanzador.LeerVersion("1.3") == new Version(1, 3, 0)
                  && ActualizacionLanzador.LeerVersion("1.2.0+abc123") == new Version(1, 2, 0) && ActualizacionLanzador.LeerVersion("nightly") == null,
            "Versiones: v1.2.0, 1.3, 1.2.0+commit; «nightly» no es una versión");
        Comprobar(ActualizacionLanzador.LeerVersion("v1.10.0") > ActualizacionLanzador.LeerVersion("v1.9.3"), "1.10.0 es más nueva que 1.9.3");

        const string hash = "82e13138e0a54d1bb5d1009a180abe2aef969d2d5301bd5ed0a3c3e73827c745";
        var release = JsonDocument.Parse($$"""
            {"tag_name":"v9.9.0","name":"Lanzador Switch 9.9.0","html_url":"https://github.com/corpjorge/lanzador-switch/releases/tag/v9.9.0","assets":[
              {"name":"LanzadorSwitch-9.9.0-win-x64.zip","size":60679007,"digest":"sha256:{{hash}}","browser_download_url":"https://github.com/corpjorge/lanzador-switch/releases/download/v9.9.0/LanzadorSwitch-9.9.0-win-x64.zip"},
              {"name":"LanzadorSwitch-9.9.0-win-x64-instalador.exe","size":61000000,"digest":"sha256:{{hash}}","browser_download_url":"https://github.com/corpjorge/lanzador-switch/releases/download/v9.9.0/LanzadorSwitch-9.9.0-win-x64-instalador.exe"},
              {"name":"SHA256SUMS.txt","size":200,"browser_download_url":"https://github.com/corpjorge/lanzador-switch/releases/download/v9.9.0/SHA256SUMS.txt"}]}
            """).RootElement;
        var v = ActualizacionLanzador.Interpretar(release, null);
        Comprobar(v is { Texto: "9.9.0", Instalador: "LanzadorSwitch-9.9.0-win-x64-instalador.exe", Sha256: hash, SePuedeInstalar: true },
            "Release con instalador: se elige el instalador (no el ZIP) con su sha256");

        var sinDigest = JsonDocument.Parse(release.GetRawText().Replace($"\"digest\":\"sha256:{hash}\",", "")).RootElement;
        Comprobar(ActualizacionLanzador.Interpretar(sinDigest, null) is { SePuedeInstalar: false }, "Sin sha256 no se instala desde el lanzador");
        var sumas = $"{new string('a', 64)}  LanzadorSwitch-9.9.0-win-x64.zip\n{hash}  LanzadorSwitch-9.9.0-win-x64-instalador.exe\n";
        Comprobar(ActualizacionLanzador.Interpretar(sinDigest, sumas) is { Sha256: hash, SePuedeInstalar: true }, "Si GitHub no da el sha256, sale de SHA256SUMS.txt");

        var soloZip = JsonDocument.Parse("""
            {"tag_name":"v1.1.2","name":"1.1.2","assets":[{"name":"LanzadorSwitch-1.1.2-win-x64.zip","size":1,"browser_download_url":"https://github.com/corpjorge/lanzador-switch/releases/download/v1.1.2/LanzadorSwitch-1.1.2-win-x64.zip"}]}
            """).RootElement;
        Comprobar(ActualizacionLanzador.Interpretar(soloZip, null) is { SePuedeInstalar: false, Instalador: null, Pagina: not null }, "Un release sin instalador solo ofrece abrir su página");
        var ajeno = JsonDocument.Parse(release.GetRawText().Replace("https://github.com/corpjorge/lanzador-switch/releases/download/v9.9.0/LanzadorSwitch-9.9.0-win-x64-instalador.exe", "https://otro.example/LanzadorSwitch-9.9.0-win-x64-instalador.exe")).RootElement;
        Comprobar(ActualizacionLanzador.Interpretar(ajeno, null) is { SePuedeInstalar: false }, "Un instalador que no está en los releases del repositorio no se descarga");

        Console.WriteLine($"  Versión abierta: {ActualizacionLanzador.VersionActual}");
        try
        {
            var publicada = ActualizacionLanzador.ConsultarAsync(CancellationToken.None).GetAwaiter().GetResult();
            Console.WriteLine(publicada == null ? "  GitHub: no hay una versión más nueva" : $"  GitHub: {publicada.Etiqueta} · instalador: {publicada.Instalador ?? "no trae"}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            Console.WriteLine($"  GitHub: no se pudo consultar ({ex.Message})");
        }
        return _fallos;
    }
}
