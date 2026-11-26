using System.Security.Claims;
using System.Text.Json;
using EduMonitor.Infrastructure;
using EduMonitor.Seguimiento.Data;
using EduMonitor.Seguimiento.DTOs;
using EduMonitor.Seguimiento.Models;
using Microsoft.EntityFrameworkCore;
namespace EduMonitor.Seguimiento.Services;

// HU-13. El docente revisa lo que propuso el agente: puede corregirlo, pedir otra redacción, rechazarlo o aprobarlo y comunicarlo.
// Cada transición valida el estado actual (no se puede aprobar una rechazada ni comunicar un borrador) y deja una huella en la bitácora.
public sealed class RecomendacionesService(AccesoCursos acceso, SeguimientoDb db, Comunicador comunicador, ColaTrabajos cola, IHttpContextAccessor http)
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    ClaimsPrincipal Usuario => http.HttpContext!.User;

    // Carga la recomendación y comprueba que el docente tenga acceso a su curso (y que el período no esté finalizado).
    async Task<Recomendacion> Cargar(int id, CancellationToken ct)
    {
        var r = await db.Recomendaciones.Include(x => x.EstudianteAnalisis).SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new ApiException(404, "La recomendación no existe.");
        await acceso.Curso(r.CursoId, escritura: true, ct);
        return r;
    }
    void Bitacora(Recomendacion r, string evento, string? detalle = null) =>
        db.Eventos.Add(new EventoRecomendacion { RecomendacionId = r.Id, Evento = evento, UsuarioId = Usuario.UserId(), Detalle = detalle });
    static void Exigir(Recomendacion r, string accion, params string[] estados)
    {
        if (!estados.Contains(r.Estado))
            throw new ApiException(409, $"No se puede {accion} una recomendación en estado «{r.Estado}».");
    }
    static List<string> Limpiar(IEnumerable<string> textos) => textos.Select(t => t.Trim()).Where(t => t.Length > 0).Select(t => t.Length > 320 ? t[..320] : t).Take(4).ToList();

    public async Task Editar(int id, EditarRecomendacion input, CancellationToken ct)
    {
        var r = await Cargar(id, ct);
        Exigir(r, "editar", EstadoRecomendacion.Borrador);
        var causas = Limpiar(input.Causas);
        var docente = Limpiar(input.AccionesDocente);
        var estudiante = Limpiar(input.AccionesEstudiante);
        if (causas.Count == 0 || docente.Count == 0 || estudiante.Count == 0)
            throw new ApiException(400, "Cada lista debe tener al menos un elemento con texto.");
        r.Resumen = input.Resumen.Trim();
        r.CausasJson = JsonSerializer.Serialize(causas, Json);
        r.AccionesDocenteJson = JsonSerializer.Serialize(docente, Json);
        r.AccionesEstudianteJson = JsonSerializer.Serialize(estudiante, Json);
        r.MensajeEstudiante = input.MensajeEstudiante.Trim();
        r.Editada = true;
        r.ActualizadoEn = DateTime.UtcNow;
        Bitacora(r, "Editada");
        await db.SaveChangesAsync(ct);
    }

    public async Task Rechazar(int id, string motivo, CancellationToken ct)
    {
        var r = await Cargar(id, ct);
        Exigir(r, "rechazar", EstadoRecomendacion.Borrador, EstadoRecomendacion.Aprobada);
        r.Estado = EstadoRecomendacion.Rechazada;
        r.MotivoRechazo = motivo.Trim();
        r.RevisadaPor = Usuario.UserId();
        r.RevisadaEn = r.ActualizadoEn = DateTime.UtcNow;
        Bitacora(r, "Rechazada", r.MotivoRechazo);
        await db.SaveChangesAsync(ct);
    }

    // Vuelve a pedirle al agente otra redacción. Es asíncrono: la IA tarda y el docente ve la recomendación «Generando» hasta que termina.
    public async Task Regenerar(int id, CancellationToken ct)
    {
        var r = await Cargar(id, ct);
        Exigir(r, "regenerar", EstadoRecomendacion.Borrador, EstadoRecomendacion.Rechazada);
        // Solo tiene sentido con las notas del último análisis: una recomendación de uno anterior se redactaría con datos desactualizados.
        var ultimo = await db.Analisis.Where(a => a.CursoId == r.CursoId).OrderByDescending(a => a.CreadoEn).ThenByDescending(a => a.Id).Select(a => a.Id).FirstAsync(ct);
        if (ultimo != r.AnalisisId)
            throw new ApiException(409, "Esta recomendación pertenece a un análisis anterior. Genera un análisis nuevo del curso.");
        r.Estado = EstadoRecomendacion.Generando;
        r.Version++;
        r.MotivoRechazo = null;
        r.ActualizadoEn = DateTime.UtcNow;
        Bitacora(r, "Regenerada", $"Versión {r.Version}");
        await db.SaveChangesAsync(ct);
        cola.Encolar(new Trabajo(r.AnalisisId, r.Id));
    }

    public async Task<ComunicacionView?> Aprobar(int id, bool comunicar, CancellationToken ct)
    {
        var r = await Cargar(id, ct);
        Exigir(r, "aprobar", EstadoRecomendacion.Borrador);
        r.Estado = EstadoRecomendacion.Aprobada;
        r.RevisadaPor = Usuario.UserId();
        r.RevisadaEn = r.ActualizadoEn = DateTime.UtcNow;
        Bitacora(r, "Aprobada");
        await db.SaveChangesAsync(ct);
        return comunicar ? await Enviar(r, ct) : null;
    }

    public async Task<ComunicacionView> Comunicar(int id, CancellationToken ct)
    {
        var r = await Cargar(id, ct);
        Exigir(r, "comunicar", EstadoRecomendacion.Aprobada);
        return await Enviar(r, ct);
    }

    // Una recomendación se comunica una sola vez. Si el envío falla queda «Aprobada» y se puede reintentar.
    async Task<ComunicacionView> Enviar(Recomendacion r, CancellationToken ct)
    {
        var analisis = await db.Analisis.SingleAsync(a => a.Id == r.AnalisisId, ct);
        var c = await comunicador.Enviar(r, analisis, Usuario.FindFirstValue(ClaimTypes.Name) ?? "El docente", Usuario.UserId(), ct);
        db.Comunicaciones.Add(c);
        if (c.Estado != EstadoComunicacion.Error)
        {
            r.Estado = EstadoRecomendacion.Comunicada;
            r.ActualizadoEn = DateTime.UtcNow;
        }
        Bitacora(r, c.Estado == EstadoComunicacion.Error ? "ComunicacionFallida" : "Comunicada", c.Estado == EstadoComunicacion.Error ? c.Error : c.Estado);
        await db.SaveChangesAsync(ct);
        return Consultas.Ver(c, r.EstudianteAnalisis);
    }
}
