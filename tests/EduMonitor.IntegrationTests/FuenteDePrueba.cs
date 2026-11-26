using System.Net.Http.Json;
using System.Text.Json;
namespace EduMonitor.IntegrationTests;

// Con Moodle como fuente nadie edita datos académicos a mano. Las pruebas que sí los editan trabajan en modo «Manual»
// y al terminar dejan la fuente como estaba.
public static class FuenteDePrueba
{
    public static async Task<string> Obtener(HttpClient admin) =>
        (await admin.GetFromJsonAsync<JsonElement>("/api/moodle/fuente")).GetProperty("fuente").GetString()!;

    public static async Task Cambiar(HttpClient admin, string fuente)
    {
        using var r = await admin.PutAsJsonAsync("/api/moodle/fuente", new { fuente });
        r.EnsureSuccessStatusCode();
    }
}

// El inicio de sesión permite 30 intentos por minuto por IP (límite de seguridad que se deja como está). La suite abre muchas sesiones
// seguidas, así que cuando recibe 429 espera y reintenta en lugar de fallar.
public static class SesionDePrueba
{
    public static async Task<HttpResponseMessage> Iniciar(HttpClient client, object credenciales)
    {
        HttpResponseMessage r;
        for (var intento = 0; ; intento++)
        {
            r = await client.PostAsJsonAsync("/api/auth/login", credenciales);
            if (r.StatusCode != System.Net.HttpStatusCode.TooManyRequests || intento == 12)
                return r;
            r.Dispose();
            await Task.Delay(TimeSpan.FromSeconds(6));
        }
    }
}
