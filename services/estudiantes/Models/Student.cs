namespace EduMonitor.Estudiantes.Models;

public class Student
{
    public int Id
    {
        get; set;
    }
    public string StudentCode { get; set; } = "";
    public string FirstNames { get; set; } = "";
    public string LastNames { get; set; } = "";
}
public class Enrollment
{
    public int StudentId
    {
        get; set;
    }
    public Student Student { get; set; } = null!;
    // Identificador externo: no existe relación EF ni FK con la base de Materias.
    public int MateriaId
    {
        get; set;
    }
    public DateTime EnrolledAt { get; set; } = DateTime.UtcNow;
}
