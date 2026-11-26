using System.Text.Json;
using System.Threading.Channels;
using EduMonitor.Seguimiento.Agente;
using EduMonitor.Seguimiento.Data;
using EduMonitor.Seguimiento.Models;
using Microsoft.EntityFrameworkCore;
namespace EduMonitor.Seguimiento.Services;

public sealed class ColaTrabajos
{
    readonly Channel<Trabajo> canal = Channel.CreateUnbounded<Trabajo>(new UnboundedChannelOptions { SingleReader = true });
    public void Encolar(Trabajo t) => canal.Writer.TryWrite(t);
    public IAsyncEnumerable<Trabajo> Leer(CancellationToken ct) => canal.Reader.ReadAllAsync(ct);
}

// Redacta las recomendaciones fuera de la petición del docente. Un solo trabajo a la vez: el modelo local usa la GPU/CPU de la máquina
// y la nube limita las peticiones por minuto. Si el servicio se reinicia a mitad de un análisis, al arrancar retoma lo que quedó "Generando".
public sealed class GeneradorEnSegundoPlano(ColaTrabajos cola, IServiceScopeFactory scopes, ILogger<GeneradorEnSegundoPlano> logger) : BackgroundService
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _ = RetomarPendientes(ct);
        try
        {
            await foreach (var trabajo in cola.Leer(ct))
                try { await Procesar(trabajo, ct); }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    logger.LogError(e, "Falló el trabajo del análisis {Analisis}", trabajo.AnalisisId);
                }
        }
        catch (OperationCanceledException) { }
    }

    async Task RetomarPendientes(CancellationToken ct)
    {
        for (var intento = 0; intento < 6 && !ct.IsCancellationRequested; intento++)
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<SeguimientoDb>();
                var analisis = await db.Analisis.Where(a => a.Estado == EstadoAnalisis.Generando).Select(a => a.Id).ToListAsync(ct);
                var sueltas = await db.Recomendaciones.Where(r => r.Estado == EstadoRecomendacion.Generando && !analisis.Contains(r.AnalisisId)).Select(r => new Trabajo(r.AnalisisId, r.Id)).ToListAsync(ct);
                foreach (var id in analisis)
                    cola.Encolar(new Trabajo(id));
                foreach (var t in sueltas)
                    cola.Encolar(t);
                return;
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogWarning("No se pudo retomar los análisis pendientes (intento {Intento}): {Motivo}", intento + 1, e.Message);
                await Task.Delay(TimeSpan.FromSeconds(10), ct);
            }
    }

    async Task Procesar(Trabajo trabajo, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SeguimientoDb>();
        var agente = scope.ServiceProvider.GetRequiredService<AgenteAcademico>();
        var analisis = await db.Analisis.SingleOrDefaultAsync(a => a.Id == trabajo.AnalisisId, ct);
        if (analisis is null)
            return;
        var consulta = db.Recomendaciones.Include(r => r.EstudianteAnalisis).Where(r => r.AnalisisId == trabajo.AnalisisId && r.Estado == EstadoRecomendacion.Generando);
        if (trabajo.RecomendacionId is { } id)
            consulta = consulta.Where(r => r.Id == id);
        var pendientes = await consulta.OrderBy(r => r.Id).ToListAsync(ct);
        var sesion = new SesionAgente();
        foreach (var rec in pendientes)
        {
            var contexto = ContextoIA.Leer(rec.EstudianteAnalisis.ContextoJson);
            var respuesta = contexto is null
                ? new RespuestaAgente(GeneradorPlantillas.Generar(new ContextoIA(analisis.Materia, analisis.NotaMinima, rec.EstudianteAnalisis.Resultado, rec.EstudianteAnalisis.Nivel, [], [], [], null)), OrigenRecomendacion.Plantilla, null)
                : await agente.Recomendar(contexto, sesion, ct);
            // Mientras la IA redactaba (puede tardar minutos), otro análisis pudo reemplazar esta recomendación: no hay que devolverla a «Borrador».
            await db.Entry(rec).ReloadAsync(ct);
            if (rec.Estado != EstadoRecomendacion.Generando)
                continue;
            var b = respuesta.Borrador;
            rec.Resumen = b.Resumen;
            rec.CausasJson = JsonSerializer.Serialize(b.Causas, Json);
            rec.AccionesDocenteJson = JsonSerializer.Serialize(b.AccionesDocente, Json);
            rec.AccionesEstudianteJson = JsonSerializer.Serialize(b.AccionesEstudiante, Json);
            rec.MensajeEstudiante = b.MensajeEstudiante;
            rec.Origen = respuesta.Origen;
            rec.Modelo = respuesta.Modelo;
            rec.Editada = false;
            rec.Estado = EstadoRecomendacion.Borrador;
            rec.ActualizadoEn = DateTime.UtcNow;
            db.Eventos.Add(new EventoRecomendacion { RecomendacionId = rec.Id, Evento = "Generada", Detalle = $"{respuesta.Origen}{(respuesta.Modelo is null ? "" : " · " + respuesta.Modelo)}" });
            // Una recomendación regenerada mientras el análisis seguía en curso ya se había contado la primera vez (versión 1).
            if (trabajo.RecomendacionId is null && rec.Version == 1)
                analisis.Generadas++;
            await db.SaveChangesAsync(ct);
        }
        if (trabajo.RecomendacionId is null)
        {
            analisis.Estado = EstadoAnalisis.Completado;
            analisis.TerminadoEn = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
    }
}
