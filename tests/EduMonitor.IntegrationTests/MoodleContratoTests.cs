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

    private AulaMoodle Leer()
    {
        var secciones = new[] { new { id = 10, section = 1, name = "Unidad de prueba",
            summary = "<h4>Temática 1: Movimiento</h4><p>Posición y velocidad.</p>", modules = modulos } };
        var informe = new { usergrades = new[] { new { userid = 4, courseid = 2, gradeitems = items } } };
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
    public void RechazaCeldaAusenteAunqueExistaLaActividad()
    {
        items.RemoveAt(0);
        Assert.Throws<ApiException>(() => Leer());
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
    public void RechazaUnTotalDefinitivoQueDifiereDelCalculoLocal()
    {
        items[^1]!["graderaw"] = 7;
        Assert.Throws<ApiException>(() => Leer());
    }
}
