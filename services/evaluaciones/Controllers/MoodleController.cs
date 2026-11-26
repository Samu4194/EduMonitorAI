using System.ComponentModel.DataAnnotations;
using EduMonitor.Evaluaciones.Moodle;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduMonitor.Evaluaciones.Controllers;

public record VincularMoodle([Required, StringLength(100, MinimumLength = 1)] string Codigo);

[ApiController, Authorize, Route("api/moodle/cursos/{cursoId:int}")]
public class MoodleController(SincronizacionMoodle service) : ControllerBase
{
    [HttpGet("vinculo")]
    public async Task<IActionResult> Estado(int cursoId, CancellationToken ct) => Ok(await service.Estado(cursoId, ct));

    [HttpPut("vinculo"), Authorize(Roles = "Admin")]
    public async Task<IActionResult> Vincular(int cursoId, VincularMoodle input, CancellationToken ct) =>
        Ok(await service.Vincular(cursoId, input.Codigo.Trim(), ct));

    [HttpGet("origen"), Authorize(Roles = "Admin")]
    public async Task<IActionResult> Origen(int cursoId, CancellationToken ct) =>
        Ok((await service.Leer(cursoId, ct)).Alumnos);

    // ¿Es el mismo docente en Moodle y en EduMonitor? Lo consultan el administrador al vincular y el docente del curso.
    [HttpGet("docentes")]
    public async Task<IActionResult> Docentes(int cursoId, CancellationToken ct) => Ok(await service.Docentes(cursoId, ct));

    [HttpPost("sincronizar"), Authorize(Roles = "Teacher")]
    public async Task<IActionResult> Importar(int cursoId, CancellationToken ct) => Ok(await service.Importar(cursoId, ct));
}
