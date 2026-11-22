using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Xunit;
namespace EduMonitor.IntegrationTests;

// Carreras nuevas y mallas curriculares versionadas. Las carreras de prueba se llaman "Prueba ..." y se borran al terminar.
[Collection("Sistema")]
public sealed class MallasTests : IAsyncLifetime
{
    private readonly string run = "IT" + Guid.NewGuid().ToString("N")[..12];
    private readonly HttpClient admin = Client();
    private string estadoInicial = "Preparación";
    private const string TeacherPassword = "IntegrationTeacher9!";
    private const string Cab = "nivel,nombre,unidad,tipo,codigo_uta,horas_contacto_docente,horas_practico_experimental,horas_autonomas,horas_totales,creditos\n";
    private static HttpClient Client() => new() { BaseAddress = new Uri(Environment.GetEnvironmentVariable("EDUMONITOR_URL") ?? "http://127.0.0.1:5080"), Timeout = TimeSpan.FromSeconds(20) };

    public async Task InitializeAsync()
    {
        await Login(admin, Environment.GetEnvironmentVariable("ADMIN_EMAIL") ?? throw new Exception("Ejecuta scripts/Test.ps1"), Environment.GetEnvironmentVariable("ADMIN_PASSWORD")!);
        await Limpiar();
        var periodos = await admin.GetFromJsonAsync<JsonElement>("/api/periodos");
        estadoInicial = periodos.EnumerateArray().First(p => p.GetProperty("activo").GetBoolean()).GetProperty("estado").GetString()!;
        // Las pruebas parten con el período en preparación (si estaba en curso, la malla estaría bloqueada); al terminar se restaura
        await Sql("UPDATE EduMonitor_Materias.dbo.Periodos SET Estado = N'Preparación' WHERE Activo = 1");
    }
    public async Task DisposeAsync()
    {
        await Limpiar();
        // El estado del período activo se restaura: las pruebas lo cambian para comprobar el bloqueo
        await Sql("UPDATE EduMonitor_Materias.dbo.Periodos SET Estado = @e WHERE Activo = 1", ("@e", estadoInicial));
        admin.Dispose();
    }
    private async Task Limpiar() => await Sql("""
        UPDATE EduMonitor_Materias.dbo.Cursos SET TeacherId = NULL WHERE TeacherId IN (SELECT Id FROM EduMonitor_Usuarios.dbo.Users WHERE Email LIKE @run);
        DELETE FROM EduMonitor_Estudiantes.dbo.Students WHERE Cedula LIKE @estudiantes;
        DELETE c FROM EduMonitor_Materias.dbo.Cursos c JOIN EduMonitor_Materias.dbo.Paralelos p ON p.Id = c.ParaleloId JOIN EduMonitor_Materias.dbo.Carreras ca ON ca.Id = p.CarreraId WHERE ca.Nombre LIKE 'Prueba %';
        DELETE p FROM EduMonitor_Materias.dbo.Paralelos p JOIN EduMonitor_Materias.dbo.Carreras ca ON ca.Id = p.CarreraId WHERE ca.Nombre LIKE 'Prueba %';
        DELETE m FROM EduMonitor_Materias.dbo.Materias m JOIN EduMonitor_Materias.dbo.Carreras ca ON ca.Id = m.CarreraId WHERE ca.Nombre LIKE 'Prueba %';
        DELETE ma FROM EduMonitor_Materias.dbo.Mallas ma JOIN EduMonitor_Materias.dbo.Carreras ca ON ca.Id = ma.CarreraId WHERE ca.Nombre LIKE 'Prueba %';
        DELETE FROM EduMonitor_Materias.dbo.Carreras WHERE Nombre LIKE 'Prueba %';
        DELETE FROM EduMonitor_Materias.dbo.Periodos WHERE Nombre LIKE 'Prueba Periodo%';
        DELETE FROM EduMonitor_Usuarios.dbo.Users WHERE Email LIKE @run AND Role='Teacher';
        """, ("@run", run + "%"), ("@estudiantes", Cedulas.Prefijo + "%"));
    private static async Task Sql(string comando, params (string, object)[] parametros)
    {
        await using var sql = new SqlConnection(Environment.GetEnvironmentVariable("EDUMONITOR_TEST_SQL"));
        await sql.OpenAsync();
        using var cmd = sql.CreateCommand();
        cmd.CommandText = comando;
        foreach (var (nombre, valor) in parametros)
            cmd.Parameters.AddWithValue(nombre, valor);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task Login(HttpClient client, string email, string password)
    {
        var r = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString());
    }
    private static async Task<JsonElement> Json(Task<HttpResponseMessage> request, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var r = await request;
        Assert.True(r.StatusCode == expected, $"Se esperaba {(int)expected} y se obtuvo {(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return await r.Content.ReadFromJsonAsync<JsonElement>();
    }
    private static async Task Status(HttpStatusCode expected, Task<HttpResponseMessage> request)
    {
        using var r = await request;
        Assert.True(r.StatusCode == expected, $"Se esperaba {(int)expected} y se obtuvo {(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
    }
    private static async Task<string> MensajeDe(HttpResponseMessage r, HttpStatusCode esperado)
    {
        Assert.Equal(esperado, r.StatusCode);
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("message").GetString()!;
    }

    private static string Regular(int nivel, string nombre, int cd = 48, int ape = 32, int aa = 64, string uta = "", string creditos = "") => $"{nivel},{nombre},BASICA,REGULAR,{uta},{cd},{ape},{aa},,{creditos}\n";
    private static string Practicas(int nivel, string nombre, int horas) => $"{nivel},{nombre},PROFESIONAL,SERVICIO_COMUNITARIO,,,,,{horas},\n";
    private Task<HttpResponseMessage> CargarMalla(string codigo, string csv, HttpClient? cliente = null)
    {
        var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(csv)), "archivo", "malla.csv");
        return (cliente ?? admin).PostAsync($"/api/carreras/{codigo}/malla", form);
    }
    private Task<HttpResponseMessage> CargarEstudiantes(string csv, string carrera, int nivel, string paralelo, string materia)
    {
        var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(csv)), "archivo", "estudiantes.csv");
        form.Add(new StringContent(carrera), "carrera");
        form.Add(new StringContent(nivel.ToString()), "nivel");
        form.Add(new StringContent(paralelo), "paralelo");
        form.Add(new StringContent(materia), "materia");
        return admin.PostAsync("/api/estudiantes/precarga", form);
    }
    private async Task<JsonElement> NuevaCarrera(string nombre, int niveles = 2) =>
        await Json(admin.PostAsJsonAsync("/api/carreras", new { nombre, modalidad = "Presencial", niveles }), HttpStatusCode.Created);
    private async Task<JsonElement> Carrera(string codigo) =>
        (await admin.GetFromJsonAsync<JsonElement>("/api/carreras")).EnumerateArray().First(c => c.GetProperty("codigo").GetString() == codigo);
    private async Task<JsonElement[]> Materias(string codigo, string consulta = "") =>
        (await admin.GetFromJsonAsync<JsonElement>($"/api/carreras/{codigo}/materias{consulta}")).EnumerateArray().ToArray();
    private async Task<JsonElement> Paralelo(string carrera, int nivel, string letra, int? malla = null, HttpStatusCode esperado = HttpStatusCode.Created) =>
        await Json(admin.PostAsJsonAsync("/api/paralelos", new { carrera, nivel, letra, malla }), esperado);

    [Fact]
    public async Task Careers_CodeAndUtaPrefixAreAssignedUniqueAndEditableFieldsAreValidated()
    {
        var sugerido = await Json(admin.GetAsync($"/api/carreras/codigo-sugerido?nombre={Uri.EscapeDataString("Prueba Kilo Alfa")}"));
        Assert.Equal("PKA", sugerido.GetProperty("codigo").GetString());
        var prefijoSugerido = sugerido.GetProperty("prefijoUta").GetString()!;
        Assert.Matches("^[0-9]{4}$", prefijoSugerido);
        // El código y el prefijo UTA no se escriben: los asigna el sistema, aunque se envíen. Nombres con las mismas iniciales reciben códigos distintos
        var primera = await Json(admin.PostAsJsonAsync("/api/carreras", new { codigo = "SOF", prefijoUta = "0808", nombre = "Prueba Kilo Alfa", modalidad = "Presencial", niveles = 2 }), HttpStatusCode.Created);
        Assert.Equal("PKA", primera.GetProperty("codigo").GetString());
        Assert.Equal(prefijoSugerido, primera.GetProperty("prefijoUta").GetString());
        Assert.Equal(0, primera.GetProperty("versiones").GetInt32());
        Assert.Equal(0, primera.GetProperty("horasTotales").GetInt32());
        var segunda = await NuevaCarrera("Prueba Kilo Alto");
        var tercera = await NuevaCarrera("Prueba Kilo Aura");
        Assert.Equal("PKA2", segunda.GetProperty("codigo").GetString());
        Assert.Equal("PKA3", tercera.GetProperty("codigo").GetString());
        // Los prefijos son consecutivos y ninguna carrera repite código ni prefijo
        Assert.Equal(int.Parse(prefijoSugerido) + 1, int.Parse(segunda.GetProperty("prefijoUta").GetString()!));
        Assert.Equal(int.Parse(prefijoSugerido) + 2, int.Parse(tercera.GetProperty("prefijoUta").GetString()!));
        var todas = (await admin.GetFromJsonAsync<JsonElement>("/api/carreras")).EnumerateArray().ToList();
        Assert.Equal(todas.Count, todas.Select(c => c.GetProperty("codigo").GetString()).Distinct().Count());
        Assert.Equal(todas.Count, todas.Select(c => c.GetProperty("prefijoUta").GetString()).Distinct().Count());
        // El nombre no se repite; los datos inválidos se rechazan
        Assert.Contains("nombre", await MensajeDe(await admin.PostAsJsonAsync("/api/carreras", new { nombre = "prueba kilo alfa", modalidad = "Presencial", niveles = 2 }), HttpStatusCode.Conflict));
        foreach (var malo in new object[]
        {
            new { nombre = "ab", modalidad = "Presencial", niveles = 2 },
            new { nombre = "Prueba Mala Uno", modalidad = "Presencial", niveles = 0 },
            new { nombre = "Prueba Mala Dos", modalidad = "Presencial", niveles = 13 },
            new { nombre = "Prueba Mala Tres", modalidad = "x", niveles = 2 },
        })
            await Status(HttpStatusCode.BadRequest, admin.PostAsJsonAsync("/api/carreras", malo));
        using (var anonimo = Client())
            await Status(HttpStatusCode.Unauthorized, anonimo.PostAsJsonAsync("/api/carreras", new { nombre = "Prueba Anonima", modalidad = "Presencial", niveles = 2 }));
        // Edición: el código y el prefijo no cambian (aunque se envíen) y los niveles solo aumentan
        var editada = await Json(admin.PutAsJsonAsync("/api/carreras/PKA", new { codigo = "XXX", prefijoUta = "0001", nombre = "Prueba Kilo Alfa Editada", modalidad = "Híbrida", niveles = 3 }));
        Assert.Equal("PKA", editada.GetProperty("codigo").GetString());
        Assert.Equal(prefijoSugerido, editada.GetProperty("prefijoUta").GetString());
        Assert.Equal("Híbrida", editada.GetProperty("modalidad").GetString());
        Assert.Equal(3, editada.GetProperty("niveles").GetInt32());
        Assert.Contains("reducir", await MensajeDe(await admin.PutAsJsonAsync("/api/carreras/PKA", new { nombre = "Prueba Kilo Alfa Editada", modalidad = "Híbrida", niveles = 2 }), HttpStatusCode.Conflict));
        await Status(HttpStatusCode.Conflict, admin.PutAsJsonAsync("/api/carreras/PKA", new { nombre = "Prueba Kilo Alto", modalidad = "Híbrida", niveles = 3 }));
        await Status(HttpStatusCode.NotFound, admin.PutAsJsonAsync("/api/carreras/NOEXISTE", new { nombre = "Prueba Nada", modalidad = "Presencial", niveles = 2 }));
    }

    [Fact]
    public async Task Mallas_AreLoadedReplacedOrVersionedAndLockedWhilePeriodRuns()
    {
        var creada = await NuevaCarrera("Prueba Mallas Uno");
        var codigo = creada.GetProperty("codigo").GetString()!;
        var prefijo = creada.GetProperty("prefijoUta").GetString()!;
        // Archivos con errores: no se guarda nada y cada error dice su fila
        var malos = new (string Csv, string Texto)[]
        {
            (Cab + Regular(1, "Solo Primer Nivel"), "faltan 2"),
            (Cab + "1,Uno,XX,REGULAR,,48,32,64,,\n" + Regular(2, "Dos"), "unidad"),
            (Cab + "1,Uno,BASICA,REGULAR,,48,32,64,200,\n" + Regular(2, "Dos"), "no coincide"),
            (Cab + "1,Uno,BASICA,REGULAR,,48,,,,\n" + Regular(2, "Dos"), "necesita"),
            (Cab + Regular(1, "Uno") + Regular(1, "UNO") + Regular(2, "Dos"), "repetida"),
            (Cab + Regular(1, "Uno") + Regular(3, "Tres"), "nivel"),
            (Cab + "1,Uno,BASICA,REGULAR,,5,5,5,,\n" + Regular(2, "Dos"), "no alcanzan 1 crédito"),
            (Cab + Regular(1, "Uno") + "2,Prac,PROFESIONAL,SERVICIO_COMUNITARIO,,,,,,\n", "horas_totales"),
            (Cab + Regular(1, "Uno", uta: "123") + Regular(2, "Dos"), "8 dígitos"),
            (Cab + Regular(1, "Uno", uta: "11110001") + Regular(2, "Dos", uta: prefijo + "0002"), $"debe empezar con {prefijo}"),
            ("nivel,nombre\n1,Uno\n", "Faltan columnas"),
        };
        foreach (var (csv, texto) in malos)
            Assert.Contains(texto, await MensajeDe(await CargarMalla(codigo, csv), HttpStatusCode.BadRequest));
        Assert.Equal(0, (await Carrera(codigo)).GetProperty("versiones").GetInt32());
        await Status(HttpStatusCode.NotFound, CargarMalla("NOEXISTE", Cab));
        // Primera carga: versión 1. Los créditos se calculan con 48 horas por crédito si no se indican
        var buena = Cab + Regular(1, "Física", uta: prefijo + "0001") + Regular(1, "Cálculo", 48, 48, 96, creditos: "5") + Practicas(2, "Prácticas", 96);
        var v1 = await Json(CargarMalla(codigo, buena));
        Assert.Equal(1, v1.GetProperty("version").GetInt32());
        Assert.True(v1.GetProperty("nuevaVersion").GetBoolean());
        Assert.Equal(3, v1.GetProperty("materias").GetInt32());
        Assert.Equal(144 + 192 + 96, v1.GetProperty("horasTotales").GetInt32());
        Assert.Equal(3 + 5 + 2, v1.GetProperty("creditosTotales").GetInt32());
        var carrera = await Carrera(codigo);
        Assert.Equal(1, carrera.GetProperty("mallaVersion").GetInt32());
        Assert.Equal(prefijo, carrera.GetProperty("prefijoUta").GetString());
        Assert.Equal(3, carrera.GetProperty("materias").GetInt32());
        Assert.Equal(10, carrera.GetProperty("creditosTotales").GetInt32());
        var materias = await Materias(codigo);
        Assert.Equal([$"{codigo}-101", $"{codigo}-102", $"{codigo}-201"], materias.Select(m => m.GetProperty("codigo").GetString()).ToArray());
        Assert.True(materias[0].GetProperty("creditosDerivado").GetBoolean());
        Assert.False(materias[1].GetProperty("creditosDerivado").GetBoolean());
        Assert.Equal(5, materias[1].GetProperty("creditos").GetInt32());
        // Mientras ningún paralelo la use, cargar de nuevo la reemplaza (sigue siendo la versión 1)
        var reemplazo = await Json(CargarMalla(codigo, Cab + Regular(1, "Física Nueva") + Regular(2, "Prácticas Nuevas")));
        Assert.Equal(1, reemplazo.GetProperty("version").GetInt32());
        Assert.False(reemplazo.GetProperty("nuevaVersion").GetBoolean());
        Assert.Equal(["Física Nueva", "Prácticas Nuevas"], (await Materias(codigo)).Select(m => m.GetProperty("nombre").GetString()).ToArray());
        Assert.Equal(1, (await Carrera(codigo)).GetProperty("versiones").GetInt32());
        Assert.Equal(prefijo, (await Carrera(codigo)).GetProperty("prefijoUta").GetString());
        // Con un paralelo que la usa, una carga nueva crea la versión 2 y conserva la 1
        var paraleloV1 = await Paralelo(codigo, 1, "PA");
        Assert.Equal(1, paraleloV1.GetProperty("mallaVersion").GetInt32());
        var v2 = await Json(CargarMalla(codigo, Cab + Regular(1, "Física Versión Dos") + Regular(1, "Cálculo Versión Dos") + Regular(2, "Prácticas Versión Dos")));
        Assert.Equal(2, v2.GetProperty("version").GetInt32());
        Assert.True(v2.GetProperty("nuevaVersion").GetBoolean());
        carrera = await Carrera(codigo);
        Assert.Equal(2, carrera.GetProperty("mallaVersion").GetInt32());
        Assert.Equal(2, carrera.GetProperty("versiones").GetInt32());
        Assert.Equal(3, carrera.GetProperty("materias").GetInt32());
        Assert.Equal($"{codigo}-V2-101", (await Materias(codigo))[0].GetProperty("codigo").GetString());
        Assert.Equal(2, (await Materias(codigo, "?malla=1")).Length);
        Assert.Equal(5, (await Materias(codigo, "?malla=todas")).Length);
        Assert.Equal(3, (await Materias(codigo, "?malla=todas&nivel=1")).Length);
        Assert.Equal("Física Nueva", (await Materias(codigo, "?malla=1"))[0].GetProperty("nombre").GetString());
        await Status(HttpStatusCode.BadRequest, admin.GetAsync($"/api/carreras/{codigo}/materias?malla=abc"));
        // Los paralelos nuevos usan la malla vigente, salvo que se pida otra; los cursos salen de esa malla
        var paraleloV2 = await Paralelo(codigo, 1, "PB");
        Assert.Equal(2, paraleloV2.GetProperty("mallaVersion").GetInt32());
        var paraleloAnterior = await Paralelo(codigo, 1, "PC", malla: 1);
        Assert.Equal(1, paraleloAnterior.GetProperty("mallaVersion").GetInt32());
        await Paralelo(codigo, 1, "PD", malla: 9, esperado: HttpStatusCode.NotFound);
        await Paralelo(codigo, 1, "PB", esperado: HttpStatusCode.Conflict);
        var cursos = (await admin.GetFromJsonAsync<JsonElement>($"/api/cursos?carrera={codigo}&nivel=1")).EnumerateArray().ToArray();
        Assert.Equal(1 + 2 + 1, cursos.Count(c => c.GetProperty("paralelo").GetString() is "PA" or "PB" or "PC"));
        Assert.Equal(2, cursos.Count(c => c.GetProperty("paralelo").GetString() == "PB"));
        Assert.All(cursos.Where(c => c.GetProperty("paralelo").GetString() == "PB"), c => Assert.Equal(2, c.GetProperty("mallaVersion").GetInt32()));
        Assert.All(cursos.Where(c => c.GetProperty("paralelo").GetString() == "PC"), c => Assert.Equal(1, c.GetProperty("mallaVersion").GetInt32()));
        // Una carrera sin malla no admite paralelos
        var sinMalla = (await NuevaCarrera("Prueba Sin Malla")).GetProperty("codigo").GetString()!;
        Assert.Contains("malla", await MensajeDe(await admin.PostAsJsonAsync("/api/paralelos", new { carrera = sinMalla, nivel = 1, letra = "PA" }), HttpStatusCode.Conflict));
        // Solo el administrador crea carreras, carga mallas y maneja el período
        var docente = await Json(admin.PostAsJsonAsync("/api/docentes", new { cedula = Cedulas.Generar(), nombres = "Docente", apellidos = "De Prueba", email = $"{run}d@example.test", telefono = "0999999999", tituloAcademico = "Ingeniero", carreras = new[] { "SOF" }, password = TeacherPassword }), HttpStatusCode.Created);
        Assert.True(docente.GetProperty("id").GetInt32() > 0);
        using (var profesor = Client())
        {
            await Login(profesor, $"{run}d@example.test", TeacherPassword);
            await Status(HttpStatusCode.Forbidden, CargarMalla(codigo, buena, profesor));
            await Status(HttpStatusCode.Forbidden, profesor.PostAsJsonAsync("/api/carreras", new { nombre = "Prueba Docente", modalidad = "Presencial", niveles = 2 }));
            await Status(HttpStatusCode.Forbidden, profesor.PutAsync("/api/periodos/1/iniciar", null));
        }
        // Período: con el período en curso la malla queda bloqueada
        var periodos = (await admin.GetFromJsonAsync<JsonElement>("/api/periodos")).EnumerateArray().ToArray();
        var activo = periodos.First(p => p.GetProperty("activo").GetBoolean()).GetProperty("id").GetInt32();
        await Sql("UPDATE EduMonitor_Materias.dbo.Periodos SET Estado = N'Preparación' WHERE Id = @id", ("@id", activo));
        var otro = await Json(admin.PostAsJsonAsync("/api/periodos", new { nombre = "Prueba Periodo " + run[..6] }), HttpStatusCode.Created);
        Assert.Equal("Preparación", otro.GetProperty("estado").GetString());
        await Status(HttpStatusCode.Conflict, admin.PutAsync($"/api/periodos/{otro.GetProperty("id").GetInt32()}/iniciar", null));
        await Status(HttpStatusCode.Conflict, admin.PutAsync($"/api/periodos/{activo}/finalizar", null));
        await Status(HttpStatusCode.NotFound, admin.PutAsync("/api/periodos/2147483647/iniciar", null));
        Assert.Equal("En curso", (await Json(admin.PutAsync($"/api/periodos/{activo}/iniciar", null))).GetProperty("estado").GetString());
        await Status(HttpStatusCode.Conflict, admin.PutAsync($"/api/periodos/{activo}/iniciar", null));
        Assert.Contains("en curso", await MensajeDe(await CargarMalla(codigo, buena), HttpStatusCode.Conflict));
        await NuevaCarrera("Prueba Durante El Periodo");
        Assert.Equal("Finalizado", (await Json(admin.PutAsync($"/api/periodos/{activo}/finalizar", null))).GetProperty("estado").GetString());
        var v3 = await Json(CargarMalla(codigo, Cab + Regular(1, "Física Tres") + Regular(2, "Prácticas Tres")));
        Assert.Equal(3, v3.GetProperty("version").GetInt32());
    }

    [Fact]
    public async Task Students_ContinueWithTheirMallaAndNewOnesUseTheNewOne()
    {
        var codigo = (await NuevaCarrera("Prueba Alumnos Malla", niveles: 1)).GetProperty("codigo").GetString()!;
        await Json(CargarMalla(codigo, Cab + Regular(1, "Materia Uno") + Regular(1, "Materia Dos")));
        await Paralelo(codigo, 1, "PA");
        var viejas = await Materias(codigo);
        // La malla 1 ya se usa en un paralelo: la carga siguiente es la malla 2
        Assert.Equal(2, (await Json(CargarMalla(codigo, Cab + Regular(1, "Materia Uno Nueva") + Regular(1, "Materia Dos Nueva")))).GetProperty("version").GetInt32());
        await Paralelo(codigo, 1, "PB");
        var nuevas = (await Materias(codigo)).ToArray();
        string Cod(JsonElement m) => m.GetProperty("codigo").GetString()!;
        var (e1, e2) = (Cedulas.Generar(), Cedulas.Generar());
        string Csv(string cedula, string apellidos) => $"cedula,apellidos,nombres,correo\n{cedula},{apellidos},Prueba,{cedula}@example.test\n";
        // Un estudiante ya registrado sigue con su malla; uno nuevo entra con la nueva
        await Json(CargarEstudiantes(Csv(e1, "Antiguo Uno"), codigo, 1, "PA", Cod(viejas[0])));
        await Json(CargarEstudiantes(Csv(e2, "Nuevo Dos"), codigo, 1, "PB", Cod(nuevas[0])));
        var choque = await MensajeDe(await CargarEstudiantes(Csv(e1, "Antiguo Uno"), codigo, 1, "PB", Cod(nuevas[1])), HttpStatusCode.Conflict);
        Assert.Contains("malla 1", choque);
        Assert.Contains("malla 2", choque);
        Assert.Contains("continúa con la malla", choque);
        Assert.DoesNotContain("créditos", choque);
        Assert.Contains("malla 2", await MensajeDe(await CargarEstudiantes(Csv(e2, "Nuevo Dos"), codigo, 1, "PA", Cod(viejas[1])), HttpStatusCode.Conflict));
        // Dentro de su malla puede seguir inscribiéndose
        await Json(CargarEstudiantes(Csv(e1, "Antiguo Uno"), codigo, 1, "PA", Cod(viejas[1])));
        await Json(CargarEstudiantes(Csv(e2, "Nuevo Dos"), codigo, 1, "PB", Cod(nuevas[1])));
        var estudiantes = (await admin.GetFromJsonAsync<JsonElement>($"/api/estudiantes?carrera={codigo}")).EnumerateArray().ToArray();
        Assert.Equal(2, estudiantes.Length);
        Assert.All(estudiantes.Where(x => x.GetProperty("cedula").GetString() == e1).SelectMany(x => x.GetProperty("inscripciones").EnumerateArray()), i => Assert.StartsWith($"{codigo}-1", i.GetProperty("materiaCodigo").GetString()));
        Assert.All(estudiantes.Where(x => x.GetProperty("cedula").GetString() == e2).SelectMany(x => x.GetProperty("inscripciones").EnumerateArray()), i => Assert.StartsWith($"{codigo}-V2-1", i.GetProperty("materiaCodigo").GetString()));
        var porMateria = (await admin.GetFromJsonAsync<JsonElement>($"/api/estudiantes?carrera={codigo}&materia={Cod(nuevas[0])}")).EnumerateArray().ToArray();
        Assert.Equal(e2, Assert.Single(porMateria).GetProperty("cedula").GetString());
    }

    [Fact]
    public async Task Careers_WithdrawalHidesBlocksNewChangesKeepsHistoryAndCanBeReactivated()
    {
        var codigo = (await NuevaCarrera("Prueba Baja Uno")).GetProperty("codigo").GetString()!;
        await Json(CargarMalla(codigo, Cab + Regular(1, "Física") + Regular(2, "Prácticas")));
        await Paralelo(codigo, 1, "PA");
        var curso = (await admin.GetFromJsonAsync<JsonElement>($"/api/cursos?carrera={codigo}&nivel=1")).EnumerateArray().Single().GetProperty("id").GetInt32();
        var materia = (await Materias(codigo, "?nivel=1")).Single().GetProperty("codigo").GetString()!;
        var docente = await Json(admin.PostAsJsonAsync("/api/docentes", new { cedula = Cedulas.Generar(), nombres = "Docente", apellidos = "De Baja", email = $"{run}b@example.test", telefono = "0999999999", tituloAcademico = "Ingeniero", carreras = new[] { codigo, "SOF" }, password = TeacherPassword }), HttpStatusCode.Created);
        var docenteId = docente.GetProperty("id").GetInt32();
        await Status(HttpStatusCode.OK, admin.PutAsJsonAsync($"/api/cursos/{curso}/docente", new { teacherId = docenteId }));
        var cedula = Cedulas.Generar();
        await Json(CargarEstudiantes($"cedula,apellidos,nombres,correo\n{cedula},Baja Prueba,Prueba,{cedula}@example.test\n", codigo, 1, "PA", materia));
        using var profesor = Client();
        await Login(profesor, $"{run}b@example.test", TeacherPassword);
        bool Contiene(JsonElement lista) => lista.EnumerateArray().Any(c => c.GetProperty("codigo").GetString() == codigo);
        Assert.True(Contiene(await profesor.GetFromJsonAsync<JsonElement>("/api/carreras")));
        Assert.Single((await profesor.GetFromJsonAsync<JsonElement>("/api/cursos")).EnumerateArray().Where(c => c.GetProperty("id").GetInt32() == curso));
        // Solo el administrador; la carrera debe existir
        await Status(HttpStatusCode.Forbidden, profesor.PutAsync($"/api/carreras/{codigo}/baja", null));
        await Status(HttpStatusCode.NotFound, admin.PutAsync("/api/carreras/NOEXISTE/baja", null));
        // Con paralelos en un período que no ha finalizado no se puede dar de baja: primero se finaliza el período
        Assert.Contains("Finaliza el período", await MensajeDe(await admin.PutAsync($"/api/carreras/{codigo}/baja", null), HttpStatusCode.Conflict));
        var activo = (await admin.GetFromJsonAsync<JsonElement>("/api/periodos")).EnumerateArray().First(p => p.GetProperty("activo").GetBoolean()).GetProperty("id").GetInt32();
        await Status(HttpStatusCode.OK, admin.PutAsync($"/api/periodos/{activo}/iniciar", null));
        Assert.Contains("en curso", await MensajeDe(await admin.PutAsync($"/api/carreras/{codigo}/baja", null), HttpStatusCode.Conflict));
        await Status(HttpStatusCode.OK, admin.PutAsync($"/api/periodos/{activo}/finalizar", null));
        var baja = await Json(admin.PutAsync($"/api/carreras/{codigo}/baja", null));
        Assert.False(baja.GetProperty("activa").GetBoolean());
        Assert.NotEqual(JsonValueKind.Null, baja.GetProperty("bajaEn").ValueKind);
        await Status(HttpStatusCode.OK, admin.PutAsync($"/api/carreras/{codigo}/baja", null));
        // Se oculta de las listas normales; el administrador la encuentra con el filtro de estado
        Assert.False(Contiene(await admin.GetFromJsonAsync<JsonElement>("/api/carreras")));
        Assert.True(Contiene(await admin.GetFromJsonAsync<JsonElement>("/api/carreras?estado=bajas")));
        Assert.True(Contiene(await admin.GetFromJsonAsync<JsonElement>("/api/carreras?estado=todas")));
        await Status(HttpStatusCode.BadRequest, admin.GetAsync("/api/carreras?estado=otro"));
        Assert.Empty((await admin.GetFromJsonAsync<JsonElement>($"/api/cursos?carrera={codigo}")).EnumerateArray());
        Assert.Empty((await admin.GetFromJsonAsync<JsonElement>($"/api/paralelos?carrera={codigo}")).EnumerateArray());
        // El docente ya no ve esa carrera ni su curso, y sigue con las demás
        Assert.False(Contiene(await profesor.GetFromJsonAsync<JsonElement>("/api/carreras")));
        Assert.False(Contiene(await profesor.GetFromJsonAsync<JsonElement>("/api/carreras?estado=bajas")));
        Assert.DoesNotContain((await profesor.GetFromJsonAsync<JsonElement>("/api/cursos")).EnumerateArray(), c => c.GetProperty("id").GetInt32() == curso);
        await Status(HttpStatusCode.NotFound, profesor.GetAsync($"/api/cursos/{curso}"));
        await Status(HttpStatusCode.NotFound, profesor.GetAsync($"/api/estudiantes/curso/{curso}"));
        Assert.Contains((await profesor.GetFromJsonAsync<JsonElement>("/api/carreras")).EnumerateArray(), c => c.GetProperty("codigo").GetString() == "SOF");
        // No admite cambios nuevos: malla, edición, paralelos, docentes ni estudiantes
        Assert.Contains("dada de baja", await MensajeDe(await CargarMalla(codigo, Cab + Regular(1, "Física") + Regular(2, "Prácticas")), HttpStatusCode.Conflict));
        Assert.Contains("dada de baja", await MensajeDe(await admin.PutAsJsonAsync($"/api/carreras/{codigo}", new { nombre = "Prueba Baja Uno", modalidad = "Presencial", niveles = 2 }), HttpStatusCode.Conflict));
        Assert.Contains("dada de baja", await MensajeDe(await admin.PostAsJsonAsync("/api/paralelos", new { carrera = codigo, nivel = 1, letra = "PB" }), HttpStatusCode.Conflict));
        Assert.Contains("dada de baja", await MensajeDe(await admin.PutAsJsonAsync($"/api/cursos/{curso}/docente", new { teacherId = docenteId }), HttpStatusCode.Conflict));
        Assert.Contains("dada de baja", await MensajeDe(await CargarEstudiantes($"cedula,apellidos,nombres,correo\n{Cedulas.Generar()},Otro,Prueba,otro@example.test\n", codigo, 1, "PA", materia), HttpStatusCode.BadRequest));
        // El historial se conserva: la malla y los estudiantes siguen ahí
        Assert.Equal(2, (await Materias(codigo)).Length);
        Assert.Single((await admin.GetFromJsonAsync<JsonElement>($"/api/estudiantes?carrera={codigo}")).EnumerateArray());
        // El docente se puede asignar a cursos de otras carreras: su carrera de baja ya no cuenta
        var sof = (await admin.GetFromJsonAsync<JsonElement>("/api/cursos?carrera=SOF&nivel=1")).EnumerateArray().First().GetProperty("id").GetInt32();
        await Status(HttpStatusCode.OK, admin.PutAsJsonAsync($"/api/cursos/{sof}/docente", new { teacherId = docenteId }));
        await Status(HttpStatusCode.OK, admin.PutAsJsonAsync($"/api/cursos/{sof}/docente", new { teacherId = (int?)null }));
        // Reactivar la devuelve a las listas
        var reactivada = await Json(admin.PutAsync($"/api/carreras/{codigo}/reactivar", null));
        Assert.True(reactivada.GetProperty("activa").GetBoolean());
        Assert.Equal(JsonValueKind.Null, reactivada.GetProperty("bajaEn").ValueKind);
        Assert.True(Contiene(await admin.GetFromJsonAsync<JsonElement>("/api/carreras")));
        Assert.False(Contiene(await admin.GetFromJsonAsync<JsonElement>("/api/carreras?estado=bajas")));
        Assert.Single((await admin.GetFromJsonAsync<JsonElement>($"/api/cursos?carrera={codigo}")).EnumerateArray());
        Assert.True(Contiene(await profesor.GetFromJsonAsync<JsonElement>("/api/carreras")));
        await Status(HttpStatusCode.OK, admin.PutAsync($"/api/carreras/{codigo}/reactivar", null));
    }
}
