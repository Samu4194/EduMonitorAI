using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using EduMonitor.Infrastructure;

namespace EduMonitor.Evaluaciones.Moodle;

public sealed class MoodleClient(HttpClient http, IConfiguration config)
{
    private Uri Endpoint => new(config["Moodle:Endpoint"] ?? "http://host.docker.internal:8085/webservice/rest/server.php");
    private string HostPublico => config["Moodle:Host"] ?? "localhost:8085";

    public Uri UrlCurso(int moodleId)
    {
        var servicioPublico = new Uri(new Uri($"{Endpoint.Scheme}://{HostPublico}"), Endpoint.AbsolutePath);
        return new Uri(servicioPublico, $"../../course/view.php?id={moodleId}");
    }

    private async Task<JsonElement> Leer(string funcion, Dictionary<string, string> parametros, CancellationToken ct)
    {
        var token = config["Moodle:Token"];
        if (string.IsNullOrWhiteSpace(token))
            throw new ApiException(503, "Falta configurar el token de lectura de Moodle.");
        parametros["wsfunction"] = funcion;
        parametros["wstoken"] = token;
        parametros["moodlewsrestformat"] = "json";
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        request.Headers.Host = HostPublico;
        request.Content = new FormUrlEncodedContent(parametros);
        try
        {
            using var response = await http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            var data = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            if (data.ValueKind == JsonValueKind.Object &&
                (data.TryGetProperty("exception", out _) || data.TryGetProperty("errorcode", out _) ||
                 data.TryGetProperty("warnings", out var warnings) && warnings.GetArrayLength() > 0))
                throw new ApiException(502, $"Moodle rechazó o devolvió incompleta la consulta {funcion}.");
            return data;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException || ex is OperationCanceledException && !ct.IsCancellationRequested)
        {
            throw new ApiException(503, "No se pudo leer Moodle. No se aplicaron cambios.");
        }
    }

    public async Task<int> Resolver(string codigo, CancellationToken ct)
    {
        var result = await Leer("core_course_get_courses_by_field", new()
        {
            ["field"] = "idnumber",
            ["value"] = codigo
        }, ct);
        var cursos = result.GetProperty("courses");
        if (cursos.GetArrayLength() != 1)
            throw new ApiException(409, "El identificador de Moodle no corresponde a un único curso accesible.");
        return cursos[0].GetProperty("id").GetInt32();
    }

    public async Task<AulaMoodle> Aula(int id, string codigo, CancellationToken ct)
    {
        Dictionary<string, string> Parametros() => new()
        {
            ["courseid"] = id.ToString()
        };
        if (await Resolver(codigo, ct) != id)
            throw new ApiException(409, "Cambió la identidad del curso Moodle.");
        var partes = await Task.WhenAll(Leer("core_course_get_contents", Parametros(), ct),
            Leer("core_enrol_get_enrolled_users", Parametros(), ct), Leer("gradereport_user_get_grade_items", Parametros(), ct));
        try
        {
            return AulaMoodle.Leer(id, codigo, partes[0], partes[1], partes[2], ReglasTipos.De(config));
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or FormatException or ArgumentException or System.Xml.XmlException or RegexMatchTimeoutException)
        {
            throw new ApiException(409, "El curso contiene datos incompletos o ambiguos. No se aplicaron cambios.");
        }
    }
}
