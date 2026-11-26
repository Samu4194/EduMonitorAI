using System.Runtime.CompilerServices;
using EduMonitor.Seguimiento.Models;
namespace EduMonitor.Seguimiento.Agente;

public record RespuestaAgente(Borrador Borrador, string Origen, string? Modelo);
public record FragmentoChat(string Texto, string Origen, string Modelo);
// El proveedor se cayó después de haber empezado a escribir: se conserva lo recibido y se avisa al docente.
public sealed class RespuestaInterrumpidaException : Exception;

// Memoria de una corrida: si un proveedor falla seguido, se deja de intentar con él durante el resto del análisis.
// Así, con Ollama apagado o sin internet, no se pierde el tiempo de espera en cada estudiante.
public sealed class SesionAgente
{
    readonly Dictionary<string, int> fallos = [];
    public bool Omitido(string clave, int limite) => fallos.GetValueOrDefault(clave) >= limite;
    public void Fallo(string clave) => fallos[clave] = fallos.GetValueOrDefault(clave) + 1;
    public void Exito(string clave) => fallos[clave] = 0;
}

// HU-12. Agente académico: dado el contexto de un estudiante redacta la recomendación pedagógica.
// Prueba los proveedores de IA en el orden configurado (local, nube); si ninguno responde algo válido usa las plantillas.
public sealed class AgenteAcademico(ProveedoresIA proveedores, OpcionesAgente opciones, ILogger<AgenteAcademico> logger)
{
    // Proveedores a probar, en orden: configurados, sin pausa por una caída reciente y no omitidos en esta corrida.
    IEnumerable<IProveedorIA> Candidatos(SesionAgente sesion) =>
        proveedores.Lista.Where(p => p.Configurado && !p.EnPausa && !sesion.Omitido(p.Clave, opciones.FallosParaOmitir));

    public async Task<RespuestaAgente> Recomendar(ContextoIA contexto, SesionAgente sesion, CancellationToken ct)
    {
        var usuario = Prompts.Usuario(contexto);
        foreach (var p in Candidatos(sesion))
        {
            try
            {
                var texto = await p.Completar(Prompts.Sistema, usuario, ct);
                var borrador = Interprete.Interpretar(texto, out var motivo);
                if (borrador is not null)
                {
                    sesion.Exito(p.Clave);
                    return new RespuestaAgente(borrador, p.Origen, p.Modelo);
                }
                sesion.Fallo(p.Clave);
                logger.LogWarning("El proveedor {Proveedor} respondió algo no válido ({Motivo}); se usa el siguiente.", p.Clave, motivo);
            }
            catch (Exception e) when (e is HttpRequestException or TimeoutException or System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException)
            {
                sesion.Fallo(p.Clave);
                if (e is HttpRequestException or TimeoutException)
                    p.Pausar();
                logger.LogWarning("El proveedor {Proveedor} no está disponible: {Motivo}", p.Clave, e.Message);
            }
        }
        return new RespuestaAgente(GeneradorPlantillas.Generar(contexto), OrigenRecomendacion.Plantilla, null);
    }

    // Pide un JSON a los proveedores en orden. «interpretar» valida la respuesta y devuelve null si no sirve (entonces se prueba el siguiente).
    public async Task<(T? Valor, string? Origen, string? Modelo)> Redactar<T>(string sistema, string usuario, Func<string, T?> interpretar, SesionAgente sesion, CancellationToken ct) where T : class
    {
        foreach (var p in Candidatos(sesion))
        {
            try
            {
                var valor = interpretar(await p.Completar(sistema, usuario, ct));
                if (valor is not null)
                {
                    sesion.Exito(p.Clave);
                    return (valor, p.Origen, p.Modelo);
                }
                sesion.Fallo(p.Clave);
                logger.LogWarning("El proveedor {Proveedor} respondió algo no válido; se usa el siguiente.", p.Clave);
            }
            catch (Exception e) when (e is HttpRequestException or TimeoutException or System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException)
            {
                sesion.Fallo(p.Clave);
                if (e is HttpRequestException or TimeoutException)
                    p.Pausar();
                logger.LogWarning("El proveedor {Proveedor} no está disponible: {Motivo}", p.Clave, e.Message);
            }
        }
        return (null, null, null);
    }

    // Chat del asistente. Prueba los proveedores en orden hasta que uno empieza a responder; si ninguno puede, no devuelve nada y
    // quien llama usa su respaldo sin IA. Una vez que un proveedor empezó, ya no se cambia de proveedor a mitad de respuesta.
    public async IAsyncEnumerable<FragmentoChat> Conversar(string sistema, IReadOnlyList<MensajeChat> historial, SesionAgente sesion, [EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var p in Candidatos(sesion))
        {
            var iniciada = false;
            var flujo = p.Conversar(sistema, historial, ct).GetAsyncEnumerator(ct);
            try
            {
                while (true)
                {
                    bool hay;
                    try { hay = await flujo.MoveNextAsync(); }
                    catch (Exception e) when (e is HttpRequestException or TimeoutException or System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException)
                    {
                        sesion.Fallo(p.Clave);
                        if (e is HttpRequestException or TimeoutException)
                            p.Pausar();
                        logger.LogWarning("El proveedor {Proveedor} no pudo conversar: {Motivo}", p.Clave, e.Message);
                        if (iniciada)
                            throw new RespuestaInterrumpidaException();
                        break;
                    }
                    if (!hay)
                    {
                        if (iniciada)
                            sesion.Exito(p.Clave);
                        else
                            sesion.Fallo(p.Clave);
                        break;
                    }
                    iniciada = true;
                    yield return new FragmentoChat(flujo.Current, p.Origen, p.Modelo);
                }
            }
            finally { await flujo.DisposeAsync(); }
            if (iniciada)
                yield break;
        }
    }

    // Carga el modelo local en segundo plano para que la primera pregunta del docente no espere el arranque en frío.
    public Task Calentar(CancellationToken ct) => Task.WhenAll(proveedores.Lista.Where(p => p.Configurado).Select(p => p.Calentar(ct)));

    public async Task<List<EstadoProveedor>> Estado(CancellationToken ct)
    {
        var estados = await Task.WhenAll(proveedores.Lista.Select(p => p.Estado(ct)));
        return [.. estados];
    }
}
