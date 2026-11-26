using System.Text.Json;
using System.Text.Json.Nodes;
using EduMonitor.Evaluaciones.Moodle;
using EduMonitor.Infrastructure;
using Xunit;

namespace EduMonitor.IntegrationTests;

public class MoodleContratoTests
{
    private const string Codigo = "FIS-PRUEBA";
    private readonly JsonArray items = [];
    private readonly JsonArray modulos = [];
    private readonly JsonArray alumnos = [new JsonObject {
        ["id"] = 4, ["idnumber"] = "EST-001", ["firstname"] = "Alma", ["lastname"] = "Ejemplo",
        ["email"] = "alumna@example.invalid", ["roles"] = new JsonArray(new JsonObject { ["shortname"] = "student" })
    }];

    public MoodleContratoTests()
    {
        string[] tipos = ["T1", "APE", "EX"];
        for (var i = 0; i < tipos.Length; i++)
        {
            int cm = 20 + i, categoria = 50 + i;
            string modulo = i == 2 ? "quiz" : "assign";
            modulos.Add(new JsonObject { ["id"] = cm, ["instance"] = cm, ["name"] = "Actividad " + tipos[i], ["modname"] = modulo });
            items.Add(new JsonObject {
                ["itemtype"] = "mod", ["cmid"] = cm, ["itemmodule"] = modulo, ["iteminstance"] = cm,
                ["itemnumber"] = 0, ["categoryid"] = categoria, ["idnumber"] = Codigo + "-U1-" + tipos[i],
                ["grademin"] = 0, ["grademax"] = 10, ["gradeishidden"] = false, ["gradeneedsupdate"] = false, ["graderaw"] = 8
            });
            items.Add(new JsonObject { ["itemtype"] = "category", ["iteminstance"] = categoria,
                ["weightraw"] = i == 2 ? .4m : .3m });
        }
        items.Add(new JsonObject { ["itemtype"] = "course", ["graderaw"] = 8 });
    }

    private AulaMoodle Leer(string resumen = "<h4>Temática 1: Movimiento</h4><p>Posición y velocidad.</p>", JsonArray? otrasFilas = null)
    {
        var secciones = new[] { new { id = 10, section = 1, name = "Unidad de prueba", summary = resumen, modules = modulos } };
        var filas = new JsonArray { new JsonObject { ["userid"] = 4, ["courseid"] = 2, ["gradeitems"] = items.DeepClone() } };
        foreach (var f in otrasFilas ?? [])
            filas.Add(f!.DeepClone());
        var informe = new JsonObject { ["usergrades"] = filas };
        return AulaMoodle.Leer(2, Codigo, JsonSerializer.SerializeToElement(secciones),
            JsonSerializer.SerializeToElement(alumnos), JsonSerializer.SerializeToElement(informe));
    }

    [Fact]
    public void DistingueCeroDePendienteAunqueMoodleCuenteAmbosComoCeroEnElTotal()
    {
        items[0]!["graderaw"] = 0;
        items[^1]!["graderaw"] = 5.6m;
        Assert.Equal(0m, Leer().Notas[(4, 20)]);
        items[0]!["graderaw"] = null;
        Assert.Null(Leer().Notas[(4, 20)]);
    }

    [Fact]
    public void UnaCeldaAusenteQuedaComoNoDisponibleYSeAvisaSinTumbarElCurso()
    {
        // Un segundo estudiante cuyo libro no trae la actividad 20: su nota queda «no disponible», nunca 0
        var segundo = alumnos[0]!.DeepClone();
        segundo["id"] = 5;
        segundo["idnumber"] = "EST-002";
        alumnos.Add(segundo);
        var incompleto = new JsonArray(new JsonObject { ["userid"] = 5, ["courseid"] = 2, ["gradeitems"] = new JsonArray(items.Where(i => (int?)i!["cmid"] != 20).Select(i => i!.DeepClone()).ToArray()) });
        var aula = Leer(otrasFilas: incompleto);
        Assert.Null(aula.Notas[(5, 20)]);
        Assert.Equal(8m, aula.Notas[(4, 20)]);
        Assert.Contains(aula.Avisos!, a => a.Contains("EST-002") && a.Contains("no disponible"));
    }

    [Fact]
    public void RechazaDosPersonasConElMismoIdentificador()
    {
        var duplicado = alumnos[0]!.DeepClone();
        duplicado["id"] = 5;
        alumnos.Add(duplicado);
        Assert.Throws<ApiException>(() => Leer());
    }

    [Fact]
    public void UnTotalDeMoodleQueDifiereDelCalculoLocalSoloAvisa()
    {
        items[^1]!["graderaw"] = 7;
        var aula = Leer();
        Assert.Contains(aula.Avisos!, a => a.Contains("difiere") && a.Contains("EST-001"));
        Assert.Equal(8m, aula.Notas[(4, 20)]);   // las notas siguen siendo las de Moodle
    }

    [Fact]
    public void NormalizaCualquierEscalaDeMoodleAUnaEscalaDeDiez()
    {
        items[0]!["grademax"] = 100;
        items[0]!["graderaw"] = 85;
        items[2]!["grademax"] = 20;
        items[2]!["graderaw"] = 15;
        var aula = Leer();
        Assert.Equal(8.5m, aula.Notas[(4, 20)]);
        Assert.Equal(7.5m, aula.Notas[(4, 21)]);
    }

    [Fact]
    public void UnaNotaQueMoodleTodaviaDebeRecalcularQuedaNoDisponible()
    {
        items[0]!["gradeneedsupdate"] = true;
        var aula = Leer();
        Assert.Null(aula.Notas[(4, 20)]);
        Assert.Contains(aula.Avisos!, a => a.Contains("no está lista"));
    }

    [Fact]
    public void LeeLasFechasDeEntregaCuandoMoodleLasTraeYLasDejaVaciasCuandoNo()
    {
        // 1764979200 = 6 dic 2025 00:00 UTC = 5 dic 2025 19:00 en Ecuador
        modulos[0]!["dates"] = new JsonArray(new JsonObject { ["label"] = "Due:", ["timestamp"] = 1764979200, ["dataid"] = "duedate" });
        modulos[2]!["dates"] = new JsonArray(new JsonObject { ["label"] = "Closes:", ["timestamp"] = 1764979200, ["dataid"] = "timeclose" });
        var aula = Leer();
        Assert.Equal(new DateOnly(2025, 12, 5), aula.Actividades.Single(a => a.Cm == 20).Fecha);
        Assert.Equal(new DateOnly(2025, 12, 5), aula.Actividades.Single(a => a.Cm == 22).Fecha);
        Assert.Null(aula.Actividades.Single(a => a.Cm == 21).Fecha);
    }

    [Fact]
    public void UnCursoSinElFormatoDelAulaDeEjemploTambienSeImporta()
    {
        // Sin códigos de actividad, sin «Temática N» en la sección y con la escala que tenga el docente
        foreach (var item in items.Where(i => (string?)i!["itemtype"] == "mod"))
            item!["idnumber"] = "";
        modulos[0]!["name"] = "Deber de funciones";
        modulos[1]!["name"] = "Proyecto práctico de laboratorio";
        modulos[2]!["name"] = "Examen final";
        alumnos[0]!["idnumber"] = "";
        alumnos[0]!["username"] = "alma.ejemplo";
        var aula = Leer(resumen: "<p>Esta unidad trata de funciones y gráficas.</p>");

        Assert.Equal("alma.ejemplo", aula.Alumnos.Single().Codigo);                      // sin código institucional se usa el usuario
        Assert.Equal(["Tarea", "APE", "Examen"], aula.Actividades.OrderBy(a => a.Cm).Select(a => a.Tipo));
        Assert.All(aula.Actividades, a => Assert.StartsWith("cm", a.Codigo));
        var unidad = Assert.Single(aula.Secciones);
        var tema = Assert.Single(unidad.Temas);                                          // la sección entera es un solo tema
        Assert.Equal("Unidad de prueba", tema.Nombre);
        Assert.Contains("funciones y gráficas", tema.Contenido);
    }

    [Fact]
    public void LaPonderacionSaleDelLibroDeMoodleSiTieneUnaCategoriaPorTipo()
    {
        var pesos = Leer().Pesos;
        Assert.Equal(new PesosMoodle(30m, 30m, 40m), pesos);
        // Con pesos en otra escala (30/30/40 puntos) el reparto es el mismo
        foreach (var c in items.Where(i => (string?)i!["itemtype"] == "category"))
            c!["weightraw"] = (decimal)c["weightraw"]! * 100m;
        Assert.Equal(new PesosMoodle(30m, 30m, 40m), Leer().Pesos);
        // Si dos tipos comparten categoría no se puede deducir: se conserva la de EduMonitor y se avisa
        items.Where(i => (string?)i!["itemtype"] == "mod").Skip(1).First()!["categoryid"] = 50;
        var sin = Leer();
        Assert.Null(sin.Pesos);
        Assert.Contains(sin.Avisos!, a => a.Contains("ponderación de EduMonitor"));
    }

    [Fact]
    public void UnCursoSinActividadesCalificadasSeRechaza()
    {
        items.Clear();
        items.Add(new JsonObject { ["itemtype"] = "course", ["graderaw"] = 8 });
        Assert.Throws<ApiException>(() => Leer());
    }
}
