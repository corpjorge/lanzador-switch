using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lanzador.Core.IA;

public sealed record Traduccion(string Nombre, string Descripcion, string Categoria);

public sealed record ElementoATraducir(string Id, string Nombre, string? Botones);

/// <summary>
/// Traduce nombres de trucos al español con Claude Code o Codex en modo no interactivo.
/// La IA solo recibe y devuelve nombres: el código del truco nunca pasa por ella.
/// </summary>
public static partial class TraductorIA
{
    private const int TamanoLote = 50;
    private static readonly TimeSpan TiempoMaximo = TimeSpan.FromMinutes(4);

    private const string Esquema = """
        {"type":"object","additionalProperties":false,"required":["trucos"],"properties":{"trucos":{"type":"array","items":{"type":"object","additionalProperties":false,"required":["id","nombre","descripcion","categoria"],"properties":{"id":{"type":"string"},"nombre":{"type":"string"},"descripcion":{"type":"string"},"categoria":{"type":"string"}}}}}}
        """;

    public static readonly string[] Categorias = ["Vida", "Dinero", "Objetos", "Movimiento", "Tiempo", "Combate", "Desbloqueos", "Gráficos", "Rendimiento", "Otros"];

    public static async Task<Dictionary<string, Traduccion>> TraducirAsync(
        string motor, string modelo, string tituloJuego, IReadOnlyList<ElementoATraducir> elementos,
        IProgress<string>? progreso, CancellationToken ct)
    {
        var resultado = new Dictionary<string, Traduccion>();
        var lotes = elementos.Chunk(TamanoLote).ToList();
        for (var i = 0; i < lotes.Count; i++)
        {
            progreso?.Report(lotes.Count > 1 ? $"Traduciendo con {NombreMotor(motor)} ({i + 1}/{lotes.Count})…" : $"Traduciendo con {NombreMotor(motor)}…");
            var prompt = ConstruirPrompt(tituloJuego, lotes[i]);
            var json = motor switch
            {
                "claude" => await EjecutarClaudeAsync(prompt, modelo, ct),
                "codex" => await EjecutarCodexAsync(prompt, modelo, ct),
                _ => throw new InvalidOperationException("No hay un motor de IA configurado."),
            };
            foreach (var (id, t) in Interpretar(json, lotes[i]))
                resultado[id] = t;
        }
        return resultado;
    }

    public static string NombreMotor(string motor) => motor switch
    {
        "claude" => "Claude Code",
        "codex" => "Codex",
        _ => "IA",
    };

    private static string ConstruirPrompt(string tituloJuego, IEnumerable<ElementoATraducir> elementos)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Eres un traductor de nombres de trucos (cheats) de juegos de Nintendo Switch.");
        sb.AppendLine($"Juego: {tituloJuego}");
        sb.AppendLine("Para cada elemento devuelve:");
        sb.AppendLine("- id: exactamente el id recibido.");
        sb.AppendLine("- nombre: traducción al español latino, corta y clara (máximo 70 caracteres), sin corchetes ni llaves. Usa los nombres oficiales en español de personajes, objetos y modos si los conoces. Conserva números y multiplicadores (x2, 999…). Traduce también las instrucciones de botones que traiga el nombre (\"hold\" = \"mantener\", \"press\" = \"pulsar\"). Si el elemento indica \"botones\", agrégalos al final entre paréntesis tal como vienen, salvo que el nombre ya los mencione.");
        sb.AppendLine("- descripcion: una frase corta en español que explique qué hace el truco. No inventes efectos que el nombre no indique.");
        sb.AppendLine($"- categoria: una de {string.Join(", ", Categorias)}.");
        sb.AppendLine("Responde solo con el JSON pedido, con un objeto por cada elemento.");
        sb.AppendLine();
        sb.AppendLine("Elementos:");
        foreach (var e in elementos)
            sb.AppendLine($"id={e.Id} | nombre={JsonSerializer.Serialize(e.Nombre)}" + (e.Botones != null ? $" | botones={e.Botones}" : ""));
        return sb.ToString();
    }

    private static IEnumerable<(string Id, Traduccion T)> Interpretar(string json, IEnumerable<ElementoATraducir> pedidos)
    {
        var ids = pedidos.Select(p => p.Id).ToHashSet();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("trucos", out var trucos) || trucos.ValueKind != JsonValueKind.Array)
            yield break;
        foreach (var t in trucos.EnumerateArray())
        {
            var id = Texto(t, "id");
            var nombre = LimpiarNombre(Texto(t, "nombre"));
            if (!ids.Contains(id) || nombre.Length == 0)
                continue;
            var categoria = Texto(t, "categoria");
            if (!Categorias.Contains(categoria))
                categoria = "Otros";
            yield return (id, new Traduccion(nombre, Texto(t, "descripcion").Trim(), categoria));
        }
    }

    private static string Texto(JsonElement e, string propiedad) =>
        e.TryGetProperty(propiedad, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";

    public static string LimpiarNombre(string nombre)
    {
        var limpio = nombre.Replace('[', ' ').Replace(']', ' ').Replace('{', ' ').Replace('}', ' ').Replace('\n', ' ').Trim();
        limpio = RxEspacios().Replace(limpio, " ");
        return limpio.Length > 90 ? limpio[..90].Trim() : limpio;
    }

    private static async Task<string> EjecutarClaudeAsync(string prompt, string modelo, CancellationToken ct)
    {
        var exe = BuscarEjecutable(["claude.exe", "claude.cmd"], [Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin")])
                  ?? throw new InvalidOperationException("No encuentro Claude Code (claude). Instálalo o elige otro motor en Configuración.");
        var psi = CrearProceso(exe);
        foreach (var a in new[] { "-p", "--output-format", "json", "--json-schema", Esquema, "--tools", "", "--no-session-persistence" })
            psi.ArgumentList.Add(a);
        if (ModeloValido(modelo))
        {
            psi.ArgumentList.Add("--model");
            psi.ArgumentList.Add(modelo);
        }

        var (salida, error, codigo) = await EjecutarAsync(psi, prompt, ct);
        if (string.IsNullOrWhiteSpace(salida))
            throw new InvalidOperationException($"Claude Code no respondió (código {codigo}). {Recortar(error)}");

        using var doc = JsonDocument.Parse(salida);
        var raiz = doc.RootElement;
        var resultadoTexto = raiz.TryGetProperty("result", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() ?? "" : "";
        if (raiz.TryGetProperty("is_error", out var esError) && esError.ValueKind == JsonValueKind.True)
        {
            var pista = resultadoTexto.Contains("authenticate", StringComparison.OrdinalIgnoreCase) || resultadoTexto.Contains("login", StringComparison.OrdinalIgnoreCase)
                ? " Abre una terminal, ejecuta \"claude\" y usa /login para volver a iniciar sesión."
                : "";
            throw new InvalidOperationException($"Claude Code devolvió un error: {Recortar(resultadoTexto)}.{pista}");
        }
        if (raiz.TryGetProperty("structured_output", out var estructurado) && estructurado.ValueKind == JsonValueKind.Object)
            return estructurado.GetRawText();
        return ExtraerObjetoJson(resultadoTexto);
    }

    private static async Task<string> EjecutarCodexAsync(string prompt, string modelo, CancellationToken ct)
    {
        var npm = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm");
        var npmGlobal = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".npm-global");
        var exe = BuscarEjecutable(["codex.cmd", "codex.exe"], [npm, npmGlobal])
                  ?? throw new InvalidOperationException("No encuentro Codex (codex). Instálalo o elige otro motor en Configuración.");

        var temporal = Path.Combine(Path.GetTempPath(), "lanzador-codex-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporal);
        try
        {
            var esquema = Path.Combine(temporal, "esquema.json");
            var salidaArchivo = Path.Combine(temporal, "salida.json");
            await File.WriteAllTextAsync(esquema, Esquema, ct);

            var comando = $"\"{exe}\" exec --skip-git-repo-check --sandbox read-only --output-schema \"{esquema}\" -o \"{salidaArchivo}\"";
            if (ModeloValido(modelo))
                comando += $" -m {modelo}";
            comando += " -";

            var psi = CrearProceso(Path.Combine(Environment.SystemDirectory, "cmd.exe"));
            psi.Arguments = $"/d /s /c \"{comando}\"";
            psi.WorkingDirectory = temporal;

            var (_, error, codigo) = await EjecutarAsync(psi, prompt, ct);
            if (!File.Exists(salidaArchivo) || new FileInfo(salidaArchivo).Length == 0)
            {
                var detalle = RxErrorCodex().Matches(error).Select(m => m.Groups[1].Value).LastOrDefault() ?? Recortar(error);
                throw new InvalidOperationException($"Codex no devolvió resultado (código {codigo}). {detalle}");
            }
            return ExtraerObjetoJson(await File.ReadAllTextAsync(salidaArchivo, ct));
        }
        finally
        {
            try { Directory.Delete(temporal, true); } catch (IOException) { }
        }
    }

    private static ProcessStartInfo CrearProceso(string exe) => new(exe)
    {
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        StandardInputEncoding = new UTF8Encoding(false),
        StandardOutputEncoding = Encoding.UTF8,
        StandardErrorEncoding = Encoding.UTF8,
        WorkingDirectory = Path.GetTempPath(),
    };

    private static async Task<(string Salida, string Error, int Codigo)> EjecutarAsync(ProcessStartInfo psi, string entrada, CancellationToken ct)
    {
        using var proceso = Process.Start(psi) ?? throw new InvalidOperationException($"No se pudo iniciar {psi.FileName}.");
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limite.CancelAfter(TiempoMaximo);
        try
        {
            var salida = proceso.StandardOutput.ReadToEndAsync(limite.Token);
            var error = proceso.StandardError.ReadToEndAsync(limite.Token);
            await proceso.StandardInput.WriteAsync(entrada.AsMemory(), limite.Token);
            proceso.StandardInput.Close();
            await proceso.WaitForExitAsync(limite.Token);
            return (await salida, await error, proceso.ExitCode);
        }
        catch (OperationCanceledException)
        {
            try { proceso.Kill(true); } catch (InvalidOperationException) { }
            if (ct.IsCancellationRequested)
                throw;
            throw new TimeoutException("La IA tardó demasiado en responder.");
        }
    }

    private static string? BuscarEjecutable(string[] nombres, string[] carpetasExtra)
    {
        var carpetas = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Concat(carpetasExtra);
        foreach (var carpeta in carpetas)
            foreach (var nombre in nombres)
            {
                try
                {
                    var ruta = Path.Combine(carpeta.Trim('"'), nombre);
                    if (File.Exists(ruta))
                        return ruta;
                }
                catch (ArgumentException) { }
            }
        return null;
    }

    private static bool ModeloValido(string modelo) => modelo.Length > 0 && RxModelo().IsMatch(modelo);

    private static string ExtraerObjetoJson(string texto)
    {
        var inicio = texto.IndexOf('{');
        var fin = texto.LastIndexOf('}');
        if (inicio < 0 || fin <= inicio)
            throw new InvalidOperationException("La IA no devolvió JSON.");
        return texto[inicio..(fin + 1)];
    }

    private static string Recortar(string texto)
    {
        var t = texto.Trim().Replace("\r", " ").Replace("\n", " ");
        return t.Length > 300 ? t[..300] + "…" : t;
    }

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex RxEspacios();

    [GeneratedRegex(@"^[A-Za-z0-9._:\-]+$")]
    private static partial Regex RxModelo();

    [GeneratedRegex("\"message\":\"([^\"]+)\"")]
    private static partial Regex RxErrorCodex();
}

/// <summary>Traducciones ya hechas, para no volver a pedirlas a la IA.</summary>
public sealed class CacheTraducciones
{
    private readonly Dictionary<string, Traduccion> _datos;
    private readonly object _bloqueo = new();

    private CacheTraducciones(Dictionary<string, Traduccion> datos) => _datos = datos;

    public static CacheTraducciones Cargar()
    {
        try
        {
            if (File.Exists(Rutas.ArchivoTraducciones))
                return new CacheTraducciones(JsonSerializer.Deserialize<Dictionary<string, Traduccion>>(File.ReadAllText(Rutas.ArchivoTraducciones)) ?? new());
        }
        catch (JsonException) { }
        return new CacheTraducciones(new());
    }

    public static string Clave(string titleId, string nombreOriginal) => $"{titleId}|{nombreOriginal}";

    public Traduccion? Buscar(string titleId, string nombreOriginal)
    {
        lock (_bloqueo)
            return _datos.TryGetValue(Clave(titleId, nombreOriginal), out var t) ? t : null;
    }

    public void Guardar(string titleId, IEnumerable<(string NombreOriginal, Traduccion T)> nuevas)
    {
        lock (_bloqueo)
        {
            foreach (var (original, t) in nuevas)
                _datos[Clave(titleId, original)] = t;
            var temporal = Rutas.ArchivoTraducciones + ".tmp";
            File.WriteAllText(temporal, JsonSerializer.Serialize(_datos, GestorConfiguracion.OpcionesJson));
            File.Move(temporal, Rutas.ArchivoTraducciones, true);
        }
    }

    public void Olvidar(string titleId, IEnumerable<string> nombresOriginales)
    {
        lock (_bloqueo)
            foreach (var n in nombresOriginales)
                _datos.Remove(Clave(titleId, n));
    }
}
