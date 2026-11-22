namespace EduMonitor.Materias.Models;

public class Carrera
{
    public int Id
    {
        get; set;
    }
    public string Codigo { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string PrefijoUta { get; set; } = "";
    public string Modalidad { get; set; } = "";
    public int Niveles
    {
        get; set;
    }
    public int HorasTotales
    {
        get; set;
    }
    public int CreditosTotales
    {
        get; set;
    }
    // Baja lógica: una carrera dada de baja no admite paralelos, mallas, docentes ni estudiantes nuevos, pero conserva su historial y su código.
    public bool Activa { get; set; } = true;
    public DateTime? BajaEn
    {
        get; set;
    }
    public List<Materia> Materias { get; set; } = [];
    public List<Malla> Mallas { get; set; } = [];
}

// Versión de la malla curricular de una carrera. La vigente (la de mayor versión) es la que usan los estudiantes nuevos;
// los estudiantes ya registrados siguen con la versión en la que empezaron. Cada paralelo se dicta con una sola malla.
public class Malla
{
    public int Id
    {
        get; set;
    }
    public int CarreraId
    {
        get; set;
    }
    public Carrera Carrera { get; set; } = null!;
    public int Version
    {
        get; set;
    }
    public DateTime CreadaEn { get; set; } = DateTime.UtcNow;
    public List<Materia> Materias { get; set; } = [];
}

// Materia de la malla curricular de una carrera. Los datos salen de las mallas oficiales de la facultad.
public class Materia
{
    public int Id
    {
        get; set;
    }
    public int CarreraId
    {
        get; set;
    }
    public Carrera Carrera { get; set; } = null!;
    public int MallaId
    {
        get; set;
    }
    public Malla Malla { get; set; } = null!;
    public string Codigo { get; set; } = "";
    public string CodigoUta { get; set; } = "";
    public string Nombre { get; set; } = "";
    public int Nivel
    {
        get; set;
    }
    public int OrdenNivel
    {
        get; set;
    }
    public string Unidad { get; set; } = "";
    public string Tipo { get; set; } = "";
    public int? HorasContactoDocente
    {
        get; set;
    }
    public int? HorasPracticoExperimental
    {
        get; set;
    }
    public int? HorasAutonomas
    {
        get; set;
    }
    public int HorasTotales
    {
        get; set;
    }
    public int Creditos
    {
        get; set;
    }
    // Prácticas, servicio comunitario y titulación solo traen horas en la malla: su crédito se calcula (48 h = 1 crédito).
    public bool CreditosDerivado
    {
        get; set;
    }
}

public class Periodo
{
    public const string Preparacion = "Preparación";
    public const string EnCurso = "En curso";
    public const string Finalizado = "Finalizado";
    public int Id
    {
        get; set;
    }
    public string Nombre { get; set; } = "";
    public bool Activo
    {
        get; set;
    }
    // Mientras el período está en preparación se puede cargar o actualizar la malla; una vez en curso queda bloqueada.
    public string Estado { get; set; } = Preparacion;
}

// Un paralelo es carrera + nivel + letra dentro de un período, por ejemplo Software, cuarto nivel, "B".
public class Paralelo
{
    public int Id
    {
        get; set;
    }
    public int PeriodoId
    {
        get; set;
    }
    public Periodo Periodo { get; set; } = null!;
    public int CarreraId
    {
        get; set;
    }
    public Carrera Carrera { get; set; } = null!;
    // Malla con la que se dicta este paralelo: sus cursos son materias de esa versión.
    public int MallaId
    {
        get; set;
    }
    public Malla Malla { get; set; } = null!;
    public int Nivel
    {
        get; set;
    }
    public string Letra { get; set; } = "";
}

// Un curso es una materia dictada en un paralelo. Es la base del aula virtual de los siguientes sprints.
public class Curso
{
    public int Id
    {
        get; set;
    }
    public int MateriaId
    {
        get; set;
    }
    public Materia Materia { get; set; } = null!;
    public int ParaleloId
    {
        get; set;
    }
    public Paralelo Paralelo { get; set; } = null!;
    // Referencia externa al servicio de Usuarios, sin FK a otra base. Se asigna después de crear el curso.
    public int? TeacherId
    {
        get; set;
    }
}
