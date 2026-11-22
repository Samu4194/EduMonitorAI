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
    [HttpPut("{id:int}/docente"), Authorize(Roles = "Admin")] public async Task<IActionResult> Assign(int id, AssignmentRequest input, CancellationToken ct) => Ok(await service.Asignar(id, input.TeacherId, ct));
}
