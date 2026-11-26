using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Xunit;
namespace EduMonitor.IntegrationTests;

// Fuente de los datos académicos: con Moodle como fuente nadie edita a mano (ni siquiera un curso sin vincular); en modo Manual sí.
[Collection("Sistema")]
public sealed class FuenteTests : IAsyncLifetime
{
    private readonly string run = "IT" + Guid.NewGuid().ToString("N")[..12];
    private readonly List<string> letras = [];
    private readonly HttpClient admin = Client();
    private const string TeacherPassword = "IntegrationTeacher9!";
    private static HttpClient Client() => new() { BaseAddress = new Uri(Environment.GetEnvironmentVariable("EDUMONITOR_URL") ?? "http://127.0.0.1:5080"), Timeout = TimeSpan.FromSeconds(30) };
    private string estadoPeriodo = "", fuentePrevia = "";

    public async Task InitializeAsync()
    {
        await Login(admin, Environment.GetEnvironmentVariable("ADMIN_EMAIL") ?? throw new Exception("Ejecuta scripts/Test.ps1"), Environment.GetEnvironmentVariable("ADMIN_PASSWORD")!);
        estadoPeriodo = (await admin.GetFromJsonAsync<JsonElement>("/api/periodos")).EnumerateArray().First(p => p.GetProperty("activo").GetBoolean()).GetProperty("estado").GetString()!;
        await Sql("UPDATE EduMonitor_Materias.dbo.Periodos SET Estado = 'En curso' WHERE Activo = 1");
        fuentePrevia = await FuenteDePrueba.Obtener(admin);
    }
    public async Task DisposeAsync()
    {
        await Sql("""
            DECLARE @cursos TABLE (Id int);
            INSERT @cursos SELECT c.Id FROM EduMonitor_Materias.dbo.Cursos c JOIN EduMonitor_Materias.dbo.Paralelos p ON p.Id = c.ParaleloId WHERE p.Letra IN (SELECT value FROM STRING_SPLIT(@letras, ','));
            DELETE FROM EduMonitor_Evaluaciones.dbo.Unidades WHERE CursoId IN (SELECT Id FROM @cursos);
            UPDATE EduMonitor_Materias.dbo.Cursos SET TeacherId = NULL WHERE TeacherId IN (SELECT Id FROM EduMonitor_Usuarios.dbo.Users WHERE Email LIKE @prefix);
            DELETE FROM EduMonitor_Materias.dbo.Cursos WHERE Id IN (SELECT Id FROM @cursos);
            DELETE FROM EduMonitor_Materias.dbo.Paralelos WHERE Letra IN (SELECT value FROM STRING_SPLIT(@letras, ','));
            DELETE FROM EduMonitor_Usuarios.dbo.Users WHERE Email LIKE @prefix AND Role='Teacher';
            """, ("@prefix", run + "%"), ("@letras", string.Join(',', letras)));
        if (estadoPeriodo.Length > 0)
            await Sql("UPDATE EduMonitor_Materias.dbo.Periodos SET Estado = @e WHERE Activo = 1", ("@e", estadoPeriodo));
        if (fuentePrevia.Length > 0)
            await FuenteDePrueba.Cambiar(admin, fuentePrevia);
        admin.Dispose();
    }

    private static async Task Sql(string texto, params (string, object)[] parametros)
    {
        await using var sql = new SqlConnection(Environment.GetEnvironmentVariable("EDUMONITOR_TEST_SQL"));
        await sql.OpenAsync();
        using var cmd = sql.CreateCommand();
        cmd.CommandText = texto;
        foreach (var (n, v) in parametros)
            cmd.Parameters.AddWithValue(n, v);
        await cmd.ExecuteNonQueryAsync();
    }
    private static async Task Login(HttpClient client, string email, string password)
    {
        var r = await SesionDePrueba.Iniciar(client, new { email, password });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString());
    }
    private static async Task<string> Cuerpo(Task<HttpResponseMessage> request, HttpStatusCode esperado)
    {
        using var r = await request;
        var texto = await r.Content.ReadAsStringAsync();
        Assert.True(r.StatusCode == esperado, $"Se esperaba {(int)esperado} y se obtuvo {(int)r.StatusCode}: {texto}");
        return texto;
    }

    [Fact]
    public async Task AcademicSource_MoodleBlocksManualEditingEverywhereAndOnlyAdminCanChangeIt()
    {
        // Curso de un docente de prueba
        var docenteId = (await (await admin.PostAsJsonAsync("/api/docentes", new
        {
            cedula = Cedulas.Generar(), nombres = "Docente", apellidos = "De Prueba", email = $"{run}@example.test", telefono = "0999999999",
            tituloAcademico = "Ingeniero", carreras = new[] { "SOF" }, password = TeacherPassword
        })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        const string alfabeto = "CDEFGHJKMNPQRSTUVWXYZ";
        string letra;
        while (true)
        {
            letra = $"{alfabeto[Random.Shared.Next(alfabeto.Length)]}{alfabeto[Random.Shared.Next(alfabeto.Length)]}";
            using var r = await admin.PostAsJsonAsync("/api/paralelos", new { carrera = "SOF", nivel = 1, letra });
            if (r.StatusCode == HttpStatusCode.Conflict)
                continue;
            Assert.Equal(HttpStatusCode.Created, r.StatusCode);
            letras.Add(letra);
            break;
        }
        var curso = (await admin.GetFromJsonAsync<JsonElement>($"/api/cursos?carrera=SOF&nivel=1&paralelo={letra}")).EnumerateArray().First().GetProperty("id").GetInt32();
        await Cuerpo(admin.PutAsJsonAsync($"/api/cursos/{curso}/docente", new { teacherId = docenteId }), HttpStatusCode.OK);
        using var docente = Client();
        await Login(docente, $"{run}@example.test", TeacherPassword);
        var unidad = new { cursoId = curso, nombre = "Unidad de prueba", descripcion = (string?)null };

        // Con Moodle como fuente un curso sin vincular tampoco se edita a mano
        await FuenteDePrueba.Cambiar(admin, "Moodle");
        var estado = await docente.GetFromJsonAsync<JsonElement>("/api/moodle/fuente");
        Assert.Equal("Moodle", estado.GetProperty("fuente").GetString());
        var rechazo = await Cuerpo(docente.PostAsJsonAsync("/api/unidades", unidad), HttpStatusCode.Conflict);
        Assert.Contains("Moodle", rechazo);
        Assert.Contains("vincule", rechazo);
        // Consultar sigue permitido
        Assert.Equal(0, (await docente.GetFromJsonAsync<JsonElement>($"/api/unidades?cursoId={curso}")).GetArrayLength());

        // Solo el administrador cambia la fuente, y solo a un valor válido
        await Cuerpo(docente.PutAsJsonAsync("/api/moodle/fuente", new { fuente = "Manual" }), HttpStatusCode.Forbidden);
        await Cuerpo(admin.PutAsJsonAsync("/api/moodle/fuente", new { fuente = "Otra" }), HttpStatusCode.BadRequest);
        using (var sinSesion = Client())
            await Cuerpo(sinSesion.GetAsync("/api/moodle/fuente"), HttpStatusCode.Unauthorized);

        // En modo Manual el docente vuelve a registrar sus datos
        await FuenteDePrueba.Cambiar(admin, "Manual");
        Assert.Equal("Manual", (await docente.GetFromJsonAsync<JsonElement>("/api/moodle/fuente")).GetProperty("fuente").GetString());
        await Cuerpo(docente.PostAsJsonAsync("/api/unidades", unidad), HttpStatusCode.Created);
    }
}
