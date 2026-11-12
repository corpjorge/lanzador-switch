using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Lanzador.Core.ClavesFirmware;

public sealed record ArchivoFirma(string Ruta, string? Sha256);

/// <summary>Copia local validada de un elemento de Firma ("downloaded" en su manifiesto).</summary>
public sealed record DescargaFirma(string Version, string ArchivoZip, IReadOnlyDictionary<string, ArchivoFirma> Archivos);

/// <summary>Una variante de claves o firmware publicada por Firma.</summary>
public sealed record ElementoFirma(string Tipo, string Variante, string Region, string? Error, DescargaFirma? Descargado)
{
    public bool EsClaves => Tipo == "keys";
    public bool EsFirmware => Tipo == "firmware";
}

public sealed record EstadoFirma(long Revision, bool Consultando, DateTimeOffset? UltimaConsulta, IReadOnlyList<string> Errores, IReadOnlyList<ElementoFirma> Elementos)
{
    /// <summary>Lee el objeto "state" del contrato de Firma (versión 1). Acepta propiedades nuevas.</summary>
    public static EstadoFirma Leer(JsonElement estado)
    {
        if (!estado.TryGetProperty("schemaVersion", out var esquema) || esquema.GetInt32() != 1)
            throw new InvalidDataException("Firma usa una versión de contrato que el lanzador no conoce.");

        var elementos = new List<ElementoFirma>();
        if (estado.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            foreach (var i in items.EnumerateArray())
            {
                DescargaFirma? descargado = null;
                if (i.TryGetProperty("downloaded", out var d) && d.ValueKind == JsonValueKind.Object)
                {
                    var archivos = new Dictionary<string, ArchivoFirma>(StringComparer.OrdinalIgnoreCase);
                    if (d.TryGetProperty("files", out var f) && f.ValueKind == JsonValueKind.Object)
                        foreach (var a in f.EnumerateObject())
                            if (Texto(a.Value, "path") is { } ruta)
                                archivos[a.Name] = new ArchivoFirma(ruta, Texto(a.Value, "sha256"));
                    if (Texto(d, "version") is { } version && Texto(d, "archivePath") is { } zip)
                        descargado = new DescargaFirma(version, zip, archivos);
                }
                elementos.Add(new ElementoFirma(Texto(i, "kind") ?? "", Texto(i, "variant") ?? "",
                    Texto(i, "region") ?? "", Texto(i, "error"), descargado));
            }

        var errores = new List<string>();
        if (estado.TryGetProperty("errors", out var errs) && errs.ValueKind == JsonValueKind.Array)
            foreach (var e in errs.EnumerateArray())
                errores.Add(e.ValueKind == JsonValueKind.String ? e.GetString()! : e.ToString());

        return new EstadoFirma(
            estado.TryGetProperty("revision", out var r) && r.TryGetInt64(out var rev) ? rev : 0,
            estado.TryGetProperty("isChecking", out var c) && c.ValueKind == JsonValueKind.True,
            DateTimeOffset.TryParse(Texto(estado, "lastCompletedAt"), out var fecha) ? fecha : null,
            errores, elementos);
    }

    public OrigenClavesFirmware OrigenPara(string carpeta, EmuladorConfig emulador)
    {
        string[] variantesClaves = emulador.Tipo == TipoEmulador.Ryujinx
            ? ["ryujinx"] : [emulador.Id.ToLowerInvariant(), "yuzu", "eden"];
        string[] variantesFirmware = emulador.Tipo == TipoEmulador.Ryujinx
            ? ["ryujinx", "switch"] : ["yuzu", "switch"];
        var claves = Elegir("keys", variantesClaves, d =>
            d.Archivos.TryGetValue("prod.keys", out var archivo) && File.Exists(archivo.Ruta));
        var firmware = Elegir("firmware", variantesFirmware, d => File.Exists(d.ArchivoZip));

        CopiaOrigen? copiaClaves = null;
        if (claves != null)
        {
            var archivos = claves.Archivos
                .Where(a => File.Exists(a.Value.Ruta))
                .ToDictionary(a => a.Key, a => new ArchivoOrigen(a.Value.Ruta, a.Value.Sha256), StringComparer.OrdinalIgnoreCase);
            if (archivos.TryGetValue("prod.keys", out var prod))
                copiaClaves = new(claves.Version, true, Path.GetDirectoryName(prod.Ruta)!, archivos);
        }

        CopiaOrigen? copiaFirmware = firmware == null ? null
            : new(firmware.Version, true, firmware.ArchivoZip, new Dictionary<string, ArchivoOrigen>());
        return new(carpeta, copiaClaves, copiaFirmware, UltimaConsulta?.LocalDateTime ?? DateTime.Now);
    }

    private DescargaFirma? Elegir(string tipo, string[] variantes, Func<DescargaFirma, bool> existe)
    {
        foreach (var variante in variantes)
            foreach (var elemento in Elementos)
                if (elemento.Tipo == tipo && elemento.Variante == variante && elemento.Region == "global"
                    && elemento.Descargado is { } copia && existe(copia))
                    return copia;
        return null;
    }

    private static string? Texto(JsonElement e, string propiedad) =>
        e.TryGetProperty(propiedad, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}

/// <summary>Conecta con Firma mediante el contrato IPC v1.</summary>
public sealed class ClienteFirma : IDisposable
{
    private readonly string _exe;
    private readonly string _canal;
    private Process? _proceso;

    public ClienteFirma(string carpeta)
    {
        carpeta = Path.GetFullPath(carpeta);
        _exe = Path.Combine(carpeta, "Firma.exe");
        if (!File.Exists(_exe))
            throw new FileNotFoundException("No se encontró Firma.exe en la carpeta elegida.", _exe);

        var config = Path.Combine(carpeta, "config.json");
        var datos = File.Exists(config)
            ? JsonSerializer.Deserialize<ConfiguracionAgente>(File.ReadAllText(config), JsonSerializerOptions.Web)?.DataDirectory
            : null;
        var rutaDatos = string.IsNullOrWhiteSpace(datos) ? "datos" : Environment.ExpandEnvironmentVariables(datos);
        var ruta = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rutaDatos, carpeta));
        var identidad = Environment.UserDomainName + "\\" + Environment.UserName + "|" + ruta.ToUpperInvariant();
        _canal = "firma.v1." + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(identidad)))[..24];
    }

    private sealed record ConfiguracionAgente([property: JsonPropertyName("dataDirectory")] string? DataDirectory);

    public async Task<EstadoFirma> IniciarYConsultarAsync(CancellationToken ct)
    {
        try
        {
            return await EnviarAsync(new { command = "check" }, 250, ct);
        }
        catch (TimeoutException) { }

        ct.ThrowIfCancellationRequested();
        if (_proceso is not { HasExited: false })
        {
            _proceso?.Dispose();
            var inicio = new ProcessStartInfo(_exe)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(_exe)!,
            };
            inicio.ArgumentList.Add("--parent-pid");
            inicio.ArgumentList.Add(Environment.ProcessId.ToString());
            _proceso = Process.Start(inicio) ?? throw new IOException("No se pudo iniciar Firma.");
        }

        for (var intento = 0; ; intento++)
        {
            ct.ThrowIfCancellationRequested();
            if (_proceso.HasExited)
            {
                // Otra instancia pudo abrir el canal mientras se iniciaba esta.
                try { return await EnviarAsync(new { command = "check" }, 500, ct); }
                catch (TimeoutException)
                {
                    throw new IOException($"Firma se cerró al iniciar (código {_proceso.ExitCode}).");
                }
            }
            try { return await EnviarAsync(new { command = "check" }, 500, ct); }
            catch (TimeoutException) when (intento < 29) { }
        }
    }

    public Task<EstadoFirma> ConsultarAsync(CancellationToken ct) =>
        EnviarAsync(new { command = "check" }, 3000, ct);

    public Task<EstadoFirma> EsperarCambioAsync(long revision, CancellationToken ct) =>
        EnviarAsync(new { command = "wait", afterRevision = revision, timeoutSeconds = 25 }, 3000, ct);

    public void Dispose()
    {
        using var proceso = _proceso;
        _proceso = null;
        if (proceso == null)
            return;
        try
        {
            if (!proceso.HasExited)
                proceso.Kill();
        }
        catch (InvalidOperationException) { } // Ya terminó.
        catch (System.ComponentModel.Win32Exception) when (proceso.HasExited) { }
    }
    private async Task<EstadoFirma> EnviarAsync(object mensaje, int esperaConexion, CancellationToken ct)
    {
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limite.CancelAfter(TimeSpan.FromSeconds(35));
        await using var canal = new NamedPipeClientStream(".", _canal, PipeDirection.InOut, PipeOptions.Asynchronous);
        await canal.ConnectAsync(esperaConexion, limite.Token);
        await using var escritor = new StreamWriter(canal, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        using var lector = new StreamReader(canal, Encoding.UTF8, leaveOpen: true);
        await escritor.WriteLineAsync(JsonSerializer.Serialize(mensaje).AsMemory(), limite.Token);
        var linea = await lector.ReadLineAsync(limite.Token) ?? throw new IOException("Firma cerró la conexión.");
        using var doc = JsonDocument.Parse(linea);
        var raiz = doc.RootElement;
        if (!raiz.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True)
            throw new IOException("Firma respondió con error: " + (raiz.TryGetProperty("error", out var e) ? e.ToString() : "desconocido"));
        return EstadoFirma.Leer(raiz.GetProperty("state"));
    }
}
