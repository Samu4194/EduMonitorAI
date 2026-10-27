using EduMonitor.Estudiantes.DTOs;
using EduMonitor.Estudiantes.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace EduMonitor.Estudiantes.Controllers;

[ApiController, Authorize, Route("api/matriculas")]
public class MatriculasController(EstudiantesService service) : ControllerBase
{
    [HttpGet("materia/{id:int}")] public async Task<IActionResult> Roster(int id, CancellationToken ct) => Ok(await service.Roster(id, ct));
    [HttpPost, Authorize(Roles = "Admin")]
    public async Task<IActionResult> Enroll(EnrollmentRequest input, CancellationToken ct)
    {
        await service.Enroll(input, ct);
        return NoContent();
    }
}
