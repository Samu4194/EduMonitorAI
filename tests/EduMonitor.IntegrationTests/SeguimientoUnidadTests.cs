using EduMonitor.Seguimiento.Agente;
using EduMonitor.Seguimiento.DTOs;
using EduMonitor.Seguimiento.Models;
using EduMonitor.Seguimiento.Reglas;
using EduMonitor.Seguimiento.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
namespace EduMonitor.IntegrationTests;

// Pruebas de unidad de Sprint 3 (HU-11 y HU-12). No necesitan la aplicación levantada: el motor de reglas, el intérprete de
// respuestas, las plantillas y la cadena de respaldo del agente son código sin base de datos ni red.
public class SeguimientoUnidadTests
{
    static readonly Pesos Pesos = new(30, 30, 40);
    static MotorReglas Motor() => new(new OpcionesReglas());

    static ActividadDato A(int id, string tipo, string? fecha = null) => new(id, $"{tipo} {id}", tipo, fecha is null ? null : DateOnly.Parse(fecha));
    static EstudianteDato E(int id, decimal? resultado, params (int Actividad, decimal Nota)[] notas) => new(id, resultado, notas.ToDictionary(n => n.Actividad, n => n.Nota));
    static CursoDato Curso(IReadOnlyList<ActividadDato> actividades, params EstudianteDato[] estudiantes) => new(7m, Pesos, actividades, estudiantes);
    static ResultadoEstudiante De(ResultadoCurso r, int id) => r.Estudiantes.Single(e => e.EstudianteId == id);

    [Fact]
    public void Rule_LowPerformance_IsMediumBelowMinimumAndHighTwoPointsBelow()
    {
        var actividades = new[] { A(1, "Tarea"), A(2, "APE") };
        var r = Motor().Evaluar(Curso(actividades, E(1, 6.5m, (1, 6.5m), (2, 6.5m)), E(2, 4.5m, (1, 5m), (2, 4m)), E(3, 8m, (1, 8m), (2, 8m))));
        Assert.Equal(Nivel.Medio, De(r, 1).Nivel);
        Assert.Contains(De(r, 1).Hallazgos, h => h.Codigo == MotorReglas.RendimientoBajo && h.Nivel == Nivel.Medio);
        Assert.Equal(Nivel.Alto, De(r, 2).Nivel);
        Assert.Equal(Nivel.Ninguno, De(r, 3).Nivel);
        Assert.Empty(De(r, 3).Hallazgos);
    }

    [Fact]
    public void Rule_CriticalGrades_OneIsMediumTwoAreHigh()
    {
        var actividades = new[] { A(1, "Tarea"), A(2, "Tarea"), A(3, "APE") };
        var r = Motor().Evaluar(Curso(actividades, E(1, 8m, (1, 4.5m), (2, 9m), (3, 9m)), E(2, 8m, (1, 4m), (2, 3m), (3, 10m))));
        Assert.Contains(De(r, 1).Hallazgos, h => h.Codigo == MotorReglas.NotaCritica && h.Nivel == Nivel.Medio);
        Assert.Contains(De(r, 2).Hallazgos, h => h.Codigo == MotorReglas.NotaCritica && h.Nivel == Nivel.Alto);
    }

    [Fact]
    public void Rule_DescendingTrend_NeedsFourGradesAndAPointFiveDrop()
    {
        var actividades = Enumerable.Range(1, 4).Select(i => A(i, "Tarea", $"2026-10-{i:00}")).ToArray();
        var cae = E(1, 8m, (1, 10m), (2, 9m), (3, 7m), (4, 6.5m));
        var estable = E(2, 8m, (1, 8m), (2, 8m), (3, 8m), (4, 7.5m));
        var pocas = E(3, 8m, (1, 10m), (2, 5.5m), (3, 5m));
        var r = Motor().Evaluar(Curso(actividades, cae, estable, pocas));
        Assert.Contains(De(r, 1).Hallazgos, h => h.Codigo == MotorReglas.TendenciaDescendente);
        Assert.DoesNotContain(De(r, 2).Hallazgos, h => h.Codigo == MotorReglas.TendenciaDescendente);
        Assert.DoesNotContain(De(r, 3).Hallazgos, h => h.Codigo == MotorReglas.TendenciaDescendente);
    }

    [Fact]
    public void Rule_WeakType_NeedsTwoGradesWellBelowTheMinimum()
    {
        var actividades = new[] { A(1, "Tarea"), A(2, "Tarea"), A(3, "Examen") };
        var r = Motor().Evaluar(Curso(actividades, E(1, 7m, (1, 5.5m), (2, 6m), (3, 9m)), E(2, 7m, (1, 5.5m), (3, 9m))));
        Assert.Contains(De(r, 1).Hallazgos, h => h.Codigo == MotorReglas.TipoDebil && h.Nivel == Nivel.Bajo);
        Assert.DoesNotContain(De(r, 2).Hallazgos, h => h.Codigo == MotorReglas.TipoDebil);
    }

    [Fact]
    public void Rule_Projection_SolvesTheAverageNeededInWhatIsLeftAndDetectsImpossibleCases()
    {
        // Una actividad de cada tipo. Tarea 10 y APE 10 suman 6 de 10; el examen (40 %) debe aportar 1 punto más: sacar 2,5.
        var actividades = new[] { A(1, "Tarea"), A(2, "APE"), A(3, "Examen") };
        var holgado = E(1, 10m, (1, 10m), (2, 10m));
        // Con 3 y 3 lleva 1,8 puntos; aunque saque 10 en el examen llega a 5,8: ya no aprueba.
        var imposible = E(2, 3m, (1, 3m), (2, 3m));
        // Con 6 y 6 lleva 3,6; necesita 8,5 en el examen para llegar a 7.
        var exigente = E(3, 6m, (1, 6m), (2, 6m));
        var r = Motor().Evaluar(Curso(actividades, holgado, imposible, exigente));
        Assert.Equal(2.5m, De(r, 1).Proyeccion!.PromedioRequerido);
        Assert.DoesNotContain(De(r, 1).Hallazgos, h => h.Codigo == MotorReglas.RiesgoDeReprobar);
        Assert.Equal(5.8m, De(r, 2).Proyeccion!.MaximoPosible);
        Assert.Contains(De(r, 2).Hallazgos, h => h.Codigo == MotorReglas.RiesgoDeReprobar && h.Nivel == Nivel.Alto);
        Assert.Equal(8.5m, De(r, 3).Proyeccion!.PromedioRequerido);
        Assert.Contains(De(r, 3).Hallazgos, h => h.Codigo == MotorReglas.RiesgoDeReprobar && h.Nivel == Nivel.Medio);
    }

    [Fact]
    public void Rule_Projection_DoesNotApplyWhenNothingIsPendingOrNothingIsGraded()
    {
        var actividades = new[] { A(1, "Tarea"), A(2, "APE") };
        var r = Motor().Evaluar(Curso(actividades, E(1, 8m, (1, 8m), (2, 8m)), E(2, null)));
        Assert.Null(De(r, 1).Proyeccion);
        Assert.Null(De(r, 2).Proyeccion);
        Assert.Equal(Nivel.Ninguno, De(r, 2).Nivel);
    }

    [Fact]
    public void Rule_Course_FlagsHardActivitiesAndCoursesWithManyStudentsAtRisk()
    {
        var actividades = new[] { A(1, "Tarea"), A(2, "Examen") };
        var estudiantes = new[] { E(1, 5m, (1, 9m), (2, 4m)), E(2, 5m, (1, 8m), (2, 5m)), E(3, 6m, (1, 9m), (2, 6m)), E(4, 9m, (1, 9m), (2, 9m)) };
        var r = Motor().Evaluar(Curso(actividades, estudiantes));
        Assert.Contains(r.HallazgosCurso, h => h.Codigo == MotorReglas.ActividadDificil && h.Titulo.StartsWith("Examen 2"));
        Assert.DoesNotContain(r.HallazgosCurso, h => h.Codigo == MotorReglas.ActividadDificil && h.Titulo.StartsWith("Tarea 1"));
        Assert.Contains(r.HallazgosCurso, h => h.Codigo == MotorReglas.CursoEnRiesgo && h.Nivel == Nivel.Alto);
    }

    [Fact]
    public void Rules_AreDeterministicAndStudentLevelIsTheHighestFinding()
    {
        var actividades = new[] { A(1, "Tarea"), A(2, "APE"), A(3, "Examen") };
        var curso = Curso(actividades, E(1, 3m, (1, 3m), (2, 3m)), E(2, 6.5m, (1, 6.5m)));
        var a = Motor().Evaluar(curso);
        var b = Motor().Evaluar(curso);
        Assert.Equal(a.Estudiantes.Select(e => (e.EstudianteId, e.Nivel, e.Hallazgos.Count)), b.Estudiantes.Select(e => (e.EstudianteId, e.Nivel, e.Hallazgos.Count)));
        foreach (var e in a.Estudiantes)
            Assert.Equal(e.Hallazgos.Max(h => h.Nivel), e.Nivel);
    }

    [Fact]
    public void Rules_ThresholdsComeFromConfiguration()
    {
        var actividades = new[] { A(1, "Tarea"), A(2, "APE") };
        var curso = Curso(actividades, E(1, 6.5m, (1, 6.5m), (2, 6.5m)));
        var estricto = new MotorReglas(new OpcionesReglas { BrechaAlta = 0.2m }).Evaluar(curso);
        Assert.Equal(Nivel.Alto, De(estricto, 1).Nivel);
        Assert.Equal(Nivel.Medio, De(Motor().Evaluar(curso), 1).Nivel);
    }

    // ---------- HU-12: interpretación de la respuesta del modelo ----------

    const string Valida = """
        {"resumen":"Tiene dificultades en las tareas y su promedio está bajo el mínimo.","causasProbables":["Posibles vacíos en los temas iniciales."],
         "accionesDocente":["Reunirse con el estudiante esta semana."],"accionesEstudiante":["Repasar los ejercicios de la tarea 2.","Asistir a tutoría."],
         "mensajeEstudiante":"He revisado tu avance y quiero ayudarte a mejorar. Tu promedio todavía está por debajo del mínimo, pero estás a tiempo de recuperarlo si repasas lo básico."}
        """;

    [Fact]
    public void Interpreter_AcceptsAValidAnswerEvenWrappedInACodeFence()
    {
        var b = Interprete.Interpretar("```json\n" + Valida + "\n```", out var motivo);
        Assert.NotNull(b);
        Assert.Null(motivo);
        Assert.Equal(2, b!.AccionesEstudiante.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("hola, no soy json")]
    [InlineData("{\"resumen\":\"corto\"}")]
    [InlineData("{ no cierra")]
    public void Interpreter_RejectsEmptyMalformedOrIncompleteAnswers(string respuesta)
    {
        Assert.Null(Interprete.Interpretar(respuesta, out var motivo));
        Assert.NotNull(motivo);
    }

    [Fact]
    public void Interpreter_RejectsLinksAndEmailsAndLimitsLists()
    {
        Assert.Null(Interprete.Interpretar(Valida.Replace("tutoría.", "tutoría en https://sitio.example/ayuda"), out var motivo));
        Assert.Contains("enlaces", motivo);
        Assert.Null(Interprete.Interpretar(Valida.Replace("esta semana.", "y escribir a alguien@example.test"), out _));
        var muchas = Valida.Replace("[\"Repasar los ejercicios de la tarea 2.\",\"Asistir a tutoría.\"]", "[\"Accion uno larga\",\"Accion dos larga\",\"Accion tres larga\",\"Accion cuatro larga\",\"Accion cinco larga\",\"Accion seis larga\"]");
        Assert.Equal(Interprete.MaxLista, Interprete.Interpretar(muchas, out _)!.AccionesEstudiante.Count);
    }

    [Fact]
    public void Interpreter_RejectsAnswersThatMixOtherAlphabets()
    {
        Assert.Null(Interprete.Interpretar(Valida.Replace("tutoría.", "tutoría 学习计划."), out var motivo));
        Assert.Contains("otro idioma", motivo);
        Assert.Null(Interprete.Interpretar(Valida.Replace("tutoría.", "tutoría, спасибо."), out _));
    }

    [Fact]
    public void Rule_Trend_DoesNotFailWhenConfiguredWithTooFewGrades()
    {
        var actividades = Enumerable.Range(1, 2).Select(i => A(i, "Tarea", $"2026-10-{i:00}")).ToArray();
        var motor = new MotorReglas(new OpcionesReglas { MinNotasTendencia = 2 });
        var r = motor.Evaluar(Curso(actividades, E(1, 8m, (1, 10m), (2, 5m))));
        Assert.DoesNotContain(De(r, 1).Hallazgos, h => h.Codigo == MotorReglas.TendenciaDescendente);
    }

    [Fact]
    public void Csv_NeutralizesCellsThatExcelWouldReadAsFormulas()
    {
        var est = new EstudianteSeguimiento(1, 1, "=HYPERLINK(\"x\")", "Ana", "ana@example.test", 5m, "En riesgo", 0, "Medio", [], null);
        var analisis = new AnalisisResumen(1, "Completado", DateTime.UtcNow, DateTime.UtcNow, 1, 0, 0, 7m, 0, 1, 0, 0, "Física", "A", "2026");
        var reporte = new ReporteView(1, "Física", "A", "2026", "Docente", DateTime.UtcNow, analisis, new(0, 0, 0, 0, 0), [], [est]);
        var texto = System.Text.Encoding.UTF8.GetString(Consultas.Csv(reporte));
        Assert.Contains("\"'=HYPERLINK(\"\"x\"\")\"", texto);
        Assert.DoesNotContain("\"=HYPERLINK", texto);
    }

    // ---------- HU-12: plantillas y respaldo ----------

    static ContextoIA Contexto(params string[] codigos) => new("Física", 7m, 5.5m, "Alto",
        [new NotaIA("Tarea 1", "Tarea", 4m), new NotaIA("APE 1", "APE", 7m)], [new TipoIA("Tarea", 4m, 1, 2)],
        codigos.Select(c => new HallazgoIA(c, "Alto", c == MotorReglas.TipoDebil ? "Debilidad en tareas" : "Hallazgo", "Detalle del hallazgo.")).ToList(), new ProyeccionIA(2, 8.7m, 10m));

    [Fact]
    public void Templates_AlwaysProduceAUsableDraftForEveryFinding()
    {
        string[] todos = [MotorReglas.RendimientoBajo, MotorReglas.NotaCritica, MotorReglas.TendenciaDescendente, MotorReglas.TipoDebil, MotorReglas.RiesgoDeReprobar];
        foreach (var codigo in todos.Select(c => new[] { c }).Append(todos).Append([]))
        {
            var b = GeneradorPlantillas.Generar(Contexto(codigo));
            Assert.InRange(b.AccionesDocente.Count, 1, Interprete.MaxLista);
            Assert.InRange(b.AccionesEstudiante.Count, 1, Interprete.MaxLista);
            Assert.NotEmpty(b.Causas);
            Assert.True(b.MensajeEstudiante.Length >= 40);
            // La plantilla también debe pasar la misma validación que se le exige al modelo.
            var json = System.Text.Json.JsonSerializer.Serialize(new { resumen = b.Resumen, causasProbables = b.Causas, accionesDocente = b.AccionesDocente, accionesEstudiante = b.AccionesEstudiante, mensajeEstudiante = b.MensajeEstudiante });
            Assert.NotNull(Interprete.Interpretar(json, out var motivo));
            Assert.Null(motivo);
        }
    }

    [Fact]
    public void Context_SentToTheModelCarriesNoPersonalData()
    {
        var motor = Motor().Evaluar(Curso([A(1, "Tarea")], E(77, 4m, (1, 4m))));
        var json = ContextoIA.De("Física", 7m, De(motor, 77)).Serializar();
        Assert.DoesNotContain("estudianteId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("correo", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("nombres", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cedula", json, StringComparison.OrdinalIgnoreCase);
    }

    sealed class Falso(string clave, string origen, Func<int, string> respuesta, bool configurado = true) : IProveedorIA
    {
        public int Llamadas;
        public string Clave => clave;
        public string Origen => origen;
        public string Modelo => "falso";
        public bool Configurado => configurado;
        public Task<EstadoProveedor> Estado(CancellationToken ct) => Task.FromResult(new EstadoProveedor(clave, origen, Modelo, configurado, true, "ok"));
        public Task<string> Completar(string sistema, string usuario, CancellationToken ct) => Task.FromResult(respuesta(++Llamadas));
    }
    static AgenteAcademico Agente(params IProveedorIA[] proveedores) => new(new ProveedoresIA(proveedores), new OpcionesAgente(), NullLogger<AgenteAcademico>.Instance);
    static string Cae(int _) => throw new HttpRequestException("sin conexión");

    [Fact]
    public async Task Agent_UsesTheFirstProviderThatAnswersValidly()
    {
        var local = new Falso("local", OrigenRecomendacion.Local, _ => Valida);
        var nube = new Falso("nube", OrigenRecomendacion.Nube, _ => Valida);
        var r = await Agente(local, nube).Recomendar(Contexto(MotorReglas.RendimientoBajo), new SesionAgente(), default);
        Assert.Equal(OrigenRecomendacion.Local, r.Origen);
        Assert.Equal(0, nube.Llamadas);
    }

    [Fact]
    public async Task Agent_FallsBackToTheCloudAndThenToTemplates()
    {
        var local = new Falso("local", OrigenRecomendacion.Local, Cae);
        var nube = new Falso("nube", OrigenRecomendacion.Nube, _ => Valida);
        var r = await Agente(local, nube).Recomendar(Contexto(MotorReglas.RendimientoBajo), new SesionAgente(), default);
        Assert.Equal(OrigenRecomendacion.Nube, r.Origen);

        var mala = new Falso("nube", OrigenRecomendacion.Nube, _ => "esto no es JSON");
        var r2 = await Agente(new Falso("local", OrigenRecomendacion.Local, Cae), mala).Recomendar(Contexto(MotorReglas.RendimientoBajo), new SesionAgente(), default);
        Assert.Equal(OrigenRecomendacion.Plantilla, r2.Origen);
        Assert.Null(r2.Modelo);
        Assert.NotEmpty(r2.Borrador.AccionesDocente);
    }

    [Fact]
    public async Task Agent_SkipsAProviderThatKeepsFailingForTheRestOfTheSession()
    {
        var local = new Falso("local", OrigenRecomendacion.Local, Cae);
        var agente = Agente(local);
        var sesion = new SesionAgente();
        for (var i = 0; i < 5; i++)
            Assert.Equal(OrigenRecomendacion.Plantilla, (await agente.Recomendar(Contexto(MotorReglas.NotaCritica), sesion, default)).Origen);
        Assert.Equal(new OpcionesAgente().FallosParaOmitir, local.Llamadas);
        // Una sesión nueva vuelve a intentarlo: el proveedor pudo haberse recuperado.
        await agente.Recomendar(Contexto(MotorReglas.NotaCritica), new SesionAgente(), default);
        Assert.Equal(new OpcionesAgente().FallosParaOmitir + 1, local.Llamadas);
    }

    [Fact]
    public async Task Agent_IgnoresProvidersThatAreNotConfigured()
    {
        var sinClave = new Falso("nube", OrigenRecomendacion.Nube, _ => Valida, configurado: false);
        var r = await Agente(sinClave).Recomendar(Contexto(MotorReglas.NotaCritica), new SesionAgente(), default);
        Assert.Equal(0, sinClave.Llamadas);
        Assert.Equal(OrigenRecomendacion.Plantilla, r.Origen);
    }
}
