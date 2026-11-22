namespace EduMonitor.Estudiantes.Models;

// Estudiante cargado por precarga. No tiene cuenta de acceso: el estudiante no inicia sesión.
// En qué cursos está lo dicen sus inscripciones: puede cursar materias de distintos niveles y paralelos (arrastres, cupos).
public class Student
{
    public int Id
    {
        get; set;
    }
    public string Cedula { get; set; } = "";
    public string Apellidos { get; set; } = "";
    public string Nombres { get; set; } = "";
    public string Correo { get; set; } = "";
    // Código de carrera del servicio de Materias. Es una referencia: no hay clave foránea entre bases.
    public string CarreraCodigo { get; set; } = "";
    // Baja lógica: un estudiante dado de baja no aparece en listas, notas ni rendimiento, pero conserva sus datos y calificaciones.
    public bool Activo { get; set; } = true;
    public DateTime? BajaEn
    {
        get; set;
    }
    public List<Inscripcion> Inscripciones { get; set; } = [];
}

// Un estudiante en un curso (materia en un paralelo). No es una matrícula manual: se crea al cargar el CSV.
// Nivel, paralelo y materia se copian del curso al cargar para poder filtrar sin consultar a Materias.
public class Inscripcion
{
    public int Id
    {
        get; set;
    }
    public int StudentId
    {
        get; set;
    }
    public Student Student { get; set; } = null!;
    // Referencia al curso del servicio de Materias, sin clave foránea entre bases.
    public int CursoId
    {
        get; set;
    }
    public int Nivel
    {
        get; set;
    }
    public string Paralelo { get; set; } = "";
    public string MateriaCodigo { get; set; } = "";
    public string MateriaNombre { get; set; } = "";
    // Versión de la malla de la carrera a la que pertenece la materia. Un estudiante sigue con la malla en la que empezó.
    public int MallaVersion { get; set; } = 1;
    // Créditos de la materia, copiados del catálogo: el estudiante puede sumar hasta EstudiantesService.MaxCreditos por período.
    public int Creditos
    {
        get; set;
    }
    // Retirar de una materia no borra la inscripción: deja de aparecer en ese curso, pero se conservan sus notas.
    public bool Activo { get; set; } = true;
    public DateTime? BajaEn
    {
        get; set;
    }
}
