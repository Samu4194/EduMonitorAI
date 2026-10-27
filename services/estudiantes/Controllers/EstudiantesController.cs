using EduMonitor.Estudiantes.DTOs;
using EduMonitor.Estudiantes.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace EduMonitor.Estudiantes.Controllers;

[ApiController, Authorize(Roles = "Admin"), Route("api/estudiantes")]
public class EstudiantesController(EstudiantesService service) : ControllerBase
{
    [HttpGet] public async Task<IActionResult> List(CancellationToken ct) => Ok(await service.List(ct));
    [HttpGet("{id:int}")] public async Task<IActionResult> Get(int id, CancellationToken ct) => Ok(await service.Get(id, ct));
    [HttpPost]
    public async Task<IActionResult> Create(StudentRequest input, CancellationToken ct)
    {
        var x = await service.Save(null, input, ct);
        return CreatedAtAction(nameof(Get), new
        {
            id = x.Id
        }, x);
    }
    [HttpPut("{id:int}")] public async Task<IActionResult> Update(int id, StudentRequest input, CancellationToken ct) => Ok(await service.Save(id, input, ct));
}
