using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EduMonitor.Infrastructure;

// Solo se reintentan lecturas. Nunca se repiten escrituras automáticamente.
public sealed class DependencyClient(HttpClient http, IHttpContextAccessor accessor, IConfiguration config, ILogger<DependencyClient> logger)
{
    // Escritura en otro servicio, con la sesión de quien llama. Un solo intento: nunca se repite una escritura sola (la operación que llame debe ser idempotente).
    public async Task<T?> Post<T>(string service, string path, CancellationToken ct)
    {
        var origin = config[$"Services:{service}"] ?? throw new InvalidOperationException($"Falta Services:{service}");
        using var request = new HttpRequestMessage(HttpMethod.Post, origin.TrimEnd('/') + path);
        request.Headers.TryAddWithoutValidation("Authorization", accessor.HttpContext!.Request.Headers.Authorization.ToString());
        try
        {
            using var response = await http.SendAsync(request, ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
                throw new ApiException(404, "El curso, el docente o la carrera solicitados no existen o no están autorizados.");
            if (response.StatusCode == HttpStatusCode.Forbidden)
                throw new ApiException(403, "No tienes permiso para esta operación.");
            if (response.StatusCode == HttpStatusCode.Unauthorized)
                throw new ApiException(401, "La sesión terminó. Inicia sesión de nuevo.");
            if (!response.IsSuccessStatusCode)
                throw new ApiException(503, $"El servicio de {service.ToLowerInvariant()} no pudo completar la operación. No se aplicaron cambios.");
            return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
        }
        catch (Exception e) when (e is HttpRequestException || (e is TaskCanceledException && !ct.IsCancellationRequested))
        {
            logger.LogWarning("Dependencia {Service} no disponible: {Reason}", service, e.Message);
            throw new ApiException(503, $"El servicio de {service.ToLowerInvariant()} no está disponible. No se aplicaron cambios.");
        }
    }

    public async Task<T> Get<T>(string service, string path, CancellationToken ct)
    {
        var origin = config[$"Services:{service}"] ?? throw new InvalidOperationException($"Falta Services:{service}");
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, origin.TrimEnd('/') + path);
            request.Headers.TryAddWithoutValidation("Authorization", accessor.HttpContext!.Request.Headers.Authorization.ToString());
            try
            {
                using var response = await http.SendAsync(request, ct);
                if (response.StatusCode == HttpStatusCode.NotFound)
                    throw new ApiException(404, "El curso, el docente o la carrera solicitados no existen o no están autorizados.");
                if (response.StatusCode == HttpStatusCode.Forbidden)
                    throw new ApiException(403, "No tienes permiso para esta operación.");
                if (response.StatusCode == HttpStatusCode.Unauthorized)
                    throw new ApiException(401, "La sesión terminó. Inicia sesión de nuevo.");
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException($"HTTP {(int)response.StatusCode}");
                return (await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct))!;
            }
            catch (Exception e) when (e is HttpRequestException || (e is TaskCanceledException && !ct.IsCancellationRequested))
            {
                logger.LogWarning("Dependencia {Service} no disponible, intento {Attempt}: {Reason}", service, attempt + 1, e.Message);
                if (attempt == 1)
                    throw new ApiException(503, $"El servicio de {service.ToLowerInvariant()} no está disponible. Inténtalo de nuevo en unos segundos.");
                await Task.Delay(150, ct);
            }
        }
        throw new ApiException(503, "Servicio no disponible.");
    }
}
