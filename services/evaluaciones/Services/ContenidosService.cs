using EduMonitor.Evaluaciones.Data;
using EduMonitor.Evaluaciones.DTOs;
using EduMonitor.Evaluaciones.Models;
using EduMonitor.Infrastructure;
using Microsoft.EntityFrameworkCore;
namespace EduMonitor.Evaluaciones.Services;

// HU-05 (unidades, temas y contenidos mínimos) y HU-06 (actividades y evaluaciones).
public sealed class ContenidosService(EvaluacionesDb db, AccesoCursos acceso, AlmacenArchivos almacen)
{
    private static string Limpio(string texto) => texto.Trim();
    private static string? Opcional(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
    private static ContenidoView Vista(Contenido c) => new(c.Id, c.Descripcion);
    // Los archivos se listan sin su contenido (solo nombre y tamaño); el contenido viaja únicamente al descargarlo.
    private static TemaView Vista(Tema t, ILookup<int, ArchivoView>? archivos) =>
        new(t.Id, t.UnidadId, t.Nombre, t.Descripcion, t.Contenidos.OrderBy(c => c.Orden).Select(Vista).ToList(), archivos?[t.Id].OrderBy(a => a.Nombre).ToList() ?? []);
    private static TemaView Vista(Tema t) => Vista(t, null);
    private static UnidadView Vista(Unidad u, ILookup<int, ArchivoView> archivos) => new(u.Id, u.CursoId, u.Nombre, u.Descripcion, u.Temas.OrderBy(t => t.Orden).Select(t => Vista(t, archivos)).ToList());
    private static UnidadView Vista(Unidad u) => Vista(u, Enumerable.Empty<ArchivoView>().ToLookup(a => a.TemaId));
    private static ArchivoView Vista(Archivo a) => new(a.Id, a.TemaId, a.Nombre, a.Tipo, a.Tamano, a.SubidoEn);

    // ---------- Archivos de un tema ----------
    public const int MaxBytesArchivo = 5 * 1024 * 1024;
    public const int MaxArchivosPorTema = 10;
    private static readonly string[] Extensiones = [".pdf", ".doc", ".docx", ".ppt", ".pptx", ".xls", ".xlsx", ".txt", ".csv", ".png", ".jpg", ".jpeg", ".zip"];

    public async Task<ArchivoView> SubirArchivo(int temaId, string nombreOriginal, string? tipo, byte[] contenido, CancellationToken ct)
    {
        var tema = await TemaDe(temaId, ct);
        var nombre = Path.GetFileName(nombreOriginal ?? "").Trim();
        if (nombre.Length is < 3 or > 200)
            throw new ApiException(400, "El nombre del archivo debe tener entre 3 y 200 caracteres.");
        if (!Extensiones.Contains(Path.GetExtension(nombre).ToLowerInvariant()))
            throw new ApiException(400, $"Ese tipo de archivo no se admite. Usa: {string.Join(", ", Extensiones)}.");
        if (contenido.Length == 0)
            throw new ApiException(400, "El archivo está vacío.");
        if (contenido.Length > MaxBytesArchivo)
            throw new ApiException(400, "El archivo pesa más de 5 MB. Sube material ligero.");
        var actuales = await db.Archivos.Where(a => a.TemaId == tema.Id).Select(a => a.Nombre).ToListAsync(ct);
        if (actuales.Count >= MaxArchivosPorTema)
            throw new ApiException(409, $"Un tema admite hasta {MaxArchivosPorTema} archivos.");
        if (actuales.Contains(nombre, StringComparer.OrdinalIgnoreCase))
            throw new ApiException(409, $"El tema ya tiene un archivo llamado «{nombre}».");
        var archivo = new Archivo { TemaId = tema.Id, Nombre = nombre, Tipo = string.IsNullOrWhiteSpace(tipo) ? "application/octet-stream" : tipo.Trim()[..Math.Min(tipo.Trim().Length, 120)], Tamano = contenido.Length, SubidoEn = DateTime.UtcNow };
        db.Archivos.Add(archivo);
        await db.SaveChangesAsync(ct);
        try
        {
            await almacen.Guardar(archivo.Id, contenido, ct);
        }
        catch
        {
            // Sin el contenido en disco el registro no sirve: se deshace
            db.Archivos.Remove(archivo);
            await db.SaveChangesAsync(CancellationToken.None);
            throw;
        }
        return Vista(archivo);
    }
    private async Task<Archivo> ArchivoDe(int id, CancellationToken ct)
    {
        var archivo = await db.Archivos.Include(a => a.Tema).ThenInclude(t => t.Unidad).SingleOrDefaultAsync(a => a.Id == id, ct) ?? throw new ApiException(404, "El archivo no existe o no está autorizado.");
        await acceso.Curso(archivo.Tema.Unidad.CursoId, ct);
        return archivo;
    }
    public async Task<(string Nombre, string Tipo, byte[] Contenido)> DescargarArchivo(int id, CancellationToken ct)
    {
        var a = await ArchivoDe(id, ct);
        var contenido = await almacen.Leer(a.Id, ct) ?? throw new ApiException(404, "El contenido del archivo ya no está disponible. Vuelve a subirlo.");
        return (a.Nombre, a.Tipo, contenido);
    }
    public async Task EliminarArchivo(int id, CancellationToken ct)
    {
        var archivo = await ArchivoDe(id, ct);
        db.Archivos.Remove(archivo);
        await db.SaveChangesAsync(ct);
        almacen.Borrar([archivo.Id]);
    }

    // ---------- Unidades ----------
    public async Task<List<UnidadView>> Unidades(int cursoId, CancellationToken ct)
    {
        await acceso.Curso(cursoId, ct);
        var unidades = await db.Unidades.AsNoTracking().Where(u => u.CursoId == cursoId).Include(u => u.Temas).ThenInclude(t => t.Contenidos).OrderBy(u => u.Orden).ToListAsync(ct);
        var archivos = (await db.Archivos.AsNoTracking().Where(a => a.Tema.Unidad.CursoId == cursoId).Select(a => new ArchivoView(a.Id, a.TemaId, a.Nombre, a.Tipo, a.Tamano, a.SubidoEn)).ToListAsync(ct)).ToLookup(a => a.TemaId);
        return unidades.Select(u => Vista(u, archivos)).ToList();
    }
    private async Task<Unidad> UnidadDe(int id, CancellationToken ct)
    {
        var unidad = await db.Unidades.SingleOrDefaultAsync(u => u.Id == id, ct) ?? throw new ApiException(404, "La unidad no existe o no está autorizada.");
        await acceso.Curso(unidad.CursoId, ct);
        return unidad;
    }
    public async Task<UnidadView> CrearUnidad(UnidadCreate input, CancellationToken ct)
    {
        await acceso.Curso(input.CursoId, ct);
        var nombre = Limpio(input.Nombre);
        if (await db.Unidades.AnyAsync(u => u.CursoId == input.CursoId && u.Nombre == nombre, ct))
            throw new ApiException(409, "Ya existe una unidad con ese nombre en este curso.");
        var orden = (await db.Unidades.Where(u => u.CursoId == input.CursoId).MaxAsync(u => (int?)u.Orden, ct) ?? 0) + 1;
        var unidad = new Unidad { CursoId = input.CursoId, Nombre = nombre, Descripcion = Opcional(input.Descripcion), Orden = orden };
        db.Unidades.Add(unidad);
        await db.SaveChangesAsync(ct);
        return Vista(unidad);
    }
    public async Task<UnidadView> EditarUnidad(int id, UnidadEdit input, CancellationToken ct)
    {
        var unidad = await UnidadDe(id, ct);
        var nombre = Limpio(input.Nombre);
        if (await db.Unidades.AnyAsync(u => u.CursoId == unidad.CursoId && u.Nombre == nombre && u.Id != id, ct))
            throw new ApiException(409, "Ya existe una unidad con ese nombre en este curso.");
        unidad.Nombre = nombre;
        unidad.Descripcion = Opcional(input.Descripcion);
        await db.SaveChangesAsync(ct);
        return Vista(await db.Unidades.AsNoTracking().Include(u => u.Temas).ThenInclude(t => t.Contenidos).SingleAsync(u => u.Id == id, ct));
    }
    public async Task EliminarUnidad(int id, CancellationToken ct)
    {
        var unidad = await UnidadDe(id, ct);
        if (await db.ActividadTemas.AnyAsync(x => x.Tema.UnidadId == id, ct))
            throw new ApiException(409, "La unidad tiene temas usados por actividades. Quita esos temas de las actividades antes de eliminarla.");
        // Los registros de archivos se van en cascada; su contenido en disco se borra aparte
        var archivos = await db.Archivos.Where(a => a.Tema.UnidadId == id).Select(a => a.Id).ToListAsync(ct);
        db.Unidades.Remove(unidad);
        await db.SaveChangesAsync(ct);
        almacen.Borrar(archivos);
    }
    public async Task MoverUnidad(int id, int direccion, CancellationToken ct)
    {
        var unidad = await UnidadDe(id, ct);
        var hermanas = await db.Unidades.Where(u => u.CursoId == unidad.CursoId).OrderBy(u => u.Orden).ThenBy(u => u.Id).ToListAsync(ct);
        var ids = hermanas.Select(u => u.Id).ToList();
        var nuevo = Reordenar(ids, id, direccion);
        foreach (var u in hermanas)
            u.Orden = nuevo.IndexOf(u.Id) + 1;
        await db.SaveChangesAsync(ct);
    }
    // Devuelve los ids con el elemento movido un lugar. Siempre reasigna 1..n, así no quedan órdenes repetidos.
    private static List<int> Reordenar(List<int> ids, int id, int direccion)
    {
        var lista = ids.ToList();
        var i = lista.IndexOf(id);
        var j = i + direccion;
        if (i >= 0 && j >= 0 && j < lista.Count && direccion != 0)
            (lista[i], lista[j]) = (lista[j], lista[i]);
        return lista;
    }

    // ---------- Temas ----------
    private async Task<Tema> TemaDe(int id, CancellationToken ct)
    {
        var tema = await db.Temas.Include(t => t.Unidad).Include(t => t.Contenidos).SingleOrDefaultAsync(t => t.Id == id, ct) ?? throw new ApiException(404, "El tema no existe o no está autorizado.");
        await acceso.Curso(tema.Unidad.CursoId, ct);
        return tema;
    }
    private static void ValidarContenidos(IEnumerable<string>? contenidos)
    {
        foreach (var c in contenidos ?? [])
            if (c is null || c.Trim().Length is < 3 or > 300)
                throw new ApiException(400, "Cada contenido mínimo debe tener entre 3 y 300 caracteres.");
    }
    public async Task<TemaView> CrearTema(int unidadId, TemaCreate input, CancellationToken ct)
    {
        var unidad = await UnidadDe(unidadId, ct);
        ValidarContenidos(input.Contenidos);
        var nombre = Limpio(input.Nombre);
        if (await db.Temas.AnyAsync(t => t.UnidadId == unidadId && t.Nombre == nombre, ct))
            throw new ApiException(409, "Ya existe un tema con ese nombre en esta unidad.");
        var orden = (await db.Temas.Where(t => t.UnidadId == unidadId).MaxAsync(t => (int?)t.Orden, ct) ?? 0) + 1;
        var tema = new Tema { UnidadId = unidad.Id, Nombre = nombre, Descripcion = Opcional(input.Descripcion), Orden = orden };
        var n = 0;
        foreach (var c in input.Contenidos ?? [])
            tema.Contenidos.Add(new Contenido { Descripcion = Limpio(c), Orden = ++n });
        db.Temas.Add(tema);
        await db.SaveChangesAsync(ct);
        return Vista(tema);
    }
    public async Task<TemaView> EditarTema(int id, TemaEdit input, CancellationToken ct)
    {
        var tema = await TemaDe(id, ct);
        var nombre = Limpio(input.Nombre);
        if (await db.Temas.AnyAsync(t => t.UnidadId == tema.UnidadId && t.Nombre == nombre && t.Id != id, ct))
            throw new ApiException(409, "Ya existe un tema con ese nombre en esta unidad.");
        tema.Nombre = nombre;
        tema.Descripcion = Opcional(input.Descripcion);
        await db.SaveChangesAsync(ct);
        return Vista(tema);
    }
    public async Task EliminarTema(int id, CancellationToken ct)
    {
        var tema = await TemaDe(id, ct);
        if (await db.ActividadTemas.AnyAsync(x => x.TemaId == id, ct))
            throw new ApiException(409, "El tema está relacionado con actividades. Quítalo de ellas antes de eliminarlo.");
        var archivos = await db.Archivos.Where(a => a.TemaId == id).Select(a => a.Id).ToListAsync(ct);
        db.Temas.Remove(tema);
        await db.SaveChangesAsync(ct);
        almacen.Borrar(archivos);
    }
    public async Task MoverTema(int id, int direccion, CancellationToken ct)
    {
        var tema = await TemaDe(id, ct);
        var hermanos = await db.Temas.Where(t => t.UnidadId == tema.UnidadId).OrderBy(t => t.Orden).ThenBy(t => t.Id).ToListAsync(ct);
        var nuevo = Reordenar(hermanos.Select(t => t.Id).ToList(), id, direccion);
        foreach (var t in hermanos)
            t.Orden = nuevo.IndexOf(t.Id) + 1;
        await db.SaveChangesAsync(ct);
    }

    // ---------- Contenidos mínimos ----------
    public async Task<ContenidoView> CrearContenido(int temaId, ContenidoRequest input, CancellationToken ct)
    {
        var tema = await TemaDe(temaId, ct);
        if (tema.Contenidos.Count >= 30)
            throw new ApiException(409, "Un tema admite hasta 30 contenidos mínimos.");
        var contenido = new Contenido { TemaId = temaId, Descripcion = Limpio(input.Descripcion), Orden = (tema.Contenidos.Count == 0 ? 0 : tema.Contenidos.Max(c => c.Orden)) + 1 };
        db.Contenidos.Add(contenido);
        await db.SaveChangesAsync(ct);
        return Vista(contenido);
    }
    private async Task<Contenido> ContenidoDe(int id, CancellationToken ct)
    {
        var contenido = await db.Contenidos.SingleOrDefaultAsync(c => c.Id == id, ct) ?? throw new ApiException(404, "El contenido no existe o no está autorizado.");
        await TemaDe(contenido.TemaId, ct);
        return contenido;
    }
    public async Task<ContenidoView> EditarContenido(int id, ContenidoRequest input, CancellationToken ct)
    {
        var contenido = await ContenidoDe(id, ct);
        contenido.Descripcion = Limpio(input.Descripcion);
        await db.SaveChangesAsync(ct);
        return Vista(contenido);
    }
    public async Task EliminarContenido(int id, CancellationToken ct)
    {
        db.Contenidos.Remove(await ContenidoDe(id, ct));
        await db.SaveChangesAsync(ct);
    }

    // ---------- Actividades (HU-06) ----------
    private IQueryable<Actividad> ConTemas() => db.Actividades.Include(a => a.Temas).ThenInclude(x => x.Tema).ThenInclude(t => t.Unidad);
    private async Task<Dictionary<int, int>> NotasPorActividad(IEnumerable<int> ids, CancellationToken ct)
    {
        var lista = ids.ToList();
        return await db.Calificaciones.Where(c => lista.Contains(c.ActividadId)).GroupBy(c => c.ActividadId).ToDictionaryAsync(g => g.Key, g => g.Count(), ct);
    }
    private static ActividadView Vista(Actividad a, int calificadas) => new(a.Id, a.CursoId, a.Nombre, a.Tipo, a.Descripcion, a.FechaEntrega,
        a.Temas.Select(x => new TemaRef(x.Tema.Id, x.Tema.Nombre, x.Tema.UnidadId, x.Tema.Unidad.Nombre)).OrderBy(t => t.UnidadId).ThenBy(t => t.Id).ToList(), calificadas);

    public async Task<List<ActividadView>> Actividades(int cursoId, string? tipo, int? temaId, CancellationToken ct)
    {
        await acceso.Curso(cursoId, ct);
        var query = ConTemas().AsNoTracking().Where(a => a.CursoId == cursoId);
        if (!string.IsNullOrWhiteSpace(tipo))
        {
            var elegido = TipoActividad.Todos.FirstOrDefault(t => t.Equals(tipo.Trim(), StringComparison.OrdinalIgnoreCase)) ?? throw new ApiException(400, "El tipo debe ser Tarea, APE o Examen.");
            query = query.Where(a => a.Tipo == elegido);
        }
        if (temaId.HasValue)
            query = query.Where(a => a.Temas.Any(x => x.TemaId == temaId));
        var actividades = await query.OrderBy(a => a.FechaEntrega == null).ThenBy(a => a.FechaEntrega).ThenBy(a => a.Id).ToListAsync(ct);
        var notas = await NotasPorActividad(actividades.Select(a => a.Id), ct);
        return actividades.Select(a => Vista(a, notas.GetValueOrDefault(a.Id))).ToList();
    }
    public async Task<ActividadView> Actividad(int id, CancellationToken ct)
    {
        var actividad = await ActividadDe(id, ct, false);
        return Vista(actividad, (await NotasPorActividad([id], ct)).GetValueOrDefault(id));
    }
    private async Task<Actividad> ActividadDe(int id, CancellationToken ct, bool tracking = true)
    {
        var query = tracking ? ConTemas() : ConTemas().AsNoTracking();
        var actividad = await query.SingleOrDefaultAsync(a => a.Id == id, ct) ?? throw new ApiException(404, "La actividad no existe o no está autorizada.");
        await acceso.Curso(actividad.CursoId, ct);
        return actividad;
    }
    // Los temas deben ser de unidades del mismo curso: así una actividad nunca se relaciona con contenidos de otro grupo.
    private async Task<List<int>> TemasDelCurso(int cursoId, List<int> temaIds, CancellationToken ct)
    {
        var ids = temaIds.Distinct().ToList();
        var validos = await db.Temas.Where(t => ids.Contains(t.Id) && t.Unidad.CursoId == cursoId).Select(t => t.Id).ToListAsync(ct);
        if (validos.Count != ids.Count)
            throw new ApiException(400, "Algún tema elegido no pertenece a este curso.");
        return ids;
    }
    // Una actividad creada a mano debe quedar relacionada con al menos un tema: el seguimiento los usa para decir qué reforzar.
    // Si el curso todavía no tiene temas se permite sin ellos. La sincronización con Moodle no pasa por aquí: ella decide los temas sola.
    public async Task ExigirTemas(int cursoId, List<int>? temaIds, CancellationToken ct)
    {
        await acceso.Curso(cursoId, ct);
        if (temaIds is { Count: > 0 })
            return;
        if (await db.Temas.AnyAsync(t => t.Unidad.CursoId == cursoId, ct))
            throw new ApiException(400, "Elige al menos un tema para la actividad. Los temas se crean en la pestaña Contenido.");
    }
    public async Task<ActividadView> CrearActividad(ActividadCreate input, CancellationToken ct)
    {
        await acceso.Curso(input.CursoId, ct);
        var temas = await TemasDelCurso(input.CursoId, input.TemaIds ?? [], ct);
        var nombre = Limpio(input.Nombre);
        if (await db.Actividades.AnyAsync(a => a.CursoId == input.CursoId && a.Tipo == input.Tipo && a.Nombre == nombre, ct))
            throw new ApiException(409, "Ya existe una actividad de ese tipo con ese nombre en este curso.");
        var actividad = new Actividad { CursoId = input.CursoId, Nombre = nombre, Tipo = input.Tipo, Descripcion = Opcional(input.Descripcion), FechaEntrega = input.FechaEntrega };
        foreach (var t in temas)
            actividad.Temas.Add(new ActividadTema { TemaId = t });
        db.Actividades.Add(actividad);
        await db.SaveChangesAsync(ct);
        return await Actividad(actividad.Id, ct);
    }
    public async Task<ActividadView> EditarActividad(int id, ActividadEdit input, CancellationToken ct)
    {
        var actividad = await ActividadDe(id, ct);
        // Sin lista de temas (null) se conservan los que ya tiene: editar el nombre o la fecha no debe desvincularla de sus temas.
        var temas = input.TemaIds is null ? actividad.Temas.Select(x => x.TemaId).ToList() : await TemasDelCurso(actividad.CursoId, input.TemaIds, ct);
        var nombre = Limpio(input.Nombre);
        if (await db.Actividades.AnyAsync(a => a.CursoId == actividad.CursoId && a.Tipo == input.Tipo && a.Nombre == nombre && a.Id != id, ct))
            throw new ApiException(409, "Ya existe una actividad de ese tipo con ese nombre en este curso.");
        actividad.Nombre = nombre;
        actividad.Tipo = input.Tipo;
        actividad.Descripcion = Opcional(input.Descripcion);
        actividad.FechaEntrega = input.FechaEntrega;
        db.ActividadTemas.RemoveRange(actividad.Temas.Where(x => !temas.Contains(x.TemaId)));
        foreach (var t in temas.Where(t => actividad.Temas.All(x => x.TemaId != t)))
            actividad.Temas.Add(new ActividadTema { TemaId = t });
        await db.SaveChangesAsync(ct);
        return await Actividad(id, ct);
    }
    public async Task EliminarActividad(int id, CancellationToken ct)
    {
        db.Actividades.Remove(await ActividadDe(id, ct));
        await db.SaveChangesAsync(ct);
    }
}
