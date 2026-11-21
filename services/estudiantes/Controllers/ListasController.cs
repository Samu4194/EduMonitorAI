using EduMonitor.Estudiantes.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace EduMonitor.Estudiantes.Controllers;

// Lista de estudiantes de un curso. La ve el administrador o el docente asignado: Materias valida el curso con el JWT reenviado.
[ApiController, Authorize, Route("api/estudiantes/curso")]
public class ListasController(EstudiantesService service) : ControllerBase
{
    [HttpGet("{id:int}")] public async Task<IActionResult> Lista(int id, CancellationToken ct) => Ok(await service.Lista(id, ct));
}
