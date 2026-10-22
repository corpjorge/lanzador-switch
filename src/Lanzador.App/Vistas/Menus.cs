using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using Lanzador.App.Modelos;
using Lanzador.Core;

namespace Lanzador.App.Vistas;

/// <summary>Menús emergentes y acciones sobre un juego que comparten Inicio y Biblioteca.</summary>
public static class Menus
{
    public static string DetalleEmulador(JuegoVM juego, EmuladorConfig emulador)
    {
        var partes = new List<string> { $"Tu calificación: {Estado.TextoCalificacion(Estado.Config.PreferenciasDe(juego.Juego.Clave).CalificacionDe(emulador.Id))}" };
        if (emulador.Tipo == TipoEmulador.Ryujinx && juego.Compat != null)
            partes.Add($"Lista de Ryubing: {juego.Compat.EstadoEs}");
        else if (emulador.Id.Contains("citron", StringComparison.OrdinalIgnoreCase) && juego.CompatCitron != null)
            partes.Add($"Lista de Citron: {juego.CompatCitron.EstadoEs}");
        else if (emulador.Tipo == TipoEmulador.Yuzu && juego.CompatYuzu != null)
            partes.Add($"Lista de yuzu (2024): {juego.CompatYuzu.EstadoEs}");
        return string.Join(" · ", partes);
    }

    public static MenuEmergente Calificar(JuegoVM juego, EmuladorConfig emulador, string? titulo = null)
    {
        var preferencias = Estado.Config.PreferenciasDe(juego.Juego.Clave);
        var actual = preferencias.CalificacionDe(emulador.Id);

        OpcionMenu Opcion(Calificacion c, string detalle) => new(Estado.TextoCalificacion(c), detalle, () =>
        {
            preferencias.Calificaciones[emulador.Id] = c;
            if (c == Calificacion.Funciona && preferencias.EmuladorPreferido == null)
                preferencias.EmuladorPreferido = emulador.Id;
            Estado.GuardarConfig();
        }, c == actual);

        return new MenuEmergente(titulo ?? $"Calificar en {emulador.Nombre}", juego.Nombre,
        [
            Opcion(Calificacion.Funciona, "Se juega bien"),
            Opcion(Calificacion.ConFallos, "Se puede jugar, pero con problemas"),
            Opcion(Calificacion.NoArranca, "No inicia o se cierra"),
            Opcion(Calificacion.SinProbar, "Quitar la calificación"),
        ]);
    }

    /// <summary>Y sobre un juego: abre su página.</summary>
    public static void AbrirJuego(JuegoVM juego) =>
        MainWindow.Actual.Mostrar(new PantallaJuego(juego, MainWindow.NombreSeccion(MainWindow.Actual.SeccionActual)));

    public static MenuEmergente OpcionesJuego(JuegoVM juego)
    {
        var preferencias = Estado.Config.PreferenciasDe(juego.Juego.Clave);
        var emuladores = Estado.EmuladoresDisponibles.ToList();
        var preferido = Estado.Config.Emulador(preferencias.EmuladorPreferido);

        var opciones = new List<OpcionMenu>
        {
            new("Jugar con…", "Elige el emulador esta vez", () => ElegirEmulador(juego)),
            new("Trucos", "Ver, activar y desactivar trucos para tu versión del juego", () => AbrirTrucos(juego)),
            new(preferencias.Favorito ? "Quitar de favoritos" : "Agregar a favoritos", "Los favoritos tienen su fila en Inicio", () =>
            {
                preferencias.Favorito = !preferencias.Favorito;
                Estado.GuardarConfig();
                Catalogo.AvisarCambio();
                MainWindow.Actual.Aviso(preferencias.Favorito ? $"Listo: {juego.Nombre} está en favoritos." : $"Listo: {juego.Nombre} ya no está en favoritos.");
            }),
            new("Emulador preferido", preferido != null ? $"Ahora: {preferido.Nombre}" : "Ninguno", () =>
                MainWindow.Actual.AbrirMenu(new MenuEmergente("Emulador preferido", juego.Nombre,
                    emuladores.Select(e => new OpcionMenu(e.Nombre, DetalleEmulador(juego, e), () =>
                    {
                        preferencias.EmuladorPreferido = e.Id;
                        Estado.GuardarConfig();
                    }, e.Id == preferencias.EmuladorPreferido, icono: Tema.IconosEmulador.De(e)))
                    .Append(new OpcionMenu("Ninguno", "Preguntar siempre", () =>
                    {
                        preferencias.EmuladorPreferido = null;
                        Estado.GuardarConfig();
                    }, preferencias.EmuladorPreferido == null))))),
            new("Calificar", "Anota cómo funciona en cada emulador", () =>
            {
                if (emuladores.Count == 1)
                    MainWindow.Actual.AbrirMenu(Calificar(juego, emuladores[0]));
                else
                    MainWindow.Actual.AbrirMenu(new MenuEmergente("Calificar", juego.Nombre,
                        emuladores.Select(e => new OpcionMenu(e.Nombre, DetalleEmulador(juego, e), () => MainWindow.Actual.AbrirMenu(Calificar(juego, e)),
                            icono: Tema.IconosEmulador.De(e)))));
            }),
            new("Abrir la carpeta del juego", juego.Juego.RutaArchivo, () => Abrir("explorer.exe", $"/select,\"{juego.Juego.RutaArchivo}\"")),
        };
        if (juego.Juego.TieneTitleId)
            opciones.Add(new("Ver en tinfoil.io", "Abre la página del juego en el navegador", () => Abrir($"https://tinfoil.io/Title/{juego.Juego.TitleId}", null)));

        return new MenuEmergente(juego.Nombre, juego.Juego.TieneTitleId ? $"ID {juego.Juego.TitleId}" : null, opciones);
    }

    public static MenuEmergente Principal()
    {
        var ventana = MainWindow.Actual;
        return new MenuEmergente("Menú", null,
        [
            new("Configuración", "Emuladores, carpetas de juegos e IA para traducir", () => ventana.Mostrar(new PantallaConfiguracion())),
            new("Emuladores", "Versiones, actualizaciones y respaldos de Eden, Ryujinx, Citron Neo y Suyu", () => ventana.IrASeccion(Seccion.Emuladores)),
            new("Controles", "Configura los mandos una vez y cópialos a todos los emuladores", () => ventana.IrASeccion(Seccion.Controles)),
            new("Volver a buscar juegos", "Lee otra vez las carpetas de juegos", () => _ = Catalogo.CargarAsync(false)),
            new("Actualizar compatibilidad", "Descarga de nuevo las listas de Ryubing y Citron", () => _ = Catalogo.CargarAsync(true)),
            new(ventana.EnPantallaCompleta ? "Usar ventana" : "Usar pantalla completa", "También con F11", () =>
            {
                ventana.PonerPantallaCompleta(!ventana.EnPantallaCompleta);
                Estado.Config.PantallaCompleta = ventana.EnPantallaCompleta;
                Estado.GuardarConfig();
            }),
            new("Salir", null, () => Application.Current.Shutdown()),
        ]);
    }

    /// <summary>
    /// A sobre un juego: pregunta con qué emulador (el preferido va primero y con el foco, así A + A lo abre). Si solo
    /// hay un emulador, lo abre directo.
    /// </summary>
    public static void Jugar(JuegoVM juego)
    {
        var emuladores = Estado.EmuladoresDisponibles.ToList();
        if (emuladores.Count == 1)
            _ = MainWindow.Actual.JugarAsync(juego, emuladores[0]);
        else
            ElegirEmulador(juego);
    }

    public static void ElegirEmulador(JuegoVM juego)
    {
        var emuladores = Estado.EmuladoresDisponibles.ToList();
        if (emuladores.Count == 0)
        {
            MainWindow.Actual.Aviso("No hay emuladores configurados. Revisa Configuración (botón ☰).", true);
            return;
        }
        var preferido = Estado.Config.PreferenciasDe(juego.Juego.Clave).EmuladorPreferido;
        var opciones = emuladores
            .OrderByDescending(e => e.Id == preferido)
            .Select(e => new OpcionMenu(
                $"Jugar con {e.Nombre}" + (e.Id == preferido ? "  ★" : ""),
                DetalleEmulador(juego, e),
                () => _ = MainWindow.Actual.JugarAsync(juego, e),
                icono: Tema.IconosEmulador.De(e)));
        MainWindow.Actual.AbrirMenu(new MenuEmergente(juego.Nombre, "¿Con qué emulador lo abres?", opciones));
    }

    public static void AbrirTrucos(JuegoVM juego, string? emuladorId = null)
    {
        if (!juego.Juego.TieneTitleId)
        {
            MainWindow.Actual.Aviso("Este archivo no indica el ID del juego en su nombre, así que no puedo buscar sus trucos.", true);
            return;
        }
        if (!Estado.EmuladoresDisponibles.Any())
        {
            MainWindow.Actual.Aviso("No hay emuladores configurados.", true);
            return;
        }
        MainWindow.Actual.Mostrar(new PantallaTrucos(juego, emuladorId));
    }

    public static void Abrir(string archivo, string? argumentos)
    {
        try
        {
            Process.Start(new ProcessStartInfo(archivo) { Arguments = argumentos ?? "", UseShellExecute = true })?.Dispose();
        }
        catch (Win32Exception ex)
        {
            MainWindow.Actual.Aviso($"No se pudo abrir: {ex.Message}", true);
        }
    }
}
