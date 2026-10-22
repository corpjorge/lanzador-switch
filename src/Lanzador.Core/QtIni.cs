using System.Text;
using System.Text.RegularExpressions;

namespace Lanzador.Core;

/// <summary>
/// Lee y modifica la sección [DisabledAddOns] del qt-config.ini de Eden (formato QSettings).
/// Todo lo demás del archivo se conserva tal cual.
/// </summary>
public sealed partial class QtIni
{
    private const string Seccion = "[DisabledAddOns]";

    private sealed class Titulo
    {
        public ulong Id;
        public string IdCrudo = "";
        public List<(string Nombre, string Crudo)> Deshabilitados = new();
    }

    private readonly List<string> _lineas;
    private readonly string _saltoLinea;
    private readonly List<Titulo> _titulos = new();
    private int _inicioSeccion = -1;
    private int _finSeccion = -1;

    public bool Modificado { get; private set; }

    private QtIni(List<string> lineas, string saltoLinea)
    {
        _lineas = lineas;
        _saltoLinea = saltoLinea;
        Analizar();
    }

    public static QtIni Leer(string ruta)
    {
        var texto = File.Exists(ruta) ? File.ReadAllText(ruta, new UTF8Encoding(false)) : "";
        var salto = texto.Contains("\r\n") ? "\r\n" : "\n";
        var lineas = texto.Split(salto).ToList();
        if (lineas.Count > 0 && lineas[^1].Length == 0)
            lineas.RemoveAt(lineas.Count - 1);
        return new QtIni(lineas, salto);
    }

    public IReadOnlySet<string> Deshabilitados(string titleIdHex)
    {
        var id = Convert.ToUInt64(titleIdHex, 16);
        var titulo = _titulos.FirstOrDefault(t => t.Id == id);
        return titulo == null
            ? new HashSet<string>()
            : titulo.Deshabilitados.Select(d => d.Nombre).ToHashSet(StringComparer.Ordinal);
    }

    public void Fijar(string titleIdHex, string nombreAddOn, bool deshabilitado)
    {
        var id = Convert.ToUInt64(titleIdHex, 16);
        var titulo = _titulos.FirstOrDefault(t => t.Id == id);
        if (titulo == null)
        {
            if (!deshabilitado)
                return;
            titulo = new Titulo { Id = id, IdCrudo = id.ToString() };
            _titulos.Add(titulo);
        }

        var indice = titulo.Deshabilitados.FindIndex(d => d.Nombre == nombreAddOn);
        if (deshabilitado && indice < 0)
        {
            titulo.Deshabilitados.Add((nombreAddOn, Codificar(nombreAddOn)));
            Modificado = true;
        }
        else if (!deshabilitado && indice >= 0)
        {
            titulo.Deshabilitados.RemoveAt(indice);
            Modificado = true;
        }
    }

    public void Guardar(string ruta)
    {
        var nuevaSeccion = new List<string> { Seccion, $"size={_titulos.Count}" };
        for (var i = 0; i < _titulos.Count; i++)
        {
            var t = _titulos[i];
            var n = i + 1;
            nuevaSeccion.Add($@"{n}\title_id\default=false");
            nuevaSeccion.Add($@"{n}\title_id={t.IdCrudo}");
            nuevaSeccion.Add($@"{n}\disabled\size={t.Deshabilitados.Count}");
            for (var j = 0; j < t.Deshabilitados.Count; j++)
            {
                nuevaSeccion.Add($@"{n}\disabled\{j + 1}\d\default=false");
                nuevaSeccion.Add($@"{n}\disabled\{j + 1}\d={t.Deshabilitados[j].Crudo}");
            }
        }

        var resultado = new List<string>();
        if (_inicioSeccion < 0)
        {
            resultado.AddRange(nuevaSeccion);
            resultado.Add("");
            resultado.AddRange(_lineas);
        }
        else
        {
            resultado.AddRange(_lineas.Take(_inicioSeccion));
            resultado.AddRange(nuevaSeccion);
            resultado.AddRange(_lineas.Skip(_finSeccion));
        }

        var temporal = ruta + ".lanzador.tmp";
        File.WriteAllText(temporal, string.Join(_saltoLinea, resultado) + _saltoLinea, new UTF8Encoding(false));
        File.Move(temporal, ruta, true);
        Modificado = false;
    }

    private void Analizar()
    {
        _inicioSeccion = _lineas.FindIndex(l => l.Trim() == Seccion);
        if (_inicioSeccion < 0)
            return;

        _finSeccion = _lineas.Count;
        for (var i = _inicioSeccion + 1; i < _lineas.Count; i++)
        {
            var l = _lineas[i].Trim();
            if (l.StartsWith('[') && l.EndsWith(']') && !l.Contains('='))
            {
                _finSeccion = i;
                break;
            }
        }
        // Conserva las líneas en blanco que separan esta sección de la siguiente.
        while (_finSeccion - 1 > _inicioSeccion && _lineas[_finSeccion - 1].Trim().Length == 0)
            _finSeccion--;

        var tamano = 0;
        var ids = new Dictionary<int, string>();
        var tamanosDeshabilitados = new Dictionary<int, int>();
        var entradas = new Dictionary<(int, int), string>();

        for (var i = _inicioSeccion + 1; i < _finSeccion; i++)
        {
            var linea = _lineas[i];
            var igual = linea.IndexOf('=');
            if (igual < 0)
                continue;
            var clave = linea[..igual];
            var valor = linea[(igual + 1)..];

            if (clave == "size")
            {
                int.TryParse(valor, out tamano);
                continue;
            }
            var m = RxTitulo().Match(clave);
            if (m.Success)
            {
                ids[int.Parse(m.Groups[1].Value)] = valor;
                continue;
            }
            m = RxTamanoDeshabilitados().Match(clave);
            if (m.Success)
            {
                int.TryParse(valor, out var t);
                tamanosDeshabilitados[int.Parse(m.Groups[1].Value)] = t;
                continue;
            }
            m = RxEntrada().Match(clave);
            if (m.Success)
                entradas[(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value))] = valor;
        }

        // QSettings solo lee hasta "size": lo que haya después son restos y se descartan.
        for (var i = 1; i <= tamano; i++)
        {
            if (!ids.TryGetValue(i, out var idCrudo) || !ulong.TryParse(idCrudo.Trim(), out var id))
                continue;
            var titulo = new Titulo { Id = id, IdCrudo = idCrudo };
            var n = tamanosDeshabilitados.GetValueOrDefault(i);
            for (var j = 1; j <= n; j++)
                if (entradas.TryGetValue((i, j), out var crudo))
                    titulo.Deshabilitados.Add((Decodificar(crudo), crudo));
            _titulos.Add(titulo);
        }
    }

    /// <summary>Decodifica un valor de QSettings (quita comillas y escapes).</summary>
    public static string Decodificar(string crudo)
    {
        var v = crudo.Trim();
        if (v.Length >= 2 && v[0] == '"' && v[^1] == '"')
            v = v[1..^1];
        var sb = new StringBuilder(v.Length);
        for (var i = 0; i < v.Length; i++)
        {
            if (v[i] == '\\' && i + 1 < v.Length)
            {
                var siguiente = v[++i];
                sb.Append(siguiente switch { 'n' => '\n', 't' => '\t', 'r' => '\r', _ => siguiente });
            }
            else
            {
                sb.Append(v[i]);
            }
        }
        return sb.ToString();
    }

    public static string Codificar(string valor)
    {
        var escapado = valor.Replace("\\", "\\\\").Replace("\"", "\\\"");
        var necesitaComillas = escapado.IndexOfAny([',', ';', '=', '"', '#']) >= 0 || escapado != escapado.Trim();
        return necesitaComillas ? $"\"{escapado}\"" : escapado;
    }

    [GeneratedRegex(@"^(\d+)\\title_id$")]
    private static partial Regex RxTitulo();

    [GeneratedRegex(@"^(\d+)\\disabled\\size$")]
    private static partial Regex RxTamanoDeshabilitados();

    [GeneratedRegex(@"^(\d+)\\disabled\\(\d+)\\d$")]
    private static partial Regex RxEntrada();
}
