using System.Text;
using System.Text.RegularExpressions;

namespace Lanzador.Core.Trucos;

/// <summary>Un bloque de un archivo de trucos: <c>[Nombre]</c> (o <c>{Maestro}</c>) seguido de sus líneas de código.</summary>
public sealed class SeccionTruco
{
    public string Nombre { get; set; } = "";
    public bool EsMaestro { get; set; }
    public List<string> Lineas { get; } = new();
    public string Codigo => string.Join("\n", Lineas);
}

/// <summary>Formato de trucos de Atmosphère, el que usan Eden, Ryujinx, Citron y tinfoil.</summary>
public static partial class ParserAtmosphere
{
    public static List<SeccionTruco> Parsear(string texto)
    {
        var secciones = new List<SeccionTruco>();
        SeccionTruco? actual = null;
        foreach (var bruta in texto.Split('\n'))
        {
            var linea = bruta.Trim().TrimStart('﻿');
            if (linea.Length == 0)
                continue;

            if (linea.Length >= 2 && ((linea[0] == '[' && linea[^1] == ']') || (linea[0] == '{' && linea[^1] == '}')))
            {
                actual = new SeccionTruco { Nombre = linea[1..^1].Trim(), EsMaestro = linea[0] == '{' };
                secciones.Add(actual);
                continue;
            }

            // Solo se aceptan palabras hexadecimales de 8 dígitos; comentarios y basura se ignoran.
            if (!RxInstruccion().IsMatch(linea))
                continue;
            if (actual == null)
            {
                actual = new SeccionTruco();
                secciones.Add(actual);
            }
            actual.Lineas.Add(NormalizarLinea(linea));
        }

        secciones.RemoveAll(s => s.Lineas.Count == 0);
        foreach (var s in secciones.Where(s => s.Nombre.Length == 0))
            s.Nombre = s.EsMaestro ? "Código maestro" : "Sin nombre";
        return secciones;
    }

    public static string NormalizarLinea(string linea) =>
        string.Join(' ', linea.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();

    /// <summary>Escribe secciones en formato Atmosphère. Los códigos maestros se escriben como trucos normales.</summary>
    public static string Escribir(IEnumerable<(string Nombre, IReadOnlyList<string> Lineas)> secciones)
    {
        var sb = new StringBuilder();
        foreach (var (nombre, lineas) in secciones)
        {
            if (sb.Length > 0)
                sb.Append("\r\n");
            sb.Append('[').Append(NombreSeguroEnArchivo(nombre)).Append("]\r\n");
            foreach (var l in lineas)
                sb.Append(l).Append("\r\n");
        }
        return sb.ToString();
    }

    public static string NombreSeguroEnArchivo(string nombre)
    {
        var limpio = nombre.Replace('[', '(').Replace(']', ')').Replace('{', '(').Replace('}', ')').Replace('\r', ' ').Replace('\n', ' ').Trim();
        return limpio.Length == 0 ? "Sin nombre" : limpio;
    }

    [GeneratedRegex(@"^[0-9A-Fa-f]{8}(\s+[0-9A-Fa-f]{8})*$")]
    private static partial Regex RxInstruccion();
}

/// <summary>Lee del código qué botones activan el truco (instrucción 8 de Atmosphère: "mientras se mantiene").</summary>
public static partial class LectorBotones
{
    private static readonly (uint Mascara, string Nombre)[] Tabla =
    [
        (0x1, "A"), (0x2, "B"), (0x4, "X"), (0x8, "Y"),
        (0x10, "pulsar stick izq"), (0x20, "pulsar stick der"),
        (0x40, "L"), (0x80, "R"), (0x100, "ZL"), (0x200, "ZR"),
        (0x400, "Más"), (0x800, "Menos"),
        (0x1000, "cruceta izquierda"), (0x2000, "cruceta arriba"), (0x4000, "cruceta derecha"), (0x8000, "cruceta abajo"),
        (0x10000, "stick izq izquierda"), (0x20000, "stick izq arriba"), (0x40000, "stick izq derecha"), (0x80000, "stick izq abajo"),
        (0x100000, "stick der izquierda"), (0x200000, "stick der arriba"), (0x400000, "stick der derecha"), (0x800000, "stick der abajo"),
        (0x1000000, "SL"), (0x2000000, "SR"),
    ];

    public static string? Describir(IEnumerable<string> lineas)
    {
        var combinaciones = new List<string>();
        foreach (var linea in lineas)
        {
            var m = RxActivador().Match(linea);
            if (!m.Success)
                continue;
            var mascara = Convert.ToUInt32(m.Groups[1].Value, 16);
            var partes = Tabla.Where(t => (mascara & t.Mascara) != 0).Select(t => t.Nombre).ToList();
            if (partes.Count == 0)
                continue;
            var combinacion = string.Join(" + ", partes);
            if (!combinaciones.Contains(combinacion))
                combinaciones.Add(combinacion);
        }
        return combinaciones.Count == 0 ? null : "mantener " + string.Join(" / ", combinaciones.Take(3));
    }

    [GeneratedRegex(@"^8([0-9A-F]{7})$")]
    private static partial Regex RxActivador();
}
