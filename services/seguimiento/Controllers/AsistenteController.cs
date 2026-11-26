using System.Text.Json;
using System.Threading.Channels;
using EduMonitor.Infrastructure;
using EduMonitor.Seguimiento.Agente;
using EduMonitor.Seguimiento.Asistente;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace EduMonitor.Seguimiento.Controllers;

public record ValoracionRequest(bool Util);

// Chat del asistente académico. Solo el docente del curso conversa (Materias decide a quién pertenece cada curso); el administrador no entra.
[ApiController, Authorize(Roles = "Teacher"), Route("api/seguimiento/cursos/{cursoId:int}/asistente")]
public class AsistenteController(ServicioAsistente servicio, LimiteDocente limite, AgenteAcademico agente, ILogger<AsistenteController> logger) : ControllerBase
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [HttpGet("sugerencias")]
    public async Task<IActionResult> Sugerencias(int cursoId, CancellationToken ct)
    {
        await servicio.Autorizar(cursoId, ct);
        return Ok(ServicioAsistente.Sugerencias());
    }

    // Selector de estudiante: por nombre o por cédula.
    [HttpGet("estudiantes")]
    public async Task<IActionResult> Estudiantes(int cursoId, [FromQuery] string? q, CancellationToken ct) => Ok(await servicio.BuscarEstudiantes(cursoId, q, ct));

    // Carga el modelo local en segundo plano al abrir el chat, para que la primera pregunta no espere el arranque en frío.
    [HttpPost("calentar")]
    public async Task<IActionResult> Calentar(int cursoId, CancellationToken ct)
    {
        await servicio.Autorizar(cursoId, ct);
        _ = agente.Calentar(CancellationToken.None);
        return NoContent();
    }

    [HttpGet("conversaciones")]
    public async Task<IActionResult> Conversaciones(int cursoId, CancellationToken ct) => Ok(await servicio.Conversaciones(cursoId, ct));

    [HttpGet("conversaciones/{id:int}")]
    public async Task<IActionResult> Detalle(int cursoId, int id, CancellationToken ct) => Ok(await servicio.Detalle(cursoId, id, ct));

    [HttpDelete("conversaciones/{id:int}")]
    public async Task<IActionResult> Borrar(int cursoId, int id, CancellationToken ct)
    {
        await servicio.Borrar(cursoId, id, ct);
        return NoContent();
    }

    [HttpPost("mensajes/{id:int}/valoracion")]
    public async Task<IActionResult> Valorar(int cursoId, int id, ValoracionRequest input, CancellationToken ct)
    {
        await servicio.Valorar(cursoId, id, input.Util, ct);
        return NoContent();
    }

    [HttpPost("mensajes/{id:int}/guardar-ruta")]
    public async Task<IActionResult> GuardarRuta(int cursoId, int id, CancellationToken ct) => Ok(await servicio.GuardarRuta(cursoId, id, ct));

    // La respuesta llega como flujo de eventos (text/event-stream): inicio, tarjeta, texto (en fragmentos), ruta, aclaracion, fin o error.
    // Cada 4 s se manda un latido para que el gateway y el navegador no cierren la conexión mientras el modelo arranca.
    [HttpPost("mensajes")]
    public async Task Mensajes(int cursoId, EntradaChat entrada, CancellationToken ct)
    {
        await servicio.Autorizar(cursoId, ct);
        servicio.Validar(entrada);
        using var turno = limite.Entrar(User.UserId()) ?? throw new ApiException(429, "Espera a que termine la respuesta anterior antes de enviar otra pregunta.");
        Response.StatusCode = 200;
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";
        var canal = Channel.CreateUnbounded<EventoChat>();
        var tarea = Task.Run(async () =>
        {
            try { await servicio.Responder(cursoId, entrada, e => canal.Writer.WriteAsync(e, ct).AsTask(), ct); }
            catch (OperationCanceledException) { }
            catch (ApiException e) { canal.Writer.TryWrite(new EventoChat("error", new { mensaje = e.Message })); }
            catch (Exception e)
            {
                logger.LogError(e, "Falló una respuesta del asistente en el curso {Curso}", cursoId);
                canal.Writer.TryWrite(new EventoChat("error", new { mensaje = "No se pudo completar la respuesta. Inténtalo de nuevo." }));
            }
            finally { canal.Writer.TryComplete(); }
        }, CancellationToken.None);

        try
        {
            var lector = canal.Reader;
            var espera = lector.WaitToReadAsync(ct).AsTask();
            while (true)
            {
                var primero = await Task.WhenAny(espera, Task.Delay(TimeSpan.FromSeconds(4), ct));
                if (primero != espera)
                {
                    await Response.WriteAsync(": latido\n\n", ct);
                    await Response.Body.FlushAsync(ct);
                    continue;
                }
                if (!await espera)
                    break;
                while (lector.TryRead(out var e))
                    await Response.WriteAsync($"event: {e.Tipo}\ndata: {JsonSerializer.Serialize(e.Datos, Json)}\n\n", ct);
                await Response.Body.FlushAsync(ct);
                espera = lector.WaitToReadAsync(ct).AsTask();
            }
        }
        catch (OperationCanceledException) { /* el docente cerró la pantalla: la respuesta se detiene */ }
        await tarea;
    }
}
