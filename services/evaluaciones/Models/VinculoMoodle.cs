namespace EduMonitor.Evaluaciones.Models;

public class VinculoMoodle
{
    public int CursoId { get; set; }
    public int MoodleId { get; set; }
    public string Codigo { get; set; } = "";
}

public class CorrespondenciaMoodle
{
    public int CursoId { get; set; }
    public string Clave { get; set; } = "";
    public int LocalId { get; set; }
}
