using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Xunit;
[assembly: CollectionBehavior(DisableTestParallelization = true)]
namespace EduMonitor.IntegrationTests;

// HTTP real -> Nginx -> YARP -> microservicios -> SQL Server. No mocks ni EF InMemory.
public sealed class SprintTests : IAsyncLifetime
{
    private readonly string run = "IT" + Guid.NewGuid().ToString("N")[..12];
    private readonly HttpClient admin = Client();
    private const string TeacherPassword = "IntegrationTeacher9!";
    private static HttpClient Client() => new() { BaseAddress = new Uri(Environment.GetEnvironmentVariable("EDUMONITOR_URL") ?? "http://127.0.0.1:5080"), Timeout = TimeSpan.FromSeconds(20) };
    public async Task InitializeAsync() => await Login(admin, Environment.GetEnvironmentVariable("ADMIN_EMAIL") ?? throw new Exception("Ejecuta scripts/Test.ps1"), Environment.GetEnvironmentVariable("ADMIN_PASSWORD")!);
    public async Task DisposeAsync()
    {
        // Solo elimina filas con el identificador aleatorio exclusivo de esta prueba.
        await using var sql = new SqlConnection(Environment.GetEnvironmentVariable("EDUMONITOR_TEST_SQL"));
        await sql.OpenAsync();
        using var cmd = sql.CreateCommand();
        cmd.CommandText = """
            DELETE FROM EduMonitor_Estudiantes.dbo.Enrollments WHERE StudentId IN (SELECT Id FROM EduMonitor_Estudiantes.dbo.Students WHERE StudentCode LIKE @prefix) OR MateriaId IN (SELECT Id FROM EduMonitor_Materias.dbo.Materias WHERE Code LIKE @prefix);
            DELETE FROM EduMonitor_Estudiantes.dbo.Students WHERE StudentCode LIKE @prefix;
            DELETE FROM EduMonitor_Materias.dbo.Materias WHERE Code LIKE @prefix;
            DELETE FROM EduMonitor_Usuarios.dbo.Users WHERE Email LIKE @prefix AND Role='Teacher';
            """;
        cmd.Parameters.AddWithValue("@prefix", run + "%");
        await cmd.ExecuteNonQueryAsync();
        admin.Dispose();
    }
    private static async Task<JsonElement> Login(HttpClient client, string email, string password)
    {
        var r = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email,
            password
        });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var json = await r.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new("Bearer", json.GetProperty("token").GetString());
        return json;
    }
    private async Task<int> Create(string path, object body)
    {
        var r = await admin.PostAsJsonAsync(path, body);
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
    }
    private Task<int> Teacher(string suffix = "A") => Create("/api/docentes", new { name = "Docente de prueba", email = $"{run}{suffix}@example.test", password = TeacherPassword });
    private Task<int> Materia(int? teacher = null, string suffix = "A") => Create("/api/materias", new { code = run + suffix, name = "Materia de prueba", academicPeriod = "2026-2027", teacherId = teacher });
    private Task<int> Student(string suffix = "A") => Create("/api/estudiantes", new { studentCode = run + suffix, firstNames = "Ana María", lastNames = "Pérez Torres" });
    private static async Task Status(HttpStatusCode expected, Task<HttpResponseMessage> request) => Assert.Equal(expected, (await request).StatusCode);
    private async Task<int> SqlCount(string query, int id)
    {
        await using var sql = new SqlConnection(Environment.GetEnvironmentVariable("EDUMONITOR_TEST_SQL"));
        await sql.OpenAsync();
        using var cmd = sql.CreateCommand();
        cmd.CommandText = query;
        cmd.Parameters.AddWithValue("@id", id);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }
    [Fact]
    public async Task HU01_LoginValidationRolesAndLogout()
    {
        using var anon = Client();
        await Status(HttpStatusCode.Unauthorized, anon.GetAsync("/api/docentes"));
        await Status(HttpStatusCode.Unauthorized, anon.PostAsJsonAsync("/api/auth/login", new
        {
            email = "unknown@example.test",
            password = "InvalidPassword9!"
        }));
        await Status(HttpStatusCode.BadRequest, anon.PostAsJsonAsync("/api/auth/login", new
        {
            email = "",
            password = ""
        }));
        anon.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "tampered.token.value");
        await Status(HttpStatusCode.Unauthorized, anon.GetAsync("/api/materias"));
        await Teacher();
        using var teacher = Client();
        await Login(teacher, $"{run}A@example.test", TeacherPassword);
        await Status(HttpStatusCode.OK, teacher.GetAsync("/api/auth/me"));
        await Status(HttpStatusCode.Forbidden, teacher.GetAsync("/api/docentes"));
        await Status(HttpStatusCode.NoContent, teacher.PostAsync("/api/auth/logout", null));
        await Status(HttpStatusCode.Unauthorized, teacher.GetAsync("/api/materias"));
        await Status(HttpStatusCode.Unauthorized, teacher.GetAsync("/api/estudiantes"));
    }
    [Fact]
    public async Task HU02_TeacherCreateReadEditDuplicateAndAccess()
    {
        var id = await Teacher();
        await Status(HttpStatusCode.Conflict, admin.PostAsJsonAsync("/api/docentes", new
        {
            name = "Duplicado",
            email = $"{run}A@EXAMPLE.TEST",
            password = TeacherPassword
        }));
        await Status(HttpStatusCode.BadRequest, admin.PostAsJsonAsync("/api/docentes", new
        {
            name = "",
            email = "invalid",
            password = ""
        }));
        await Status(HttpStatusCode.BadRequest, admin.PostAsJsonAsync("/api/docentes", new
        {
            name = "Sin contraseña",
            email = $"{run}B@example.test",
            password = (string?)null
        }));
        await Status(HttpStatusCode.OK, admin.PutAsJsonAsync($"/api/docentes/{id}", new
        {
            name = "Docente actualizado",
            email = $"{run}A@example.test",
            password = (string?)null
        }));
        var item = await admin.GetFromJsonAsync<JsonElement>($"/api/docentes/{id}");
        Assert.Equal("Docente actualizado", item.GetProperty("name").GetString());
        Assert.False(item.TryGetProperty("passwordHash", out _));
        Assert.Equal(1, await SqlCount("SELECT COUNT(*) FROM EduMonitor_Usuarios.dbo.Users WHERE Id=@id AND Name=N'Docente actualizado' AND PasswordHash<>''", id));
        using var teacher = Client();
        await Login(teacher, $"{run}A@example.test", TeacherPassword);
        await Status(HttpStatusCode.Forbidden, teacher.PostAsJsonAsync("/api/docentes", new
        {
            name = "No permitido",
            email = $"{run}C@example.test",
            password = TeacherPassword
        }));
        await Status(HttpStatusCode.Forbidden, teacher.PutAsJsonAsync($"/api/docentes/{id}", new
        {
            name = "No permitido",
            email = $"{run}A@example.test"
        }));
    }
    [Fact]
    public async Task HU03_PeriodRegistrationAssignmentAndIsolation()
    {
        var id = await Materia();
        var t = await Teacher();
        var other = await Teacher("B");
        await Status(HttpStatusCode.Conflict, admin.PostAsJsonAsync("/api/materias", new
        {
            code = run + "A",
            name = "Duplicada",
            academicPeriod = "2026"
        }));
        await Status(HttpStatusCode.BadRequest, admin.PostAsJsonAsync("/api/materias", new
        {
            code = run + "B",
            name = "Sin período",
            academicPeriod = ""
        }));
        await Status(HttpStatusCode.NotFound, admin.PutAsJsonAsync($"/api/materias/{id}/docente", new
        {
            teacherId = int.MaxValue
        }));
        await Status(HttpStatusCode.OK, admin.PutAsJsonAsync($"/api/materias/{id}/docente", new
        {
            teacherId = t
        }));
        await Status(HttpStatusCode.OK, admin.PutAsJsonAsync($"/api/materias/{id}", new
        {
            code = run + "A",
            name = "Materia actualizada",
            academicPeriod = "2027-2028",
            teacherId = t
        }));
        using var teacher = Client();
        await Login(teacher, $"{run}A@example.test", TeacherPassword);
        await Status(HttpStatusCode.OK, teacher.GetAsync($"/api/materias/{id}"));
        var alien = await Materia(other, "B");
        await Status(HttpStatusCode.NotFound, teacher.GetAsync($"/api/materias/{alien}"));
        await Status(HttpStatusCode.Forbidden, teacher.PutAsJsonAsync($"/api/materias/{id}", new
        {
            code = run + "A",
            name = "No permitido",
            academicPeriod = "2027"
        }));
        await Status(HttpStatusCode.OK, admin.PutAsJsonAsync($"/api/materias/{id}/docente", new
        {
            teacherId = other
        }));
        await Status(HttpStatusCode.NotFound, teacher.GetAsync($"/api/materias/{id}"));
        Assert.Equal(1, await SqlCount("SELECT COUNT(*) FROM EduMonitor_Materias.dbo.Materias WHERE Id=@id AND AcademicPeriod='2027-2028'", id));
    }
    [Fact]
    public async Task HU04_StudentsEnrollmentAndTeacherRoster()
    {
        var t = await Teacher();
        var m = await Materia(t);
        var student = await Student();
        await Status(HttpStatusCode.Conflict, admin.PostAsJsonAsync("/api/estudiantes", new
        {
            studentCode = run + "A",
            firstNames = "Otro",
            lastNames = "Estudiante"
        }));
        await Status(HttpStatusCode.BadRequest, admin.PostAsJsonAsync("/api/estudiantes", new
        {
            studentCode = run + "B",
            firstNames = "Ana",
            lastNames = ""
        }));
        await Status(HttpStatusCode.OK, admin.PutAsJsonAsync($"/api/estudiantes/{student}", new
        {
            studentCode = run + "A",
            firstNames = "Ana Lucía",
            lastNames = "Pérez Torres"
        }));
        await Status(HttpStatusCode.NoContent, admin.PostAsJsonAsync("/api/matriculas", new
        {
            materiaId = m,
            studentId = student
        }));
        await Status(HttpStatusCode.Conflict, admin.PostAsJsonAsync("/api/matriculas", new
        {
            materiaId = m,
            studentId = student
        }));
        await Status(HttpStatusCode.NotFound, admin.PostAsJsonAsync("/api/matriculas", new
        {
            materiaId = int.MaxValue,
            studentId = student
        }));
        using var teacher = Client();
        await Login(teacher, $"{run}A@example.test", TeacherPassword);
        var roster = await teacher.GetFromJsonAsync<JsonElement>($"/api/matriculas/materia/{m}");
        Assert.Equal(1, roster.GetArrayLength());
        Assert.Equal("Ana Lucía", roster[0].GetProperty("firstNames").GetString());
        var alien = await Materia(null, "B");
        await Status(HttpStatusCode.NotFound, teacher.GetAsync($"/api/matriculas/materia/{alien}"));
        await Status(HttpStatusCode.Forbidden, teacher.GetAsync("/api/estudiantes"));
        await Status(HttpStatusCode.Forbidden, teacher.PostAsJsonAsync("/api/matriculas", new
        {
            materiaId = m,
            studentId = student
        }));
        Assert.Equal(1, await SqlCount("SELECT COUNT(*) FROM EduMonitor_Estudiantes.dbo.Enrollments WHERE StudentId=@id", student));
    }
    private static async Task Docker(params string[] args)
    {
        var start = new System.Diagnostics.ProcessStartInfo("docker") { WorkingDirectory = Environment.GetEnvironmentVariable("EDUMONITOR_ROOT")!, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("compose");
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        using var p = System.Diagnostics.Process.Start(start)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await p.WaitForExitAsync(timeout.Token);
        Assert.True(p.ExitCode == 0, await stdout + await stderr);
    }
    [Fact]
    public async Task DatabaseIsolation_ServiceLoginCannotReadAnotherDatabase()
    {
        var cs = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("EDUMONITOR_TEST_SQL"))
        {
            UserID = "edumonitor_materias",
            Password = Environment.GetEnvironmentVariable("MATERIAS_PASSWORD"),
            InitialCatalog = "EduMonitor_Materias"
        };
        await using var sql = new SqlConnection(cs.ConnectionString);
        await sql.OpenAsync();
        using var own = sql.CreateCommand();
        own.CommandText = "SELECT COUNT(*) FROM dbo.Materias";
        await own.ExecuteScalarAsync();
        using var other = sql.CreateCommand();
        other.CommandText = "SELECT COUNT(*) FROM EduMonitor_Usuarios.dbo.Users";
        var error = await Assert.ThrowsAsync<SqlException>(async () => await other.ExecuteScalarAsync());
        Assert.Equal(916, error.Number);
    }
    private async Task WaitForStudents()
    {
        for (var i = 0; i < 30; i++)
        {
            using var r = await admin.GetAsync("/api/estudiantes");
            if (r.StatusCode == HttpStatusCode.OK)
                return;
            await Task.Delay(500);
        }
        Assert.Fail("Estudiantes no se recuperó dentro del plazo.");
    }
    [Fact, Trait("Category", "Availability")]
    public async Task Failure_StudentsStopsInIsolationAndRecoversPersistentData()
    {
        var student = await Student();
        var materia = await Materia();
        await Status(HttpStatusCode.NoContent, admin.PostAsJsonAsync("/api/matriculas", new
        {
            materiaId = materia,
            studentId = student
        }));
        try
        {
            await Docker("stop", "estudiantes");
            await Status(HttpStatusCode.OK, admin.GetAsync("/"));
            using var login = Client();
            await Login(login, Environment.GetEnvironmentVariable("ADMIN_EMAIL")!, Environment.GetEnvironmentVariable("ADMIN_PASSWORD")!);
            await Status(HttpStatusCode.OK, admin.GetAsync("/api/docentes"));
            await Status(HttpStatusCode.OK, admin.GetAsync("/api/materias"));
            var watch = System.Diagnostics.Stopwatch.StartNew();
            await Status(HttpStatusCode.ServiceUnavailable, admin.GetAsync("/api/estudiantes"));
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(15));
            await Status(HttpStatusCode.ServiceUnavailable, admin.PostAsJsonAsync("/api/estudiantes", new
            {
                studentCode = run + "B",
                firstNames = "No debe",
                lastNames = "Guardarse"
            }));
        }
        finally { await Docker("start", "estudiantes"); await WaitForStudents(); }
        await Status(HttpStatusCode.OK, admin.GetAsync($"/api/estudiantes/{student}"));
        var roster = await admin.GetFromJsonAsync<JsonElement>($"/api/matriculas/materia/{materia}");
        Assert.Equal(student, roster[0].GetProperty("id").GetInt32());
        Assert.Equal(1, await SqlCount("SELECT COUNT(*) FROM EduMonitor_Estudiantes.dbo.Students WHERE Id=@id", student));
    }
    [Fact, Trait("Category", "Availability")]
    public async Task Failure_MateriasUnavailableRejectsEnrollmentWithoutSaving()
    {
        var student = await Student();
        var materia = await Materia();
        try
        {
            await Docker("stop", "materias");
            await Status(HttpStatusCode.OK, admin.GetAsync("/api/estudiantes"));
            await Status(HttpStatusCode.ServiceUnavailable, admin.PostAsJsonAsync("/api/matriculas", new
            {
                materiaId = materia,
                studentId = student
            }));
            Assert.Equal(0, await SqlCount("SELECT COUNT(*) FROM EduMonitor_Estudiantes.dbo.Enrollments WHERE StudentId=@id", student));
        }
        finally
        {
            await Docker("start", "materias");
            for (var i = 0; i < 30; i++)
            {
                using var r = await admin.GetAsync("/api/materias");
                if (r.IsSuccessStatusCode)
                    break;
                await Task.Delay(500);
            }
        }
        await Status(HttpStatusCode.NoContent, admin.PostAsJsonAsync("/api/matriculas", new
        {
            materiaId = materia,
            studentId = student
        }));
    }
}
