using EduMonitor.Estudiantes.DTOs;
using EduMonitor.Estudiantes.Services;
using EduMonitor.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace EduMonitor.Estudiantes.Controllers;

[ApiController, Authorize(Roles = "Admin"), Route("api/estudiantes")]
public class EstudiantesController(EstudiantesService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? carrera, [FromQuery] int? nivel, [FromQuery] string? paralelo, [FromQuery] string? materia, [FromQuery] string? estado, CancellationToken ct) =>
        Ok(await service.List(carrera, nivel, paralelo, materia, estado, ct));
    [HttpGet("{id:int}")] public async Task<IActionResult> Get(int id, CancellationToken ct) => Ok(await service.Get(id, ct));
    [HttpPost]
    public async Task<IActionResult> Create(StudentRequest input, CancellationToken ct)
    {
        var x = await service.Crear(input, ct);
        return CreatedAtAction(nameof(Get), new
        {
            id = x.Id
        }, x);
    }
    [HttpPut("{id:int}")] public async Task<IActionResult> Update(int id, StudentEdit input, CancellationToken ct) => Ok(await service.Editar(id, input, ct));
    // Baja lógica: el estudiante deja de aparecer en listas y calificaciones, pero no se borra nada.
    [HttpPost("{id:int}/baja")] public async Task<IActionResult> Baja(int id, CancellationToken ct) => Ok(await service.CambiarEstado(id, false, ct));
    [HttpPost("{id:int}/reactivar")] public async Task<IActionResult> Reactivar(int id, CancellationToken ct) => Ok(await service.CambiarEstado(id, true, ct));
    // Retiro de una sola materia (inscripción): el resto de sus materias no cambia.
    [HttpPost("inscripciones/{id:int}/baja")] public async Task<IActionResult> RetirarMateria(int id, CancellationToken ct) => Ok(await service.CambiarEstadoInscripcion(id, false, ct));
    [HttpPost("inscripciones/{id:int}/reactivar")] public async Task<IActionResult> ReincorporarMateria(int id, CancellationToken ct) => Ok(await service.CambiarEstadoInscripcion(id, true, ct));
    // Precarga desde un CSV (cedula, apellidos, nombres, correo) a UNA materia de un paralelo: el destino (carrera, nivel, paralelo y materia) es obligatorio.
    [HttpPost("precarga"), RequestSizeLimit(2_000_000)]
    public async Task<IActionResult> Precarga(IFormFile archivo, [FromForm] string? carrera, [FromForm] int? nivel, [FromForm] string? paralelo, [FromForm] string? materia, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(carrera) || nivel is null || string.IsNullOrWhiteSpace(paralelo) || string.IsNullOrWhiteSpace(materia))
            throw new ApiException(400, "Elige la carrera, el nivel, el paralelo y la materia donde se cargarán los estudiantes.");
        using var memoria = new MemoryStream();
        await archivo.CopyToAsync(memoria, ct);
        return Ok(await service.Cargar(memoria.ToArray(), new EduMonitor.Estudiantes.Services.Precarga.Destino(carrera?.Trim(), nivel, paralelo?.Trim(), materia?.Trim()), ct));
    }
}
