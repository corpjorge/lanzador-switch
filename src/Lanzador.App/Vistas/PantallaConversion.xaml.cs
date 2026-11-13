using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Lanzador.App.Audio;
using Lanzador.App.Entrada;
using Lanzador.App.Modelos;
using Lanzador.Core.Conversion;
using Microsoft.VisualBasic.FileIO;

namespace Lanzador.App.Vistas;

/// <summary>
/// Los juegos comprimidos (.nsz) de las carpetas de juegos: cuáles se pueden convertir a .nsp, la conversión con su
/// verificación y, solo cuando el .nsp quedó verificado, la opción de enviar el .nsz original a la papelera.
/// </summary>
public partial class PantallaConversion : UserControl, IPantalla
{
    private List<ConversionVM> _archivos = new();
    private CancellationTokenSource? _cancelacion;
    private int _indice;
    private bool _trabajando;
    private bool _buscando;
    private bool _cambios;

    private readonly string? _rutaInicial;

    /// <param name="rutaInicial">El .nsz que debe quedar elegido al abrir (p. ej. el del juego desde el que se llegó).</param>
    public PantallaConversion(string? rutaInicial = null)
    {
        _rutaInicial = rutaInicial;
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            if (_archivos.Count == 0 && !_buscando)
                await BuscarAsync();
        };
    }

    public IReadOnlyList<Ayuda> Ayudas { get; } =
    [
        new("A", "Convertir / verificar", "Enter"),
        new("X", "Convertir todos", "T"),
        new("Y", "Opciones", "O"),
        new("B", "Volver", "Esc"),
    ];

    private ConversionVM? Actual => _indice >= 0 && _indice < _archivos.Count ? _archivos[_indice] : null;

    public void Ejecutar(Accion accion)
    {
        switch (accion)
        {
            case Accion.Arriba: { var antes = _indice; Seleccionar(_indice - 1); Sonidos.Moverse(antes != _indice); break; }
            case Accion.Abajo: { var antes = _indice; Seleccionar(_indice + 1); Sonidos.Moverse(antes != _indice); break; }
            case Accion.Aceptar: if (Actual != null) AccionPrincipal(Actual); break;
            case Accion.Trucos: ConfirmarTodos(); break;
            case Accion.Opciones: if (Actual != null) AbrirOpciones(Actual); break;
            case Accion.Atras: PedirSalir(MainWindow.Actual.Volver); break;
        }
    }

    private void Seleccionar(int indice)
    {
        if (_archivos.Count == 0)
            return;
        indice = Math.Clamp(indice, 0, _archivos.Count - 1);
        foreach (var a in _archivos)
            a.Seleccionado = false;
        _indice = indice;
        _archivos[indice].Seleccionado = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (ListaArchivos.ItemContainerGenerator.ContainerFromIndex(_indice) is FrameworkElement fila)
                fila.BringIntoView(new Rect(0, -30, fila.ActualWidth, fila.ActualHeight + 60));
        });
    }

    private async Task BuscarAsync()
    {
        _buscando = true;
        TextoResumen.Text = "Buscando juegos comprimidos en tus carpetas de juegos…";
        var carpetas = Estado.Config.CarpetasEscaneo;
        List<ArchivoComprimido> encontrados;
        try
        {
            encontrados = await Task.Run(() => ConversorNsz.Buscar(carpetas));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            encontrados = new();
            MainWindow.Actual.Aviso($"No se pudieron leer las carpetas de juegos: {ex.Message}", true);
        }
        _buscando = false;
        // Primero lo que se puede convertir, después lo ya convertido y al final lo que no.
        _archivos = encontrados.OrderBy(a => a.Estado).Select(a => new ConversionVM(a)).ToList();
        ListaArchivos.ItemsSource = _archivos;
        TextoVacio.Text = carpetas.Count == 0
            ? "No hay carpetas de juegos configuradas. Agrégalas en ☰ → Configuración."
            : "No hay juegos .nsz en tus carpetas de juegos: no hay nada que convertir.";
        TextoVacio.Visibility = _archivos.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Seleccionar(Math.Max(0, _archivos.FindIndex(a => a.Archivo.Ruta.Equals(_rutaInicial, StringComparison.OrdinalIgnoreCase))));
        ActualizarResumen();
    }

    private void ActualizarResumen()
    {
        if (_archivos.Count == 0)
        {
            TextoResumen.Text = "Ningún juego comprimido";
            return;
        }
        var partes = new List<string>();
        var convertibles = _archivos.Where(a => a.Convertible).ToList();
        if (convertibles.Count > 0)
            partes.Add($"{Cuantos(convertibles.Count, "se puede convertir", "se pueden convertir")} (ocuparán {ConversorNsz.Tamano(convertibles.Sum(a => a.Archivo.TamanoDestino ?? 0))})");
        if (_archivos.Count(a => a.PorVerificar) is > 0 and var porVerificar)
            partes.Add(Cuantos(porVerificar, "ya tiene su NSP, sin verificar", "ya tienen su NSP, sin verificar"));
        if (_archivos.Count(a => a.PuedeBorrar) is > 0 and var listos)
            partes.Add(Cuantos(listos, "convertido y verificado", "convertidos y verificados"));
        if (_archivos.Count(a => a.Estado is EstadoComprimido.NoCompatible or EstadoComprimido.Danado) is > 0 and var otros)
            partes.Add(Cuantos(otros, "no se puede convertir", "no se pueden convertir"));
        if (_archivos.Count(a => a.Borrado) is > 0 and var borrados)
            partes.Add(Cuantos(borrados, "original borrado", "originales borrados"));
        foreach (var raiz in convertibles.Select(a => Path.GetPathRoot(a.Archivo.Ruta)).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                partes.Add($"{ConversorNsz.Tamano(new DriveInfo(raiz).AvailableFreeSpace)} libres en {raiz}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
        }
        TextoResumen.Text = string.Join("  ·  ", partes);
    }

    private static string Cuantos(int n, string uno, string varios) => $"{n} {(n == 1 ? uno : varios)}";

    private void AccionPrincipal(ConversionVM vm)
    {
        if (_trabajando)
            MainWindow.Actual.Aviso("Espera a que termine la conversión en curso (B para cancelarla).");
        else if (vm.Borrado)
            MainWindow.Actual.Aviso("El .nsz original ya está en la papelera; el .nsp quedó en su lugar.");
        else if (vm.Verificado)
            ConfirmarBorrar([vm]);
        else if (vm.Convertible)
            _ = ProcesarAsync([vm]);
        else if (vm.PorVerificar)
            _ = ProcesarAsync([vm]);
        else
            MainWindow.Actual.Aviso(vm.Archivo.Detalle, vm.Estado == EstadoComprimido.Danado);
    }

    private void ConfirmarTodos()
    {
        if (_trabajando)
        {
            MainWindow.Actual.Aviso("Ya hay una conversión en curso.");
            return;
        }
        var pendientes = _archivos.Where(a => a.Convertible).ToList();
        if (pendientes.Count == 0)
        {
            var listos = _archivos.Where(a => a.PuedeBorrar).ToList();
            if (listos.Count > 0)
                ConfirmarBorrar(listos);
            else
                MainWindow.Actual.Aviso("No queda ningún .nsz por convertir.");
            return;
        }
        var total = pendientes.Sum(a => a.Archivo.TamanoDestino ?? 0);
        MainWindow.Actual.AbrirMenu(new MenuEmergente($"Convertir {Cuantos(pendientes.Count, "juego", "juegos")} a NSP",
            $"Se crean los .nsp junto a cada .nsz (unos {ConversorNsz.Tamano(total)} en total) y se verifica cada uno. Los .nsz no se tocan; al terminar podrás decidir si los borras.",
        [
            new OpcionMenu("Convertir todos ahora", "Uno tras otro; puedes cancelar con B", () => _ = ProcesarAsync(pendientes)),
            new OpcionMenu("Cancelar", null, null),
        ]));
    }

    /// <summary>Convierte (o verifica, si el .nsp ya existía) los archivos uno tras otro y al final ofrece borrar los originales verificados.</summary>
    private async Task ProcesarAsync(List<ConversionVM> lista)
    {
        if (_trabajando || lista.Count == 0)
            return;
        _trabajando = true;
        _cancelacion = new CancellationTokenSource();
        var ct = _cancelacion.Token;
        foreach (var vm in lista)
        {
            vm.EnCola = true;
            vm.Resultado = null;
            vm.ResultadoError = false;
            vm.Refrescar();
        }
        var verificados = new List<ConversionVM>();
        var contenido = new List<string>();
        var errores = 0;
        try
        {
            foreach (var vm in lista)
            {
                if (ct.IsCancellationRequested)
                    break;
                var indice = _archivos.IndexOf(vm);
                if (lista.Count > 1 && indice >= 0)
                    Seleccionar(indice);
                var soloVerificar = vm.PorVerificar;
                vm.EnCola = false;
                vm.Ocupado = true;
                vm.Progreso = 0;
                vm.TextoProgreso = soloVerificar ? "Verificando el .nsp que ya existe…" : "Preparando…";
                vm.Refrescar();
                Estado.CopiaEnCurso = $"se está {(soloVerificar ? "verificando" : "convirtiendo")} {vm.Nombre}";
                MainWindow.Actual.FijarEstado($"{(soloVerificar ? "Verificando" : "Convirtiendo")} {vm.Nombre}…");

                var archivo = vm.Archivo;
                var progreso = new Progress<ProgresoConversion>(p =>
                {
                    vm.Progreso = p.Fraccion;
                    vm.TextoProgreso = p.Texto;
                });
                var resultado = await Task.Run(() => soloVerificar
                    ? ConversorNsz.Verificar(archivo.Destino, archivo.Ruta, progreso, ct)
                    : ConversorNsz.Convertir(archivo.Ruta, progreso, ct));

                vm.Ocupado = false;
                vm.Resultado = resultado.Mensaje;
                vm.ResultadoError = !resultado.Exito;
                vm.Verificado = resultado is { Exito: true, Verificado: true };
                if (resultado.Exito && !soloVerificar)
                {
                    _cambios = true;
                    vm.Archivo = await Task.Run(() => ConversorNsz.Examinar(archivo.Ruta));
                    // Una actualización o un DLC: su carpeta tiene que estar dada de alta en los emuladores.
                    if (vm.EsContenido && Path.GetDirectoryName(archivo.Destino) is { } carpeta)
                        contenido.Add(carpeta);
                }
                if (vm.Verificado)
                    verificados.Add(vm);
                else if (!ct.IsCancellationRequested)
                    errores++;
                vm.Refrescar();
            }
        }
        finally
        {
            foreach (var vm in lista)
            {
                vm.EnCola = false;
                vm.Ocupado = false;
                vm.Refrescar();
            }
            Estado.CopiaEnCurso = null;
            MainWindow.Actual.FijarEstado("");
            _cancelacion.Dispose();
            _cancelacion = null;
            _trabajando = false;
            ActualizarResumen();
        }

        if (contenido.Count > 0)
        {
            var nota = await MainWindow.Actual.AsegurarContenidoAsync(contenido);
            foreach (var vm in lista.Where(v => v.EsContenido && v.Verificado))
            {
                vm.Resultado += " " + nota;
                vm.Refrescar();
            }
        }
        if (verificados.Count > 0)
            ConfirmarBorrar(verificados, errores);
        else if (ct.IsCancellationRequested)
            MainWindow.Actual.Aviso("Conversión cancelada. No quedó ningún archivo a medias.");
        else
            MainWindow.Actual.Aviso(lista.Count == 1 ? lista[0].Resultado ?? "No se pudo convertir." : $"No se pudo convertir ninguno de los {lista.Count}. Mira el motivo en cada fila.", true);
    }

    /// <summary>Solo para archivos cuyo .nsp ya se verificó: pregunta si se envían los .nsz originales a la papelera.</summary>
    private void ConfirmarBorrar(List<ConversionVM> lista, int errores = 0)
    {
        lista = lista.Where(a => a.PuedeBorrar).ToList();
        if (lista.Count == 0)
            return;
        var espacio = ConversorNsz.Tamano(lista.Sum(a => a.Archivo.Tamano));
        var uno = lista.Count == 1;
        var titulo = uno ? "Convertido y verificado" : $"{lista.Count} juegos convertidos y verificados";
        var subtitulo = (uno
                            ? $"{Path.GetFileName(lista[0].Archivo.Destino)} está completo: se comprobó el sha256 de todo su contenido.\n¿Qué haces con el .nsz original ({espacio})?"
                            : $"Los .nsp están completos: se comprobó el sha256 de todo su contenido.\n¿Qué haces con los .nsz originales ({espacio})?")
                        + (errores > 0 ? $"\n{Cuantos(errores, "archivo no se pudo convertir", "archivos no se pudieron convertir")}: su .nsz se conserva." : "");
        Sonidos.Tocar(Sonido.Aplicado);
        MainWindow.Actual.AbrirMenu(new MenuEmergente(titulo, subtitulo,
        [
            new OpcionMenu(uno ? "Conservar el .nsz" : "Conservar los .nsz", "Puedes borrarlo después desde aquí (A sobre el juego)", null),
            new OpcionMenu(uno ? "Enviar el .nsz a la papelera" : $"Enviar los {lista.Count} .nsz a la papelera", $"Libera {espacio}; el .nsp se queda", () => Borrar(lista)),
        ]));
    }

    private void Borrar(List<ConversionVM> lista)
    {
        var borrados = 0;
        long liberado = 0;
        string? error = null;
        foreach (var vm in lista.Where(a => a.PuedeBorrar))
        {
            try
            {
                // Última comprobación: el .nsp verificado sigue ahí.
                if (!File.Exists(vm.Archivo.Destino))
                {
                    vm.Verificado = false;
                    error = $"{Path.GetFileName(vm.Archivo.Destino)} ya no está: el .nsz se conserva.";
                    continue;
                }
                if (File.Exists(vm.Archivo.Ruta))
                    FileSystem.DeleteFile(vm.Archivo.Ruta, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                vm.Borrado = true;
                vm.Resultado = "El .nsz original se envió a la papelera; el .nsp quedó en su lugar.";
                borrados++;
                liberado += vm.Archivo.Tamano;
                _cambios = true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
            {
                error = $"No se pudo borrar {Path.GetFileName(vm.Archivo.Ruta)}: {ex.Message}";
            }
            vm.Refrescar();
        }
        ActualizarResumen();
        if (error != null)
            MainWindow.Actual.Aviso(error, true);
        else if (borrados > 0)
            MainWindow.Actual.Aviso($"Listo: {Cuantos(borrados, ".nsz enviado", ".nsz enviados")} a la papelera ({ConversorNsz.Tamano(liberado)}).");
    }

    private void AbrirOpciones(ConversionVM vm)
    {
        var opciones = new List<OpcionMenu>();
        if (vm.Convertible)
            opciones.Add(new("Convertir a NSP", vm.TextoTamano, () => _ = ProcesarAsync([vm]), deshabilitada: _trabajando));
        if (vm.PorVerificar)
            opciones.Add(new("Verificar el NSP que ya existe", "Comprueba el sha256 de su contenido y que sea la conversión de este .nsz", () => _ = ProcesarAsync([vm]), deshabilitada: _trabajando));
        if (!vm.Borrado && vm.Estado is EstadoComprimido.Convertible or EstadoComprimido.YaConvertido)
            opciones.Add(new("Enviar el .nsz a la papelera", vm.Verificado ? $"Libera {ConversorNsz.Tamano(vm.Archivo.Tamano)}; el .nsp ya está verificado" : "Primero hay que convertirlo y verificarlo",
                () => ConfirmarBorrar([vm]), deshabilitada: !vm.PuedeBorrar || _trabajando));
        opciones.Add(new("Convertir todos los que faltan", null, ConfirmarTodos, deshabilitada: _trabajando || !_archivos.Any(a => a.Convertible)));
        var carpeta = vm.Carpeta;
        if (Directory.Exists(carpeta))
            opciones.Add(new("Abrir la carpeta", carpeta, () => Menus.Abrir("explorer.exe", File.Exists(vm.Archivo.Ruta) ? $"/select,\"{vm.Archivo.Ruta}\"" : $"\"{carpeta}\"")));
        opciones.Add(new("Volver a buscar", "Lee otra vez las carpetas de juegos", () => _ = BuscarAsync(), deshabilitada: _trabajando));
        MainWindow.Actual.AbrirMenu(new MenuEmergente(vm.Nombre, vm.Detalle, opciones));
    }

    public void PedirSalir(Action salir)
    {
        if (!_trabajando)
        {
            salir();
            if (_cambios)
                _ = Catalogo.CargarAsync(false); // la biblioteca pasa a usar los .nsp
            return;
        }
        MainWindow.Actual.AbrirMenu(new MenuEmergente("Hay una conversión en curso", "Si la cancelas se borra el .nsp a medias; el .nsz queda como estaba.",
        [
            new OpcionMenu("Seguir esperando", null, null),
            new OpcionMenu("Cancelar la conversión", null, () => _cancelacion?.Cancel()),
        ]));
    }

    private void AlPulsarArchivo(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ConversionVM vm)
            return;
        var indice = _archivos.IndexOf(vm);
        if (indice == _indice)
            AccionPrincipal(vm);
        else
            Seleccionar(indice);
    }
}
