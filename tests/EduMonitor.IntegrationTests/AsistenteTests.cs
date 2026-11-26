using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Xunit;
namespace EduMonitor.IntegrationTests;

// Asistente académico: chat del docente, preguntas precargadas, rutas de mejora, historial temporal y privacidad.
// HTTP real -> Nginx -> YARP -> microservicios -> SQL Server. No dependen de qué proveedor de IA haya: con Ollama o Gemini la respuesta llega
// con origen Local o Nube y sin ninguno con origen Plantilla; lo que no cambia (datos exactos, seudónimos, historial) se comprueba siempre.
[Collection("Sistema")]
public sealed class AsistenteTests : IAsyncLifetime
{
    private static readonly string[] Origenes = ["Plantilla", "Local", "Nube"];
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
        await FuenteDePrueba.Cambiar(admin, "Manual");
    }
    public async Task DisposeAsync()
    {
        await Sql("""
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
            """, ("@prefix", run + "%"), ("@letras", string.Join(',', letras)), ("@estudiantes", Cedulas.Prefijo + "%"));
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
    private static async Task<int> SqlCount(string texto, params (string, object)[] parametros)
    {
        await using var sql = new SqlConnection(Environment.GetEnvironmentVariable("EDUMONITOR_TEST_SQL"));
        await sql.OpenAsync();
        using var cmd = sql.CreateCommand();
        cmd.CommandText = texto;
        foreach (var (n, v) in parametros)
            cmd.Parameters.AddWithValue(n, v);
        return (int)(await cmd.ExecuteScalarAsync())!;
    }
    private static async Task Login(HttpClient client, string email, string password)
    {
        var r = await SesionDePrueba.Iniciar(client, new { email, password });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString());
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
    private static async Task<int> Create(HttpClient client, string path, object body) => (await Json(client.PostAsJsonAsync(path, body), HttpStatusCode.Created)).GetProperty("id").GetInt32();

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
    private async Task<(int Id, string Cedula)> Estudiante(string letra, string apellidos, string nombres = "Prueba")
    {
        var cedula = Cedulas.Generar();
        return (await Create(admin, "/api/estudiantes", new { cedula, apellidos, nombres, correo = $"{cedula}@example.test", carrera = "SOF", nivel = 1, paralelo = letra }), cedula);
    }
    private static Task<HttpResponseMessage> Guardar(HttpClient c, int actividad, params (int Estudiante, decimal Nota)[] notas) =>
        c.PutAsJsonAsync($"/api/calificaciones/actividad/{actividad}", new { notas = notas.Select(n => new { estudianteId = n.Estudiante, nota = (decimal?)n.Nota }) });

    // Curso con dos temas, tres actividades relacionadas con ellos y estudiantes de perfiles distintos.
    //  Alfa: 5 en la tarea y 6 en el APE -> necesita 9,25 en el examen para llegar a 7. Beta: 9 en todo. Gamma: 6,5 y 6,5.
    //  «Mismo Apellido» x2: mismo nombre completo, para comprobar que el sistema pregunta en vez de adivinar.
    private sealed record Escenario(HttpClient Docente, HttpClient Otro, int Curso, (int Id, string Cedula) Alfa, (int Id, string Cedula) Beta, (int Id, string Cedula) Gamma,
        (int Id, string Cedula) Repetido1, (int Id, string Cedula) Repetido2);
    private async Task<Escenario> Preparar()
    {
        var (a, ca) = await Docente("A");
        var (_, cb) = await Docente("B");
        var letra = await Paralelo();
        var curso = (await admin.GetFromJsonAsync<JsonElement>($"/api/cursos?carrera=SOF&nivel=1&paralelo={letra}")).EnumerateArray().Select(c => c.GetProperty("id").GetInt32()).First();
        await Status(HttpStatusCode.OK, admin.PutAsJsonAsync($"/api/cursos/{curso}/docente", new { teacherId = a }));
        var alfa = await Estudiante(letra, "Alfa Uno");
        var beta = await Estudiante(letra, "Beta Dos");
        var gamma = await Estudiante(letra, "Gamma Tres");
        var r1 = await Estudiante(letra, "Mismo Apellido");
        var r2 = await Estudiante(letra, "Mismo Apellido");

        var unidad = await Create(ca, "/api/unidades", new { cursoId = curso, nombre = "Unidad 1", descripcion = (string?)null });
        var funciones = await Create(ca, $"/api/unidades/{unidad}/temas", new { nombre = "Funciones", descripcion = (string?)null, contenidos = Array.Empty<string>() });
        var limites = await Create(ca, $"/api/unidades/{unidad}/temas", new { nombre = "Límites", descripcion = (string?)null, contenidos = Array.Empty<string>() });
        object Act(string nombre, string tipo, string fecha, params int[] temas) => new { cursoId = curso, nombre, tipo, descripcion = (string?)null, fechaEntrega = fecha, temaIds = temas };
        var tarea = await Create(ca, "/api/actividades", Act("Tarea 1", "Tarea", "2026-10-01", funciones));
        var ape = await Create(ca, "/api/actividades", Act("APE 1", "APE", "2026-10-15", limites));
        var examen = await Create(ca, "/api/actividades", Act("Examen 1", "Examen", "2026-12-20", funciones, limites));
        await Status(HttpStatusCode.OK, Guardar(ca, tarea, (alfa.Id, 5m), (beta.Id, 9m), (gamma.Id, 6.5m), (r1.Id, 8m), (r2.Id, 8m)));
        await Status(HttpStatusCode.OK, Guardar(ca, ape, (alfa.Id, 6m), (beta.Id, 9m), (gamma.Id, 6.5m), (r1.Id, 8m), (r2.Id, 8m)));
        await Status(HttpStatusCode.OK, Guardar(ca, examen, (beta.Id, 9m)));
        return new Escenario(ca, cb, curso, alfa, beta, gamma, r1, r2);
    }

    private static string Base(int curso) => $"/api/seguimiento/cursos/{curso}/asistente";
    private sealed record Evento(string Tipo, JsonElement Datos);

    // Lee el flujo de eventos (text/event-stream) completo.
    private static async Task<List<Evento>> Chat(HttpClient c, int curso, object cuerpo)
    {
        using var largo = new HttpClient { BaseAddress = c.BaseAddress, Timeout = TimeSpan.FromMinutes(5) };
        largo.DefaultRequestHeaders.Authorization = c.DefaultRequestHeaders.Authorization;
        using var pedido = new HttpRequestMessage(HttpMethod.Post, $"{Base(curso)}/mensajes") { Content = JsonContent.Create(cuerpo) };
        using var r = await largo.SendAsync(pedido, HttpCompletionOption.ResponseHeadersRead);
        Assert.True(r.StatusCode == HttpStatusCode.OK, $"El chat respondió {(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        Assert.StartsWith("text/event-stream", r.Content.Headers.ContentType!.ToString());
        var eventos = new List<Evento>();
        using var lector = new StreamReader(await r.Content.ReadAsStreamAsync());
        string? tipo = null;
        while (await lector.ReadLineAsync() is { } linea)
        {
            if (linea.StartsWith("event: ", StringComparison.Ordinal))
                tipo = linea[7..];
            else if (linea.StartsWith("data: ", StringComparison.Ordinal) && tipo is not null)
            {
                using var doc = JsonDocument.Parse(linea[6..]);
                eventos.Add(new Evento(tipo, doc.RootElement.Clone()));
                tipo = null;
            }
        }
        return eventos;
    }
    private static JsonElement Uno(List<Evento> e, string tipo) => e.Last(x => x.Tipo == tipo).Datos;
    private static void SinError(List<Evento> e) => Assert.DoesNotContain(e, x => x.Tipo == "error");

    [Fact]
    public async Task Assistant_SuggestionsSearchAndAccessRules()
    {
        var e = await Preparar();
        var sugerencias = await Json(e.Docente.GetAsync($"{Base(e.Curso)}/sugerencias"));
        var ids = sugerencias.EnumerateArray().Select(s => s.GetProperty("id").GetString()).ToList();
        Assert.Contains("ruta", ids);
        Assert.Contains("resumen", ids);
        Assert.Contains("atencion", ids);
        Assert.True(sugerencias.EnumerateArray().Single(s => s.GetProperty("id").GetString() == "ruta").GetProperty("requiereEstudiante").GetBoolean());

        // El selector busca solo por nombre (sin importar tildes ni mayúsculas) y nunca por cédula ni devolviéndola
        var porApellido = await Json(e.Docente.GetAsync($"{Base(e.Curso)}/estudiantes?q=ALFA"));
        var alfa = Assert.Single(porApellido.EnumerateArray());
        Assert.Equal(e.Alfa.Id, alfa.GetProperty("id").GetInt32());
        Assert.False(alfa.TryGetProperty("cedula", out _));
        Assert.Empty((await Json(e.Docente.GetAsync($"{Base(e.Curso)}/estudiantes?q={e.Alfa.Cedula}"))).EnumerateArray());
        var porNombre = await Json(e.Docente.GetAsync($"{Base(e.Curso)}/estudiantes?q=gamma%20TRES"));
        Assert.Equal(e.Gamma.Id, Assert.Single(porNombre.EnumerateArray()).GetProperty("id").GetInt32());
        Assert.Empty((await Json(e.Docente.GetAsync($"{Base(e.Curso)}/estudiantes?q=zzzz"))).EnumerateArray());

        // Solo el docente del curso conversa: otro docente no lo ve y el administrador no entra.
        await Status(HttpStatusCode.NotFound, e.Otro.GetAsync($"{Base(e.Curso)}/sugerencias"));
        await Status(HttpStatusCode.NotFound, e.Otro.GetAsync($"{Base(e.Curso)}/estudiantes?q=alfa"));
        await Status(HttpStatusCode.Forbidden, admin.GetAsync($"{Base(e.Curso)}/sugerencias"));
        await Status(HttpStatusCode.Forbidden, admin.PostAsJsonAsync($"{Base(e.Curso)}/mensajes", new { texto = "hola" }));
        using var sinSesion = Client();
        await Status(HttpStatusCode.Unauthorized, sinSesion.GetAsync($"{Base(e.Curso)}/sugerencias"));
        await Status(HttpStatusCode.NotFound, e.Otro.PostAsJsonAsync($"{Base(e.Curso)}/mensajes", new { texto = "hola" }));
        // Validaciones
        await Status(HttpStatusCode.BadRequest, e.Docente.PostAsJsonAsync($"{Base(e.Curso)}/mensajes", new { texto = "   " }));
        await Status(HttpStatusCode.BadRequest, e.Docente.PostAsJsonAsync($"{Base(e.Curso)}/mensajes", new { texto = new string('a', 2000) }));
        await Status(HttpStatusCode.BadRequest, e.Docente.PostAsJsonAsync($"{Base(e.Curso)}/mensajes", new { preguntaId = "no-existe" }));
        await Status(HttpStatusCode.BadRequest, e.Docente.PostAsJsonAsync($"{Base(e.Curso)}/mensajes", new { preguntaId = "ruta" }));      // exige elegir estudiante
    }

    [Fact]
    public async Task Assistant_PreloadedQuestionsShowExactDataAndNeverStoreRealNames()
    {
        var e = await Preparar();
        var resumen = await Chat(e.Docente, e.Curso, new { preguntaId = "resumen" });
        SinError(resumen);
        Assert.Equal(["inicio", "tarjeta"], resumen.Take(2).Select(x => x.Tipo));
        var tabla = Uno(resumen, "tarjeta").GetProperty("tarjeta");
        Assert.Equal("Estado del curso", tabla.GetProperty("titulo").GetString());
        var filas = tabla.GetProperty("filas").EnumerateArray().ToDictionary(f => f[0].GetString()!, f => f[1].GetString()!);
        Assert.Equal("5", filas["Estudiantes"]);
        var fin = Uno(resumen, "fin");
        Assert.Contains(fin.GetProperty("origen").GetString(), Origenes);
        Assert.Contains(fin.GetProperty("alcance").GetString(), new[] { "curso", "general" });

        // «Atención urgente»: la tarjeta muestra al docente los nombres reales; la base solo guarda seudónimos.
        var atencion = await Chat(e.Docente, e.Curso, new { preguntaId = "atencion", conversacionId = Uno(resumen, "inicio").GetProperty("conversacionId").GetInt32() });
        SinError(atencion);
        var tarjeta = Uno(atencion, "tarjeta").GetProperty("tarjeta");
        var textoTarjeta = tarjeta.ToString();
        Assert.Contains("Alfa Uno", textoTarjeta);
        Assert.DoesNotContain("Estudiante A", textoTarjeta);
        Assert.Contains(tarjeta.GetProperty("filas")[0][1].GetString(), new[] { "Alto", "Medio", "Bajo" });
        var conv = Uno(atencion, "inicio").GetProperty("conversacionId").GetInt32();
        Assert.Equal(0, await SqlCount("SELECT COUNT(*) FROM EduMonitor_Seguimiento.dbo.Mensajes m JOIN EduMonitor_Seguimiento.dbo.Conversaciones c ON c.Id = m.ConversacionId WHERE c.Id = @c AND (m.Texto LIKE '%Alfa%' OR m.TarjetasJson LIKE '%Alfa%' OR m.Texto LIKE '%Gamma%' OR m.TarjetasJson LIKE '%Gamma%')", ("@c", conv)));
        Assert.True(await SqlCount("SELECT COUNT(*) FROM EduMonitor_Seguimiento.dbo.Mensajes WHERE ConversacionId = @c AND TarjetasJson LIKE '%Estudiante A%'", ("@c", conv)) > 0);

        // El resto de preguntas del curso también responden con una tarjeta de datos
        foreach (var id in new[] { "actividad", "temas", "evaluaciones", "tutoria" })
        {
            var r = await Chat(e.Docente, e.Curso, new { preguntaId = id });
            SinError(r);
            Assert.Contains(r, x => x.Tipo == "tarjeta");
            Assert.Contains(r, x => x.Tipo == "fin");
        }
        // Rendimiento por tema: «Límites» y «Funciones» salen de las actividades relacionadas
        var temas = Uno(await Chat(e.Docente, e.Curso, new { preguntaId = "temas" }), "tarjeta").GetProperty("tarjeta").GetProperty("filas").EnumerateArray().Select(f => f[0].GetString()).ToList();
        Assert.Contains("Funciones", temas);
        Assert.Contains("Límites", temas);
    }

    [Fact]
    public async Task Assistant_BuildsAnImprovementRouteWithExactNumbersAndCanSaveItAsARecommendation()
    {
        var e = await Preparar();
        var eventos = await Chat(e.Docente, e.Curso, new { preguntaId = "ruta", estudianteIds = new[] { e.Alfa.Id } });
        SinError(eventos);
        var ruta = eventos.Last(x => x.Tipo == "ruta").Datos;
        Assert.False(ruta.GetProperty("redactando").GetBoolean());
        var datos = ruta.GetProperty("ruta");
        Assert.Contains("Alfa", datos.GetProperty("estudiante").GetString());
        // Alfa lleva 5 y 6: para llegar a 7 necesita 9,25 en el examen; lo calcula el sistema, no el modelo
        Assert.Equal("MuyExigente", datos.GetProperty("estado").GetString());
        Assert.Equal(9.25m, datos.GetProperty("promedioRequerido").GetDecimal());
        Assert.Equal(1, datos.GetProperty("pendientes").GetInt32());
        var etapa = Assert.Single(datos.GetProperty("etapas").EnumerateArray());
        Assert.Contains("Examen 1", etapa.GetProperty("titulo").GetString());
        Assert.Equal(9.25m, etapa.GetProperty("meta").GetDecimal());
        Assert.InRange(etapa.GetProperty("acciones").GetArrayLength(), 1, 4);
        var temasDebiles = datos.GetProperty("temas").EnumerateArray().Select(t => t.GetProperty("tema").GetString()).ToList();
        Assert.Contains("Funciones", temasDebiles);
        Assert.Contains("Límites", temasDebiles);
        Assert.Contains(datos.GetProperty("origen").GetString(), Origenes);
        var inicio = Uno(await Chat(e.Docente, e.Curso, new { preguntaId = "ruta", estudianteIds = new[] { e.Alfa.Id } }), "inicio");
        Assert.Contains(inicio.GetProperty("alias").EnumerateArray(), a => a.GetProperty("nombre").GetString()!.Contains("Alfa"));

        // Todos los estudiantes en riesgo: una ruta por cada uno, nunca de quien va bien
        var lote = await Chat(e.Docente, e.Curso, new { preguntaId = "rutas" });
        SinError(lote);
        var rutas = lote.Where(x => x.Tipo == "ruta" && !x.Datos.GetProperty("redactando").GetBoolean()).Select(x => x.Datos.GetProperty("estudianteId").GetInt32()).Distinct().ToList();
        Assert.Contains(e.Alfa.Id, rutas);
        Assert.DoesNotContain(e.Beta.Id, rutas);

        // Guardar la ruta como recomendación exige un análisis previo; después entra al flujo de revisión
        var mensajeRuta = lote.Where(x => x.Tipo == "ruta").Last(x => x.Datos.GetProperty("estudianteId").GetInt32() == e.Alfa.Id).Datos.GetProperty("mensajeId").GetInt32();
        await Status(HttpStatusCode.Conflict, e.Docente.PostAsync($"{Base(e.Curso)}/mensajes/{mensajeRuta}/guardar-ruta", null));
        await Json(e.Docente.PostAsync($"/api/seguimiento/cursos/{e.Curso}/analisis", null), HttpStatusCode.Accepted);
        await EsperarAnalisis(e.Docente, e.Curso);
        await Status(HttpStatusCode.NotFound, e.Otro.PostAsync($"{Base(e.Curso)}/mensajes/{mensajeRuta}/guardar-ruta", null));
        await Status(HttpStatusCode.Forbidden, admin.PostAsync($"{Base(e.Curso)}/mensajes/{mensajeRuta}/guardar-ruta", null));
        var guardado = await Json(e.Docente.PostAsync($"{Base(e.Curso)}/mensajes/{mensajeRuta}/guardar-ruta", null));
        var recId = guardado.GetProperty("recomendacionId").GetInt32();
        var panel = await Json(e.Docente.GetAsync($"/api/seguimiento/cursos/{e.Curso}"));
        var rec = panel.GetProperty("estudiantes").EnumerateArray().Single(x => x.GetProperty("estudianteId").GetInt32() == e.Alfa.Id).GetProperty("recomendacion");
        Assert.Equal(recId, rec.GetProperty("id").GetInt32());
        Assert.Equal("Borrador", rec.GetProperty("estado").GetString());
        Assert.Equal(JsonValueKind.Object, rec.GetProperty("ruta").ValueKind);
        Assert.NotEmpty(rec.GetProperty("ruta").GetProperty("etapas").EnumerateArray());
        Assert.DoesNotContain("Estudiante A", rec.ToString());

        // El estudiante recibe la ruta en el correo (simulado si no hay SMTP)
        var correo = await Json(e.Docente.PostAsJsonAsync($"/api/seguimiento/recomendaciones/{recId}/aprobar", new { comunicar = true }));
        Assert.Contains("Tu ruta de mejora:", correo.GetProperty("comunicacion").GetProperty("cuerpo").GetString());
        // Una vez comunicada no se sobrescribe
        await Status(HttpStatusCode.Conflict, e.Docente.PostAsync($"{Base(e.Curso)}/mensajes/{mensajeRuta}/guardar-ruta", null));
    }

    [Fact]
    public async Task Assistant_AsksWhichStudentInsteadOfGuessing()
    {
        var e = await Preparar();
        var duda = await Chat(e.Docente, e.Curso, new { texto = "¿Cómo va Mismo Apellido?" });
        Assert.Equal(["aclaracion"], duda.Select(x => x.Tipo));
        var grupo = Assert.Single(duda[0].Datos.GetProperty("grupos").EnumerateArray());
        Assert.Equal(2, grupo.GetArrayLength());
        // El correo distingue a los homónimos; la cédula no se muestra
        Assert.Equivalent(new[] { $"{e.Repetido1.Cedula}@example.test", $"{e.Repetido2.Cedula}@example.test" }, grupo.EnumerateArray().Select(p => p.GetProperty("correo").GetString()));
        Assert.All(grupo.EnumerateArray(), p => Assert.False(p.TryGetProperty("cedula", out _)));
        // Preguntar no crea nada en el historial
        Assert.Equal(0, await SqlCount("SELECT COUNT(*) FROM EduMonitor_Seguimiento.dbo.Conversaciones WHERE CursoId = @c", ("@c", e.Curso)));

        // Una sola palabra suelta del apellido tampoco se da por buena: se pide confirmar
        var suelta = await Chat(e.Docente, e.Curso, new { texto = "¿Y Gamma cómo va?" });
        Assert.Equal("aclaracion", suelta[0].Tipo);

        // Con la elección del docente (o sin dudas por tratarse de otra persona) la conversación sigue
        var elegido = await Chat(e.Docente, e.Curso, new { texto = "¿Cómo va Mismo Apellido?", estudianteIds = new[] { e.Repetido1.Id } });
        SinError(elegido);
        Assert.Contains(elegido, x => x.Tipo == "inicio");
        Assert.Contains(elegido, x => x.Tipo == "fin");
        var ignorada = await Chat(e.Docente, e.Curso, new { texto = "¿Y Gamma cómo va?", ignorarDudas = true });
        Assert.Contains(ignorada, x => x.Tipo == "inicio");
        // Con nombre completo no hay duda
        var claro = await Chat(e.Docente, e.Curso, new { texto = "¿Cómo va Gamma Tres?" });
        Assert.Equal("inicio", claro[0].Tipo);
    }

    [Fact]
    public async Task Assistant_FreeQuestionsKeepATemporaryPrivateHistoryTheTeacherCanDelete()
    {
        var e = await Preparar();
        // Aunque el docente escriba una cédula, no se guarda ni sale hacia el modelo: se omite
        var primera = await Chat(e.Docente, e.Curso, new { texto = $"¿Qué le recomiendas a Alfa Uno? Su cédula es {e.Alfa.Cedula}." });
        SinError(primera);
        var inicio = Uno(primera, "inicio");
        var conv = inicio.GetProperty("conversacionId").GetInt32();
        Assert.Contains("Alfa Uno", inicio.GetProperty("mensaje").GetProperty("texto").GetString());
        Assert.DoesNotContain(e.Alfa.Cedula, inicio.GetProperty("mensaje").GetProperty("texto").GetString());
        Assert.Equal(e.Alfa.Id, Assert.Single(inicio.GetProperty("alias").EnumerateArray()).GetProperty("estudianteId").GetInt32());
        Assert.Contains("[número omitido]", inicio.GetProperty("mensaje").GetProperty("texto").GetString());
        var fin = Uno(primera, "fin");
        Assert.Contains(fin.GetProperty("origen").GetString(), Origenes);
        Assert.NotEmpty(string.Concat(primera.Where(x => x.Tipo == "texto").Select(x => x.Datos.GetProperty("delta").GetString())));

        // Lo que se guarda no tiene nombres ni cédulas: solo el seudónimo
        Assert.Equal(0, await SqlCount("SELECT COUNT(*) FROM EduMonitor_Seguimiento.dbo.Conversaciones c JOIN EduMonitor_Seguimiento.dbo.Mensajes m ON m.ConversacionId = c.Id WHERE c.Id = @c AND (c.Titulo LIKE '%Alfa%' OR m.Texto LIKE '%Alfa%' OR m.Texto LIKE '%' + @ced + '%')", ("@c", conv), ("@ced", e.Alfa.Cedula)));
        Assert.True(await SqlCount("SELECT COUNT(*) FROM EduMonitor_Seguimiento.dbo.Mensajes WHERE ConversacionId = @c AND Texto LIKE '%Estudiante A%'", ("@c", conv)) > 0);

        // Una segunda pregunta continúa la misma conversación con el mismo seudónimo
        var segunda = await Chat(e.Docente, e.Curso, new { conversacionId = conv, texto = "¿Y qué temas debería reforzar Alfa Uno?" });
        SinError(segunda);
        Assert.Equal(conv, Uno(segunda, "inicio").GetProperty("conversacionId").GetInt32());

        var lista = await Json(e.Docente.GetAsync($"{Base(e.Curso)}/conversaciones"));
        var item = Assert.Single(lista.EnumerateArray());
        Assert.Contains("Alfa Uno", item.GetProperty("titulo").GetString());                      // el docente ve el nombre real
        Assert.True(item.GetProperty("venceEn").GetDateTime() > DateTime.UtcNow.AddDays(29));    // 30 días de retención
        var detalle = await Json(e.Docente.GetAsync($"{Base(e.Curso)}/conversaciones/{conv}"));
        var mensajes = detalle.GetProperty("mensajes").EnumerateArray().ToList();
        Assert.True(mensajes.Count >= 4);
        Assert.Equal("docente", mensajes[0].GetProperty("rol").GetString());
        Assert.Contains("Alfa Uno", mensajes[0].GetProperty("texto").GetString());
        Assert.DoesNotContain("Estudiante A", mensajes[0].GetProperty("texto").GetString());
        var respuesta = mensajes.First(m => m.GetProperty("rol").GetString() == "asistente");
        await Status(HttpStatusCode.NoContent, e.Docente.PostAsJsonAsync($"{Base(e.Curso)}/mensajes/{respuesta.GetProperty("id").GetInt32()}/valoracion", new { util = true }));
        Assert.True((await Json(e.Docente.GetAsync($"{Base(e.Curso)}/conversaciones/{conv}"))).GetProperty("mensajes").EnumerateArray().First(m => m.GetProperty("rol").GetString() == "asistente").GetProperty("util").GetBoolean());

        // El historial es privado: otro docente no lo ve ni lo toca
        await Status(HttpStatusCode.NotFound, e.Otro.GetAsync($"{Base(e.Curso)}/conversaciones/{conv}"));
        await Status(HttpStatusCode.NotFound, e.Otro.DeleteAsync($"{Base(e.Curso)}/conversaciones/{conv}"));
        await Status(HttpStatusCode.NotFound, e.Otro.PostAsJsonAsync($"{Base(e.Curso)}/mensajes/{respuesta.GetProperty("id").GetInt32()}/valoracion", new { util = false }));

        // Es temporal: pasados los 30 días desaparece sola al abrir la lista
        await Sql("UPDATE EduMonitor_Seguimiento.dbo.Conversaciones SET ActualizadoEn = DATEADD(day, -31, SYSUTCDATETIME()) WHERE Id = @c", ("@c", conv));
        Assert.Empty((await Json(e.Docente.GetAsync($"{Base(e.Curso)}/conversaciones"))).EnumerateArray());
        await Status(HttpStatusCode.NotFound, e.Docente.GetAsync($"{Base(e.Curso)}/conversaciones/{conv}"));
        Assert.Equal(0, await SqlCount("SELECT COUNT(*) FROM EduMonitor_Seguimiento.dbo.Mensajes WHERE ConversacionId = @c", ("@c", conv)));

        // Y el docente la puede borrar cuando quiera
        var otra = Uno(await Chat(e.Docente, e.Curso, new { preguntaId = "resumen" }), "inicio").GetProperty("conversacionId").GetInt32();
        await Status(HttpStatusCode.NoContent, e.Docente.DeleteAsync($"{Base(e.Curso)}/conversaciones/{otra}"));
        await Status(HttpStatusCode.NotFound, e.Docente.GetAsync($"{Base(e.Curso)}/conversaciones/{otra}"));
        Assert.Equal(0, await SqlCount("SELECT COUNT(*) FROM EduMonitor_Seguimiento.dbo.Mensajes WHERE ConversacionId = @c", ("@c", otra)));
    }

    private static async Task EsperarAnalisis(HttpClient c, int curso)
    {
        var limite = DateTime.UtcNow.AddSeconds(240);
        while (true)
        {
            var panel = await Json(c.GetAsync($"/api/seguimiento/cursos/{curso}"));
            if (panel.GetProperty("analisis").ValueKind == JsonValueKind.Object && panel.GetProperty("analisis").GetProperty("estado").GetString() == "Completado"
                && panel.GetProperty("estudiantes").EnumerateArray().All(x => x.GetProperty("recomendacion").ValueKind != JsonValueKind.Object || x.GetProperty("recomendacion").GetProperty("estado").GetString() != "Generando"))
                return;
            Assert.True(DateTime.UtcNow < limite, "El análisis no terminó a tiempo.");
            await Task.Delay(800);
        }
    }
}
