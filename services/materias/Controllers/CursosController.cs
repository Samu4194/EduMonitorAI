using EduMonitor.Materias.DTOs;
using EduMonitor.Materias.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace EduMonitor.Materias.Controllers;

[ApiController, Authorize, Route("api/cursos")]
public class CursosController(MateriasService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? carrera, [FromQuery] int? nivel, [FromQuery] string? paralelo, [FromQuery] int? periodoId, CancellationToken ct) =>
        Ok(await service.Cursos(carrera, nivel, paralelo, periodoId, User, ct));
    [HttpGet("{id:int}")] public async Task<IActionResult> Get(int id, CancellationToken ct) => Ok(await service.Curso(id, User, ct));
    // Lo usa Usuarios al dar de baja a un docente: deja sin docente sus cursos de períodos no finalizados (idempotente).
    [HttpPost("docente/{teacherId:int}/liberar"), Authorize(Roles = "Admin")] public async Task<IActionResult> Release(int teacherId, CancellationToken ct) => Ok(await service.LiberarDocente(teacherId, ct));
    [HttpPut("{id:int}/docente"), Authorize(Roles = "Admin")] public async Task<IActionResult> Assign(int id, AssignmentRequest input, CancellationToken ct) => Ok(await service.Asignar(id, input.TeacherId, ct));
}
