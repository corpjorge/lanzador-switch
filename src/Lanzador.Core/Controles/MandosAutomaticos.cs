namespace Lanzador.Core.Controles;

/// <summary>
/// Reparto automático de mandos al abrir un juego: los mandos encendidos ocupan los primeros jugadores, sin huecos.
/// Así el mando que tienes en la mano siempre juega, aunque el que estaba como jugador 1 esté apagado o haya cambiado.
/// <list type="bullet">
/// <item>Primero van los mandos elegidos en Controles que estén encendidos, en su orden de jugador (es el orden de preferencia).</item>
/// <item>Después, los demás mandos encendidos, en el orden en que los ve el sistema.</item>
/// <item>Los que están apagados no se asignan: un jugador sin mando encendido dejaría al juego esperando a alguien que no está.</item>
/// </list>
/// </summary>
public static class MandosAutomaticos
{
    /// <summary>
    /// Los jugadores que corresponden a los mandos encendidos; lista vacía si no hay ninguno (entonces no se cambia nada).
    /// </summary>
    public static List<AsignacionJugador> Repartir(IReadOnlyList<AsignacionJugador> preferidos, IReadOnlyList<MandoConectado> conectados)
    {
        var resultado = new List<AsignacionJugador>();
        var usados = new HashSet<(string Guid, int Puerto)>();

        bool Libre(string guid, int puerto) => !usados.Contains((guid.ToLowerInvariant(), puerto));
        void Agregar(string guid, int puerto, string tipo, string nombre, string? mapeo)
        {
            if (resultado.Count >= Controles.MaxJugadores)
                return;
            usados.Add((guid.ToLowerInvariant(), puerto));
            resultado.Add(new AsignacionJugador
            {
                Jugador = resultado.Count + 1,
                Guid = guid.ToLowerInvariant(),
                Puerto = puerto,
                // «Portátil» solo vale para el jugador 1: si el reparto lo mueve, pasa a ser un Pro Controller.
                Tipo = tipo == "Portátil" && resultado.Count > 0 ? "Pro Controller" : tipo,
                Nombre = nombre,
                Mapeo = mapeo,
            });
        }

        foreach (var p in preferidos.Where(p => p.Guid.Length == 32).OrderBy(p => p.Jugador))
        {
            var mando = conectados.FirstOrDefault(m => m.Guid.Equals(p.Guid, StringComparison.OrdinalIgnoreCase) && m.Puerto == p.Puerto);
            if (mando != null && Libre(mando.Guid, mando.Puerto))
                Agregar(mando.Guid, mando.Puerto, p.Tipo, p.Nombre.Length > 0 ? p.Nombre : mando.NombreLegible, p.Mapeo ?? mando.Mapeo);
        }
        foreach (var mando in conectados.Where(m => m.Guid.Length == 32))
        {
            if (!Libre(mando.Guid, mando.Puerto))
                continue;
            // Si ese modelo de mando ya se eligió alguna vez (aunque fuera otro puerto), se respeta el tipo que se le puso.
            var conocido = preferidos.FirstOrDefault(p => p.Guid.Equals(mando.Guid, StringComparison.OrdinalIgnoreCase));
            Agregar(mando.Guid, mando.Puerto, conocido?.Tipo ?? GeneradorControles.TipoPorDefecto(mando.Guid), mando.NombreLegible, mando.Mapeo);
        }
        return resultado;
    }

    /// <summary>"J1 Mando Xbox Series · J2 DualSense".</summary>
    public static string Resumen(IEnumerable<AsignacionJugador> jugadores) =>
        string.Join(" · ", jugadores.Select(j => $"J{j.Jugador} {(j.Nombre.Length > 0 ? j.Nombre : Controles.NombreMando(j.Guid))}"));
}
