namespace Lanzador.Core;

/// <summary>Iconos y fondos de cada juego desde tinfoil.media, guardados en caché.</summary>
public static class Imagenes
{
    private static readonly SemaphoreSlim Limite = new(4);

    public static string RutaIcono(string titleId) => Path.Combine(Rutas.Iconos, titleId + ".jpg");
    public static string RutaIconoGrande(string titleId) => Path.Combine(Rutas.Iconos, titleId + "-512.jpg");
    public static string RutaFondo(string titleId) => Path.Combine(Rutas.Fondos, titleId + ".jpg");

    public static Task<string?> IconoAsync(string titleId, CancellationToken ct) =>
        DescargarAsync($"https://tinfoil.media/ti/{titleId}/256/256/", RutaIcono(titleId), ct);

    /// <summary>Icono de 512 px para las carátulas grandes (con foco miden ~285 px, y en una tele 4K el doble); si no hay, el de 256.</summary>
    public static async Task<string?> IconoGrandeAsync(string titleId, CancellationToken ct) =>
        await DescargarAsync($"https://tinfoil.media/ti/{titleId}/512/512/", RutaIconoGrande(titleId), ct)
        ?? await IconoAsync(titleId, ct);

    public static Task<string?> FondoAsync(string titleId, CancellationToken ct) =>
        DescargarAsync($"https://tinfoil.media/thi/{titleId}/1280/720/", RutaFondo(titleId), ct);

    private static async Task<string?> DescargarAsync(string url, string destino, CancellationToken ct)
    {
        if (File.Exists(destino) && new FileInfo(destino).Length > 0)
            return destino;
        var marcaSinImagen = destino + ".nohay";
        if (File.Exists(marcaSinImagen) && DateTime.UtcNow - File.GetLastWriteTimeUtc(marcaSinImagen) < TimeSpan.FromDays(7))
            return null;

        await Limite.WaitAsync(ct);
        try
        {
            using var respuesta = await Http.Cliente.GetAsync(url, ct);
            var tipo = respuesta.Content.Headers.ContentType?.MediaType ?? "";
            var bytes = respuesta.IsSuccessStatusCode && tipo.StartsWith("image/") ? await respuesta.Content.ReadAsByteArrayAsync(ct) : [];
            if (bytes.Length < 512)
            {
                await File.WriteAllTextAsync(marcaSinImagen, "", ct);
                return null;
            }
            var temporal = destino + ".tmp";
            await File.WriteAllBytesAsync(temporal, bytes, ct);
            File.Move(temporal, destino, true);
            return destino;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return null;
        }
        finally
        {
            Limite.Release();
        }
    }
}
