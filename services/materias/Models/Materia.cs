namespace EduMonitor.Materias.Models;

public class Materia
{
    public int Id
    {
        get; set;
    }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string AcademicPeriod { get; set; } = "";
    // Referencia externa, sin FK a otra base. Puede asignarse después del registro.
    public int? TeacherId
    {
        get; set;
    }
}
