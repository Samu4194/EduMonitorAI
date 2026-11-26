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

// Ajustes del módulo académico guardados en la base para poder cambiarlos sin reiniciar (p. ej. la fuente de los datos académicos).
public class Ajuste
{
    public string Clave { get; set; } = "";
    public string Valor { get; set; } = "";
}
