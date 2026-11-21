using EduMonitor.Materias.DTOs;
using EduMonitor.Materias.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace EduMonitor.Materias.Controllers;

[ApiController, Authorize, Route("api/paralelos")]
public class ParalelosController(MateriasService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? carrera, [FromQuery] int? nivel, [FromQuery] int? periodoId, CancellationToken ct) => Ok(await service.Paralelos(carrera, nivel, periodoId, User, ct));
    [HttpPost, Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create(ParaleloRequest input, CancellationToken ct)
    {
        var x = await service.CrearParalelo(input, ct);
        return Created($"/api/paralelos/{x.Id}", x);
    }
}
