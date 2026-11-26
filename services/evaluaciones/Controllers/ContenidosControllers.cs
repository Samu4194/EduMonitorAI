using EduMonitor.Evaluaciones.DTOs;
using EduMonitor.Evaluaciones.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace EduMonitor.Evaluaciones.Controllers;

// Consultar lo puede hacer el administrador o el docente del curso. Escribir, solo el docente del curso.
// Materias valida el curso con el JWT reenviado: un curso ajeno responde 404.
[ApiController, EdicionAcademicaManual, Authorize, Route("api/unidades")]
public class UnidadesController(ContenidosService service) : ControllerBase
{
    [HttpGet] public async Task<IActionResult> List([FromQuery, System.ComponentModel.DataAnnotations.Range(1, int.MaxValue)] int cursoId, CancellationToken ct) => Ok(await service.Unidades(cursoId, ct));
    [HttpPost, Authorize(Roles = "Teacher")]
    public async Task<IActionResult> Create(UnidadCreate input, CancellationToken ct)
    {
        var x = await service.CrearUnidad(input, ct);
        return Created($"/api/unidades?cursoId={x.CursoId}", x);
    }
    [HttpPut("{id:int}"), Authorize(Roles = "Teacher")] public async Task<IActionResult> Update(int id, UnidadEdit input, CancellationToken ct) => Ok(await service.EditarUnidad(id, input, ct));
    [HttpDelete("{id:int}"), Authorize(Roles = "Teacher")] public async Task<IActionResult> Delete(int id, CancellationToken ct) { await service.EliminarUnidad(id, ct); return NoContent(); }
    [HttpPost("{id:int}/mover"), Authorize(Roles = "Teacher")] public async Task<IActionResult> Move(int id, MoverRequest input, CancellationToken ct) { await service.MoverUnidad(id, input.Direccion, ct); return NoContent(); }
    [HttpPost("{id:int}/temas"), Authorize(Roles = "Teacher")]
    public async Task<IActionResult> AddTema(int id, TemaCreate input, CancellationToken ct)
    {
        var x = await service.CrearTema(id, input, ct);
        return Created($"/api/temas/{x.Id}", x);
    }
}

[ApiController, Authorize(Roles = "Teacher"), Route("api/temas")]
public class TemasController(ContenidosService service) : ControllerBase
{
    [EdicionAcademicaManual]
    [HttpPut("{id:int}")] public async Task<IActionResult> Update(int id, TemaEdit input, CancellationToken ct) => Ok(await service.EditarTema(id, input, ct));
    [EdicionAcademicaManual]
    [HttpDelete("{id:int}")] public async Task<IActionResult> Delete(int id, CancellationToken ct) { await service.EliminarTema(id, ct); return NoContent(); }
    [EdicionAcademicaManual]
    [HttpPost("{id:int}/mover")] public async Task<IActionResult> Move(int id, MoverRequest input, CancellationToken ct) { await service.MoverTema(id, input.Direccion, ct); return NoContent(); }
    // Material de apoyo del tema: archivos ligeros (hasta 5 MB). La descarga exige ser el docente del curso.
    // Subir o borrar es edición académica: con Moodle como fuente (o con el curso vinculado) el material se administra en Moodle.
    [EdicionAcademicaManual]
    [HttpPost("{id:int}/archivos"), RequestSizeLimit(6_000_000)]
    public async Task<IActionResult> Upload(int id, IFormFile archivo, CancellationToken ct)
    {
        using var memoria = new MemoryStream();
        await archivo.CopyToAsync(memoria, ct);
        var x = await service.SubirArchivo(id, archivo.FileName, archivo.ContentType, memoria.ToArray(), ct);
        return Created($"/api/temas/archivos/{x.Id}", x);
    }
    [HttpGet("archivos/{archivoId:int}")]
    public async Task<IActionResult> Download(int archivoId, CancellationToken ct)
    {
        var (nombre, tipo, contenido) = await service.DescargarArchivo(archivoId, ct);
        return File(contenido, tipo, nombre);
    }
    [EdicionAcademicaManual]
    [HttpDelete("archivos/{archivoId:int}")] public async Task<IActionResult> DeleteFile(int archivoId, CancellationToken ct) { await service.EliminarArchivo(archivoId, ct); return NoContent(); }
    [EdicionAcademicaManual]
    [HttpPost("{id:int}/contenidos")]
    public async Task<IActionResult> AddContenido(int id, ContenidoRequest input, CancellationToken ct)
    {
        var x = await service.CrearContenido(id, input, ct);
        return Created($"/api/contenidos/{x.Id}", x);
    }
}

[ApiController, EdicionAcademicaManual, Authorize(Roles = "Teacher"), Route("api/contenidos")]
public class ContenidosController(ContenidosService service) : ControllerBase
{
    [HttpPut("{id:int}")] public async Task<IActionResult> Update(int id, ContenidoRequest input, CancellationToken ct) => Ok(await service.EditarContenido(id, input, ct));
    [HttpDelete("{id:int}")] public async Task<IActionResult> Delete(int id, CancellationToken ct) { await service.EliminarContenido(id, ct); return NoContent(); }
}

[ApiController, EdicionAcademicaManual, Authorize, Route("api/actividades")]
public class ActividadesController(ContenidosService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery, System.ComponentModel.DataAnnotations.Range(1, int.MaxValue)] int cursoId, [FromQuery] string? tipo, [FromQuery] int? temaId, CancellationToken ct) =>
        Ok(await service.Actividades(cursoId, tipo, temaId, ct));
    [HttpGet("{id:int}")] public async Task<IActionResult> Get(int id, CancellationToken ct) => Ok(await service.Actividad(id, ct));
    [HttpPost, Authorize(Roles = "Teacher")]
    public async Task<IActionResult> Create(ActividadCreate input, CancellationToken ct)
    {
        await service.ExigirTemas(input.CursoId, input.TemaIds, ct);
        var x = await service.CrearActividad(input, ct);
        return CreatedAtAction(nameof(Get), new { id = x.Id }, x);
    }
    [HttpPut("{id:int}"), Authorize(Roles = "Teacher")] public async Task<IActionResult> Update(int id, ActividadEdit input, CancellationToken ct) => Ok(await service.EditarActividad(id, input, ct));
    [HttpDelete("{id:int}"), Authorize(Roles = "Teacher")] public async Task<IActionResult> Delete(int id, CancellationToken ct) { await service.EliminarActividad(id, ct); return NoContent(); }
}
