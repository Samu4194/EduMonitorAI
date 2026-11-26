using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Xunit;
[assembly: CollectionBehavior(DisableTestParallelization = true)]
namespace EduMonitor.IntegrationTests;

// HTTP real -> Nginx -> YARP -> microservicios -> SQL Server. No mocks ni EF InMemory.
[Collection("Sistema")]
public sealed class SprintTests : IAsyncLifetime
{
    private readonly string run = "IT" + Guid.NewGuid().ToString("N")[..12];
    // Cédulas de prueba: 97 + 5 dígitos de la corrida + contador (docentes) y 98 + lo mismo (estudiantes). No existen en la vida real.
    private readonly string digitos = Random.Shared.Next(0, 100000).ToString("D5");
    private readonly List<string> letras = [];
    private readonly HttpClient admin = Client();
    private const string TeacherPassword = "IntegrationTeacher9!";
    private static HttpClient Client() => new() { BaseAddress = new Uri(Environment.GetEnvironmentVariable("EDUMONITOR_URL") ?? "http://127.0.0.1:5080"), Timeout = TimeSpan.FromSeconds(20) };
    public async Task InitializeAsync() => await Login(admin, Environment.GetEnvironmentVariable("ADMIN_EMAIL") ?? throw new Exception("Ejecuta scripts/Test.ps1"), Environment.GetEnvironmentVariable("ADMIN_PASSWORD")!);
    public async Task DisposeAsync()
    {
        // Solo elimina filas con el identificador exclusivo de esta prueba: correos, cédulas y paralelos creados por ella.
        await using var sql = new SqlConnection(Environment.GetEnvironmentVariable("EDUMONITOR_TEST_SQL"));
        await sql.OpenAsync();
        using var cmd = sql.CreateCommand();
        cmd.CommandText = """
            UPDATE EduMonitor_Materias.dbo.Cursos SET TeacherId = NULL WHERE TeacherId IN (SELECT Id FROM EduMonitor_Usuarios.dbo.Users WHERE Email LIKE @prefix);
            DELETE c FROM EduMonitor_Materias.dbo.Cursos c JOIN EduMonitor_Materias.dbo.Paralelos p ON p.Id = c.ParaleloId WHERE p.Letra IN (SELECT value FROM STRING_SPLIT(@letras, ','));
            DELETE FROM EduMonitor_Materias.dbo.Paralelos WHERE Letra IN (SELECT value FROM STRING_SPLIT(@letras, ','));
            DELETE FROM EduMonitor_Estudiantes.dbo.Students WHERE Cedula LIKE @estudiantes;
            DELETE FROM EduMonitor_Usuarios.dbo.Users WHERE Email LIKE @prefix AND Role='Teacher';
            """;
        cmd.Parameters.AddWithValue("@prefix", run + "%");
        cmd.Parameters.AddWithValue("@letras", string.Join(',', letras));
        cmd.Parameters.AddWithValue("@estudiantes", Cedulas.Prefijo + "%");
        await cmd.ExecuteNonQueryAsync();
        admin.Dispose();
    }
    private static async Task<JsonElement> Login(HttpClient client, string email, string password)
    {
        var r = await SesionDePrueba.Iniciar(client, new { email, password });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var json = await r.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new("Bearer", json.GetProperty("token").GetString());
        return json;
    }
    private static async Task<JsonElement> Body(Task<HttpResponseMessage> request) => await (await request).Content.ReadFromJsonAsync<JsonElement>();
    private static async Task Status(HttpStatusCode expected, Task<HttpResponseMessage> request)
    {
        using var r = await request;
        Assert.True(r.StatusCode == expected, $"Se esperaba {(int)expected} y se obtuvo {(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
    }
    private async Task<int> Create(string path, object body)
    {
        var r = await admin.PostAsJsonAsync(path, body);
        Assert.True(r.StatusCode == HttpStatusCode.Created, $"{path}: {(int)r.StatusCode} {await r.Content.ReadAsStringAsync()}");
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
    }
    private static string CedulaDocente() => Cedulas.Generar();
    private static string CedulaEstudiante() => Cedulas.Generar();
    private object Ficha(string suffix, string cedula, params string[] carreras) => new
    {
        cedula,
        nombres = "Docente",
        apellidos = "De Prueba " + suffix,
        email = $"{run}{suffix}@example.test",
        telefono = "0999999999",
        tituloAcademico = "Ingeniero en Sistemas",
        carreras = carreras.Length > 0 ? carreras : ["SOF"],
        password = TeacherPassword
    };
    private Task<int> Teacher(string suffix = "A", params string[] carreras) => Create("/api/docentes", Ficha(suffix, CedulaDocente(), carreras));
    private async Task<HttpClient> TeacherClient(string suffix = "A")
    {
        var c = Client();
        await Login(c, $"{run}{suffix}@example.test", TeacherPassword);
        return c;
    }
    // Crea un paralelo de prueba (con sus cursos) en una carrera y un nivel. Las letras raras evitan chocar con los paralelos A y B de demostración.
    private async Task<string> Paralelo(string carrera = "SOF", int nivel = 1)
    {
        const string alfabeto = "CDEFGHJKMNPQRSTUVWXYZ";
        for (var i = 0; i < 10; i++)
        {
            var letra = $"{alfabeto[Random.Shared.Next(alfabeto.Length)]}{alfabeto[Random.Shared.Next(alfabeto.Length)]}";
            using var r = await admin.PostAsJsonAsync("/api/paralelos", new { carrera, nivel, letra });
            if (r.StatusCode == HttpStatusCode.Conflict)
                continue;
            Assert.Equal(HttpStatusCode.Created, r.StatusCode);
            letras.Add(letra);
            return letra;
        }
        throw new Exception("No se pudo crear un paralelo de prueba.");
    }
    private async Task<JsonElement[]> Cursos(string letra, string carrera = "SOF", int nivel = 1)
    {
        var json = await admin.GetFromJsonAsync<JsonElement>($"/api/cursos?carrera={carrera}&nivel={nivel}&paralelo={letra}");
        return json.EnumerateArray().ToArray();
    }
    private object Estudiante(string cedula, string letra, string carrera = "SOF", int nivel = 1, string apellidos = "Pérez Torres") => new
    {
        cedula,
        apellidos,
        nombres = "Ana María",
        correo = $"{cedula}@example.test",
        carrera,
        nivel,
        paralelo = letra
    };
    private async Task<int> SqlCount(string query, params (string, object)[] parametros)
    {
        await using var sql = new SqlConnection(Environment.GetEnvironmentVariable("EDUMONITOR_TEST_SQL"));
        await sql.OpenAsync();
        using var cmd = sql.CreateCommand();
        cmd.CommandText = query;
        foreach (var (nombre, valor) in parametros)
            cmd.Parameters.AddWithValue(nombre, valor);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }
    // La carga CSV siempre lleva destino: carrera, nivel, paralelo y materia (código).
    private static async Task<HttpResponseMessage> Precarga(HttpClient client, string csv, string? paralelo = null, string? materia = null)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray()), "archivo", "estudiantes.csv");
        if (paralelo is not null && materia is not null)
        {
            form.Add(new StringContent("SOF"), "carrera");
            form.Add(new StringContent("1"), "nivel");
            form.Add(new StringContent(paralelo), "paralelo");
            form.Add(new StringContent(materia), "materia");
        }
        return await client.PostAsync("/api/estudiantes/precarga", form);
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
        await Status(HttpStatusCode.Unauthorized, anon.GetAsync("/api/cursos"));
        await Teacher("A", "SOF", "TI");
        using var teacher = Client();
        var sesion = await Login(teacher, $"{run}A@example.test", TeacherPassword);
        // El token y la respuesta del login llevan las carreras del docente: con ellas se segmenta el acceso.
        Assert.Equal(["SOF", "TI"], sesion.GetProperty("user").GetProperty("carreras").EnumerateArray().Select(x => x.GetString()!).ToArray());
        await Status(HttpStatusCode.OK, teacher.GetAsync("/api/auth/me"));
        await Status(HttpStatusCode.Forbidden, teacher.GetAsync("/api/docentes"));
        await Status(HttpStatusCode.Forbidden, teacher.GetAsync("/api/estudiantes"));
        await Status(HttpStatusCode.OK, teacher.GetAsync("/api/cursos"));
        await Status(HttpStatusCode.NoContent, teacher.PostAsync("/api/auth/logout", null));
        await Status(HttpStatusCode.Unauthorized, teacher.GetAsync("/api/cursos"));
        await Status(HttpStatusCode.Unauthorized, teacher.GetAsync("/api/carreras"));
    }
    [Fact]
    public async Task HU02_TeacherProfileCreateReadEditDuplicateAndAccess()
    {
        var cedula = CedulaDocente();
        var id = await Create("/api/docentes", Ficha("A", cedula, "SOF"));
        var creado = await admin.GetFromJsonAsync<JsonElement>($"/api/docentes/{id}");
        Assert.Equal(cedula, creado.GetProperty("cedula").GetString());
        Assert.Equal("De Prueba A", creado.GetProperty("apellidos").GetString());
        Assert.Equal("Ingeniero en Sistemas", creado.GetProperty("tituloAcademico").GetString());
        Assert.Equal(["SOF"], creado.GetProperty("carreras").EnumerateArray().Select(x => x.GetString()!).ToArray());
        // Duplicados y datos inválidos
        await Status(HttpStatusCode.Conflict, admin.PostAsJsonAsync("/api/docentes", new
        {
            cedula = CedulaDocente(), nombres = "Otro", apellidos = "Docente", email = $"{run}A@EXAMPLE.TEST", carreras = new[] { "SOF" }, password = TeacherPassword
        }));
        await Status(HttpStatusCode.Conflict, admin.PostAsJsonAsync("/api/docentes", new
        {
            cedula, nombres = "Otro", apellidos = "Docente", email = $"{run}C@example.test", carreras = new[] { "SOF" }, password = TeacherPassword
        }));
        await Status(HttpStatusCode.BadRequest, admin.PostAsJsonAsync("/api/docentes", new
        {
            cedula = "123", nombres = "", apellidos = "", email = "invalid", carreras = Array.Empty<string>(), password = ""
        }));
        await Status(HttpStatusCode.BadRequest, admin.PostAsJsonAsync("/api/docentes", new
        {
            cedula = CedulaDocente(), nombres = "Sin", apellidos = "Carrera", email = $"{run}D@example.test", carreras = Array.Empty<string>(), password = TeacherPassword
        }));
        await Status(HttpStatusCode.BadRequest, admin.PostAsJsonAsync("/api/docentes", new
        {
            cedula = CedulaDocente(), nombres = "Sin", apellidos = "Contraseña", email = $"{run}E@example.test", carreras = new[] { "SOF" }, password = (string?)null
        }));
        // La cédula debe ser ecuatoriana: solo números, 10 dígitos, provincia existente y dígito verificador correcto
        foreach (var invalida in new[] { Cedulas.ConDigitoErroneo(CedulaDocente()), "2559123456", "2469123456", "24591234AB", "245912345", "24591234567", "2459 12345", "" })
            await Status(HttpStatusCode.BadRequest, admin.PostAsJsonAsync("/api/docentes", Ficha("V", invalida, "SOF")));
        Assert.Equal(0, await SqlCount("SELECT COUNT(*) FROM EduMonitor_Usuarios.dbo.Users WHERE Email=@e", ("@e", $"{run}V@example.test")));
        var mensaje = (await Body(admin.PostAsJsonAsync("/api/docentes", Ficha("V", Cedulas.ConDigitoErroneo(CedulaDocente()), "SOF")))).ToString();
        Assert.Contains("dígito verificador", mensaje);
        // Una sesión abierta queda revocada cuando cambian las carreras del docente (el token las lleva).
        using var teacher = await TeacherClient("A");
        await Status(HttpStatusCode.OK, teacher.GetAsync("/api/cursos"));
        await Status(HttpStatusCode.OK, admin.PutAsJsonAsync($"/api/docentes/{id}", new
        {
            cedula, nombres = "Docente", apellidos = "Actualizado", email = $"{run}A@example.test", telefono = (string?)null, tituloAcademico = (string?)null,
            carreras = new[] { "SOF", "TI" }, password = (string?)null
        }));
        await Status(HttpStatusCode.Unauthorized, teacher.GetAsync("/api/cursos"));
        var editado = await admin.GetFromJsonAsync<JsonElement>($"/api/docentes/{id}");
        Assert.Equal("Actualizado", editado.GetProperty("apellidos").GetString());
        Assert.Equal(["SOF", "TI"], editado.GetProperty("carreras").EnumerateArray().Select(x => x.GetString()!).ToArray());
        Assert.False(editado.TryGetProperty("passwordHash", out _));
        Assert.Equal(1, await SqlCount("SELECT COUNT(*) FROM EduMonitor_Usuarios.dbo.TeacherProfiles p JOIN EduMonitor_Usuarios.dbo.Users u ON u.Id = p.UserId WHERE p.UserId=@id AND p.Apellidos=N'Actualizado' AND u.PasswordHash<>''", ("@id", id)));
        // El filtro por carrera sirve para elegir docentes de esa carrera al asignar cursos.
        var enTi = await admin.GetFromJsonAsync<JsonElement>("/api/docentes?carrera=TI");
        Assert.Contains(enTi.EnumerateArray(), x => x.GetProperty("id").GetInt32() == id);
        var enAut = await admin.GetFromJsonAsync<JsonElement>("/api/docentes?carrera=AUT");
        Assert.DoesNotContain(enAut.EnumerateArray(), x => x.GetProperty("id").GetInt32() == id);
        using var teacher2 = await TeacherClient("A");
        await Status(HttpStatusCode.Forbidden, teacher2.PostAsJsonAsync("/api/docentes", Ficha("F", CedulaDocente(), "SOF")));
        await Status(HttpStatusCode.Forbidden, teacher2.PutAsJsonAsync($"/api/docentes/{id}", Ficha("A", cedula, "SOF")));
    }
    [Fact]
    public async Task HU03_CatalogParallelsAndAssignmentBySegmentedCareer()
    {
        // Catálogo cargado desde las mallas curriculares
        var carreras = await admin.GetFromJsonAsync<JsonElement>("/api/carreras");
        // Las cinco carreras del catálogo inicial; el administrador puede haber creado otras después
        Assert.All(new[] { "SOF", "TI", "TEL", "IND", "AUT" }, codigo => Assert.Contains(carreras.EnumerateArray(), x => x.GetProperty("codigo").GetString() == codigo));
        var software = carreras.EnumerateArray().Single(x => x.GetProperty("codigo").GetString() == "SOF");
        Assert.Equal(43, software.GetProperty("materias").GetInt32());
        Assert.Equal(135, software.GetProperty("creditosTotales").GetInt32());
        var nivel1 = (await admin.GetFromJsonAsync<JsonElement>("/api/carreras/SOF/materias?nivel=1")).EnumerateArray().ToArray();
        Assert.Equal(5, nivel1.Length);
        Assert.Equal(720, nivel1.Sum(x => x.GetProperty("horasTotales").GetInt32()));
        Assert.Equal(15, nivel1.Sum(x => x.GetProperty("creditos").GetInt32()));
        await Status(HttpStatusCode.NotFound, admin.GetAsync("/api/carreras/XXX/materias"));
        var docente = await Teacher("A", "SOF");
        var enOtraCarrera = await Teacher("B", "TI");
        using var teacher = await TeacherClient("A");
        // Un docente solo ve las carreras a las que pertenece
        var suyas = await teacher.GetFromJsonAsync<JsonElement>("/api/carreras");
        Assert.Equal(["SOF"], suyas.EnumerateArray().Select(x => x.GetProperty("codigo").GetString()!).ToArray());
        await Status(HttpStatusCode.NotFound, teacher.GetAsync("/api/carreras/TI/materias"));
        // Paralelos y cursos
        var letra = await Paralelo("SOF", 1);
        await Status(HttpStatusCode.Conflict, admin.PostAsJsonAsync("/api/paralelos", new { carrera = "SOF", nivel = 1, letra }));
        await Status(HttpStatusCode.BadRequest, admin.PostAsJsonAsync("/api/paralelos", new { carrera = "SOF", nivel = 10, letra = "ZZ" }));
        await Status(HttpStatusCode.BadRequest, admin.PostAsJsonAsync("/api/paralelos", new { carrera = "SOF", nivel = 1, letra = "1" }));
        await Status(HttpStatusCode.Forbidden, teacher.PostAsJsonAsync("/api/paralelos", new { carrera = "SOF", nivel = 1, letra = "ZZ" }));
        var cursos = await Cursos(letra);
        Assert.Equal(5, cursos.Length);
        Assert.All(cursos, c => Assert.Equal(JsonValueKind.Null, c.GetProperty("teacherId").ValueKind));
        var curso = cursos[0].GetProperty("id").GetInt32();
        var otro = cursos[1].GetProperty("id").GetInt32();
        // Asignación: el docente debe existir y pertenecer a la carrera de la materia
        await Status(HttpStatusCode.NotFound, admin.PutAsJsonAsync($"/api/cursos/{curso}/docente", new { teacherId = int.MaxValue }));
        await Status(HttpStatusCode.Conflict, admin.PutAsJsonAsync($"/api/cursos/{curso}/docente", new { teacherId = enOtraCarrera }));
        await Status(HttpStatusCode.Forbidden, teacher.PutAsJsonAsync($"/api/cursos/{curso}/docente", new { teacherId = docente }));
        Assert.Equal(docente, (await Body(admin.PutAsJsonAsync($"/api/cursos/{curso}/docente", new { teacherId = docente }))).GetProperty("teacherId").GetInt32());
        Assert.Equal(1, await SqlCount("SELECT COUNT(*) FROM EduMonitor_Materias.dbo.Cursos WHERE Id=@id AND TeacherId=@t", ("@id", curso), ("@t", docente)));
        // El docente solo ve los cursos que tiene asignados
        var mios = (await teacher.GetFromJsonAsync<JsonElement>("/api/cursos")).EnumerateArray().ToArray();
        Assert.Single(mios);
        Assert.Equal(curso, mios[0].GetProperty("id").GetInt32());
        await Status(HttpStatusCode.OK, teacher.GetAsync($"/api/cursos/{curso}"));
        await Status(HttpStatusCode.NotFound, teacher.GetAsync($"/api/cursos/{otro}"));
        // Quitar el docente
        Assert.Equal(JsonValueKind.Null, (await Body(admin.PutAsJsonAsync($"/api/cursos/{curso}/docente", new { teacherId = (int?)null }))).GetProperty("teacherId").ValueKind);
        await Status(HttpStatusCode.NotFound, teacher.GetAsync($"/api/cursos/{curso}"));
    }
    [Fact]
    public async Task HU04_StudentsPreloadByParallelAndTeacherList()
    {
        var letra = await Paralelo("SOF", 1);
        var otraLetra = await Paralelo("SOF", 1);
        var docente = await Teacher("A", "SOF");
        var cursos = await Cursos(letra);
        var curso = cursos[0].GetProperty("id").GetInt32();
        var materia = (await admin.GetFromJsonAsync<JsonElement>($"/api/cursos/{curso}")).GetProperty("materiaCodigo").GetString()!;
        var cursoAjeno = (await Cursos(otraLetra))[0].GetProperty("id").GetInt32();
        await Status(HttpStatusCode.OK, admin.PutAsJsonAsync($"/api/cursos/{curso}/docente", new { teacherId = docente }));
        // Registro manual
        var cedula = CedulaEstudiante();
        var id = await Create("/api/estudiantes", Estudiante(cedula, letra));
        await Status(HttpStatusCode.Conflict, admin.PostAsJsonAsync("/api/estudiantes", Estudiante(cedula, letra, apellidos: "Otro")));
        await Status(HttpStatusCode.BadRequest, admin.PostAsJsonAsync("/api/estudiantes", Estudiante("12", letra)));
        foreach (var invalida in new[] { Cedulas.ConDigitoErroneo(CedulaEstudiante()), "2559123456", "2469123456", "24591234AB", "245912345", "24591234567", "2459 12345" })
            await Status(HttpStatusCode.BadRequest, admin.PostAsJsonAsync("/api/estudiantes", Estudiante(invalida, letra)));
        await Status(HttpStatusCode.BadRequest, admin.PostAsJsonAsync("/api/estudiantes", Estudiante(CedulaEstudiante(), "AA", nivel: 10)));
        await Status(HttpStatusCode.BadRequest, admin.PostAsJsonAsync("/api/estudiantes", Estudiante(CedulaEstudiante(), "AB")));
        await Status(HttpStatusCode.BadRequest, admin.PostAsJsonAsync("/api/estudiantes", Estudiante(CedulaEstudiante(), letra, carrera: "NOEXISTE")));
        await Status(HttpStatusCode.OK, admin.PutAsJsonAsync($"/api/estudiantes/{id}", Estudiante(cedula, letra, apellidos: "Pérez Actualizado")));
        Assert.Equal(1, await SqlCount("SELECT COUNT(DISTINCT s.Id) FROM EduMonitor_Estudiantes.dbo.Students s JOIN EduMonitor_Estudiantes.dbo.Inscripciones i ON i.StudentId = s.Id WHERE s.Id=@id AND s.Apellidos=N'Pérez Actualizado' AND i.Paralelo=@p", ("@id", id), ("@p", letra)));
        // Precarga por CSV: crea dos, actualiza una (misma cédula). La carrera puede ir por nombre y el nivel en romano.
        var nueva1 = CedulaEstudiante();
        var nueva2 = CedulaEstudiante();
        var csv = $"cedula,apellidos,nombres,correo\n{cedula},Pérez Cargado,Ana,{cedula}@example.test\n{nueva1},Mora Cruz,Luis,{nueva1}@example.test\n{nueva2},Sáenz Díaz,Eva,{nueva2}@example.test\n";
        Assert.Equal(HttpStatusCode.BadRequest, (await Precarga(admin, csv)).StatusCode); // sin destino no se carga
        var ok = await Precarga(admin, csv, letra, materia);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var resultado = await ok.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, resultado.GetProperty("creados").GetInt32());
        Assert.Equal(1, resultado.GetProperty("actualizados").GetInt32());
        // Punto y coma (Excel en español) y encabezado con tilde
        var conPuntoYComa = $"Cédula;Apellidos;Nombres;Correo\n{CedulaEstudiante()};Ñandú Pérez;José;a@example.test\n";
        Assert.Equal(HttpStatusCode.OK, (await Precarga(admin, conPuntoYComa, letra, materia)).StatusCode);
        // Con errores no se guarda nada
        var antes = await SqlCount("SELECT COUNT(DISTINCT StudentId) FROM EduMonitor_Estudiantes.dbo.Inscripciones WHERE Paralelo=@p", ("@p", letra));
        var malo = $"cedula,apellidos,nombres,correo\n{CedulaEstudiante()},Bien Hecho,Ana,x@example.test\n123,,Pedro,no-es-correo\n{CedulaEstudiante()},Sin Correo,Eva,\n";
        var error = await Precarga(admin, malo, letra, materia);
        Assert.Equal(HttpStatusCode.BadRequest, error.StatusCode);
        var mensaje = (await error.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("message").GetString()!;
        Assert.Contains("Fila 3", mensaje);
        Assert.Contains("Fila 4", mensaje);
        Assert.Equal(antes, await SqlCount("SELECT COUNT(DISTINCT StudentId) FROM EduMonitor_Estudiantes.dbo.Inscripciones WHERE Paralelo=@p", ("@p", letra)));
        Assert.Equal(HttpStatusCode.BadRequest, (await Precarga(admin, "cedula,apellidos\n1,2\n", letra, materia)).StatusCode);
        // En el CSV una cédula con letras o con dígito verificador incorrecto es un error de esa fila y no se guarda nada
        var conLetras = $"cedula,apellidos,nombres,correo\n{CedulaEstudiante()},Bien,Ana,a@example.test\n2459-12345,Con Guion,Eva,b@example.test\n{Cedulas.ConDigitoErroneo(CedulaEstudiante())},Mal Digito,Eva,c@example.test\n";
        var rechazo = await Precarga(admin, conLetras, letra, materia);
        Assert.Equal(HttpStatusCode.BadRequest, rechazo.StatusCode);
        var detalleRechazo = (await rechazo.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("message").GetString()!;
        Assert.Contains("Fila 3", detalleRechazo);
        Assert.Contains("Fila 4", detalleRechazo);
        Assert.Contains("dígito verificador", detalleRechazo);
        Assert.Equal(antes, await SqlCount("SELECT COUNT(DISTINCT StudentId) FROM EduMonitor_Estudiantes.dbo.Inscripciones WHERE Paralelo=@p", ("@p", letra)));
        // Lista por curso: el docente asignado ve a su paralelo, sin cédula; un curso ajeno responde 404
        using var teacher = await TeacherClient("A");
        var lista = (await teacher.GetFromJsonAsync<JsonElement>($"/api/estudiantes/curso/{curso}")).EnumerateArray().ToArray();
        Assert.Equal(antes, lista.Length);
        Assert.Contains(lista, x => x.GetProperty("apellidos").GetString() == "Mora Cruz");
        Assert.All(lista, x => Assert.False(x.TryGetProperty("cedula", out _)));
        await Status(HttpStatusCode.NotFound, teacher.GetAsync($"/api/estudiantes/curso/{cursoAjeno}"));
        await Status(HttpStatusCode.Forbidden, teacher.GetAsync("/api/estudiantes"));
        await Status(HttpStatusCode.Forbidden, Precarga(teacher, csv, letra, materia));
        // Sin la matrícula: ya no existe esa operación
        await Status(HttpStatusCode.NotFound, admin.PostAsJsonAsync("/api/matriculas", new { materiaId = 1, studentId = id }));
        var enParalelo = await admin.GetFromJsonAsync<JsonElement>($"/api/estudiantes?carrera=SOF&nivel=1&paralelo={letra}");
        Assert.Equal(antes, enParalelo.GetArrayLength());
    }
    private static async Task Docker(params string[] args)
    {
        var start = new System.Diagnostics.ProcessStartInfo("docker") { WorkingDirectory = Environment.GetEnvironmentVariable("EDUMONITOR_ROOT")!, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("compose");
        // Debe ser el mismo archivo con el que se levantó el sistema (EDUMONITOR_COMPOSE_FILE, p. ej. compose.express.yaml).
        // Con otro, Compose arrancaría también servicios que esa configuración no usa, como el SQL Server de Docker.
        if (Environment.GetEnvironmentVariable("EDUMONITOR_COMPOSE_FILE") is { Length: > 0 } file)
        {
            start.ArgumentList.Add("-f");
            start.ArgumentList.Add(file);
        }
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
            // Debe conectarse con el login SQL del servicio aunque la conexión de pruebas use autenticación de Windows.
            IntegratedSecurity = false,
            UserID = "edumonitor_materias",
            Password = Environment.GetEnvironmentVariable("MATERIAS_PASSWORD"),
            InitialCatalog = "EduMonitor_Materias"
        };
        await using var sql = new SqlConnection(cs.ConnectionString);
        await sql.OpenAsync();
        using var own = sql.CreateCommand();
        own.CommandText = "SELECT COUNT(*) FROM dbo.Carreras";
        await own.ExecuteScalarAsync();
        using var other = sql.CreateCommand();
        other.CommandText = "SELECT COUNT(*) FROM EduMonitor_Usuarios.dbo.Users";
        var error = await Assert.ThrowsAsync<SqlException>(async () => await other.ExecuteScalarAsync());
        Assert.Equal(916, error.Number);
    }
    private async Task Esperar(string path)
    {
        for (var i = 0; i < 30; i++)
        {
            using var r = await admin.GetAsync(path);
            if (r.StatusCode == HttpStatusCode.OK)
                return;
            await Task.Delay(500);
        }
        Assert.Fail($"{path} no se recuperó dentro del plazo.");
    }
    [Fact, Trait("Category", "Availability")]
    public async Task Failure_StudentsStopsInIsolationAndRecoversPersistentData()
    {
        var letra = await Paralelo("SOF", 1);
        var cedula = CedulaEstudiante();
        var student = await Create("/api/estudiantes", Estudiante(cedula, letra));
        try
        {
            await Docker("stop", "estudiantes");
            await Status(HttpStatusCode.OK, admin.GetAsync("/"));
            using var login = Client();
            await Login(login, Environment.GetEnvironmentVariable("ADMIN_EMAIL")!, Environment.GetEnvironmentVariable("ADMIN_PASSWORD")!);
            await Status(HttpStatusCode.OK, admin.GetAsync("/api/docentes"));
            await Status(HttpStatusCode.OK, admin.GetAsync("/api/carreras"));
            await Status(HttpStatusCode.OK, admin.GetAsync("/api/cursos"));
            var watch = System.Diagnostics.Stopwatch.StartNew();
            await Status(HttpStatusCode.ServiceUnavailable, admin.GetAsync("/api/estudiantes"));
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(15));
            await Status(HttpStatusCode.ServiceUnavailable, admin.PostAsJsonAsync("/api/estudiantes", Estudiante(CedulaEstudiante(), letra)));
        }
        finally { await Docker("start", "estudiantes"); await Esperar("/api/estudiantes"); }
        await Status(HttpStatusCode.OK, admin.GetAsync($"/api/estudiantes/{student}"));
        Assert.Equal(1, await SqlCount("SELECT COUNT(*) FROM EduMonitor_Estudiantes.dbo.Students WHERE Id=@id", ("@id", student)));
    }
    [Fact, Trait("Category", "Availability")]
    public async Task Failure_MateriasUnavailableRejectsStudentChangesWithoutSaving()
    {
        var letra = await Paralelo("SOF", 1);
        var docente = await Teacher("A", "SOF");
        var curso = (await Cursos(letra))[0].GetProperty("id").GetInt32();
        await Status(HttpStatusCode.OK, admin.PutAsJsonAsync($"/api/cursos/{curso}/docente", new { teacherId = docente }));
        using var teacher = await TeacherClient("A");
        var cedula = CedulaEstudiante();
        var cedulaCsv = CedulaEstudiante();
        var csv = $"cedula,apellidos,nombres,correo\n{cedulaCsv},Mora Cruz,Luis,l@example.test\n";
        var materia = (await admin.GetFromJsonAsync<JsonElement>($"/api/cursos/{curso}")).GetProperty("materiaCodigo").GetString()!;
        try
        {
            await Docker("stop", "materias");
            // El listado general de estudiantes no depende de Materias...
            await Status(HttpStatusCode.OK, admin.GetAsync("/api/estudiantes"));
            // ...pero registrar, precargar o listar por curso exige validar carreras, paralelos y cursos: sin Materias no se guarda nada.
            await Status(HttpStatusCode.ServiceUnavailable, admin.PostAsJsonAsync("/api/estudiantes", Estudiante(cedula, letra)));
            await Status(HttpStatusCode.ServiceUnavailable, Precarga(admin, csv, letra, materia));
            await Status(HttpStatusCode.ServiceUnavailable, teacher.GetAsync($"/api/estudiantes/curso/{curso}"));
            Assert.Equal(0, await SqlCount("SELECT COUNT(*) FROM EduMonitor_Estudiantes.dbo.Students WHERE Cedula IN (@a, @b)", ("@a", cedula), ("@b", cedulaCsv)));
        }
        finally { await Docker("start", "materias"); await Esperar("/api/carreras"); }
        await Status(HttpStatusCode.Created, admin.PostAsJsonAsync("/api/estudiantes", Estudiante(cedula, letra)));
        await Status(HttpStatusCode.OK, teacher.GetAsync($"/api/estudiantes/curso/{curso}"));
    }
}
