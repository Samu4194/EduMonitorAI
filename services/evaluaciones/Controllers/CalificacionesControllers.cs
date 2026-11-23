using EduMonitor.Evaluaciones.DTOs;
using EduMonitor.Evaluaciones.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace EduMonitor.Evaluaciones.Controllers;

[ApiController, EdicionAcademicaManual, Authorize, Route("api/calificaciones")]
public class CalificacionesController(CalificacionesService service) : ControllerBase
{
    // Estudiantes del paralelo del curso, sus notas por actividad y el resultado ponderado.
    [HttpGet("curso/{cursoId:int}")] public async Task<IActionResult> Matriz(int cursoId, CancellationToken ct) => Ok(await service.Matriz(cursoId, ct));
    [HttpPut("actividad/{actividadId:int}"), Authorize(Roles = "Teacher")] public async Task<IActionResult> Save(int actividadId, GuardarNotas input, CancellationToken ct) => Ok(await service.Guardar(actividadId, input, ct));
    [HttpGet("curso/{cursoId:int}/ponderaciones")] public async Task<IActionResult> Weights(int cursoId, CancellationToken ct) => Ok(await service.Ponderacion(cursoId, ct));
    [HttpPut("curso/{cursoId:int}/ponderaciones"), Authorize(Roles = "Teacher")] public async Task<IActionResult> SaveWeights(int cursoId, PonderacionRequest input, CancellationToken ct) => Ok(await service.GuardarPonderacion(cursoId, input, ct));
}

[ApiController, Authorize, Route("api/rendimiento")]
public class RendimientoController(CalificacionesService service) : ControllerBase
{
    [HttpGet("curso/{cursoId:int}")] public async Task<IActionResult> Curso(int cursoId, [FromQuery] int? unidadId, [FromQuery] int? temaId, CancellationToken ct) => Ok(await service.Rendimiento(cursoId, unidadId, temaId, ct));
    [HttpGet("curso/{cursoId:int}/estudiante/{estudianteId:int}")] public async Task<IActionResult> Estudiante(int cursoId, int estudianteId, CancellationToken ct) => Ok(await service.Estudiante(cursoId, estudianteId, ct));
}
