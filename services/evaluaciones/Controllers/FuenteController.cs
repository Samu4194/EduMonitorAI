using System.ComponentModel.DataAnnotations;
using EduMonitor.Evaluaciones.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace EduMonitor.Evaluaciones.Controllers;

public record CambiarFuente([Required, RegularExpression("^(Moodle|Manual)$", ErrorMessage = "La fuente debe ser Moodle o Manual.")] string Fuente);

// De dónde salen los datos académicos. Cualquier usuario lo consulta (la pantalla lo necesita para saber si se puede editar);
// solo el administrador lo cambia.
[ApiController, Authorize, Route("api/moodle/fuente")]
public class FuenteController(FuenteAcademica fuente, IConfiguration config) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Obtener(CancellationToken ct) =>
        Ok(new { fuente = await fuente.Obtener(ct), moodleConfigurado = !string.IsNullOrWhiteSpace(config["Moodle:Token"]) });

    [HttpPut, Authorize(Roles = "Admin")]
    public async Task<IActionResult> Cambiar(CambiarFuente input, CancellationToken ct)
    {
        await fuente.Cambiar(input.Fuente, ct);
        return Ok(new { fuente = input.Fuente });
    }
}
