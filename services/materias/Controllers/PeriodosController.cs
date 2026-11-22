using EduMonitor.Materias.DTOs;
using EduMonitor.Materias.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace EduMonitor.Materias.Controllers;

[ApiController, Authorize, Route("api/periodos")]
public class PeriodosController(MateriasService service) : ControllerBase
{
    [HttpGet] public async Task<IActionResult> List(CancellationToken ct) => Ok(await service.Periodos(ct));
    [HttpPost, Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create(PeriodoRequest input, CancellationToken ct)
    {
        var x = await service.CrearPeriodo(input, ct);
        return Created($"/api/periodos/{x.Id}", x);
    }
    [HttpPut("{id:int}/activar"), Authorize(Roles = "Admin")] public async Task<IActionResult> Activate(int id, CancellationToken ct) => Ok(await service.ActivarPeriodo(id, ct));
    // Iniciar el período bloquea la carga de mallas; finalizarlo la libera.
    [HttpPut("{id:int}/iniciar"), Authorize(Roles = "Admin")] public async Task<IActionResult> Start(int id, CancellationToken ct) => Ok(await service.CambiarEstadoPeriodo(id, true, ct));
    [HttpPut("{id:int}/finalizar"), Authorize(Roles = "Admin")] public async Task<IActionResult> Finish(int id, CancellationToken ct) => Ok(await service.CambiarEstadoPeriodo(id, false, ct));
}
