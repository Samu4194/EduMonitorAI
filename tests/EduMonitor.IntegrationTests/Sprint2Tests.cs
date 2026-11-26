using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Xunit;
namespace EduMonitor.IntegrationTests;

// Sprint 2: HU-05 a HU-08. HTTP real -> Nginx -> YARP -> microservicios -> SQL Server, igual que el Sprint 1.
[Collection("Sistema")]
public sealed class Sprint2Tests : IAsyncLifetime
{
    private readonly string run = "IT" + Guid.NewGuid().ToString("N")[..12];
    private readonly string digitos = Random.Shared.Next(0, 100000).ToString("D5");
    private readonly List<string> letras = [];
    private readonly HttpClient admin = Client();
    private const string TeacherPassword = "IntegrationTeacher9!";
    private static HttpClient Client() => new() { BaseAddress = new Uri(Environment.GetEnvironmentVariable("EDUMONITOR_URL") ?? "http://127.0.0.1:5080"), Timeout = TimeSpan.FromSeconds(20) };
    private string estadoPeriodo = "", fuentePrevia = "";
    public async Task InitializeAsync()
    {
        await Login(admin, Environment.GetEnvironmentVariable("ADMIN_EMAIL") ?? throw new Exception("Ejecuta scripts/Test.ps1"), Environment.GetEnvironmentVariable("ADMIN_PASSWORD")!);
        // Estas pruebas escriben en los cursos y un curso de un período finalizado es de solo lectura: se trabaja con el período en curso
        // y al terminar se restaura el estado que tenía.
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
        // Primero lo que depende de los cursos de prueba (evaluaciones), luego los cursos, paralelos, estudiantes y docentes de esta corrida.
        cmd.CommandText = """
            DECLARE @cursos TABLE (Id int);
            INSERT @cursos SELECT c.Id FROM EduMonitor_Materias.dbo.Cursos c JOIN EduMonitor_Materias.dbo.Paralelos p ON p.Id = c.ParaleloId WHERE p.Letra IN (SELECT value FROM STRING_SPLIT(@letras, ','));
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
        return await r.Content.ReadFromJsonAsync<JsonElement>();
    }
    private async Task<int> Create(HttpClient client, string path, object body)
    {
        var json = await Json(client.PostAsJsonAsync(path, body), HttpStatusCode.Created);
        return json.GetProperty("id").GetInt32();
    }

    // ---- Preparación: docentes, paralelos, cursos y estudiantes de prueba ----
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
    private async Task<string> Paralelo(int nivel = 1, string carrera = "SOF")
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
    private async Task<int[]> Cursos(string letra) =>
        (await admin.GetFromJsonAsync<JsonElement>($"/api/cursos?carrera=SOF&nivel=1&paralelo={letra}")).EnumerateArray().Select(c => c.GetProperty("id").GetInt32()).ToArray();
    private async Task<int> Estudiante(string letra, string apellidos)
    {
        var cedula = Cedulas.Generar();
        return await Create(admin, "/api/estudiantes", new { cedula, apellidos, nombres = "Prueba", correo = $"{cedula}@example.test", carrera = "SOF", nivel = 1, paralelo = letra });
    }
    private async Task Asignar(int curso, int docente) =>
        await Status(HttpStatusCode.OK, admin.PutAsJsonAsync($"/api/cursos/{curso}/docente", new { teacherId = docente }));

    private record Aula(HttpClient Docente, HttpClient Otro, int Curso, int CursoVecino, int CursoAjeno, int[] Estudiantes, int Unidad, int Tema);
    // Docente A: dos cursos del mismo paralelo (curso y vecino). Docente B: un curso de otro paralelo (ajeno). Tres estudiantes en el paralelo de A.
    private async Task<Aula> Preparar(bool conTema = true)
    {
        var (a, ca) = await Docente("A");
        var (b, cb) = await Docente("B");
        var letra = await Paralelo();
        var otra = await Paralelo();
        var cursos = await Cursos(letra);
        var ajenos = await Cursos(otra);
        await Asignar(cursos[0], a);
        await Asignar(cursos[1], a);
        await Asignar(ajenos[0], b);
        var estudiantes = new[] { await Estudiante(letra, "Alfa Uno"), await Estudiante(letra, "Beta Dos"), await Estudiante(letra, "Gamma Tres") };
        await Estudiante(otra, "Intruso Ajeno");
        var unidad = 0;
        var tema = 0;
        if (conTema)
        {
            unidad = await Create(ca, "/api/unidades", new { cursoId = cursos[0], nombre = "Fundamentos", descripcion = "Introducción" });
            tema = (await Json(ca.PostAsJsonAsync($"/api/unidades/{unidad}/temas", new { nombre = "Variables", descripcion = (string?)null, contenidos = new[] { "Tipos de dato" } }), HttpStatusCode.Created)).GetProperty("id").GetInt32();
        }
        return new Aula(ca, cb, cursos[0], cursos[1], ajenos[0], estudiantes, unidad, tema);
    }
    private static object Actividad(int curso, string nombre, string tipo, params int[] temas) => new { cursoId = curso, nombre, tipo, descripcion = (string?)null, fechaEntrega = "2026-12-01", temaIds = temas };
    private static object Nota(int estudiante, decimal? nota) => new { estudianteId = estudiante, nota };
    private static Task<HttpResponseMessage> Guardar(HttpClient c, int actividad, params object[] notas) => c.PutAsJsonAsync($"/api/calificaciones/actividad/{actividad}", new { notas });
    private static decimal Dec(JsonElement e, string nombre) => e.GetProperty(nombre).GetDecimal();
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

    [Fact]
    public async Task HU05_UnitsTopicsAndMinimumContents()
    {
        var aula = await Preparar(conTema: false);
        var d = aula.Docente;
        // Escenario 1: registrar una unidad y verla en su materia
        var u1 = await Create(d, "/api/unidades", new { cursoId = aula.Curso, nombre = "Unidad 1", descripcion = "Primera" });
        var u2 = await Create(d, "/api/unidades", new { cursoId = aula.Curso, nombre = "Unidad 2", descripcion = (string?)null });
        var lista = (await d.GetFromJsonAsync<JsonElement>($"/api/unidades?cursoId={aula.Curso}")).EnumerateArray().ToArray();
        Assert.Equal(["Unidad 1", "Unidad 2"], lista.Select(x => x.GetProperty("nombre").GetString()!).ToArray());
        // Escenario 2: tema con sus contenidos mínimos, relacionado con la unidad
        var tema = await Json(d.PostAsJsonAsync($"/api/unidades/{u1}/temas", new { nombre = "Sintaxis", descripcion = "Reglas", contenidos = new[] { "Variables", "Operadores" } }), HttpStatusCode.Created);
        Assert.Equal(u1, tema.GetProperty("unidadId").GetInt32());
        Assert.Equal(2, tema.GetProperty("contenidos").GetArrayLength());
        var temaId = tema.GetProperty("id").GetInt32();
        var contenido = await Create(d, $"/api/temas/{temaId}/contenidos", new { descripcion = "Estructuras de control" });
        Assert.Equal(3, await SqlCount("SELECT COUNT(*) FROM EduMonitor_Evaluaciones.dbo.Contenidos WHERE TemaId=@t", ("@t", temaId)));
        // Escenario 3: consulta y edición
        await Status(HttpStatusCode.OK, d.PutAsJsonAsync($"/api/unidades/{u1}", new { nombre = "Unidad 1 editada", descripcion = "Nueva" }));
        await Status(HttpStatusCode.OK, d.PutAsJsonAsync($"/api/temas/{temaId}", new { nombre = "Sintaxis básica", descripcion = (string?)null }));
        await Status(HttpStatusCode.OK, d.PutAsJsonAsync($"/api/contenidos/{contenido}", new { descripcion = "Control de flujo" }));
        var editada = (await d.GetFromJsonAsync<JsonElement>($"/api/unidades?cursoId={aula.Curso}")).EnumerateArray().First();
        Assert.Equal("Unidad 1 editada", editada.GetProperty("nombre").GetString());
        var temaEditado = editada.GetProperty("temas")[0];
        Assert.Equal("Sintaxis básica", temaEditado.GetProperty("nombre").GetString());
        Assert.Contains(temaEditado.GetProperty("contenidos").EnumerateArray(), c => c.GetProperty("descripcion").GetString() == "Control de flujo");
        // Organizar: subir la segunda unidad
        await Status(HttpStatusCode.NoContent, d.PostAsJsonAsync($"/api/unidades/{u2}/mover", new { direccion = -1 }));
        var reordenada = (await d.GetFromJsonAsync<JsonElement>($"/api/unidades?cursoId={aula.Curso}")).EnumerateArray().Select(x => x.GetProperty("id").GetInt32()).ToArray();
        Assert.Equal([u2, u1], reordenada);
        // Escenario 4: campos obligatorios, duplicados y valores inválidos
        await Status(HttpStatusCode.BadRequest, d.PostAsJsonAsync("/api/unidades", new { cursoId = aula.Curso, nombre = "", descripcion = (string?)null }));
        await Status(HttpStatusCode.BadRequest, d.PostAsJsonAsync("/api/unidades", new { cursoId = aula.Curso, nombre = "ab", descripcion = (string?)null }));
        await Status(HttpStatusCode.BadRequest, d.PostAsJsonAsync($"/api/unidades/{u1}/temas", new { nombre = "", descripcion = (string?)null, contenidos = Array.Empty<string>() }));
        await Status(HttpStatusCode.BadRequest, d.PostAsJsonAsync($"/api/temas/{temaId}/contenidos", new { descripcion = "x" }));
        await Status(HttpStatusCode.Conflict, d.PostAsJsonAsync("/api/unidades", new { cursoId = aula.Curso, nombre = "Unidad 2", descripcion = (string?)null }));
        Assert.Equal(2, await SqlCount("SELECT COUNT(*) FROM EduMonitor_Evaluaciones.dbo.Unidades WHERE CursoId=@c", ("@c", aula.Curso)));
        // Escenario 5: otro docente y el administrador no pueden modificar contenidos de un curso que no es suyo
        await Status(HttpStatusCode.NotFound, aula.Otro.GetAsync($"/api/unidades?cursoId={aula.Curso}"));
        await Status(HttpStatusCode.NotFound, aula.Otro.PostAsJsonAsync("/api/unidades", new { cursoId = aula.Curso, nombre = "Intrusa", descripcion = (string?)null }));
        await Status(HttpStatusCode.NotFound, aula.Otro.PutAsJsonAsync($"/api/unidades/{u1}", new { nombre = "Cambiada", descripcion = (string?)null }));
        await Status(HttpStatusCode.NotFound, aula.Otro.DeleteAsync($"/api/temas/{temaId}"));
        await Status(HttpStatusCode.NotFound, aula.Otro.PutAsJsonAsync($"/api/contenidos/{contenido}", new { descripcion = "Cambiado" }));
        await Status(HttpStatusCode.Forbidden, admin.PostAsJsonAsync("/api/unidades", new { cursoId = aula.Curso, nombre = "Del administrador", descripcion = (string?)null }));
        await Status(HttpStatusCode.OK, admin.GetAsync($"/api/unidades?cursoId={aula.Curso}"));
        await Status(HttpStatusCode.Unauthorized, Client().GetAsync($"/api/unidades?cursoId={aula.Curso}"));
        // Los contenidos del vecino (otra materia del mismo paralelo) no se mezclan
        Assert.Equal(0, (await d.GetFromJsonAsync<JsonElement>($"/api/unidades?cursoId={aula.CursoVecino}")).GetArrayLength());
        // Eliminar: una unidad sin actividades se borra con sus temas y contenidos
        await Status(HttpStatusCode.NoContent, d.DeleteAsync($"/api/unidades/{u1}"));
        Assert.Equal(0, await SqlCount("SELECT COUNT(*) FROM EduMonitor_Evaluaciones.dbo.Contenidos WHERE TemaId=@t", ("@t", temaId)));
    }

    [Fact]
    public async Task HU06_ActivitiesLinkedToTopicsPerCourse()
    {
        var aula = await Preparar();
        var d = aula.Docente;
        var temaVecino = 0;
        {
            var uv = await Create(d, "/api/unidades", new { cursoId = aula.CursoVecino, nombre = "Unidad del vecino", descripcion = (string?)null });
            temaVecino = (await Json(d.PostAsJsonAsync($"/api/unidades/{uv}/temas", new { nombre = "Tema vecino", descripcion = (string?)null, contenidos = Array.Empty<string>() }), HttpStatusCode.Created)).GetProperty("id").GetInt32();
        }
        // Escenario 1 y 2: registrar tarea, APE y examen relacionados con un tema
        var tarea = await Create(d, "/api/actividades", Actividad(aula.Curso, "Tarea 1", "Tarea", aula.Tema));
        var ape = await Create(d, "/api/actividades", Actividad(aula.Curso, "APE 1", "APE", aula.Tema));
        var examen = await Create(d, "/api/actividades", Actividad(aula.Curso, "Examen 1", "Examen", aula.Tema));
        var detalle = await d.GetFromJsonAsync<JsonElement>($"/api/actividades/{tarea}");
        Assert.Equal("Tarea", detalle.GetProperty("tipo").GetString());
        Assert.Equal("Variables", detalle.GetProperty("temas")[0].GetProperty("nombre").GetString());
        Assert.Equal(3, (await d.GetFromJsonAsync<JsonElement>($"/api/actividades?cursoId={aula.Curso}")).GetArrayLength());
        Assert.Equal(1, (await d.GetFromJsonAsync<JsonElement>($"/api/actividades?cursoId={aula.Curso}&tipo=APE")).GetArrayLength());
        Assert.Equal(3, (await d.GetFromJsonAsync<JsonElement>($"/api/actividades?cursoId={aula.Curso}&temaId={aula.Tema}")).GetArrayLength());
        // Escenario 3: edición (cambia el tipo y la fecha)
        await Status(HttpStatusCode.OK, d.PutAsJsonAsync($"/api/actividades/{ape}", new { nombre = "APE 1 corregida", tipo = "APE", descripcion = "Laboratorio", fechaEntrega = "2026-12-15", temaIds = new[] { aula.Tema } }));
        var editada = await d.GetFromJsonAsync<JsonElement>($"/api/actividades/{ape}");
        Assert.Equal("APE 1 corregida", editada.GetProperty("nombre").GetString());
        Assert.Equal("2026-12-15", editada.GetProperty("fechaEntrega").GetString());
        // Editar sin enviar la lista de temas no desvincula la actividad de los que ya tenía
        await Status(HttpStatusCode.OK, d.PutAsJsonAsync($"/api/actividades/{ape}", new { nombre = "APE 1 corregida", tipo = "APE", descripcion = "Laboratorio", fechaEntrega = "2026-12-15" }));
        Assert.Equal(1, (await d.GetFromJsonAsync<JsonElement>($"/api/actividades/{ape}")).GetProperty("temas").GetArrayLength());
        // Escenario 4: validaciones
        await Status(HttpStatusCode.BadRequest, d.PostAsJsonAsync("/api/actividades", Actividad(aula.Curso, "", "Tarea", aula.Tema)));
        await Status(HttpStatusCode.BadRequest, d.PostAsJsonAsync("/api/actividades", Actividad(aula.Curso, "Proyecto", "Proyecto", aula.Tema)));
        // Si el curso ya tiene temas, una actividad nueva debe relacionarse con al menos uno (también cuando ni se envía el campo)
        var sinTema = await MensajeDe(await d.PostAsJsonAsync("/api/actividades", Actividad(aula.Curso, "Sin tema", "Tarea")), HttpStatusCode.BadRequest);
        Assert.Contains("al menos un tema", sinTema);
        await Status(HttpStatusCode.BadRequest, d.PostAsJsonAsync("/api/actividades", new { cursoId = aula.Curso, nombre = "Sin campo de temas", tipo = "Examen", descripcion = (string?)null, fechaEntrega = (string?)null }));
        await Status(HttpStatusCode.BadRequest, d.PostAsJsonAsync("/api/actividades", Actividad(aula.Curso, "Tema ajeno", "Tarea", temaVecino)));
        await Status(HttpStatusCode.Conflict, d.PostAsJsonAsync("/api/actividades", Actividad(aula.Curso, "Tarea 1", "Tarea", aula.Tema)));
        Assert.Equal(3, await SqlCount("SELECT COUNT(*) FROM EduMonitor_Evaluaciones.dbo.Actividades WHERE CursoId=@c", ("@c", aula.Curso)));
        // Escenario 5: separación por materia y paralelo; un docente ajeno no ve ni cambia nada
        Assert.Equal(0, (await d.GetFromJsonAsync<JsonElement>($"/api/actividades?cursoId={aula.CursoVecino}")).GetArrayLength());
        await Status(HttpStatusCode.NotFound, aula.Otro.GetAsync($"/api/actividades?cursoId={aula.Curso}"));
        await Status(HttpStatusCode.NotFound, aula.Otro.GetAsync($"/api/actividades/{tarea}"));
        await Status(HttpStatusCode.NotFound, aula.Otro.PutAsJsonAsync($"/api/actividades/{tarea}", new { nombre = "Robada", tipo = "Tarea", descripcion = (string?)null, fechaEntrega = (string?)null, temaIds = new[] { aula.Tema } }));
        await Status(HttpStatusCode.NotFound, aula.Otro.DeleteAsync($"/api/actividades/{tarea}"));
        await Status(HttpStatusCode.NotFound, aula.Otro.PostAsJsonAsync("/api/actividades", Actividad(aula.Curso, "Intrusa", "Tarea", aula.Tema)));
        await Status(HttpStatusCode.Forbidden, admin.PostAsJsonAsync("/api/actividades", Actividad(aula.Curso, "Del administrador", "Tarea", aula.Tema)));
        // Un tema en uso no se puede eliminar; al borrar las actividades queda libre
        await Status(HttpStatusCode.Conflict, d.DeleteAsync($"/api/temas/{aula.Tema}"));
        await Status(HttpStatusCode.Conflict, d.DeleteAsync($"/api/unidades/{aula.Unidad}"));
        await Status(HttpStatusCode.NoContent, d.DeleteAsync($"/api/actividades/{tarea}"));
        await Status(HttpStatusCode.NoContent, d.DeleteAsync($"/api/actividades/{ape}"));
        await Status(HttpStatusCode.NoContent, d.DeleteAsync($"/api/actividades/{examen}"));
        await Status(HttpStatusCode.NoContent, d.DeleteAsync($"/api/temas/{aula.Tema}"));
    }

    [Fact]
    public async Task HU07_GradesScaleWeightsAndPending()
    {
        var aula = await Preparar();
        var d = aula.Docente;
        var tarea = await Create(d, "/api/actividades", Actividad(aula.Curso, "Tarea 1", "Tarea", aula.Tema));
        var ape = await Create(d, "/api/actividades", Actividad(aula.Curso, "APE 1", "APE", aula.Tema));
        var examen = await Create(d, "/api/actividades", Actividad(aula.Curso, "Examen 1", "Examen", aula.Tema));
        var (s1, s2, s3) = (aula.Estudiantes[0], aula.Estudiantes[1], aula.Estudiantes[2]);
        // Escenario 1: solo aparecen los estudiantes precargados del paralelo del curso
        var matriz = await d.GetFromJsonAsync<JsonElement>($"/api/calificaciones/curso/{aula.Curso}");
        Assert.Equal(3, matriz.GetProperty("estudiantes").GetArrayLength());
        Assert.DoesNotContain(matriz.GetProperty("estudiantes").EnumerateArray(), x => x.GetProperty("apellidos").GetString() == "Intruso Ajeno");
        Assert.Equal(3, matriz.GetProperty("actividades").GetArrayLength());
        Assert.False(matriz.GetProperty("ponderacion").GetProperty("personalizada").GetBoolean());
        // Escenario 2: registro correcto
        var guardado = await Json(Guardar(d, tarea, Nota(s1, 8m), Nota(s2, 4m)));
        Assert.Equal(2, guardado.GetProperty("guardadas").GetInt32());
        Assert.Equal(1, guardado.GetProperty("pendientes").GetInt32());
        await Status(HttpStatusCode.OK, Guardar(d, ape, Nota(s1, 6m)));
        await Status(HttpStatusCode.OK, Guardar(d, examen, Nota(s1, 9m)));
        // Escenario 4: fuera de rango, estudiante de otro paralelo, repetidos; no se guarda nada
        await Status(HttpStatusCode.BadRequest, Guardar(d, tarea, Nota(s3, 7m), Nota(s1, 10.5m)));
        await Status(HttpStatusCode.BadRequest, Guardar(d, tarea, Nota(s3, -1m)));
        await Status(HttpStatusCode.BadRequest, Guardar(d, tarea, Nota(s3, 11m)));
        await Status(HttpStatusCode.BadRequest, Guardar(d, tarea, Nota(int.MaxValue, 5m)));
        await Status(HttpStatusCode.BadRequest, Guardar(d, tarea, Nota(s3, 5m), Nota(s3, 6m)));
        Assert.Equal(0, await SqlCount("SELECT COUNT(*) FROM EduMonitor_Evaluaciones.dbo.Calificaciones WHERE ActividadId=@a AND EstudianteId=@e", ("@a", tarea), ("@e", s3)));
        Assert.Equal(8m, Dec((await d.GetFromJsonAsync<JsonElement>($"/api/calificaciones/curso/{aula.Curso}")).GetProperty("estudiantes").EnumerateArray().First(x => x.GetProperty("estudianteId").GetInt32() == s1).GetProperty("notas"), tarea.ToString()));
        // Escenario 5: ponderaciones predeterminadas 30/30/40. s1 = (30*8 + 30*6 + 40*9) / 100 = 7.80
        var fila = async (int estudiante) => (await d.GetFromJsonAsync<JsonElement>($"/api/calificaciones/curso/{aula.Curso}")).GetProperty("estudiantes").EnumerateArray().First(x => x.GetProperty("estudianteId").GetInt32() == estudiante);
        var f1 = await fila(s1);
        Assert.Equal(7.80m, Dec(f1, "resultado"));
        Assert.Equal("Óptimo", f1.GetProperty("estado").GetString());
        Assert.True(f1.GetProperty("definitivo").GetBoolean());
        // Escenario 6: pendientes no son cero. s2 solo tiene tarea (4): su resultado es 4.00 y es parcial. s3 no tiene nada.
        var f2 = await fila(s2);
        Assert.Equal(4.00m, Dec(f2, "resultado"));
        Assert.False(f2.GetProperty("definitivo").GetBoolean());
        Assert.Equal(2, f2.GetProperty("pendientes").GetInt32());
        Assert.Equal("En riesgo", f2.GetProperty("estado").GetString());
        var f3 = await fila(s3);
        Assert.Equal(JsonValueKind.Null, f3.GetProperty("resultado").ValueKind);
        Assert.Equal("Sin calificaciones", f3.GetProperty("estado").GetString());
        // Edición: cambiar una nota y dejar otra como pendiente (nota nula la elimina)
        await Status(HttpStatusCode.OK, Guardar(d, tarea, Nota(s1, 10m), Nota(s2, null)));
        Assert.Equal(1, await SqlCount("SELECT COUNT(*) FROM EduMonitor_Evaluaciones.dbo.Calificaciones WHERE ActividadId=@a", ("@a", tarea)));
        Assert.Equal(8.40m, Dec(await fila(s1), "resultado"));
        Assert.Equal(JsonValueKind.Null, (await fila(s2)).GetProperty("resultado").ValueKind);
        // Ponderaciones del curso: deben sumar 100; la nota mínima va de 0 a 10
        await Status(HttpStatusCode.BadRequest, d.PutAsJsonAsync($"/api/calificaciones/curso/{aula.Curso}/ponderaciones", new { tareas = 30m, ape = 30m, examen = 30m, notaMinima = 7m }));
        await Status(HttpStatusCode.BadRequest, d.PutAsJsonAsync($"/api/calificaciones/curso/{aula.Curso}/ponderaciones", new { tareas = 120m, ape = -10m, examen = -10m, notaMinima = 7m }));
        await Status(HttpStatusCode.BadRequest, d.PutAsJsonAsync($"/api/calificaciones/curso/{aula.Curso}/ponderaciones", new { tareas = 20m, ape = 30m, examen = 50m, notaMinima = 11m }));
        await Status(HttpStatusCode.OK, d.PutAsJsonAsync($"/api/calificaciones/curso/{aula.Curso}/ponderaciones", new { tareas = 20m, ape = 30m, examen = 50m, notaMinima = 7m }));
        // s1: tarea 10, ape 6, examen 9 con 20/30/50 = (200 + 180 + 450) / 100 = 8.30
        Assert.Equal(8.30m, Dec(await fila(s1), "resultado"));
        Assert.True((await d.GetFromJsonAsync<JsonElement>($"/api/calificaciones/curso/{aula.Curso}/ponderaciones")).GetProperty("personalizada").GetBoolean());
        // Dos actividades del mismo tipo se promedian antes de ponderar: tarea 2 con 4 -> tarea = (10 + 4) / 2 = 7; (140 + 180 + 450) / 100 = 7.70
        var tarea2 = await Create(d, "/api/actividades", Actividad(aula.Curso, "Tarea 2", "Tarea", aula.Tema));
        Assert.False((await fila(s1)).GetProperty("definitivo").GetBoolean());
        await Status(HttpStatusCode.OK, Guardar(d, tarea2, Nota(s1, 4m)));
        Assert.Equal(7.70m, Dec(await fila(s1), "resultado"));
        // Acceso restringido: otro docente no ve ni modifica; el administrador solo consulta
        await Status(HttpStatusCode.NotFound, aula.Otro.GetAsync($"/api/calificaciones/curso/{aula.Curso}"));
        await Status(HttpStatusCode.NotFound, Guardar(aula.Otro, tarea, Nota(s1, 1m)));
        await Status(HttpStatusCode.NotFound, aula.Otro.PutAsJsonAsync($"/api/calificaciones/curso/{aula.Curso}/ponderaciones", new { tareas = 100m, ape = 0m, examen = 0m, notaMinima = 0m }));
        await Status(HttpStatusCode.Forbidden, Guardar(admin, tarea, Nota(s1, 1m)));
        await Status(HttpStatusCode.OK, admin.GetAsync($"/api/calificaciones/curso/{aula.Curso}"));
        // El curso vecino (otra materia del mismo paralelo) tiene sus propias actividades y notas
        Assert.Equal(0, (await d.GetFromJsonAsync<JsonElement>($"/api/calificaciones/curso/{aula.CursoVecino}")).GetProperty("actividades").GetArrayLength());
    }

    [Fact]
    public async Task HU08_PerformanceIndicatorsByGroupStudentAndTopic()
    {
        var aula = await Preparar();
        var d = aula.Docente;
        var tema2 = (await Json(d.PostAsJsonAsync($"/api/unidades/{aula.Unidad}/temas", new { nombre = "Funciones", descripcion = (string?)null, contenidos = Array.Empty<string>() }), HttpStatusCode.Created)).GetProperty("id").GetInt32();
        var tarea = await Create(d, "/api/actividades", Actividad(aula.Curso, "Tarea 1", "Tarea", aula.Tema));
        var ape = await Create(d, "/api/actividades", Actividad(aula.Curso, "APE 1", "APE", aula.Tema));
        var examen = await Create(d, "/api/actividades", Actividad(aula.Curso, "Examen 1", "Examen", tema2));
        var (s1, s2, s3) = (aula.Estudiantes[0], aula.Estudiantes[1], aula.Estudiantes[2]);
        // Sin calificaciones: se indica, no se inventan resultados
        var vacio = await d.GetFromJsonAsync<JsonElement>($"/api/rendimiento/curso/{aula.Curso}");
        Assert.Equal(JsonValueKind.Null, vacio.GetProperty("resumen").GetProperty("promedioGrupo").ValueKind);
        Assert.False(vacio.GetProperty("resumen").GetProperty("completo").GetBoolean());
        await Status(HttpStatusCode.OK, Guardar(d, tarea, Nota(s1, 8m), Nota(s2, 4m)));
        await Status(HttpStatusCode.OK, Guardar(d, ape, Nota(s1, 6m)));
        await Status(HttpStatusCode.OK, Guardar(d, examen, Nota(s1, 9m)));
        // Escenario 1: por materia y paralelo. s1 = 7.80, s2 = 4.00 (parcial), s3 sin notas.
        var r = await d.GetFromJsonAsync<JsonElement>($"/api/rendimiento/curso/{aula.Curso}");
        var resumen = r.GetProperty("resumen");
        Assert.Equal(3, resumen.GetProperty("totalEstudiantes").GetInt32());
        Assert.Equal(2, resumen.GetProperty("conResultado").GetInt32());
        Assert.Equal(5.90m, Dec(resumen, "promedioGrupo"));
        Assert.Equal(1, resumen.GetProperty("enRiesgo").GetInt32());
        Assert.Equal(50.0m, Dec(resumen, "porcentajeRiesgo"));
        Assert.Equal(4.00m, Dec(resumen, "notaMasBaja"));
        Assert.Equal(5, resumen.GetProperty("notasPendientes").GetInt32());
        Assert.False(resumen.GetProperty("completo").GetBoolean());
        // Escenario 4: identificación de calificaciones bajas (menores a la nota mínima)
        var filas = r.GetProperty("estudiantes").EnumerateArray().ToDictionary(x => x.GetProperty("estudianteId").GetInt32());
        Assert.Equal("Óptimo", filas[s1].GetProperty("estado").GetString());
        Assert.Equal("En riesgo", filas[s2].GetProperty("estado").GetString());
        Assert.Equal("Sin calificaciones", filas[s3].GetProperty("estado").GetString());
        // Escenario 5: lo incompleto no se presenta como definitivo
        Assert.False(filas[s2].GetProperty("definitivo").GetBoolean());
        Assert.Equal(2, filas[s2].GetProperty("pendientes").GetInt32());
        Assert.True(filas[s1].GetProperty("definitivo").GetBoolean());
        // Escenario 3: por tema. tema 1 (tarea y APE): 8, 4, 6 -> promedio 6.00; tema 2 (examen): 9
        var temas = r.GetProperty("temas").EnumerateArray().ToDictionary(x => x.GetProperty("temaId").GetInt32());
        Assert.Equal(6.00m, Dec(temas[aula.Tema], "promedio"));
        Assert.Equal(2, temas[aula.Tema].GetProperty("notasBajas").GetInt32());
        Assert.Equal(3, temas[aula.Tema].GetProperty("calificadas").GetInt32());
        Assert.Equal(9.00m, Dec(temas[tema2], "promedio"));
        var filtrado = await d.GetFromJsonAsync<JsonElement>($"/api/rendimiento/curso/{aula.Curso}?temaId={tema2}");
        Assert.Equal(1, filtrado.GetProperty("resumen").GetProperty("actividades").GetInt32());
        Assert.Equal(9.00m, Dec(filtrado.GetProperty("resumen"), "promedioGrupo"));
        Assert.Single(filtrado.GetProperty("temas").EnumerateArray());
        var porUnidad = await d.GetFromJsonAsync<JsonElement>($"/api/rendimiento/curso/{aula.Curso}?unidadId={aula.Unidad}");
        Assert.Equal(3, porUnidad.GetProperty("resumen").GetProperty("actividades").GetInt32());
        // Escenario 2: consulta individual
        var detalle = await d.GetFromJsonAsync<JsonElement>($"/api/rendimiento/curso/{aula.Curso}/estudiante/{s1}");
        Assert.Equal(7.80m, Dec(detalle, "resultado"));
        Assert.Equal(3, detalle.GetProperty("notas").GetArrayLength());
        Assert.Equal(2, detalle.GetProperty("temas").GetArrayLength());
        var d2 = await d.GetFromJsonAsync<JsonElement>($"/api/rendimiento/curso/{aula.Curso}/estudiante/{s2}");
        Assert.Contains(d2.GetProperty("notas").EnumerateArray(), n => n.GetProperty("nota").ValueKind == JsonValueKind.Null);
        // Un grupo completo se declara completo: todos con todas las notas
        foreach (var act in new[] { tarea, ape, examen })
            await Status(HttpStatusCode.OK, Guardar(d, act, Nota(s1, 8m), Nota(s2, 8m), Nota(s3, 8m)));
        var completo = (await d.GetFromJsonAsync<JsonElement>($"/api/rendimiento/curso/{aula.Curso}")).GetProperty("resumen");
        Assert.True(completo.GetProperty("completo").GetBoolean());
        Assert.Equal(0, completo.GetProperty("notasPendientes").GetInt32());
        Assert.Equal(8.00m, Dec(completo, "promedioGrupo"));
        Assert.Equal(0, completo.GetProperty("enRiesgo").GetInt32());
        // Filtros inválidos y acceso restringido
        await Status(HttpStatusCode.BadRequest, d.GetAsync($"/api/rendimiento/curso/{aula.Curso}?temaId=2147483647"));
        await Status(HttpStatusCode.BadRequest, d.GetAsync($"/api/rendimiento/curso/{aula.Curso}?unidadId=2147483647"));
        await Status(HttpStatusCode.NotFound, d.GetAsync($"/api/rendimiento/curso/{aula.Curso}/estudiante/2147483647"));
        await Status(HttpStatusCode.NotFound, aula.Otro.GetAsync($"/api/rendimiento/curso/{aula.Curso}"));
        await Status(HttpStatusCode.NotFound, aula.Otro.GetAsync($"/api/rendimiento/curso/{aula.Curso}/estudiante/{s1}"));
        await Status(HttpStatusCode.NotFound, d.GetAsync($"/api/rendimiento/curso/{aula.CursoAjeno}"));
        await Status(HttpStatusCode.OK, admin.GetAsync($"/api/rendimiento/curso/{aula.Curso}"));
        // El curso vecino no ve resultados del curso con notas
        Assert.Equal(0, (await d.GetFromJsonAsync<JsonElement>($"/api/rendimiento/curso/{aula.CursoVecino}")).GetProperty("resumen").GetProperty("actividades").GetInt32());
    }

    [Fact]
    public async Task HU05_TopicFilesAreUploadedListedDownloadedAndDeleted()
    {
        var aula = await Preparar();
        var d = aula.Docente;
        Task<HttpResponseMessage> Subir(HttpClient cliente, int tema, string nombre, byte[] contenido)
        {
            var form = new MultipartFormDataContent();
            var parte = new ByteArrayContent(contenido);
            parte.Headers.ContentType = new("application/pdf");
            form.Add(parte, "archivo", nombre);
            return cliente.PostAsync($"/api/temas/{tema}/archivos", form);
        }
        async Task<string> Mensaje(HttpResponseMessage r, HttpStatusCode esperado)
        {
            Assert.True(r.StatusCode == esperado, $"Se esperaba {(int)esperado} y se obtuvo {(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
            return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("message").GetString()!;
        }
        var bytes = System.Text.Encoding.UTF8.GetBytes("%PDF-1.4 ejemplo de guía");
        var subido = await Json(Subir(d, aula.Tema, "Guía 1.pdf", bytes), HttpStatusCode.Created);
        var id = subido.GetProperty("id").GetInt32();
        Assert.Equal("Guía 1.pdf", subido.GetProperty("nombre").GetString());
        Assert.Equal(bytes.Length, subido.GetProperty("tamano").GetInt32());
        // Aparece en el tema, sin su contenido, y se descarga igual que se subió
        var tema = (await d.GetFromJsonAsync<JsonElement>($"/api/unidades?cursoId={aula.Curso}")).EnumerateArray().SelectMany(u => u.GetProperty("temas").EnumerateArray()).First(x => x.GetProperty("id").GetInt32() == aula.Tema);
        var listado = Assert.Single(tema.GetProperty("archivos").EnumerateArray());
        Assert.Equal(id, listado.GetProperty("id").GetInt32());
        Assert.False(listado.TryGetProperty("contenido", out _));
        using (var descarga = await d.GetAsync($"/api/temas/archivos/{id}"))
        {
            Assert.Equal(HttpStatusCode.OK, descarga.StatusCode);
            Assert.Equal(bytes, await descarga.Content.ReadAsByteArrayAsync());
        }
        // Un archivo de varios megas (dentro del límite) también se guarda y se descarga íntegro
        var grande = new byte[3 * 1024 * 1024];
        Random.Shared.NextBytes(grande);
        var idGrande = (await Json(Subir(d, aula.Tema, "Diapositivas.pptx", grande), HttpStatusCode.Created)).GetProperty("id").GetInt32();
        using (var descarga = await d.GetAsync($"/api/temas/archivos/{idGrande}"))
            Assert.Equal(grande, await descarga.Content.ReadAsByteArrayAsync());
        // Reglas: nombre repetido, tipo no admitido, vacío y más de 5 MB
        Assert.Contains("ya tiene", await Mensaje(await Subir(d, aula.Tema, "guía 1.PDF", bytes), HttpStatusCode.Conflict));
        Assert.Contains("no se admite", await Mensaje(await Subir(d, aula.Tema, "programa.exe", bytes), HttpStatusCode.BadRequest));
        Assert.Contains("vacío", await Mensaje(await Subir(d, aula.Tema, "vacio.pdf", []), HttpStatusCode.BadRequest));
        Assert.Contains("5 MB", await Mensaje(await Subir(d, aula.Tema, "pesado.pdf", new byte[5 * 1024 * 1024 + 1]), HttpStatusCode.BadRequest));
        // Otro docente no sube, no descarga ni borra; un tema o archivo inexistente responde 404
        await Status(HttpStatusCode.NotFound, Subir(aula.Otro, aula.Tema, "ajeno.pdf", bytes));
        await Status(HttpStatusCode.NotFound, aula.Otro.GetAsync($"/api/temas/archivos/{id}"));
        await Status(HttpStatusCode.NotFound, aula.Otro.DeleteAsync($"/api/temas/archivos/{id}"));
        await Status(HttpStatusCode.NotFound, d.GetAsync("/api/temas/archivos/2147483647"));
        await Status(HttpStatusCode.Forbidden, admin.GetAsync($"/api/temas/archivos/{id}"));
        // Con el período finalizado se descarga, pero no se sube ni se borra
        await EstadoDelPeriodo("Finalizado");
        await Status(HttpStatusCode.OK, d.GetAsync($"/api/temas/archivos/{id}"));
        await Status(HttpStatusCode.Conflict, Subir(d, aula.Tema, "tarde.pdf", bytes));
        await Status(HttpStatusCode.Conflict, d.DeleteAsync($"/api/temas/archivos/{id}"));
        await EstadoDelPeriodo("En curso");
        // Eliminar el archivo; y al eliminar un tema se van sus archivos
        await Status(HttpStatusCode.NoContent, d.DeleteAsync($"/api/temas/archivos/{id}"));
        await Status(HttpStatusCode.NotFound, d.GetAsync($"/api/temas/archivos/{id}"));
        await Status(HttpStatusCode.NoContent, d.DeleteAsync($"/api/temas/archivos/{idGrande}"));
        var otroTema = (await Json(d.PostAsJsonAsync($"/api/unidades/{aula.Unidad}/temas", new { nombre = "Tema con archivo", descripcion = (string?)null, contenidos = Array.Empty<string>() }), HttpStatusCode.Created)).GetProperty("id").GetInt32();
        var huerfano = (await Json(Subir(d, otroTema, "apuntes.pdf", bytes), HttpStatusCode.Created)).GetProperty("id").GetInt32();
        await Status(HttpStatusCode.NoContent, d.DeleteAsync($"/api/temas/{otroTema}"));
        Assert.Equal(0, await SqlCount("SELECT COUNT(*) FROM EduMonitor_Evaluaciones.dbo.Archivos WHERE Id=@a", ("@a", huerfano)));
    }

    [Fact]
    public async Task Courses_OfAFinishedPeriodAreReadOnly()
    {
        var aula = await Preparar();
        var d = aula.Docente;
        var tarea = await Create(d, "/api/actividades", Actividad(aula.Curso, "Tarea 1", "Tarea", aula.Tema));
        await Status(HttpStatusCode.OK, Guardar(d, tarea, Nota(aula.Estudiantes[0], 8m)));
        Assert.Equal("En curso", (await d.GetFromJsonAsync<JsonElement>($"/api/cursos/{aula.Curso}")).GetProperty("periodoEstado").GetString());
        await EstadoDelPeriodo("Finalizado");
        Assert.Equal("Finalizado", (await d.GetFromJsonAsync<JsonElement>($"/api/cursos/{aula.Curso}")).GetProperty("periodoEstado").GetString());
        // Se puede consultar todo
        await Status(HttpStatusCode.OK, d.GetAsync($"/api/unidades?cursoId={aula.Curso}"));
        await Status(HttpStatusCode.OK, d.GetAsync($"/api/actividades?cursoId={aula.Curso}"));
        await Status(HttpStatusCode.OK, d.GetAsync($"/api/actividades/{tarea}"));
        await Status(HttpStatusCode.OK, d.GetAsync($"/api/calificaciones/curso/{aula.Curso}"));
        await Status(HttpStatusCode.OK, d.GetAsync($"/api/rendimiento/curso/{aula.Curso}"));
        await Status(HttpStatusCode.OK, d.GetAsync($"/api/estudiantes/curso/{aula.Curso}"));
        // Pero no se cambia contenido, actividades, notas ni ponderaciones
        async Task Bloqueado(Task<HttpResponseMessage> peticion)
        {
            using var r = await peticion;
            Assert.True(r.StatusCode == HttpStatusCode.Conflict, $"Se esperaba 409 y se obtuvo {(int)r.StatusCode}");
            Assert.Contains("solo lectura", (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("message").GetString());
        }
        await Bloqueado(d.PostAsJsonAsync("/api/unidades", new { cursoId = aula.Curso, nombre = "Otra unidad", descripcion = (string?)null }));
        await Bloqueado(d.PutAsJsonAsync($"/api/unidades/{aula.Unidad}", new { nombre = "Renombrada", descripcion = (string?)null }));
        await Bloqueado(d.PostAsJsonAsync($"/api/unidades/{aula.Unidad}/temas", new { nombre = "Otro tema", descripcion = (string?)null, contenidos = Array.Empty<string>() }));
        await Bloqueado(d.DeleteAsync($"/api/temas/{aula.Tema}"));
        await Bloqueado(d.PostAsJsonAsync("/api/actividades", Actividad(aula.Curso, "Tarea 2", "Tarea", aula.Tema)));
        await Bloqueado(d.PutAsJsonAsync($"/api/actividades/{tarea}", Actividad(aula.Curso, "Tarea cambiada", "Tarea", aula.Tema)));
        await Bloqueado(d.DeleteAsync($"/api/actividades/{tarea}"));
        await Bloqueado(Guardar(d, tarea, Nota(aula.Estudiantes[0], 2m)));
        await Bloqueado(d.PutAsJsonAsync($"/api/calificaciones/curso/{aula.Curso}/ponderaciones", new { tareas = 50, ape = 20, examen = 30, notaMinima = 7 }));
        Assert.Equal(1, await SqlCount("SELECT COUNT(*) FROM EduMonitor_Evaluaciones.dbo.Calificaciones WHERE ActividadId=@a AND Nota=8", ("@a", tarea)));
        // Con el período otra vez en curso se vuelve a trabajar
        await EstadoDelPeriodo("En curso");
        await Status(HttpStatusCode.OK, Guardar(d, tarea, Nota(aula.Estudiantes[0], 9m)));
    }

    [Fact]
    public async Task Students_SoftDeleteHidesFromGroupKeepsGradesAndReactivatesByCsv()
    {
        var aula = await Preparar();
        var d = aula.Docente;
        var tarea = await Create(d, "/api/actividades", Actividad(aula.Curso, "Tarea 1", "Tarea", aula.Tema));
        var (s1, s2) = (aula.Estudiantes[0], aula.Estudiantes[1]);
        await Status(HttpStatusCode.OK, Guardar(d, tarea, Nota(s1, 8m), Nota(s2, 4m)));
        var ficha = await admin.GetFromJsonAsync<JsonElement>($"/api/estudiantes/{s2}");
        Assert.True(ficha.GetProperty("activo").GetBoolean());
        // Solo el administrador da de baja; un id inexistente responde 404
        await Status(HttpStatusCode.Forbidden, d.PostAsync($"/api/estudiantes/{s2}/baja", null));
        await Status(HttpStatusCode.NotFound, admin.PostAsync("/api/estudiantes/2147483647/baja", null));
        var baja = await Json(admin.PostAsync($"/api/estudiantes/{s2}/baja", null));
        Assert.False(baja.GetProperty("activo").GetBoolean());
        Assert.NotEqual(JsonValueKind.Null, baja.GetProperty("bajaEn").ValueKind);
        await Status(HttpStatusCode.OK, admin.PostAsync($"/api/estudiantes/{s2}/baja", null));
        // Desaparece de la lista del docente, de la grilla de notas y del rendimiento
        Assert.Equal(2, (await d.GetFromJsonAsync<JsonElement>($"/api/estudiantes/curso/{aula.Curso}")).GetArrayLength());
        var matriz = await d.GetFromJsonAsync<JsonElement>($"/api/calificaciones/curso/{aula.Curso}");
        Assert.DoesNotContain(matriz.GetProperty("estudiantes").EnumerateArray(), x => x.GetProperty("estudianteId").GetInt32() == s2);
        Assert.Equal(2, (await d.GetFromJsonAsync<JsonElement>($"/api/rendimiento/curso/{aula.Curso}")).GetProperty("resumen").GetProperty("totalEstudiantes").GetInt32());
        await Status(HttpStatusCode.BadRequest, Guardar(d, tarea, Nota(s2, 9m)));
        // El listado del administrador oculta las bajas por defecto y las muestra con el filtro
        var letra = ficha.GetProperty("inscripciones")[0].GetProperty("paralelo").GetString()!;
        bool Aparece(JsonElement lista) => lista.EnumerateArray().Any(x => x.GetProperty("id").GetInt32() == s2);
        Assert.False(Aparece(await admin.GetFromJsonAsync<JsonElement>($"/api/estudiantes?carrera=SOF&nivel=1&paralelo={letra}")));
        Assert.True(Aparece(await admin.GetFromJsonAsync<JsonElement>($"/api/estudiantes?carrera=SOF&nivel=1&paralelo={letra}&estado=bajas")));
        Assert.True(Aparece(await admin.GetFromJsonAsync<JsonElement>($"/api/estudiantes?carrera=SOF&nivel=1&paralelo={letra}&estado=todos")));
        await Status(HttpStatusCode.BadRequest, admin.GetAsync("/api/estudiantes?estado=otro"));
        // Sus notas se conservan y la cédula sigue ocupada: no se puede registrar de nuevo
        Assert.Equal(1, await SqlCount("SELECT COUNT(*) FROM EduMonitor_Evaluaciones.dbo.Calificaciones WHERE ActividadId=@a AND EstudianteId=@e", ("@a", tarea), ("@e", s2)));
        var duplicado = await admin.PostAsJsonAsync("/api/estudiantes", new { cedula = ficha.GetProperty("cedula").GetString(), apellidos = "Otro", nombres = "Nombre", correo = "x@example.test", carrera = "SOF", nivel = 1, paralelo = letra });
        Assert.Equal(HttpStatusCode.Conflict, duplicado.StatusCode);
        Assert.Contains("baja", (await duplicado.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("message").GetString());
        // Reactivación con el endpoint y también al volver a venir en un CSV
        var reactivado = await Json(admin.PostAsync($"/api/estudiantes/{s2}/reactivar", null));
        Assert.True(reactivado.GetProperty("activo").GetBoolean());
        Assert.Equal(JsonValueKind.Null, reactivado.GetProperty("bajaEn").ValueKind);
        await Status(HttpStatusCode.OK, admin.PostAsync($"/api/estudiantes/{s2}/baja", null));
        var materiaCurso = (await admin.GetFromJsonAsync<JsonElement>($"/api/cursos/{aula.Curso}")).GetProperty("materiaCodigo").GetString()!;
        var carga = await Json(CargarCsv(Csv((ficha.GetProperty("cedula").GetString()!, "Beta Dos")), "SOF", 1, letra, materiaCurso));
        Assert.Equal(1, carga.GetProperty("actualizados").GetInt32());
        Assert.Equal(1, carga.GetProperty("reactivados").GetInt32());
        var vuelta = (await d.GetFromJsonAsync<JsonElement>($"/api/calificaciones/curso/{aula.Curso}")).GetProperty("estudiantes").EnumerateArray().First(x => x.GetProperty("estudianteId").GetInt32() == s2);
        Assert.Equal(4m, Dec(vuelta.GetProperty("notas"), tarea.ToString()));
    }

    // La carga CSV es de una materia a la vez: el destino (carrera, nivel, paralelo y materia) siempre se elige y el archivo solo trae a los estudiantes.
    private async Task<HttpResponseMessage> EnviarCsv(string csv, (string Nombre, string Valor)[] destino, HttpClient? cliente = null)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(csv)), "archivo", "estudiantes.csv");
        foreach (var (nombre, valor) in destino)
            form.Add(new StringContent(valor), nombre);
        return await (cliente ?? admin).PostAsync("/api/estudiantes/precarga", form);
    }
    private Task<HttpResponseMessage> CargarCsv(string csv, string carrera, int nivel, string paralelo, string materia, HttpClient? cliente = null) =>
        EnviarCsv(csv, [("carrera", carrera), ("nivel", nivel.ToString()), ("paralelo", paralelo), ("materia", materia)], cliente);
    private static string Csv(params (string Cedula, string Apellidos)[] filas) =>
        "cedula,apellidos,nombres,correo\n" + string.Concat(filas.Select(f => $"{f.Cedula},{f.Apellidos},Prueba,{f.Cedula}@example.test\n"));
    private static async Task<string> MensajeDe(HttpResponseMessage r, HttpStatusCode esperado)
    {
        Assert.Equal(esperado, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("message").GetString()!;
    }
    private async Task<JsonElement> FichaDe(string cedula) =>
        (await admin.GetFromJsonAsync<JsonElement>("/api/estudiantes?estado=todos")).EnumerateArray().First(x => x.GetProperty("cedula").GetString() == cedula);
    private async Task<JsonElement[]> CursosDe(string carrera, int nivel, string letra) =>
        (await admin.GetFromJsonAsync<JsonElement>($"/api/cursos?carrera={carrera}&nivel={nivel}&paralelo={letra}")).EnumerateArray().ToArray();

    [Fact]
    public async Task Students_EnrollmentPerSubjectMixesLevelsAndWithdrawsPerSubject()
    {
        var (a, ca) = await Docente("A");
        var (b, cb) = await Docente("B");
        var letra1 = await Paralelo(1);
        var letra2 = await Paralelo(2);
        var c1 = await CursosDe("SOF", 1, letra1);
        var c2 = await CursosDe("SOF", 2, letra2);
        string Cod(JsonElement c) => c.GetProperty("materiaCodigo").GetString()!;
        int Id(JsonElement c) => c.GetProperty("id").GetInt32();
        Assert.Equal(5, c1.Length);
        await Asignar(Id(c1[0]), a);
        await Asignar(Id(c1[1]), b);
        var (p, q, r) = (Cedulas.Generar(), Cedulas.Generar(), Cedulas.Generar());
        var (fp, fq, fr) = ((p, "Regular Uno"), (q, "Mixta Dos"), (r, "Doble Tres"));
        // El destino es obligatorio y se valida una sola vez; el archivo con problemas no guarda nada
        Assert.Contains("Elige", await MensajeDe(await EnviarCsv(Csv(fp), []), HttpStatusCode.BadRequest));
        Assert.Contains("Elige", await MensajeDe(await EnviarCsv(Csv(fp), [("carrera", "SOF"), ("nivel", "1"), ("paralelo", letra1)]), HttpStatusCode.BadRequest));
        Assert.Contains("materia", await MensajeDe(await CargarCsv(Csv(fp), "SOF", 1, letra1, "Materia Inventada"), HttpStatusCode.BadRequest));
        var otroNivel = (await admin.GetFromJsonAsync<JsonElement>("/api/carreras/SOF/materias?nivel=3")).EnumerateArray().First().GetProperty("codigo").GetString()!;
        Assert.Contains("no existe", await MensajeDe(await CargarCsv(Csv(fp), "SOF", 1, letra1, otroNivel), HttpStatusCode.BadRequest));
        Assert.Contains("paralelo", await MensajeDe(await CargarCsv(Csv(fp), "SOF", 1, "ZZ", Cod(c1[0])), HttpStatusCode.BadRequest));
        Assert.Contains("no existe", await MensajeDe(await CargarCsv(Csv(fp), "NOEXISTE", 1, letra1, Cod(c1[0])), HttpStatusCode.BadRequest));
        Assert.Contains("ya aparece", await MensajeDe(await CargarCsv(Csv(fp, fp), "SOF", 1, letra1, Cod(c1[0])), HttpStatusCode.BadRequest));
        Assert.Contains("no coinciden", await MensajeDe(await CargarCsv(Csv(fp, (p, "Cambiada Tres")), "SOF", 1, letra1, Cod(c1[0])), HttpStatusCode.BadRequest));
        Assert.Equal(0, await SqlCount("SELECT COUNT(*) FROM EduMonitor_Estudiantes.dbo.Students WHERE Cedula IN (@p, @q, @r)", ("@p", p), ("@q", q), ("@r", r)));
        // Cargas independientes: P cursa todo I, Q solo una materia de I y una de II, R dos materias de I
        var primera = await Json(CargarCsv(Csv(fp, fq), "SOF", 1, letra1, Cod(c1[0])));
        Assert.Equal(2, primera.GetProperty("creados").GetInt32());
        Assert.Equal(2, primera.GetProperty("inscripciones").GetInt32());
        var segunda = await Json(CargarCsv(Csv(fp, fr), "SOF", 1, letra1, Cod(c1[1])));
        Assert.Equal(1, segunda.GetProperty("creados").GetInt32());
        Assert.Equal(1, segunda.GetProperty("actualizados").GetInt32());
        await Json(CargarCsv(Csv(fp, fr), "SOF", 1, letra1, Cod(c1[2])));
        await Json(CargarCsv(Csv(fp), "SOF", 1, letra1, Cod(c1[3])));
        await Json(CargarCsv(Csv(fp), "SOF", 1, letra1, Cod(c1[4])));
        await Json(CargarCsv(Csv(fq), "SOF", 2, letra2, Cod(c2[0])));
        async Task<HashSet<string>> Lista(HttpClient c, JsonElement curso) =>
            (await c.GetFromJsonAsync<JsonElement>($"/api/estudiantes/curso/{Id(curso)}")).EnumerateArray().Select(x => x.GetProperty("apellidos").GetString()!).ToHashSet();
        // Cada curso lista solo a quienes cursan esa materia
        Assert.Equal(new HashSet<string> { "Regular Uno", "Mixta Dos" }, await Lista(ca, c1[0]));
        Assert.Equal(new HashSet<string> { "Regular Uno", "Doble Tres" }, await Lista(cb, c1[1]));
        Assert.Equal(new HashSet<string> { "Regular Uno", "Doble Tres" }, await Lista(admin, c1[2]));
        Assert.Equal(new HashSet<string> { "Regular Uno" }, await Lista(admin, c1[3]));
        Assert.Equal(new HashSet<string> { "Mixta Dos" }, await Lista(admin, c2[0]));
        Assert.Empty(await Lista(admin, c2[1]));
        await Status(HttpStatusCode.NotFound, ca.GetAsync($"/api/estudiantes/curso/{Id(c1[1])}"));
        // Listado del administrador: Q cursa en dos niveles; el filtro por materia solo deja a quienes la cursan
        var todos = await admin.GetFromJsonAsync<JsonElement>($"/api/estudiantes?carrera=SOF&paralelo={letra1}");
        Assert.Equal(3, todos.GetArrayLength());
        var estudianteQ = await FichaDe(q);
        Assert.Equal(2, estudianteQ.GetProperty("inscripciones").GetArrayLength());
        var porMateria = await admin.GetFromJsonAsync<JsonElement>($"/api/estudiantes?carrera=SOF&nivel=1&materia={Cod(c1[0])}&paralelo={letra1}");
        Assert.Equal(2, porMateria.GetArrayLength());
        Assert.All(porMateria.EnumerateArray(), x => Assert.Equal(Cod(c1[0]), x.GetProperty("inscripciones").EnumerateArray().Single().GetProperty("materiaCodigo").GetString()));
        Assert.Equal(1, (await admin.GetFromJsonAsync<JsonElement>($"/api/estudiantes?carrera=SOF&nivel=2&paralelo={letra2}")).GetArrayLength());
        // Un docente con notas: al retirar a P de la materia deja de aparecer, sus notas se conservan y sigue en sus demás materias
        var unidad = await Create(ca, "/api/unidades", new { cursoId = Id(c1[0]), nombre = "Unidad", descripcion = (string?)null });
        var tema = (await Json(ca.PostAsJsonAsync($"/api/unidades/{unidad}/temas", new { nombre = "Tema", descripcion = (string?)null, contenidos = Array.Empty<string>() }), HttpStatusCode.Created)).GetProperty("id").GetInt32();
        var tarea = await Create(ca, "/api/actividades", Actividad(Id(c1[0]), "Tarea 1", "Tarea", tema));
        var estudianteP = await FichaDe(p);
        var pId = estudianteP.GetProperty("id").GetInt32();
        await Status(HttpStatusCode.OK, Guardar(ca, tarea, Nota(pId, 8m)));
        var inscripcionP = estudianteP.GetProperty("inscripciones").EnumerateArray().First(i => i.GetProperty("materiaCodigo").GetString() == Cod(c1[0])).GetProperty("id").GetInt32();
        await Status(HttpStatusCode.Forbidden, ca.PostAsync($"/api/estudiantes/inscripciones/{inscripcionP}/baja", null));
        await Status(HttpStatusCode.NotFound, admin.PostAsync("/api/estudiantes/inscripciones/2147483647/baja", null));
        var retirado = await Json(admin.PostAsync($"/api/estudiantes/inscripciones/{inscripcionP}/baja", null));
        Assert.True(retirado.GetProperty("activo").GetBoolean());
        await Status(HttpStatusCode.OK, admin.PostAsync($"/api/estudiantes/inscripciones/{inscripcionP}/baja", null));
        Assert.Equal(new HashSet<string> { "Mixta Dos" }, await Lista(ca, c1[0]));
        Assert.DoesNotContain((await ca.GetFromJsonAsync<JsonElement>($"/api/calificaciones/curso/{Id(c1[0])}")).GetProperty("estudiantes").EnumerateArray(), x => x.GetProperty("estudianteId").GetInt32() == pId);
        await Status(HttpStatusCode.BadRequest, Guardar(ca, tarea, Nota(pId, 9m)));
        Assert.Contains("Regular Uno", await Lista(admin, c1[3]));
        Assert.Equal(1, await SqlCount("SELECT COUNT(*) FROM EduMonitor_Evaluaciones.dbo.Calificaciones WHERE ActividadId=@a AND EstudianteId=@e", ("@a", tarea), ("@e", pId)));
        // Volver a cargarlo en esa materia lo reincorpora con su nota
        var recarga = await Json(CargarCsv(Csv(fp), "SOF", 1, letra1, Cod(c1[0])));
        Assert.Equal(1, recarga.GetProperty("inscripciones").GetInt32());
        Assert.Contains("Regular Uno", await Lista(ca, c1[0]));
        var nota = (await ca.GetFromJsonAsync<JsonElement>($"/api/calificaciones/curso/{Id(c1[0])}")).GetProperty("estudiantes").EnumerateArray().First(x => x.GetProperty("estudianteId").GetInt32() == pId);
        Assert.Equal(8m, Dec(nota.GetProperty("notas"), tarea.ToString()));
        // Retiro y reincorporación con el endpoint
        await Status(HttpStatusCode.OK, admin.PostAsync($"/api/estudiantes/inscripciones/{inscripcionP}/baja", null));
        Assert.True((await Json(admin.PostAsync($"/api/estudiantes/inscripciones/{inscripcionP}/reactivar", null))).GetProperty("activo").GetBoolean());
        Assert.Contains("Regular Uno", await Lista(ca, c1[0]));
        // Edición de datos personales: no toca sus materias
        var editado = await Json(admin.PutAsJsonAsync($"/api/estudiantes/{pId}", new { cedula = p, apellidos = "Regular Editado", nombres = "Prueba", correo = $"{p}@example.test" }));
        Assert.Equal(5, editado.GetProperty("inscripciones").GetArrayLength());
        Assert.Equal(2, (await admin.GetFromJsonAsync<JsonElement>($"/api/estudiantes?carrera=SOF&materia={Cod(c1[0])}&paralelo={letra1}")).GetArrayLength());
        Assert.NotNull(cb);
    }

    [Fact]
    public async Task Students_EnrollmentRulesOneParaleloPerSubjectAndOneCareer()
    {
        var (letraA, letraB) = (await Paralelo(1), await Paralelo(1));
        var letraTi = await Paralelo(1, "TI");
        var cursosA = await CursosDe("SOF", 1, letraA);
        var cursosB = await CursosDe("SOF", 1, letraB);
        var cursosTi = await CursosDe("TI", 1, letraTi);
        string Cod(JsonElement c) => c.GetProperty("materiaCodigo").GetString()!;
        var cedula = Cedulas.Generar();
        var archivo = Csv((cedula, "Regla Uno"));
        await Json(CargarCsv(archivo, "SOF", 1, letraA, Cod(cursosA[0])));
        // La misma materia en otro paralelo se rechaza y dice dónde la cursa
        var otroParalelo = await MensajeDe(await CargarCsv(archivo, "SOF", 1, letraB, Cod(cursosB[0])), HttpStatusCode.Conflict);
        Assert.Contains("ya la cursa", otroParalelo);
        Assert.Contains(letraA, otroParalelo);
        // Otra materia, en otro paralelo del mismo nivel, sí se permite
        await Json(CargarCsv(archivo, "SOF", 1, letraB, Cod(cursosB[1])));
        var estudiante = await FichaDe(cedula);
        Assert.Equal(2, estudiante.GetProperty("inscripciones").GetArrayLength());
        // Otra carrera se rechaza sin guardar nada
        Assert.Contains("dos carreras", await MensajeDe(await CargarCsv(archivo, "TI", 1, letraTi, Cod(cursosTi[0])), HttpStatusCode.Conflict));
        Assert.Equal(2, (await FichaDe(cedula)).GetProperty("inscripciones").GetArrayLength());
        // Cambiar de paralelo es retirar de uno y cargar el otro; con el otro activo no se puede reincorporar el primero
        var primera = estudiante.GetProperty("inscripciones").EnumerateArray().First(i => i.GetProperty("materiaCodigo").GetString() == Cod(cursosA[0])).GetProperty("id").GetInt32();
        await Status(HttpStatusCode.OK, admin.PostAsync($"/api/estudiantes/inscripciones/{primera}/baja", null));
        var cambio = await Json(CargarCsv(archivo, "SOF", 1, letraB, Cod(cursosB[0])));
        Assert.Equal(1, cambio.GetProperty("inscripciones").GetInt32());
        Assert.Contains(letraB, await MensajeDe(await admin.PostAsync($"/api/estudiantes/inscripciones/{primera}/reactivar", null), HttpStatusCode.Conflict));
        // El registro individual no puede repetir una cédula: el CSV es la vía para sumar materias
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/api/estudiantes", new { cedula, apellidos = "Regla Uno", nombres = "Prueba", correo = $"{cedula}@example.test", carrera = "TI", nivel = 1, paralelo = letraTi })).StatusCode);
    }

    [Fact]
    public async Task Students_UploadAlwaysNeedsDestinationAndFileMustAgreeWithIt()
    {
        var letraA = await Paralelo(1);
        var letraB = await Paralelo(1);
        var cursosA = await CursosDe("SOF", 1, letraA);
        string Cod(JsonElement c) => c.GetProperty("materiaCodigo").GetString()!;
        var (p, r) = (Cedulas.Generar(), Cedulas.Generar());
        // Sin destino, con un destino incompleto o solo con las columnas en el archivo no se carga nada
        Assert.Contains("Elige", await MensajeDe(await EnviarCsv(Csv((p, "Destino Uno")), []), HttpStatusCode.BadRequest));
        Assert.Contains("Elige", await MensajeDe(await EnviarCsv(Csv((p, "Destino Uno")), [("carrera", "SOF"), ("nivel", "1"), ("paralelo", letraA)]), HttpStatusCode.BadRequest));
        var conColumnas = $"cedula,apellidos,nombres,correo,carrera,nivel,paralelo,materia\n{p},Destino Uno,Prueba,{p}@example.test,SOF,1,{letraA},{Cod(cursosA[0])}\n";
        Assert.Contains("Elige", await MensajeDe(await EnviarCsv(conColumnas, []), HttpStatusCode.BadRequest));
        Assert.Equal(0, await SqlCount("SELECT COUNT(*) FROM EduMonitor_Estudiantes.dbo.Students WHERE Cedula = @c", ("@c", p)));
        // Con destino completo, el archivo solo trae a los estudiantes y se inscriben en esa materia
        var carga = await Json(CargarCsv(Csv((p, "Destino Uno")), "SOF", 1, letraA, Cod(cursosA[0])));
        Assert.Equal(1, carga.GetProperty("inscripciones").GetInt32());
        Assert.Equal(1, (await FichaDe(p)).GetProperty("inscripciones").GetArrayLength());
        // Si el archivo trae columnas de destino y dicen otra cosa, se rechaza todo
        var otraLetra = $"cedula,apellidos,nombres,correo,paralelo\n{r},Destino Dos,Prueba,{r}@example.test,{letraB}\n";
        var choca = await MensajeDe(await CargarCsv(otraLetra, "SOF", 1, letraA, Cod(cursosA[0])), HttpStatusCode.BadRequest);
        Assert.Contains("estás cargando", choca);
        Assert.Contains(letraA, choca);
        var otraMateria = $"cedula,apellidos,nombres,correo,materia\n{r},Destino Dos,Prueba,{r}@example.test,{Cod(cursosA[1])}\n";
        Assert.Contains("estás cargando", await MensajeDe(await CargarCsv(otraMateria, "SOF", 1, letraA, Cod(cursosA[0])), HttpStatusCode.BadRequest));
        Assert.Equal(0, await SqlCount("SELECT COUNT(*) FROM EduMonitor_Estudiantes.dbo.Students WHERE Cedula = @c", ("@c", r)));
        // Si las columnas coinciden (carrera por nombre, nivel en romano, paralelo en minúscula) la carga es válida
        var coincide = $"cedula,apellidos,nombres,correo,carrera,nivel,paralelo\n{r},Destino Dos,Prueba,{r}@example.test,Software,I,{letraA.ToLowerInvariant()}\n";
        await Json(CargarCsv(coincide, "SOF", 1, letraA, Cod(cursosA[0])));
    }

    [Fact]
    public async Task Students_CreditLimitIs15PerPeriodAndFewerIsAllowed()
    {
        var letra1 = await Paralelo(1);
        var letra2 = await Paralelo(2);
        var c1 = await CursosDe("SOF", 1, letra1);
        var c2 = await CursosDe("SOF", 2, letra2);
        string Cod(JsonElement c) => c.GetProperty("materiaCodigo").GetString()!;
        int Cred(JsonElement c) => c.GetProperty("creditos").GetInt32();
        int Id(JsonElement c) => c.GetProperty("id").GetInt32();
        Assert.Equal(15, c1.Sum(Cred));
        var (p, q, r) = (Cedulas.Generar(), Cedulas.Generar(), Cedulas.Generar());
        // Una sola materia es válido: sumar menos de 15 créditos no es un problema
        await Json(CargarCsv(Csv((q, "Creditos Uno")), "SOF", 1, letra1, Cod(c1[0])));
        var fichaQ = await FichaDe(q);
        Assert.Equal(Cred(c1[0]), fichaQ.GetProperty("creditos").GetInt32());
        Assert.Equal(15, fichaQ.GetProperty("maxCreditos").GetInt32());
        // Todo el nivel I, materia por materia, suma exactamente 15
        foreach (var curso in c1)
            await Json(CargarCsv(Csv((p, "Creditos Dos")), "SOF", 1, letra1, Cod(curso)));
        Assert.Equal(15, (await FichaDe(p)).GetProperty("creditos").GetInt32());
        // Con I completo, cargarlo en una materia de II supera el máximo: se rechaza diciendo por qué y no se guarda nada
        var rechazo = await MensajeDe(await CargarCsv(Csv((p, "Creditos Dos"), (q, "Creditos Uno")), "SOF", 2, letra2, Cod(c2[0])), HttpStatusCode.Conflict);
        Assert.Contains("ya tiene 15 créditos", rechazo);
        Assert.Contains("máximo por período es 15", rechazo);
        Assert.Contains(p, rechazo);
        Assert.DoesNotContain(q, rechazo);
        Assert.Equal(15, (await FichaDe(p)).GetProperty("creditos").GetInt32());
        Assert.Equal(Cred(c1[0]), (await FichaDe(q)).GetProperty("creditos").GetInt32());
        Assert.Empty((await admin.GetFromJsonAsync<JsonElement>($"/api/estudiantes/curso/{Id(c2[0])}")).EnumerateArray());
        // Retirar una materia libera créditos y entonces cabe una de II con los mismos créditos
        var retirada = (await FichaDe(p)).GetProperty("inscripciones").EnumerateArray().First(i => i.GetProperty("materiaCodigo").GetString() == Cod(c1[0])).GetProperty("id").GetInt32();
        await Status(HttpStatusCode.OK, admin.PostAsync($"/api/estudiantes/inscripciones/{retirada}/baja", null));
        Assert.Equal(15 - Cred(c1[0]), (await FichaDe(p)).GetProperty("creditos").GetInt32());
        var deII = c2.First(c => Cred(c) == Cred(c1[0]));
        await Json(CargarCsv(Csv((p, "Creditos Dos")), "SOF", 2, letra2, Cod(deII)));
        Assert.Equal(15, (await FichaDe(p)).GetProperty("creditos").GetInt32());
        // Con el tope lleno no se puede reincorporar la materia retirada
        Assert.Contains("créditos", await MensajeDe(await admin.PostAsync($"/api/estudiantes/inscripciones/{retirada}/reactivar", null), HttpStatusCode.Conflict));
        // La suma cuenta todas las materias, aunque el listado se filtre por nivel
        var filtrada = (await admin.GetFromJsonAsync<JsonElement>($"/api/estudiantes?carrera=SOF&nivel=2&paralelo={letra2}")).EnumerateArray().First(x => x.GetProperty("cedula").GetString() == p);
        Assert.Equal(15, filtrada.GetProperty("creditos").GetInt32());
        // El registro individual de un paralelo completo (15) es válido
        Assert.Equal(HttpStatusCode.Created, (await admin.PostAsJsonAsync("/api/estudiantes", new { cedula = r, apellidos = "Creditos Tres", nombres = "Prueba", correo = $"{r}@example.test", carrera = "SOF", nivel = 1, paralelo = letra1 })).StatusCode);
        Assert.Equal(15, (await FichaDe(r)).GetProperty("creditos").GetInt32());
    }

    private static async Task Docker(params string[] args)
    {
        var start = new System.Diagnostics.ProcessStartInfo("docker") { WorkingDirectory = Environment.GetEnvironmentVariable("EDUMONITOR_ROOT")!, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("compose");
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
    private async Task Esperar(HttpClient client, string path)
    {
        for (var i = 0; i < 30; i++)
        {
            using var r = await client.GetAsync(path);
            if (r.StatusCode == HttpStatusCode.OK)
                return;
            await Task.Delay(500);
        }
        Assert.Fail($"{path} no se recuperó dentro del plazo.");
    }

    [Fact, Trait("Category", "Availability")]
    public async Task Failure_StudentsOrEvaluationsDownDegradeOnlyTheirOwnFeatures()
    {
        var aula = await Preparar();
        var d = aula.Docente;
        var tarea = await Create(d, "/api/actividades", Actividad(aula.Curso, "Tarea 1", "Tarea", aula.Tema));
        await Status(HttpStatusCode.OK, Guardar(d, tarea, Nota(aula.Estudiantes[0], 9m)));
        try
        {
            // Sin Estudiantes: las notas y el rendimiento necesitan la lista del paralelo y avisan; los contenidos y actividades siguen disponibles.
            await Docker("stop", "estudiantes");
            await Status(HttpStatusCode.ServiceUnavailable, d.GetAsync($"/api/calificaciones/curso/{aula.Curso}"));
            await Status(HttpStatusCode.ServiceUnavailable, Guardar(d, tarea, Nota(aula.Estudiantes[0], 1m)));
            await Status(HttpStatusCode.ServiceUnavailable, d.GetAsync($"/api/rendimiento/curso/{aula.Curso}"));
            await Status(HttpStatusCode.OK, d.GetAsync($"/api/unidades?cursoId={aula.Curso}"));
            await Status(HttpStatusCode.OK, d.GetAsync($"/api/actividades?cursoId={aula.Curso}"));
            await Status(HttpStatusCode.OK, d.GetAsync("/api/cursos"));
        }
        finally { await Docker("start", "estudiantes"); await Esperar(admin, "/api/estudiantes"); }
        // La nota no cambió con el fallo y el sistema se recuperó sin reiniciar los demás servicios
        var fila = (await d.GetFromJsonAsync<JsonElement>($"/api/calificaciones/curso/{aula.Curso}")).GetProperty("estudiantes").EnumerateArray().First(x => x.GetProperty("estudianteId").GetInt32() == aula.Estudiantes[0]);
        Assert.Equal(9m, Dec(fila.GetProperty("notas"), tarea.ToString()));
        try
        {
            // Sin Evaluaciones: solo falla la gestión académica; docentes, carreras, cursos y estudiantes siguen funcionando.
            await Docker("stop", "evaluaciones");
            var espera = System.Diagnostics.Stopwatch.StartNew();
            await Status(HttpStatusCode.ServiceUnavailable, d.GetAsync($"/api/unidades?cursoId={aula.Curso}"));
            Assert.True(espera.Elapsed < TimeSpan.FromSeconds(15));
            await Status(HttpStatusCode.ServiceUnavailable, d.PostAsJsonAsync("/api/unidades", new { cursoId = aula.Curso, nombre = "No se guarda", descripcion = (string?)null }));
            await Status(HttpStatusCode.OK, d.GetAsync("/api/cursos"));
            await Status(HttpStatusCode.OK, d.GetAsync($"/api/estudiantes/curso/{aula.Curso}"));
            await Status(HttpStatusCode.OK, admin.GetAsync("/api/docentes"));
        }
        finally { await Docker("start", "evaluaciones"); await Esperar(d, $"/api/unidades?cursoId={aula.Curso}"); }
        Assert.Equal(0, await SqlCount("SELECT COUNT(*) FROM EduMonitor_Evaluaciones.dbo.Unidades WHERE Nombre=N'No se guarda'"));
        Assert.Equal(1, (await d.GetFromJsonAsync<JsonElement>($"/api/unidades?cursoId={aula.Curso}")).GetArrayLength());
    }
}
