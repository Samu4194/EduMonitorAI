using EduMonitor.Seguimiento.Agente;
using EduMonitor.Seguimiento.DTOs;
using EduMonitor.Seguimiento.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace EduMonitor.Seguimiento.Controllers;

// Todo es de lectura para el administrador y el docente del curso; solo el docente genera, revisa y comunica (Materias decide a quién pertenece cada curso).
[ApiController, Authorize, Route("api/seguimiento")]
public class SeguimientoController(AnalisisService analisis, Consultas consultas, RecomendacionesService recomendaciones, AgenteAcademico agente, OpcionesAgente opciones, OpcionesCorreo correo) : ControllerBase
{
    // Qué proveedores de IA están disponibles ahora mismo (el panel lo muestra antes de analizar).
    [HttpGet("agente/estado")]
    public async Task<IActionResult> Estado(CancellationToken ct) =>
        Ok(new EstadoAgenteView(await agente.Estado(ct), true, opciones.NivelMinimo, correo.Real));

    [HttpGet("cursos/{cursoId:int}")]
    public async Task<IActionResult> Panel(int cursoId, CancellationToken ct) => Ok(await consultas.Panel(cursoId, ct));

    // 202: el análisis queda registrado y las recomendaciones se redactan en segundo plano.
    [HttpPost("cursos/{cursoId:int}/analisis"), Authorize(Roles = "Teacher")]
    public async Task<IActionResult> Analizar(int cursoId, [FromQuery] bool soloAlertas, CancellationToken ct) => Accepted(await analisis.Iniciar(cursoId, soloAlertas, ct));

    [HttpGet("cursos/{cursoId:int}/comunicaciones")]
    public async Task<IActionResult> Comunicaciones(int cursoId, CancellationToken ct) => Ok(await consultas.Comunicaciones(cursoId, ct));

    [HttpGet("cursos/{cursoId:int}/reporte")]
    public async Task<IActionResult> Reporte(int cursoId, CancellationToken ct) => Ok(await consultas.Reporte(cursoId, ct));

    [HttpGet("cursos/{cursoId:int}/reporte.csv")]
    public async Task<IActionResult> ReporteCsv(int cursoId, CancellationToken ct)
    {
        var reporte = await consultas.Reporte(cursoId, ct);
        return File(Consultas.Csv(reporte), "text/csv; charset=utf-8", $"seguimiento-{reporte.Materia}-{reporte.Paralelo}.csv".Replace(' ', '-'));
    }

    [HttpPut("recomendaciones/{id:int}"), Authorize(Roles = "Teacher")]
    public async Task<IActionResult> Editar(int id, EditarRecomendacion input, CancellationToken ct)
    {
        await recomendaciones.Editar(id, input, ct);
        return NoContent();
    }

    [HttpPost("recomendaciones/{id:int}/aprobar"), Authorize(Roles = "Teacher")]
    public async Task<IActionResult> Aprobar(int id, AprobarRequest input, CancellationToken ct) => Ok(new { comunicacion = await recomendaciones.Aprobar(id, input.Comunicar, ct) });

    [HttpPost("recomendaciones/{id:int}/comunicar"), Authorize(Roles = "Teacher")]
    public async Task<IActionResult> Comunicar(int id, CancellationToken ct) => Ok(await recomendaciones.Comunicar(id, ct));

    [HttpPost("recomendaciones/{id:int}/rechazar"), Authorize(Roles = "Teacher")]
    public async Task<IActionResult> Rechazar(int id, RechazarRequest input, CancellationToken ct)
    {
        await recomendaciones.Rechazar(id, input.Motivo, ct);
        return NoContent();
    }

    [HttpPost("recomendaciones/{id:int}/regenerar"), Authorize(Roles = "Teacher")]
    public async Task<IActionResult> Regenerar(int id, CancellationToken ct)
    {
        await recomendaciones.Regenerar(id, ct);
        return Accepted();
    }
}
