namespace EduMonitor.Evaluaciones.Models;

// Todo cuelga de un curso del servicio de Materias (una materia en un paralelo). Se guarda su id, sin FK entre bases.
public class Unidad
{
    public int Id { get; set; }
    public int CursoId { get; set; }
    public int Orden { get; set; }
    public string Nombre { get; set; } = "";
    public string? Descripcion { get; set; }
    public List<Tema> Temas { get; set; } = [];
}

public class Tema
{
    public int Id { get; set; }
    public int UnidadId { get; set; }
    public Unidad Unidad { get; set; } = null!;
    public int Orden { get; set; }
    public string Nombre { get; set; } = "";
    public string? Descripcion { get; set; }
    public List<Contenido> Contenidos { get; set; } = [];
}

// Material de apoyo de un tema (guías, ejemplos, diapositivas). La base guarda sus datos; el contenido va a disco (AlmacenArchivos).
public class Archivo
{
    public int Id { get; set; }
    public int TemaId { get; set; }
    public Tema Tema { get; set; } = null!;
    public string Nombre { get; set; } = "";
    public string Tipo { get; set; } = "";
    public int Tamano { get; set; }
    public DateTime SubidoEn { get; set; }
}

// Contenido mínimo: lo que el estudiante debe haber aprendido del tema.
public class Contenido
{
    public int Id { get; set; }
    public int TemaId { get; set; }
    public int Orden { get; set; }
    public string Descripcion { get; set; } = "";
}

public static class TipoActividad
{
    public const string Tarea = "Tarea", Ape = "APE", Examen = "Examen";
    public static readonly string[] Todos = [Tarea, Ape, Examen];
}

public class Actividad
{
    public int Id { get; set; }
    public int CursoId { get; set; }
    public string Nombre { get; set; } = "";
    public string Tipo { get; set; } = TipoActividad.Tarea;
    public string? Descripcion { get; set; }
    public DateOnly? FechaEntrega { get; set; }
    public List<ActividadTema> Temas { get; set; } = [];
}

public class ActividadTema
{
    public int ActividadId { get; set; }
    public Actividad Actividad { get; set; } = null!;
    public int TemaId { get; set; }
    public Tema Tema { get; set; } = null!;
}

// Una nota existe solo si el docente la registró: la ausencia de fila significa "pendiente", nunca cero.
public class Calificacion
{
    public int Id { get; set; }
    public int ActividadId { get; set; }
    public Actividad Actividad { get; set; } = null!;
    // Id del estudiante en el servicio de Estudiantes.
    public int EstudianteId { get; set; }
    public decimal Nota { get; set; }
    public DateTime Actualizada { get; set; }
}

// Porcentajes por tipo de actividad y nota mínima de aprobación de un curso. Sin fila se usan los valores por defecto.
public class Ponderacion
{
    public int CursoId { get; set; }
    public decimal Tareas { get; set; }
    public decimal Ape { get; set; }
    public decimal Examen { get; set; }
    public decimal NotaMinima { get; set; }
}
