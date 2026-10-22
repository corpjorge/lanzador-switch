using Lanzador.App.Entrada;

namespace Lanzador.App.Vistas;

/// <summary>Un botón de la barra inferior. <see cref="Boton"/> puede traer varios glifos separados por espacio ("LB RB").</summary>
public sealed record Ayuda(string Boton, string Texto, string Tecla)
{
    public IReadOnlyList<string> Glifos => Boton.Split(' ', StringSplitOptions.RemoveEmptyEntries);
}

/// <summary>Una pantalla o menú que recibe acciones del mando o del teclado.</summary>
public interface IPantalla
{
    void Ejecutar(Accion accion);

    /// <summary>Botones que se muestran en la barra inferior.</summary>
    IReadOnlyList<Ayuda> Ayudas { get; }

    /// <summary>True si el teclado debe funcionar normal (para escribir en campos de texto).</summary>
    bool UsaTeclado => false;

    /// <summary>Se llama cuando la pantalla vuelve a quedar arriba.</summary>
    void AlMostrar() { }

    /// <summary>
    /// Antes de dejar la pantalla (volver o cambiar de sección): llama a <paramref name="salir"/> si se puede salir ya,
    /// o pregunta primero (cambios sin aplicar, una descarga en curso…).
    /// </summary>
    void PedirSalir(Action salir) => salir();
}

/// <summary>Las secciones de la cabecera, que se recorren con LB y RB.</summary>
public enum Seccion
{
    Inicio,
    Biblioteca,
    Emuladores,
    Controles,
}

/// <summary>Una pantalla que es una sección de la cabecera (Inicio, Biblioteca, Emuladores, Controles).</summary>
public interface ISeccion : IPantalla
{
    Seccion Seccion { get; }
}
