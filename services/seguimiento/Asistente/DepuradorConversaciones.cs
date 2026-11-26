using EduMonitor.Seguimiento.Data;
using Microsoft.EntityFrameworkCore;
namespace EduMonitor.Seguimiento.Asistente;

// El historial del asistente es temporal: a diario se borran las conversaciones que superaron los días de retención.
// (Además cada docente depura las suyas al abrir su lista, así que nunca ve una conversación vencida.)
public sealed class DepuradorConversaciones(IServiceScopeFactory scopes, OpcionesAsistente cfg, ILogger<DepuradorConversaciones> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(20), ct); } catch (OperationCanceledException) { return; }
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<SeguimientoDb>();
                var limite = DateTime.UtcNow.AddDays(-cfg.RetencionDias);
                var n = await db.Conversaciones.Where(c => c.ActualizadoEn < limite).ExecuteDeleteAsync(ct);
                if (n > 0)
                    logger.LogInformation("Se borraron {N} conversaciones del asistente con más de {Dias} días", n, cfg.RetencionDias);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogWarning("No se pudo depurar el historial del asistente: {Motivo}", e.Message);
            }
            try { await Task.Delay(TimeSpan.FromHours(6), ct); } catch (OperationCanceledException) { return; }
        }
    }
}
