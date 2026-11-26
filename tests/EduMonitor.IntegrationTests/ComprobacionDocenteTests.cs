using EduMonitor.Evaluaciones.Moodle;
using Xunit;

namespace EduMonitor.IntegrationTests;

// En Moodle califica el profesor matriculado; en EduMonitor ve el docente asignado. Si no coinciden, se avisa.
public class ComprobacionDocenteTests
{
    static AlumnoMoodle Profesor(string correo, string nombres = "Diego", string apellidos = "Torres") => new(1, "P1", nombres, apellidos, correo);

    [Fact]
    public void Evaluar_AcceptsTheAssignedTeacherIgnoringCaseAndSpaces()
    {
        var r = ComprobacionDocente.Evaluar(" Docente@Example.TEST ", [Profesor("docente@example.test")]);
        Assert.True(r.Coincide);
        Assert.Empty(r.Avisos);
    }

    [Fact]
    public void Evaluar_WarnsWhenAnotherPersonGradesInMoodle()
    {
        var r = ComprobacionDocente.Evaluar("asignado@example.test", [Profesor("otro@example.test", "Otra", "Persona")]);
        Assert.False(r.Coincide);
        var aviso = Assert.Single(r.Avisos);
        Assert.Contains("asignado@example.test", aviso);
        Assert.Contains("otro@example.test", aviso);
    }

    [Fact]
    public void Evaluar_WarnsWhenThereIsNoTeacherOnEitherSide()
    {
        Assert.Contains("no tiene docente asignado", Assert.Single(ComprobacionDocente.Evaluar(null, [Profesor("a@example.test")]).Avisos));
        Assert.Contains("ningún profesor matriculado", Assert.Single(ComprobacionDocente.Evaluar("a@example.test", []).Avisos));
        Assert.Contains("ningún profesor matriculado", Assert.Single(ComprobacionDocente.Evaluar("a@example.test", null).Avisos));
    }

    [Fact]
    public void Evaluar_AcceptsAnyOfSeveralMoodleTeachers()
    {
        Assert.True(ComprobacionDocente.Evaluar("apoyo@example.test", [Profesor("titular@example.test"), Profesor("apoyo@example.test")]).Coincide);
    }
}
