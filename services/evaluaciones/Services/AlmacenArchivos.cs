namespace EduMonitor.Evaluaciones.Services;

// Contenido de los archivos de los temas. Se guarda en disco (un volumen del servicio) y no en la base:
// escribir binarios de varios megas en SQL Server resultó demasiado lento. Cada archivo se llama como su id.
public sealed class AlmacenArchivos(IConfiguration config)
{
    private readonly string carpeta = config["Archivos:Ruta"] is { Length: > 0 } ruta ? ruta : Path.Combine(AppContext.BaseDirectory, "archivos");
    private string Ruta(int id) => Path.Combine(carpeta, $"{id}.bin");

    public async Task Guardar(int id, byte[] contenido, CancellationToken ct)
    {
        Directory.CreateDirectory(carpeta);
        await File.WriteAllBytesAsync(Ruta(id), contenido, ct);
    }
    public async Task<byte[]?> Leer(int id, CancellationToken ct) => File.Exists(Ruta(id)) ? await File.ReadAllBytesAsync(Ruta(id), ct) : null;
    public void Borrar(IEnumerable<int> ids)
    {
        foreach (var id in ids)
            if (File.Exists(Ruta(id)))
                File.Delete(Ruta(id));
    }
}
