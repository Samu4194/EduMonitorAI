using EduMonitor.Materias.DTOs;
using EduMonitor.Materias.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace EduMonitor.Materias.Controllers;

[ApiController, Authorize, Route("api/materias")]
public class MateriasController(MateriasService service) : ControllerBase
{
    [HttpGet] public async Task<IActionResult> List(CancellationToken ct) => Ok(await service.List(User, ct));
    [HttpGet("{id:int}")] public async Task<IActionResult> Get(int id, CancellationToken ct) => Ok(await service.Get(id, User, ct));
    [HttpPost, Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create(MateriaRequest input, CancellationToken ct)
    {
        var x = await service.Save(null, input, ct);
        return CreatedAtAction(nameof(Get), new
        {
            id = x.Id
        }, x);
    }
    [HttpPut("{id:int}"), Authorize(Roles = "Admin")] public async Task<IActionResult> Update(int id, MateriaRequest input, CancellationToken ct) => Ok(await service.Save(id, input, ct));
    [HttpPut("{id:int}/docente"), Authorize(Roles = "Admin")] public async Task<IActionResult> Assign(int id, AssignmentRequest input, CancellationToken ct) => Ok(await service.Assign(id, input.TeacherId, ct));
}
