using EduMonitor.Materias.DTOs;
using EduMonitor.Materias.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace EduMonitor.Materias.Controllers;

[ApiController, Authorize, Route("api/carreras")]
public class CarrerasController(MateriasService service) : ControllerBase
{
    // estado (solo administrador): activas (por defecto), bajas o todas. Un docente solo ve sus carreras activas.
    [HttpGet] public async Task<IActionResult> List([FromQuery] string? estado, CancellationToken ct) => Ok(await service.Carreras(estado, User, ct));
    // malla: nada = la vigente, un número = esa versión, "todas" = todas las versiones.
    [HttpGet("{codigo}/materias")] public async Task<IActionResult> Materias(string codigo, [FromQuery] int? nivel, [FromQuery] string? malla, CancellationToken ct) => Ok(await service.MateriasDe(codigo, nivel, malla, User, ct));
    // Código que el sistema asignaría a una carrera con ese nombre (solo para mostrarlo en el formulario).
    [HttpGet("codigo-sugerido"), Authorize(Roles = "Admin")] public async Task<IActionResult> CodigoSugerido([FromQuery] string nombre, CancellationToken ct) => Ok(await service.SugerirCodigo(nombre ?? "", ct));
    [HttpPost, Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create(CarreraRequest input, CancellationToken ct)
    {
        var x = await service.CrearCarrera(input, ct);
        return Created($"/api/carreras/{x.Codigo}", x);
    }
    // Baja lógica: la carrera se oculta y deja de admitir cambios, pero conserva su historial y su código. No se elimina.
    [HttpPut("{codigo}/baja"), Authorize(Roles = "Admin")] public async Task<IActionResult> Withdraw(string codigo, CancellationToken ct) => Ok(await service.CambiarEstadoCarrera(codigo, false, ct));
    [HttpPut("{codigo}/reactivar"), Authorize(Roles = "Admin")] public async Task<IActionResult> Reactivate(string codigo, CancellationToken ct) => Ok(await service.CambiarEstadoCarrera(codigo, true, ct));
    [HttpPut("{codigo}"), Authorize(Roles = "Admin")] public async Task<IActionResult> Edit(string codigo, CarreraRequest input, CancellationToken ct) => Ok(await service.EditarCarrera(codigo, input, ct));
    // Carga o actualiza la malla con un CSV. Solo con el período en preparación.
    [HttpPost("{codigo}/malla"), Authorize(Roles = "Admin"), RequestSizeLimit(2_000_000)]
    public async Task<IActionResult> CargarMalla(string codigo, IFormFile archivo, CancellationToken ct)
    {
        using var memoria = new MemoryStream();
        await archivo.CopyToAsync(memoria, ct);
        return Ok(await service.CargarMalla(codigo, memoria.ToArray(), ct));
    }
}
