using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Lanzador.App.Entrada;
using Lanzador.App.Modelos;
using Lanzador.Core;
using Lanzador.Core.Actualizaciones;
using Lanzador.App.Audio;

namespace Lanzador.App.Vistas;

/// <summary>Versión instalada y disponible de cada emulador, con actualización en su sitio y vuelta atrás.</summary>
public partial class PantallaEmuladores : UserControl, ISeccion
{
    private readonly List<EmuladorVM> _emuladores;
    private CancellationTokenSource? _cancelacion;
    private int _indice;
    private bool _recargarBiblioteca;

    public PantallaEmuladores()
    {
        InitializeComponent();
        _emuladores = Estado.Config.Emuladores
            .Select(e => new EmuladorVM(e, FuentesActualizacion.Para(e, Estado.Config.Actualizaciones)))
            .ToList();
        ListaEmuladores.ItemsSource = _emuladores;
        Seleccionar(0);
        Loaded += async (_, _) =>
        {
            if (_emuladores.All(e => e.Instalada == null))
                await ConsultarTodosAsync();
        };
    }

    public Seccion Seccion => Seccion.Emuladores;

    public IReadOnlyList<Ayuda> Ayudas { get; } =
    [
        new("A", "Actualizar", "Enter"),
        new("X", "Buscar de nuevo", "T"),
        new("Y", "Opciones", "O"),
        new("B", "Volver", "Esc"),
    ];

    private EmuladorVM? Actual => _indice >= 0 && _indice < _emuladores.Count ? _emuladores[_indice] : null;

    public void Ejecutar(Accion accion)
    {
        switch (accion)
        {
            // Tarjetas en dos columnas: ← → en la fila, ↑ ↓ entre filas.
            case Accion.Izquierda: { var antes = _indice; if (_indice % 2 == 1) Seleccionar(_indice - 1); Sonidos.Moverse(antes != _indice); break; }
            case Accion.Derecha: { var antes = _indice; if (_indice % 2 == 0 && _indice + 1 < _emuladores.Count) Seleccionar(_indice + 1); Sonidos.Moverse(antes != _indice); break; }
            case Accion.Arriba: { var antes = _indice; if (_indice >= 2) Seleccionar(_indice - 2); Sonidos.Moverse(antes != _indice); break; }
            case Accion.Abajo: { var antes = _indice; if (_indice + 2 < _emuladores.Count) Seleccionar(_indice + 2); Sonidos.Moverse(antes != _indice); break; }
            case Accion.Aceptar: if (Actual != null) AccionPrincipal(Actual); break;
            case Accion.Opciones: if (Actual != null) AbrirOpciones(Actual); break;
            case Accion.Trucos:
                if (_emuladores.Any(e => e.Ocupado))
                    MainWindow.Actual.Aviso("Espera a que termine la actualización.");
                else
                    _ = ConsultarTodosAsync();
                break;
            case Accion.Atras: PedirSalir(MainWindow.Actual.Volver); break;
        }
    }

    private void Seleccionar(int indice)
    {
        if (_emuladores.Count == 0)
            return;
        indice = Math.Clamp(indice, 0, _emuladores.Count - 1);
        foreach (var e in _emuladores)
            e.Seleccionado = false;
        _indice = indice;
        _emuladores[indice].Seleccionado = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (ListaEmuladores.ItemContainerGenerator.ContainerFromIndex(_indice) is FrameworkElement contenedor)
                contenedor.BringIntoView(new Rect(0, -30, contenedor.ActualWidth, contenedor.ActualHeight + 60));
        });
    }

    private Task ConsultarTodosAsync() => Task.WhenAll(_emuladores.Select(ConsultarAsync));

    private async Task ConsultarAsync(EmuladorVM vm)
    {
        vm.Consultando = vm.Fuente is { PermiteDescarga: true };
        vm.ErrorConsulta = null;
        vm.Refrescar();
        var emulador = vm.Emulador;
        (vm.Instalada, vm.Respaldo) = await Task.Run(() => (VersionesInstaladas.Leer(emulador), Actualizador.UltimoRespaldo(emulador.Id)));
        vm.Refrescar();
        if (vm.Fuente is not { PermiteDescarga: true } fuente)
            return;
        try
        {
            vm.Disponible = await fuente.ConsultarAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidDataException or JsonException or IOException)
        {
            vm.Disponible = null;
            vm.ErrorConsulta = ex is TaskCanceledException ? "El servidor tardó demasiado en responder." : Actualizador.Explicar(ex);
        }
        finally
        {
            vm.Consultando = false;
            vm.Refrescar();
        }
    }

    private void AccionPrincipal(EmuladorVM vm)
    {
        if (vm.Ocupado)
            MainWindow.Actual.Aviso($"{vm.Nombre} ya se está actualizando.");
        else if (vm.SoloPagina)
            AbrirPagina(vm);
        else if (vm.Fuente == null)
            MainWindow.Actual.Aviso($"El lanzador no sabe actualizar {vm.Nombre}.", true);
        else if (vm.Consultando)
            MainWindow.Actual.Aviso("Espera, estoy buscando la última versión…");
        else if (vm.ErrorConsulta != null || vm.Disponible == null)
            _ = ConsultarAsync(vm);
        else
            Confirmar(vm);
    }

    private void Confirmar(EmuladorVM vm)
    {
        var d = vm.Disponible!;
        var tamano = d.Tamano is { } t ? $" · {t / 1048576.0:0} MB" : "";
        var cambio = vm.Instalado ? $"{vm.Instalada?.Version ?? "Versión desconocida"} → {d.Version}" : $"Versión {d.Version}";
        var datos = vm.Emulador.Tipo == TipoEmulador.Ryujinx ? "portable" : "user";
        var (titulo, opcion) = !vm.Instalado ? ($"Instalar {vm.Nombre}", "Instalar ahora")
            : vm.AlDia ? ($"{vm.Nombre} ya está al día", "Reinstalar esta versión")
            : ($"Actualizar {vm.Nombre}", "Actualizar ahora");
        var subtitulo = $"{cambio}{tamano}\n" +
                        (vm.Instalado
                            ? $"Se guarda un respaldo para volver atrás. Tu carpeta {datos} no se toca."
                            : $"Se instala en {Path.GetDirectoryName(vm.Emulador.RutaExe)} en modo portable (carpeta {datos}).");
        var opciones = new List<OpcionMenu> { new(opcion, d.Archivo, () => _ = ActualizarAsync(vm)) };
        if (d.PaginaNotas != null)
            opciones.Add(new("Ver los cambios", "Abre la página de la versión en el navegador", () => Menus.Abrir(d.PaginaNotas, null)));
        opciones.Add(new("Cancelar", null, null));
        MainWindow.Actual.AbrirMenu(new MenuEmergente(titulo, subtitulo, opciones));
    }

    private async Task ActualizarAsync(EmuladorVM vm)
    {
        if (vm.Disponible is not { } version || vm.Ocupado)
            return;
        if (Actualizador.Bloqueo(vm.Emulador) is { } motivo)
        {
            MainWindow.Actual.Aviso(motivo, true);
            return;
        }

        var eraNuevo = !vm.Instalado;
        _cancelacion = new CancellationTokenSource();
        Empezar(vm, "Preparando la descarga…");
        var progreso = new Progress<ProgresoActualizacion>(p =>
        {
            vm.Etapa = p.Etapa;
            vm.Progreso = p.Fraccion;
            vm.TextoProgreso = p.Texto;
            MainWindow.Actual.FijarEstado($"{vm.Nombre}: {p.Texto}");
            if (vm.Ocupado && p.Etapa is EtapaActualizacion.Respaldando or EtapaActualizacion.Copiando or EtapaActualizacion.Restaurando)
                Estado.CopiaEnCurso = $"se están copiando los archivos de {vm.Nombre}";
        });
        var resultado = await new Actualizador().ActualizarAsync(vm.Emulador, version, progreso, _cancelacion.Token);
        _cancelacion = null;

        if (resultado.Exito && eraNuevo)
        {
            // Recién instalado (Citron, Suyu): queda activo y en modo portable.
            var emulador = Estado.Config.Emulador(vm.Emulador.Id) ?? vm.Emulador;
            emulador.Activo = true;
            emulador.CarpetaDatos = Actualizador.CarpetaPortable(emulador);
            Estado.GuardarConfig();
            _recargarBiblioteca = true;

            // Y los mandos: los elegidos en ☰ → Controles o, si nunca se eligieron ahí, los de Eden. Así lee el mando desde la primera vez.
            var todos = Estado.Config.Emuladores.ToList();
            var jugadores = Estado.Config.Jugadores.Count > 0
                ? Estado.Config.Jugadores
                : Estado.Config.Emulador(Estado.Config.OrigenControles) is { Instalado: true } origenControles && origenControles.Id != emulador.Id
                    ? Lanzador.Core.Controles.GeneradorControles.DesdeEmulador(origenControles)
                    : [];
            if (jugadores.Count > 0)
            {
                vm.TextoProgreso = "Configurando los mandos…";
                var mandos = await Task.Run(() => Lanzador.Core.Controles.GeneradorControles.Aplicar(emulador, jugadores, todos));
                resultado = resultado with
                {
                    Mensaje = resultado.Mensaje + (mandos.Exito ? " Mandos configurados." : $" No se pudieron configurar los mandos: {mandos.Mensaje}"),
                };
            }
        }
        await Terminar(vm, resultado);
    }

    private async Task VolverAnteriorAsync(EmuladorVM vm)
    {
        if (Actualizador.Bloqueo(vm.Emulador) is { } motivo)
        {
            MainWindow.Actual.Aviso(motivo, true);
            return;
        }
        Empezar(vm, "Restaurando la versión anterior…");
        Estado.CopiaEnCurso = $"se está restaurando {vm.Nombre}";
        var progreso = new Progress<ProgresoActualizacion>(p =>
        {
            vm.Progreso = p.Fraccion;
            vm.TextoProgreso = p.Texto;
        });
        var resultado = await Task.Run(() => Actualizador.VolverAnterior(vm.Emulador, progreso));
        await Terminar(vm, resultado);
    }

    private static void Empezar(EmuladorVM vm, string texto)
    {
        vm.Ocupado = true;
        vm.Etapa = null;
        vm.Resultado = null;
        vm.ResultadoError = false;
        vm.Progreso = 0;
        vm.TextoProgreso = texto;
        vm.Refrescar();
    }

    private async Task Terminar(EmuladorVM vm, ResultadoActualizacion resultado)
    {
        Estado.CopiaEnCurso = null;
        vm.Ocupado = false;
        vm.Etapa = null;
        vm.Resultado = resultado.Mensaje;
        vm.ResultadoError = !resultado.Exito;
        MainWindow.Actual.FijarEstado("");
        MainWindow.Actual.Aviso(resultado.Mensaje, !resultado.Exito);
        var emulador = vm.Emulador;
        (vm.Instalada, vm.Respaldo) = await Task.Run(() => (VersionesInstaladas.Leer(emulador), Actualizador.UltimoRespaldo(emulador.Id)));
        vm.Refrescar();
    }

    private void AbrirOpciones(EmuladorVM vm)
    {
        var opciones = new List<OpcionMenu>();
        if (vm.PuedeDescargar)
        {
            var texto = !vm.Instalado ? "Instalar" : vm.AlDia ? "Reinstalar" : "Actualizar";
            opciones.Add(new(texto, $"{vm.Disponible!.Version} · {vm.Disponible.Archivo}", () => Confirmar(vm), deshabilitada: vm.Ocupado));
        }
        if (vm.Fuente != null && vm.SoloPagina)
            opciones.Add(new("Abrir la página de descargas", vm.Fuente.PaginaDescargas, () => AbrirPagina(vm)));
        var r = vm.Respaldo;
        if (r != null || vm.PuedeDescargar)
            opciones.Add(new("Volver a la versión anterior",
                r == null ? "No hay respaldos todavía" : $"Respaldo del {r.Fecha:dd/MM/yyyy HH:mm}: vuelve a {r.VersionAnterior ?? "la versión que tenías"}",
                () => ConfirmarVolver(vm), deshabilitada: r == null || vm.Ocupado));
        if (vm.Disponible?.PaginaNotas is { } notas)
            opciones.Add(new("Ver los cambios de la versión", notas, () => Menus.Abrir(notas, null)));
        if (vm.Fuente != null && !vm.SoloPagina)
            opciones.Add(new("Abrir la página de descargas", vm.Fuente.PaginaDescargas, () => AbrirPagina(vm)));
        var carpeta = Path.GetDirectoryName(vm.Emulador.RutaExe);
        if (Directory.Exists(carpeta))
            opciones.Add(new("Abrir la carpeta del emulador", carpeta, () => Menus.Abrir("explorer.exe", $"\"{carpeta}\"")));
        if (Directory.Exists(Rutas.Respaldos) && Actualizador.Respaldos(vm.Emulador.Id).Count > 0)
            opciones.Add(new("Abrir la carpeta de respaldos", Rutas.Respaldos, () => Menus.Abrir("explorer.exe", $"\"{Rutas.Respaldos}\"")));
        MainWindow.Actual.AbrirMenu(new MenuEmergente(vm.Nombre, vm.TextoFuente, opciones));
    }

    private void ConfirmarVolver(EmuladorVM vm)
    {
        if (vm.Respaldo is not { } r)
            return;
        MainWindow.Actual.AbrirMenu(new MenuEmergente($"¿Volver a la versión anterior de {vm.Nombre}?",
            $"{vm.Instalada?.Version ?? r.VersionNueva} → {r.VersionAnterior ?? "la versión que tenías"} (respaldo del {r.Fecha:dd/MM/yyyy HH:mm}). Tus datos no se tocan.",
        [
            new OpcionMenu("Volver a la versión anterior", $"{r.Reemplazados.Count} archivos del respaldo", () => _ = VolverAnteriorAsync(vm)),
            new OpcionMenu("Cancelar", null, null),
        ]));
    }

    private static void AbrirPagina(EmuladorVM vm)
    {
        if (vm.Fuente == null)
            return;
        Menus.Abrir(vm.Fuente.PaginaDescargas, null);
        MainWindow.Actual.Aviso($"Se abrió la página de descargas de {vm.Nombre} en el navegador.");
    }

    public void PedirSalir(Action salir)
    {
        var ocupado = _emuladores.FirstOrDefault(e => e.Ocupado);
        if (ocupado == null)
        {
            MainWindow.Actual.FijarEstado("");
            salir();
            if (_recargarBiblioteca)
                _ = Catalogo.CargarAsync(false);
            return;
        }
        if (_cancelacion != null && ocupado.Etapa is null or EtapaActualizacion.Descargando or EtapaActualizacion.Verificando or EtapaActualizacion.Extrayendo)
            MainWindow.Actual.AbrirMenu(new MenuEmergente($"Se está actualizando {ocupado.Nombre}", "Todavía no se ha cambiado nada de la instalación.",
            [
                new OpcionMenu("Seguir esperando", null, null),
                new OpcionMenu("Cancelar la actualización", "Se borra lo descargado; el emulador queda como estaba", () => _cancelacion?.Cancel()),
            ]));
        else
            MainWindow.Actual.Aviso($"Espera unos segundos: se están copiando los archivos de {ocupado.Nombre}.");
    }

    private void AlPulsarEmulador(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not EmuladorVM vm)
            return;
        var indice = _emuladores.IndexOf(vm);
        if (indice == _indice)
            AccionPrincipal(vm);
        else
            Seleccionar(indice);
    }
}
