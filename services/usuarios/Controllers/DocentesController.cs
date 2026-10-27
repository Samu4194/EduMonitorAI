using EduMonitor.Usuarios.DTOs;
using EduMonitor.Usuarios.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace EduMonitor.Usuarios.Controllers;

[ApiController, Authorize(Roles = "Admin"), Route("api/docentes")]
public class DocentesController(UsuariosService service) : ControllerBase
{
    [HttpGet] public async Task<IActionResult> List(CancellationToken ct) => Ok(await service.List(ct));
    [HttpGet("{id:int}")] public async Task<IActionResult> Get(int id, CancellationToken ct) => Ok(await service.Get(id, ct));
    [HttpPost]
    public async Task<IActionResult> Create(TeacherRequest input, CancellationToken ct)
    {
        var x = await service.Save(null, input, ct);
        return CreatedAtAction(nameof(Get), new
        {
            id = x.Id
        }, x);
    }
    [HttpPut("{id:int}")] public async Task<IActionResult> Update(int id, TeacherRequest input, CancellationToken ct) => Ok(await service.Save(id, input, ct));
}
