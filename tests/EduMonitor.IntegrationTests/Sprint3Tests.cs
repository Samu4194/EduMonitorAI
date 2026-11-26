using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Xunit;
namespace EduMonitor.IntegrationTests;

// Sprint 3: HU-11 a HU-14 (agente académico). HTTP real -> Nginx -> YARP -> microservicios -> SQL Server.
// Las pruebas no dependen de qué proveedor de IA esté disponible: con Ollama o Gemini la recomendación llega con origen Local o Nube,
// y sin ninguno llega con origen Plantilla. En todos los casos debe cumplirse el mismo contrato.
[Collection("Sistema")]
public sealed class Sprint3Tests : IAsyncLifetime
{
    private static readonly string[] Origenes = ["Plantilla", "Local", "Nube"];
    private readonly string run = "IT" + Guid.NewGuid().ToString("N")[..12];
    private readonly List<string> letras = [];
    private readonly HttpClient admin = Client();
    private const string TeacherPassword = "IntegrationTeacher9!";
    private static HttpClient Client() => new() { BaseAddress = new Uri(Environment.GetEnvironmentVariable("EDUMONITOR_URL") ?? "http://127.0.0.1:5080"), Timeout = TimeSpan.FromSeconds(20) };
    private string estadoPeriodo = "", fuentePrevia = "";

    public async Task InitializeAsync()
    {
        await Login(admin, Environment.GetEnvironmentVariable("ADMIN_EMAIL") ?? throw new Exception("Ejecuta scripts/Test.ps1"), Environment.GetEnvironmentVariable("ADMIN_PASSWORD")!);
        estadoPeriodo = (await admin.GetFromJsonAsync<JsonElement>("/api/periodos")).EnumerateArray().First(p => p.GetProperty("activo").GetBoolean()).GetProperty("estado").GetString()!;
        await EstadoDelPeriodo("En curso");
        fuentePrevia = await FuenteDePrueba.Obtener(admin);
        await FuenteDePrueba.Cambiar(admin, "Manual");
    }
    private async Task EstadoDelPeriodo(string estado)
    {
        await using var sql = new SqlConnection(Environment.GetEnvironmentVariable("EDUMONITOR_TEST_SQL"));
        await sql.OpenAsync();
        using var cmd = sql.CreateCommand();
        cmd.CommandText = "UPDATE EduMonitor_Materias.dbo.Periodos SET Estado = @estado WHERE Activo = 1";
        cmd.Parameters.AddWithValue("@estado", estado);
        await cmd.ExecuteNonQueryAsync();
    }
    public async Task DisposeAsync()
    {
        await using var sql = new SqlConnection(Environment.GetEnvironmentVariable("EDUMONITOR_TEST_SQL"));
        await sql.OpenAsync();
        using var cmd = sql.CreateCommand();
        cmd.CommandText = """
            DECLARE @cursos TABLE (Id int);
            INSERT @cursos SELECT c.Id FROM EduMonitor_Materias.dbo.Cursos c JOIN EduMonitor_Materias.dbo.Paralelos p ON p.Id = c.ParaleloId WHERE p.Letra IN (SELECT value FROM STRING_SPLIT(@letras, ','));
            DELETE FROM EduMonitor_Seguimiento.dbo.Conversaciones WHERE CursoId IN (SELECT Id FROM @cursos);
            DELETE FROM EduMonitor_Seguimiento.dbo.Eventos WHERE RecomendacionId IN (SELECT Id FROM EduMonitor_Seguimiento.dbo.Recomendaciones WHERE CursoId IN (SELECT Id FROM @cursos));
            DELETE FROM EduMonitor_Seguimiento.dbo.Comunicaciones WHERE CursoId IN (SELECT Id FROM @cursos);
            DELETE FROM EduMonitor_Seguimiento.dbo.Recomendaciones WHERE CursoId IN (SELECT Id FROM @cursos);
            DELETE FROM EduMonitor_Seguimiento.dbo.Hallazgos WHERE AnalisisId IN (SELECT Id FROM EduMonitor_Seguimiento.dbo.Analisis WHERE CursoId IN (SELECT Id FROM @cursos));
            DELETE FROM EduMonitor_Seguimiento.dbo.Analisis WHERE CursoId IN (SELECT Id FROM @cursos);
            DELETE FROM EduMonitor_Evaluaciones.dbo.Actividades WHERE CursoId IN (SELECT Id FROM @cursos);
            DELETE FROM EduMonitor_Evaluaciones.dbo.Ponderaciones WHERE CursoId IN (SELECT Id FROM @cursos);
            DELETE FROM EduMonitor_Evaluaciones.dbo.Unidades WHERE CursoId IN (SELECT Id FROM @cursos);
            UPDATE EduMonitor_Materias.dbo.Cursos SET TeacherId = NULL WHERE TeacherId IN (SELECT Id FROM EduMonitor_Usuarios.dbo.Users WHERE Email LIKE @prefix);
            DELETE FROM EduMonitor_Materias.dbo.Cursos WHERE Id IN (SELECT Id FROM @cursos);
            DELETE FROM EduMonitor_Materias.dbo.Paralelos WHERE Letra IN (SELECT value FROM STRING_SPLIT(@letras, ','));
            DELETE FROM EduMonitor_Estudiantes.dbo.Students WHERE Cedula LIKE @estudiantes;
            DELETE FROM EduMonitor_Usuarios.dbo.Users WHERE Email LIKE @prefix AND Role='Teacher';
            """;
        cmd.Parameters.AddWithValue("@prefix", run + "%");
        cmd.Parameters.AddWithValue("@letras", string.Join(',', letras));
        cmd.Parameters.AddWithValue("@estudiantes", Cedulas.Prefijo + "%");
        await cmd.ExecuteNonQueryAsync();
        if (estadoPeriodo.Length > 0)
            await EstadoDelPeriodo(estadoPeriodo);
        if (fuentePrevia.Length > 0)
            await FuenteDePrueba.Cambiar(admin, fuentePrevia);
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
    private static async Task Status(HttpStatusCode expected, Task<HttpResponseMessage> request)
    {
        using var r = await request;
        Assert.True(r.StatusCode == expected, $"Se esperaba {(int)expected} y se obtuvo {(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
    }
    private static async Task<JsonElement> Json(Task<HttpResponseMessage> request, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var r = await request;
        Assert.True(r.StatusCode == expected, $"Se esperaba {(int)expected} y se obtuvo {(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return r.Content.Headers.ContentLength is 0 ? default : await r.Content.ReadFromJsonAsync<JsonElement>();
    }
    private async Task<int> Create(HttpClient client, string path, object body) =>
        (await Json(client.PostAsJsonAsync(path, body), HttpStatusCode.Created)).GetProperty("id").GetInt32();

    private async Task<(int Id, HttpClient Client)> Docente(string suffix)
    {
        var id = await Create(admin, "/api/docentes", new
        {
            cedula = Cedulas.Generar(), nombres = "Docente", apellidos = "De Prueba " + suffix, email = $"{run}{suffix}@example.test",
            telefono = "0999999999", tituloAcademico = "Ingeniero", carreras = new[] { "SOF" }, password = TeacherPassword
        });
        var c = Client();
        await Login(c, $"{run}{suffix}@example.test", TeacherPassword);
        return (id, c);
    }
    private async Task<string> Paralelo()
    {
        const string alfabeto = "CDEFGHJKMNPQRSTUVWXYZ";
        for (var i = 0; i < 10; i++)
        {
            var letra = $"{alfabeto[Random.Shared.Next(alfabeto.Length)]}{alfabeto[Random.Shared.Next(alfabeto.Length)]}";
            using var r = await admin.PostAsJsonAsync("/api/paralelos", new { carrera = "SOF", nivel = 1, letra });
            if (r.StatusCode == HttpStatusCode.Conflict)
                continue;
            Assert.Equal(HttpStatusCode.Created, r.StatusCode);
            letras.Add(letra);
            return letra;
        }
        throw new Exception("No se pudo crear un paralelo de prueba.");
    }
    private async Task<int> Estudiante(string letra, string apellidos)
    {
        var cedula = Cedulas.Generar();
        return await Create(admin, "/api/estudiantes", new { cedula, apellidos, nombres = "Prueba", correo = $"{cedula}@example.test", carrera = "SOF", nivel = 1, paralelo = letra });
    }
    private static object Actividad(int curso, string nombre, string tipo, string fecha) => new { cursoId = curso, nombre, tipo, descripcion = (string?)null, fechaEntrega = fecha };
    private static Task<HttpResponseMessage> Guardar(HttpClient c, int actividad, params (int Estudiante, decimal Nota)[] notas) =>
        c.PutAsJsonAsync($"/api/calificaciones/actividad/{actividad}", new { notas = notas.Select(n => new { estudianteId = n.Estudiante, nota = (decimal?)n.Nota }) });

    // Curso con tres estudiantes pensados para disparar reglas distintas:
    //  Alfa:  3 y 3 en tarea y APE, examen pendiente -> ya no puede aprobar (Alto).
    //  Beta:  9 en todo -> sin alertas.
    //  Gamma: 6,5 y 6,5 -> por debajo de la nota mínima (Medio).
    private record Escenario(HttpClient Docente, HttpClient Otro, int Curso, int Alfa, int Beta, int Gamma);
    private async Task<Escenario> Preparar()
    {
        var (a, ca) = await Docente("A");
        var (_, cb) = await Docente("B");
        var letra = await Paralelo();
        var cursos = (await admin.GetFromJsonAsync<JsonElement>($"/api/cursos?carrera=SOF&nivel=1&paralelo={letra}")).EnumerateArray().Select(c => c.GetProperty("id").GetInt32()).ToArray();
        await Status(HttpStatusCode.OK, admin.PutAsJsonAsync($"/api/cursos/{cursos[0]}/docente", new { teacherId = a }));
        var (alfa, beta, gamma) = (await Estudiante(letra, "Alfa Uno"), await Estudiante(letra, "Beta Dos"), await Estudiante(letra, "Gamma Tres"));
        var tarea = await Create(ca, "/api/actividades", Actividad(cursos[0], "Tarea 1", "Tarea", "2026-10-01"));
        var ape = await Create(ca, "/api/actividades", Actividad(cursos[0], "APE 1", "APE", "2026-10-15"));
        var examen = await Create(ca, "/api/actividades", Actividad(cursos[0], "Examen 1", "Examen", "2026-12-20"));
        await Status(HttpStatusCode.OK, Guardar(ca, tarea, (alfa, 3m), (beta, 9m), (gamma, 6.5m)));
        await Status(HttpStatusCode.OK, Guardar(ca, ape, (alfa, 3m), (beta, 9m), (gamma, 6.5m)));
        await Status(HttpStatusCode.OK, Guardar(ca, examen, (beta, 9m)));
        return new Escenario(ca, cb, cursos[0], alfa, beta, gamma);
    }

    private static string Ruta(int curso) => $"/api/seguimiento/cursos/{curso}";
    private static async Task<JsonElement> Panel(HttpClient c, int curso) => await Json(c.GetAsync(Ruta(curso)));
    private static JsonElement Fila(JsonElement panel, int estudianteId) => panel.GetProperty("estudiantes").EnumerateArray().Single(e => e.GetProperty("estudianteId").GetInt32() == estudianteId);

    // La redacción es asíncrona: se consulta el panel hasta que el análisis termina.
    private static async Task<JsonElement> EsperarAnalisis(HttpClient c, int curso, string? estado = "Completado")
    {
        var limite = DateTime.UtcNow.AddSeconds(240);
        while (true)
        {
            var panel = await Panel(c, curso);
            var listo = panel.GetProperty("analisis").ValueKind == JsonValueKind.Object && panel.GetProperty("analisis").GetProperty("estado").GetString() == estado
                && panel.GetProperty("estudiantes").EnumerateArray().All(e => e.GetProperty("recomendacion").ValueKind != JsonValueKind.Object || e.GetProperty("recomendacion").GetProperty("estado").GetString() != "Generando");
            if (listo)
                return panel;
            Assert.True(DateTime.UtcNow < limite, "El análisis no terminó a tiempo.");
            await Task.Delay(800);
        }
    }
    private static async Task<JsonElement> EsperarRecomendacion(HttpClient c, int curso, int estudiante, string estado)
    {
        var limite = DateTime.UtcNow.AddSeconds(240);
        while (true)
        {
            var fila = Fila(await Panel(c, curso), estudiante);
            if (fila.GetProperty("recomendacion").GetProperty("estado").GetString() == estado)
                return fila.GetProperty("recomendacion");
            Assert.True(DateTime.UtcNow < limite, $"La recomendación no llegó al estado {estado}.");
            await Task.Delay(800);
        }
    }
    private static async Task<JsonElement> Analizar(Escenario e)
    {
        var resumen = await Json(e.Docente.PostAsync($"{Ruta(e.Curso)}/analisis", null), HttpStatusCode.Accepted);
        Assert.Equal(3, resumen.GetProperty("totalEstudiantes").GetInt32());
        return await EsperarAnalisis(e.Docente, e.Curso);
    }
    private static object Edicion(string resumen = "Resumen revisado por el docente de la asignatura.", string mensaje = "Hola, revisé tu avance y quiero ayudarte a mejorar. Todavía estás a tiempo de recuperar tu promedio si repasas lo básico y consultas tus dudas.") => new
    {
        resumen, causas = new[] { "Falta de práctica en los ejercicios iniciales." }, accionesDocente = new[] { "Reunirse con el estudiante esta semana." },
        accionesEstudiante = new[] { "Repasar los ejercicios de la tarea 1.", "Asistir a las tutorías." }, mensajeEstudiante = mensaje
    };

    [Fact]
    public async Task HU11_12_AnalysisDetectsDifficultiesByRulesAndTheAgentRecommends()
    {
        var e = await Preparar();

        var vacio = await Panel(e.Docente, e.Curso);
        Assert.Equal(JsonValueKind.Null, vacio.GetProperty("analisis").ValueKind);
        Assert.Empty(vacio.GetProperty("estudiantes").EnumerateArray());

        // Solo el docente del curso analiza: otro docente no lo ve (404) y el administrador es de solo lectura (403).
        await Status(HttpStatusCode.NotFound, e.Otro.PostAsync($"{Ruta(e.Curso)}/analisis", null));
        await Status(HttpStatusCode.NotFound, e.Otro.GetAsync(Ruta(e.Curso)));
        await Status(HttpStatusCode.Forbidden, admin.PostAsync($"{Ruta(e.Curso)}/analisis", null));
        await Status(HttpStatusCode.OK, admin.GetAsync(Ruta(e.Curso)));
        using (var sinSesion = Client())
            await Status(HttpStatusCode.Unauthorized, sinSesion.GetAsync(Ruta(e.Curso)));

        var panel = await Analizar(e);
        var analisis = panel.GetProperty("analisis");
        Assert.Equal(3, analisis.GetProperty("totalEstudiantes").GetInt32());
        Assert.Equal(1, analisis.GetProperty("alto").GetInt32());
        Assert.Equal(1, analisis.GetProperty("medio").GetInt32());
        Assert.Equal(1, analisis.GetProperty("sinAlertas").GetInt32());
        Assert.Equal(analisis.GetProperty("aRecomendar").GetInt32(), analisis.GetProperty("generadas").GetInt32());

        // HU-11: las reglas, no la IA, deciden quién está en riesgo y por qué.
        var alfa = Fila(panel, e.Alfa);
        Assert.Equal("Alto", alfa.GetProperty("nivel").GetString());
        var codigos = alfa.GetProperty("hallazgos").EnumerateArray().Select(h => h.GetProperty("codigo").GetString()).ToList();
        Assert.Contains("RIESGO_DE_REPROBAR", codigos);
        Assert.Contains("RENDIMIENTO_BAJO", codigos);
        Assert.Equal("Medio", Fila(panel, e.Gamma).GetProperty("nivel").GetString());
        var beta = Fila(panel, e.Beta);
        Assert.Equal("Sin alertas", beta.GetProperty("nivel").GetString());
        Assert.Empty(beta.GetProperty("hallazgos").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, beta.GetProperty("recomendacion").ValueKind);
        // Los más graves aparecen primero.
        Assert.Equal(e.Alfa, panel.GetProperty("estudiantes")[0].GetProperty("estudianteId").GetInt32());

        // HU-12: cada estudiante en alerta recibe una recomendación completa en borrador, con su origen.
        foreach (var id in new[] { e.Alfa, e.Gamma })
        {
            var r = Fila(panel, id).GetProperty("recomendacion");
            Assert.Equal("Borrador", r.GetProperty("estado").GetString());
            Assert.Contains(r.GetProperty("origen").GetString(), Origenes);
            Assert.True(r.GetProperty("resumen").GetString()!.Length >= 10);
            Assert.InRange(r.GetProperty("accionesDocente").GetArrayLength(), 1, 4);
            Assert.InRange(r.GetProperty("accionesEstudiante").GetArrayLength(), 1, 4);
            Assert.InRange(r.GetProperty("causas").GetArrayLength(), 1, 4);
            Assert.True(r.GetProperty("mensajeEstudiante").GetString()!.Length >= 40);
            Assert.False(r.GetProperty("editada").GetBoolean());
        }
        // El agente nunca ve datos personales: la recomendación no menciona el nombre del estudiante.
        Assert.DoesNotContain("Alfa", Fila(panel, e.Alfa).GetProperty("recomendacion").GetProperty("mensajeEstudiante").GetString());

        // El estado del agente informa qué proveedores hay y que las plantillas siempre están como respaldo.
        var estado = await Json(e.Docente.GetAsync("/api/seguimiento/agente/estado"));
        Assert.True(estado.GetProperty("plantillas").GetBoolean());
        Assert.NotEqual(0, estado.GetProperty("proveedores").GetArrayLength());
    }

    [Fact]
    public async Task HU11_CourseWithoutGradesOrStudentsCannotBeAnalyzedAndFinishedPeriodsAreReadOnly()
    {
        var (a, ca) = await Docente("A");
        var letra = await Paralelo();
        var cursos = (await admin.GetFromJsonAsync<JsonElement>($"/api/cursos?carrera=SOF&nivel=1&paralelo={letra}")).EnumerateArray().Select(c => c.GetProperty("id").GetInt32()).ToArray();
        await Status(HttpStatusCode.OK, admin.PutAsJsonAsync($"/api/cursos/{cursos[0]}/docente", new { teacherId = a }));
        await Status(HttpStatusCode.Conflict, ca.PostAsync($"{Ruta(cursos[0])}/analisis", null));   // sin estudiantes
        var alumno = await Estudiante(letra, "Solo Uno");
        await Status(HttpStatusCode.Conflict, ca.PostAsync($"{Ruta(cursos[0])}/analisis", null));   // sin calificaciones
        var tarea = await Create(ca, "/api/actividades", Actividad(cursos[0], "Tarea 1", "Tarea", "2026-12-01"));
        await Status(HttpStatusCode.OK, Guardar(ca, tarea, (alumno, 5m)));
        await Json(ca.PostAsync($"{Ruta(cursos[0])}/analisis", null), HttpStatusCode.Accepted);
        await EsperarAnalisis(ca, cursos[0]);

        await EstadoDelPeriodo("Finalizado");
        await Status(HttpStatusCode.Conflict, ca.PostAsync($"{Ruta(cursos[0])}/analisis", null));
        var panel = await Panel(ca, cursos[0]);
        Assert.True(panel.GetProperty("soloLectura").GetBoolean());
        Assert.Equal(1, panel.GetProperty("estudiantes").GetArrayLength());
    }

    [Fact]
    public async Task HU13_TeacherEditsRejectsRegeneratesApprovesAndCommunicates()
    {
        var e = await Preparar();
        var panel = await Analizar(e);
        var alfa = Fila(panel, e.Alfa).GetProperty("recomendacion").GetProperty("id").GetInt32();
        var gamma = Fila(panel, e.Gamma).GetProperty("recomendacion").GetProperty("id").GetInt32();
        string R(int id, string accion = "") => $"/api/seguimiento/recomendaciones/{id}{accion}";

        // Editar: valida el contenido y marca la recomendación como editada por el docente.
        await Status(HttpStatusCode.BadRequest, e.Docente.PutAsJsonAsync(R(alfa), Edicion(mensaje: "muy corto")));
        await Status(HttpStatusCode.NotFound, e.Otro.PutAsJsonAsync(R(alfa), Edicion()));
        await Status(HttpStatusCode.Forbidden, admin.PutAsJsonAsync(R(alfa), Edicion()));
        await Status(HttpStatusCode.NoContent, e.Docente.PutAsJsonAsync(R(alfa), Edicion()));
        var editada = Fila(await Panel(e.Docente, e.Curso), e.Alfa).GetProperty("recomendacion");
        Assert.True(editada.GetProperty("editada").GetBoolean());
        Assert.Equal("Resumen revisado por el docente de la asignatura.", editada.GetProperty("resumen").GetString());

        // Comunicar exige antes la aprobación del docente: el agente jamás escribe al estudiante por su cuenta.
        await Status(HttpStatusCode.Conflict, e.Docente.PostAsync(R(alfa, "/comunicar"), null));
        await Status(HttpStatusCode.NotFound, e.Otro.PostAsJsonAsync(R(gamma, "/aprobar"), new { comunicar = false }));
        await Status(HttpStatusCode.Forbidden, admin.PostAsJsonAsync(R(gamma, "/aprobar"), new { comunicar = false }));

        // Aprobar sin comunicar y comunicar después.
        await Json(e.Docente.PostAsJsonAsync(R(gamma, "/aprobar"), new { comunicar = false }));
        Assert.Equal("Aprobada", Fila(await Panel(e.Docente, e.Curso), e.Gamma).GetProperty("recomendacion").GetProperty("estado").GetString());
        await Status(HttpStatusCode.Conflict, e.Docente.PutAsJsonAsync(R(gamma), Edicion()));       // ya aprobada: no se edita
        await Status(HttpStatusCode.Conflict, e.Docente.PostAsJsonAsync(R(gamma, "/aprobar"), new { comunicar = false }));
        var correo = await Json(e.Docente.PostAsync(R(gamma, "/comunicar"), null));
        Assert.Contains(correo.GetProperty("estado").GetString(), new[] { "Simulado", "Enviado" });
        Assert.EndsWith("@example.test", correo.GetProperty("destinatario").GetString());
        Assert.Contains("Gamma", correo.GetProperty("cuerpo").GetString() + correo.GetProperty("estudiante").GetString());
        Assert.StartsWith("Estimado/a Prueba", correo.GetProperty("cuerpo").GetString());
        await Status(HttpStatusCode.Conflict, e.Docente.PostAsync(R(gamma, "/comunicar"), null));    // una sola comunicación por recomendación
        Assert.Equal("Comunicada", Fila(await Panel(e.Docente, e.Curso), e.Gamma).GetProperty("recomendacion").GetProperty("estado").GetString());

        // Rechazar exige un motivo; una rechazada no se puede aprobar, pero sí volver a generar.
        await Status(HttpStatusCode.BadRequest, e.Docente.PostAsJsonAsync(R(alfa, "/rechazar"), new { motivo = "no" }));
        await Status(HttpStatusCode.NoContent, e.Docente.PostAsJsonAsync(R(alfa, "/rechazar"), new { motivo = "No refleja la situación del estudiante." }));
        var rechazada = Fila(await Panel(e.Docente, e.Curso), e.Alfa).GetProperty("recomendacion");
        Assert.Equal("Rechazada", rechazada.GetProperty("estado").GetString());
        Assert.Equal("No refleja la situación del estudiante.", rechazada.GetProperty("motivoRechazo").GetString());
        await Status(HttpStatusCode.Conflict, e.Docente.PostAsJsonAsync(R(alfa, "/aprobar"), new { comunicar = true }));

        await Status(HttpStatusCode.Accepted, e.Docente.PostAsync(R(alfa, "/regenerar"), null));
        var nueva = await EsperarRecomendacion(e.Docente, e.Curso, e.Alfa, "Borrador");
        Assert.Equal(2, nueva.GetProperty("version").GetInt32());
        Assert.False(nueva.GetProperty("editada").GetBoolean());
        Assert.NotEqual("Resumen revisado por el docente de la asignatura.", nueva.GetProperty("resumen").GetString());

        // Aprobar y comunicar en un solo paso.
        var junto = await Json(e.Docente.PostAsJsonAsync(R(alfa, "/aprobar"), new { comunicar = true }));
        Assert.Contains(junto.GetProperty("comunicacion").GetProperty("estado").GetString(), new[] { "Simulado", "Enviado" });
        Assert.Equal("Comunicada", Fila(await Panel(e.Docente, e.Curso), e.Alfa).GetProperty("recomendacion").GetProperty("estado").GetString());

        var comunicaciones = await Json(e.Docente.GetAsync($"{Ruta(e.Curso)}/comunicaciones"));
        Assert.Equal(2, comunicaciones.GetArrayLength());
        Assert.All(comunicaciones.EnumerateArray(), c => Assert.False(string.IsNullOrWhiteSpace(c.GetProperty("estudiante").GetString())));
        await Status(HttpStatusCode.NotFound, e.Otro.GetAsync($"{Ruta(e.Curso)}/comunicaciones"));

        // Un análisis nuevo conserva lo ya comunicado y reemplaza lo pendiente de revisar.
        var panel2 = await Analizar(e);
        Assert.NotEqual(panel.GetProperty("analisis").GetProperty("id").GetInt32(), panel2.GetProperty("analisis").GetProperty("id").GetInt32());
        Assert.Equal("Borrador", Fila(panel2, e.Alfa).GetProperty("recomendacion").GetProperty("estado").GetString());
        Assert.Equal(2, (await Json(e.Docente.GetAsync($"{Ruta(e.Curso)}/comunicaciones"))).GetArrayLength());
        await Status(HttpStatusCode.Conflict, e.Docente.PostAsJsonAsync(R(alfa, "/aprobar"), new { comunicar = false })); // la anterior ya fue comunicada

        // Con el período finalizado el docente consulta pero ya no decide.
        var vigente = Fila(panel2, e.Alfa).GetProperty("recomendacion").GetProperty("id").GetInt32();
        await EstadoDelPeriodo("Finalizado");
        await Status(HttpStatusCode.Conflict, e.Docente.PostAsJsonAsync(R(vigente, "/aprobar"), new { comunicar = false }));
        await Status(HttpStatusCode.OK, e.Docente.GetAsync(Ruta(e.Curso)));
    }

    [Fact]
    public async Task HU14_ReportSummarizesTheCourseAndExportsToCsv()
    {
        var e = await Preparar();
        await Status(HttpStatusCode.NotFound, e.Docente.GetAsync($"{Ruta(e.Curso)}/reporte"));      // aún no hay análisis
        var panel = await Analizar(e);
        var gamma = Fila(panel, e.Gamma).GetProperty("recomendacion").GetProperty("id").GetInt32();
        await Json(e.Docente.PostAsJsonAsync($"/api/seguimiento/recomendaciones/{gamma}/aprobar", new { comunicar = true }));

        var reporte = await Json(e.Docente.GetAsync($"{Ruta(e.Curso)}/reporte"));
        Assert.Equal(3, reporte.GetProperty("analisis").GetProperty("totalEstudiantes").GetInt32());
        Assert.Equal(1, reporte.GetProperty("recomendaciones").GetProperty("comunicada").GetInt32());
        Assert.Equal(1, reporte.GetProperty("recomendaciones").GetProperty("borrador").GetInt32());
        Assert.Equal(3, reporte.GetProperty("estudiantes").GetArrayLength());
        Assert.Contains("Docente", reporte.GetProperty("docente").GetString());
        var fila = Fila(reporte, e.Gamma);
        Assert.Equal("Comunicada", fila.GetProperty("recomendacion").GetProperty("estado").GetString());
        Assert.Equal(JsonValueKind.Object, fila.GetProperty("recomendacion").GetProperty("comunicacion").ValueKind);

        using var csv = await e.Docente.GetAsync($"{Ruta(e.Curso)}/reporte.csv");
        Assert.Equal(HttpStatusCode.OK, csv.StatusCode);
        Assert.StartsWith("text/csv", csv.Content.Headers.ContentType!.ToString());
        var bytes = await csv.Content.ReadAsByteArrayAsync();
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);   // BOM: Excel reconoce las tildes
        var lineas = System.Text.Encoding.UTF8.GetString(bytes).TrimStart('﻿').Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(4, lineas.Length);
        Assert.Contains("Nivel de alerta", lineas[0]);
        Assert.Contains(lineas.Skip(1), l => l.Contains("Gamma Tres") && l.Contains("Comunicada"));

        // El administrador puede leer el reporte; otro docente no.
        await Status(HttpStatusCode.OK, admin.GetAsync($"{Ruta(e.Curso)}/reporte"));
        await Status(HttpStatusCode.NotFound, e.Otro.GetAsync($"{Ruta(e.Curso)}/reporte"));
        await Status(HttpStatusCode.NotFound, e.Otro.GetAsync($"{Ruta(e.Curso)}/reporte.csv"));
    }
}
