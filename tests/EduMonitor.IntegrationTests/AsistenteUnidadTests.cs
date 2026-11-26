using System.Runtime.CompilerServices;
using System.Text.Json;
using EduMonitor.Seguimiento.Agente;
using EduMonitor.Seguimiento.Asistente;
using EduMonitor.Seguimiento.DTOs;
using EduMonitor.Seguimiento.Models;
using EduMonitor.Seguimiento.Reglas;
using EduMonitor.Seguimiento.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
namespace EduMonitor.IntegrationTests;

// Pruebas de unidad del asistente académico: búsqueda de estudiantes, seudónimos, ruta de mejora, verificador de cifras y respaldo del chat.
// Son código puro, sin base de datos ni IA: no necesitan la aplicación levantada.
public class AsistenteUnidadTests
{
    static readonly Persona Andres = new(1, "Aguilar Mora", "Andrés Felipe", "andres@x.test");
    static readonly Persona Carlos = new(2, "Aguilar Mora", "Carlos Daniel", "carlos@x.test");
    static readonly Persona Elena = new(3, "Viteri Paz", "Elena Belén", "elena@x.test");
    static readonly Persona Clara = new(4, "Mena Ríos", "Clara", "clara@x.test");
    static readonly Persona Pazmino = new(5, "Pazmiño Yánez", "Andrea Lissette", "andrea@x.test");
    static readonly List<Persona> Lista = [Andres, Carlos, Elena, Clara, Pazmino];

    // ---------- Buscar al estudiante por nombre o cédula ----------

    [Fact]
    public void Search_FindsAStudentByTwoNameWordsIgnoringAccentsAndCase()
    {
        Assert.Equal([Elena], Seudonimos.Buscar("¿Cómo ayudo a Elena Viteri?", Lista).Resueltos);
        Assert.Equal([Elena], Seudonimos.Buscar("ruta para VITERI PAZ elena", Lista).Resueltos);
        Assert.Equal([Pazmino], Seudonimos.Buscar("andrea pazmino", Lista).Resueltos);
        Assert.Empty(Seudonimos.Buscar("andrea pazmino", Lista).Dudas);
    }

    [Fact]
    public void Search_DoesNotIdentifyAnyoneByIdNumber()
    {
        var r = Seudonimos.Buscar("Revisa al estudiante con cédula 1850848522 por favor", Lista);
        Assert.Empty(r.Resueltos);
        Assert.Empty(r.Dudas);
    }

    [Fact]
    public void Search_NeverGuessesBetweenTwoStudentsWhoMatchEquallyWell()
    {
        var r = Seudonimos.Buscar("¿Cómo va Aguilar Mora?", Lista);
        Assert.Empty(r.Resueltos);
        var duda = Assert.Single(r.Dudas);
        Assert.Equivalent(new[] { Andres, Carlos }, duda);
        // Con el nombre completo ya no hay duda
        Assert.Equal([Andres], Seudonimos.Buscar("¿Cómo va Aguilar Mora Andrés?", Lista).Resueltos);
    }

    [Fact]
    public void Search_AsksForConfirmationWhenOnlyOneCapitalizedWordMatches()
    {
        var r = Seudonimos.Buscar("¿Y Viteri cómo va?", Lista);
        Assert.Empty(r.Resueltos);
        Assert.Equal([Elena], Assert.Single(r.Dudas));
        // Una palabra común en minúscula no se confunde con un apellido
        Assert.Empty(Seudonimos.Buscar("la paz y la mena de oro", Lista).Dudas);
    }

    [Fact]
    public void Search_IgnoresQuestionsThatMentionNobody()
    {
        var r = Seudonimos.Buscar("Resume el estado del curso y propón una tutoría", Lista);
        Assert.Empty(r.Resueltos);
        Assert.Empty(r.Dudas);
    }

    [Fact]
    public void Picker_FiltersByNameOnly()
    {
        Assert.Equal([Elena], Seudonimos.Filtrar("viteri", Lista));
        Assert.Equal([Pazmino], Seudonimos.Filtrar("pazmiño andrea", Lista));
        Assert.Empty(Seudonimos.Filtrar("1850848522", Lista));
        Assert.Empty(Seudonimos.Filtrar("a", Lista));
    }

    // ---------- Seudónimos: lo que sale hacia el modelo y lo que vuelve ----------

    [Fact]
    public void Hide_ReplacesNamesAndIdNumbersWithPseudonymsAndNothingPersonalSurvives()
    {
        var alias = new Dictionary<int, string> { [3] = "Estudiante A", [1] = "Estudiante B" };
        var oculto = Seudonimos.Ocultar("Compara a Elena Viteri Paz con Andrés Aguilar (cédula 1850848522) y con la cédula 1804935680 de Clara", alias, Lista);
        Assert.Contains("[número omitido]", oculto);
        Assert.Contains("Estudiante A", oculto);
        Assert.Contains("Estudiante B", oculto);
        foreach (var prohibido in new[] { "Elena", "Viteri", "Andrés", "Aguilar", "1850848522", "1804935680", "Clara" })
            Assert.DoesNotContain(prohibido, oculto);
        // Una racha de palabras del mismo nombre queda como un solo seudónimo
        Assert.DoesNotContain("Estudiante A Estudiante A", oculto);
    }

    [Fact]
    public void Show_TranslatesPseudonymsBackToRealNamesAndLeavesUnknownOnes()
    {
        var nombres = new Dictionary<string, string> { ["Estudiante A"] = "Viteri Paz Elena Belén" };
        Assert.Equal("Viteri Paz Elena Belén debe reforzar. Estudiante Z no existe.", Seudonimos.Mostrar("Estudiante A debe reforzar. Estudiante Z no existe.", nombres));
    }

    [Fact]
    public void Agent_PrefersTheCloudAndKeepsTheLocalModelAsFallback()
    {
        Assert.Equal(["nube", "local"], new OpcionesAgente().Orden);
    }

    [Fact]
    public void Cloud_PausesAfterAConnectionFailureButTheLocalModelDoesNot()
    {
        var nube = new ProveedorCompatibleOpenAI("nube", OrigenRecomendacion.Nube, new ProveedorOpciones { BaseUrl = "https://example.test", Modelo = "m", ApiKey = "k" }, new HttpClient());
        var local = new ProveedorCompatibleOpenAI("local", OrigenRecomendacion.Local, new ProveedorOpciones { BaseUrl = "http://localhost:1", Modelo = "m" }, new HttpClient());
        Assert.False(nube.EnPausa);
        nube.Pausar();
        local.Pausar();
        Assert.True(nube.EnPausa);
        // La local solo tarda en arrancar: pausarla dejaría al sistema sin IA justo cuando es el respaldo.
        Assert.False(local.EnPausa);
    }

    [Fact]
    public void Show_TranslatesGroupedPseudonymsWrittenInPlural()
    {
        var nombres = new Dictionary<string, string> { ["Estudiante A"] = "Elena", ["Estudiante B"] = "Clara", ["Estudiante C"] = "Rosa" };
        Assert.Equal("Tendencia descendente en Elena y Clara.", Seudonimos.Mostrar("Tendencia descendente en Estudiantes A y B.", nombres));
        Assert.Equal("Atender a Elena, Clara y Rosa hoy.", Seudonimos.Mostrar("Atender a Estudiantes A, B y C hoy.", nombres));
        // Si alguna letra no pertenece a la conversación se deja el texto tal cual, sin inventar nombres.
        Assert.Equal("Estudiantes A y Z", Seudonimos.Mostrar("Estudiantes A y Z", nombres));
        Assert.Contains("Estudiante B", Seudonimos.AliasEn("Estudiantes A y B"));
    }

    [Fact]
    public void Translator_TranslatesPluralPseudonymsAcrossStreamedPieces()
    {
        var nombres = new Dictionary<string, string> { ["Estudiante D"] = "Diego", ["Estudiante E"] = "Eva" };
        var t = new Seudonimos.Traductor(nombres);
        var partes = new[] { "Bajan Estudi", "antes D", " y ", "E en las ", "últimas notas." };
        var salida = string.Concat(partes.Select(t.Procesar)) + t.Terminar();
        Assert.Equal("Bajan Diego y Eva en las últimas notas.", salida);
    }

    [Fact]
    public void Translator_DoesNotLeakHalfAPseudonymWhenTheModelStreamsInPieces()
    {
        var nombres = new Dictionary<string, string> { ["Estudiante A"] = "Elena", ["Estudiante B"] = "Clara" };
        var t = new Seudonimos.Traductor(nombres);
        var partes = new[] { "Hoy Estudian", "te A necesita ", "apoyo. Estu", "diante B va ", "bien. Estudiante", " A y E", "studiante B." };
        var salida = string.Concat(partes.Select(t.Procesar)) + t.Terminar();
        Assert.Equal("Hoy Elena necesita apoyo. Clara va bien. Elena y Clara.", salida);
        foreach (var parcial in new[] { "Estudian", "Estu" })
            Assert.DoesNotContain(parcial, salida);
    }

    // ---------- Verificador de cifras ----------

    [Fact]
    public void Verifier_AcceptsFiguresPresentInTheDataAndFlagsInventedOnes()
    {
        const string datos = "Estudiante A: resultado 5,60 sobre 10; nota mínima 7,00; 40 % del curso por debajo del mínimo.";
        Assert.Empty(Verificador.Revisar("Estudiante A tiene 5.6 y el 40% del curso está bajo 7,00. Estudia 3 sesiones de 45 minutos.", datos));
        var raras = Verificador.Revisar("Estudiante A tiene 6,25 y el 75 % del curso está bajo el mínimo.", datos);
        Assert.Contains("6,25", raras);
        Assert.Contains("75 %", raras);
    }

    // ---------- Ruta de mejora ----------


    static DatosCurso Curso(params (int Id, decimal? Resultado, (int Actividad, decimal Nota)[] Notas)[] estudiantes)
    {
        var funciones = new TemaReference(10, "Funciones", 1, "Unidad 1");
        var limites = new TemaReference(11, "Límites", 1, "Unidad 1");
        var actividades = new List<ActividadTemasReference>
        {
            new(1, "Tarea 1", "Tarea", new DateOnly(2026, 10, 1), [funciones], 0),
            new(2, "Examen 1", "Examen", new DateOnly(2026, 10, 20), [funciones, limites], 0),
            new(3, "APE 1", "APE", new DateOnly(2026, 12, 5), [limites], 0),
            new(4, "Examen 2", "Examen", new DateOnly(2026, 12, 15), [funciones, limites], 0),
        };
        var pesos = new Pesos(30, 30, 40);
        var filas = estudiantes.Select(e => new FilaReference(e.Id, $"Apellido{e.Id}", $"Nombre{e.Id}", e.Notas.ToDictionary(n => n.Actividad, n => n.Nota), e.Resultado, "", false, 4 - e.Notas.Length)).ToList();
        var matriz = new MatrizReference(1, new PonderacionReference(30, 30, 40, 7), [.. actividades.Select(a => new ActividadReference(a.Id, a.Nombre, a.Tipo, a.FechaEntrega))], filas);
        var datos = new CursoDato(7m, pesos, [.. actividades.Select(a => new ActividadDato(a.Id, a.Nombre, a.Tipo, a.FechaEntrega))],
            [.. filas.Select(f => new EstudianteDato(f.EstudianteId, f.Resultado, f.Notas))]);
        var lista = estudiantes.Select(e => new Persona(e.Id, $"Apellido{e.Id}", $"Nombre{e.Id}", "")).ToList();
        return new DatosCurso(new CursoReference(1, "Física", "SOF", 1, "A", 9, "2026"), matriz, actividades, lista, new MotorReglas(new OpcionesReglas()).Evaluar(datos), pesos);
    }

    static (int, decimal?, (int, decimal)[]) Estudiante(int id, decimal? resultado, params (int, decimal)[] notas) => (id, resultado, notas);

    [Fact]
    public void Route_ComputesTheNeededAverageAndOrdersTheStagesByDueDate()
    {
        var d = Curso(Estudiante(1, 4.86m, (1, 6m), (2, 4m)));
        var persona = d.Persona(1)!;
        var ruta = RutaMejora.Construir(d, persona, "Estudiante A", new OpcionesReglas());
        Assert.Equal("Exigente", ruta.Estado);
        Assert.Equal(8.8m, ruta.PromedioRequerido);
        Assert.Equal(2, ruta.Pendientes);
        Assert.Equal(["APE 1", "Examen 2"], ruta.Etapas.Select(e => e.Actividades.Single().Nombre));
        Assert.All(ruta.Etapas, e => Assert.Equal(8.8m, e.Meta));
        Assert.Equal("hasta el 5 dic", ruta.Etapas[0].Plazo);
        // Los temas débiles salen de las notas bajas y se asignan a la etapa cuya actividad los evalúa
        Assert.Contains(ruta.Temas, t => t.Tema == "Funciones");
        Assert.Contains("Límites", ruta.Etapas[0].Temas);
        Assert.Equal(["Límites"], ruta.Etapas[0].Debiles);
        // Cada etapa lista lo que evalúa su actividad, con lo que al estudiante más le cuesta primero
        Assert.Equal(["Funciones", "Límites"], ruta.Etapas[1].Temas.Order());
        Assert.Equal(2, ruta.Etapas[1].Temas.Count);
        Assert.All(ruta.Etapas, e => Assert.NotEmpty(e.Acciones));
        Assert.Contains("Estudiante A", ruta.Resumen);
        Assert.DoesNotContain("Nombre1", JsonSerializer.Serialize(ruta));
    }

    [Fact]
    public void StudentGradesCard_ListsOnlyThatStudentsGradesGroupedByTypeWithPendingMarked()
    {
        // Dos estudiantes: la tarjeta de uno nunca debe traer notas del otro.
        var d = Curso(Estudiante(1, 4.86m, (1, 6m), (2, 4m)), Estudiante(2, 9m, (1, 10m), (2, 9m), (3, 8m), (4, 9m)));
        var tabla = Datos.NotasDe(d, d.Persona(1)!, "Estudiante A");
        Assert.Equal("Notas de Estudiante A", tabla.Titulo);
        // Orden por tipo: Tarea, APE, Examen
        Assert.Equal(["Tarea 1", "APE 1", "Examen 1", "Examen 2"], tabla.Filas.Take(4).Select(f => f[0]));
        Assert.Equal("6,00", tabla.Filas[0][2]);
        Assert.Equal("—", tabla.Filas[1][2]); // APE 1 sin nota
        Assert.Equal("4,00", tabla.Filas[2][2]);
        Assert.Equal("—", tabla.Filas[3][2]);
        Assert.Equal("4,86", tabla.Filas[^1][2]);
        Assert.DoesNotContain("10,00", tabla.Filas.SelectMany(f => f));
        Assert.Contains(tabla.Notas, n => n.Contains("sin nota"));
        Assert.DoesNotContain("Nombre1", JsonSerializer.Serialize(tabla));
    }

    [Fact]
    public void Route_SwitchesToRecoveryWhenPassingIsNoLongerPossible()
    {
        var d = Curso(Estudiante(2, 1.43m, (1, 2m), (2, 1m)));
        var ruta = RutaMejora.Construir(d, d.Persona(2)!, "Estudiante B", new OpcionesReglas());
        Assert.Equal("Imposible", ruta.Estado);
        var etapa = Assert.Single(ruta.Etapas);
        Assert.Equal("Plan de recuperación", etapa.Titulo);
        Assert.Null(etapa.Meta);
        Assert.Contains("recuperación", ruta.Situacion);
    }

    [Fact]
    public void Route_NeedsNoStagesForAStudentWhoAlreadyPassedEverything()
    {
        var d = Curso(Estudiante(3, 9m, (1, 9m), (2, 9m), (3, 9m), (4, 9m)));
        var ruta = RutaMejora.Construir(d, d.Persona(3)!, "Estudiante C", new OpcionesReglas());
        Assert.Equal("Aprobado", ruta.Estado);
        Assert.Empty(ruta.Etapas);
    }

    [Fact]
    public void Route_ShowsThatTheGradeIsAlreadySecuredWhenNoFurtherAverageIsNeeded()
    {
        var d = Curso(Estudiante(4, 10m, (1, 10m), (2, 10m), (3, 10m)));
        var ruta = RutaMejora.Construir(d, d.Persona(4)!, "Estudiante D", new OpcionesReglas());
        Assert.Equal("Asegurado", ruta.Estado);
        Assert.All(ruta.Etapas, e => Assert.Null(e.Meta));
    }

    [Fact]
    public void RouteText_AcceptsAValidAnswerAndKeepsTheTemplateForWhatTheModelGotWrong()
    {
        var d = Curso(Estudiante(1, 4.86m, (1, 6m), (2, 4m)));
        var plantilla = RutaMejora.Construir(d, d.Persona(1)!, "Estudiante A", new OpcionesReglas());
        const string valida = """
            {"resumen":"Estudiante A necesita reforzar los temas de la unidad 1.",
             "etapas":[{"acciones":["Repasa límites con 5 ejercicios.","Pide retroalimentación del docente."]}],
             "accionesDocente":["Ofrecer una tutoría breve de refuerzo."],
             "mensajeEstudiante":"Tienes tiempo para mejorar: organiza tu estudio por temas, empieza por límites y consulta tus dudas con anticipación para llegar mejor al examen."}
            """;
        var r = InterpreteRuta.Interpretar(valida, plantilla.Etapas.Count, out var motivo);
        Assert.NotNull(r);
        Assert.Null(motivo);
        var final = InterpreteRuta.Aplicar(plantilla, r!, OrigenRecomendacion.Local, "qwen2.5:3b");
        Assert.Equal("Repasa límites con 5 ejercicios.", final.Etapas[0].Acciones[0]);
        Assert.Equal(plantilla.Etapas[1].Acciones, final.Etapas[1].Acciones);
        Assert.Equal(OrigenRecomendacion.Local, final.Origen);
        Assert.Equal(plantilla.Etapas[0].Meta, final.Etapas[0].Meta);
    }

    [Theory]
    [InlineData("")]
    [InlineData("no es json")]
    [InlineData("{\"resumen\":\"Texto largo suficiente\",\"etapas\":[{\"acciones\":[\"Visita https://sitio.example/ayuda hoy\"]}],\"mensajeEstudiante\":\"x\"}")]
    [InlineData("{\"resumen\":\"Texto largo suficiente\",\"etapas\":[{\"acciones\":[\"学习计划每天复习\"]}],\"mensajeEstudiante\":\"x\"}")]
    public void RouteText_RejectsMalformedOrInappropriateAnswers(string respuesta) =>
        Assert.Null(InterpreteRuta.Interpretar(respuesta, 2, out _));

    [Fact]
    public void RouteEmail_ListsEveryStageWithItsActions()
    {
        var d = Curso(Estudiante(1, 4.86m, (1, 6m), (2, 4m)));
        var json = JsonSerializer.Serialize(RutaMejora.Construir(d, d.Persona(1)!, "Estudiante A", new OpcionesReglas()), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var texto = Comunicador.RutaComoTexto(json);
        Assert.StartsWith("Tu ruta de mejora:", texto);
        Assert.Contains("1. Examen: Examen 2", texto.Replace("APE: APE 1", "Examen: Examen 2")); // formato «n. título (plazo)»
        Assert.Contains("nota objetivo: 8,80", texto);
        Assert.Equal("", Comunicador.RutaComoTexto(null));
        Assert.Equal("", Comunicador.RutaComoTexto("{}"));
    }

    // ---------- Chat con respaldo ----------

    sealed class FalsoChat(string clave, string origen, Func<int, IEnumerable<string>> fragmentos, bool configurado = true) : IProveedorIA
    {
        public int Llamadas;
        public string Clave => clave;
        public string Origen => origen;
        public string Modelo => "modelo-" + clave;
        public bool Configurado => configurado;
        public Task<EstadoProveedor> Estado(CancellationToken ct) => Task.FromResult(new EstadoProveedor(clave, origen, Modelo, configurado, true, "ok"));
        public Task<string> Completar(string sistema, string usuario, CancellationToken ct) => throw new NotSupportedException();
        public async IAsyncEnumerable<string> Conversar(string sistema, IReadOnlyList<MensajeChat> historial, [EnumeratorCancellation] CancellationToken ct)
        {
            var n = ++Llamadas;
            foreach (var f in fragmentos(n))
            {
                await Task.Yield();
                yield return f;
            }
        }
    }

    static AgenteAcademico Agente(params IProveedorIA[] proveedores) => new(new ProveedoresIA(proveedores), new OpcionesAgente(), NullLogger<AgenteAcademico>.Instance);
    static async Task<List<FragmentoChat>> Leer(IAsyncEnumerable<FragmentoChat> flujo)
    {
        var lista = new List<FragmentoChat>();
        await foreach (var f in flujo)
            lista.Add(f);
        return lista;
    }

    [Fact]
    public async Task Chat_StreamsFromTheFirstProviderThatAnswers()
    {
        var local = new FalsoChat("local", OrigenRecomendacion.Local, _ => ["Hola ", "docente."]);
        var nube = new FalsoChat("nube", OrigenRecomendacion.Nube, _ => ["no debería usarse"]);
        var f = await Leer(Agente(local, nube).Conversar("s", [], new SesionAgente(), CancellationToken.None));
        Assert.Equal("Hola docente.", string.Concat(f.Select(x => x.Texto)));
        Assert.All(f, x => Assert.Equal(OrigenRecomendacion.Local, x.Origen));
        Assert.Equal(0, nube.Llamadas);
    }

    [Fact]
    public async Task Chat_FallsBackToTheNextProviderIfTheFirstFailsBeforeWriting()
    {
        var local = new FalsoChat("local", OrigenRecomendacion.Local, _ => throw new HttpRequestException("apagado"));
        var nube = new FalsoChat("nube", OrigenRecomendacion.Nube, _ => ["Respuesta ", "de la nube"]);
        var f = await Leer(Agente(local, nube).Conversar("s", [], new SesionAgente(), CancellationToken.None));
        Assert.Equal("Respuesta de la nube", string.Concat(f.Select(x => x.Texto)));
        Assert.All(f, x => Assert.Equal(OrigenRecomendacion.Nube, x.Origen));
    }

    [Fact]
    public async Task Chat_ReturnsNothingWhenNoProviderCanAnswerSoTheCallerUsesItsFallback()
    {
        var local = new FalsoChat("local", OrigenRecomendacion.Local, _ => throw new HttpRequestException("apagado"));
        var sinConfigurar = new FalsoChat("nube", OrigenRecomendacion.Nube, _ => ["x"], configurado: false);
        Assert.Empty(await Leer(Agente(local, sinConfigurar).Conversar("s", [], new SesionAgente(), CancellationToken.None)));
        Assert.Equal(0, sinConfigurar.Llamadas);
    }

    [Fact]
    public async Task Chat_ReportsAnInterruptedAnswerInsteadOfSwitchingProviderHalfWay()
    {
        IEnumerable<string> Corta(int _) { yield return "Empecé "; throw new TimeoutException("se colgó"); }
        var local = new FalsoChat("local", OrigenRecomendacion.Local, Corta);
        var nube = new FalsoChat("nube", OrigenRecomendacion.Nube, _ => ["otra respuesta"]);
        var recibido = new List<string>();
        await Assert.ThrowsAsync<RespuestaInterrumpidaException>(async () =>
        {
            await foreach (var f in Agente(local, nube).Conversar("s", [], new SesionAgente(), CancellationToken.None))
                recibido.Add(f.Texto);
        });
        Assert.Equal(["Empecé "], recibido);
        Assert.Equal(0, nube.Llamadas);
    }
}
