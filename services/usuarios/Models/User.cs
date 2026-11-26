namespace EduMonitor.Usuarios.Models;

public class User
{
    public int Id
    {
        get; set;
    }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = "Teacher";
    // Un docente dado de baja no puede iniciar sesión ni recibir cursos; sus datos y su historial se conservan.
    public bool Activo { get; set; } = true;
    public int TokenVersion
    {
        get; set;
    }
    public TeacherProfile? Profile { get; set; }
}

// Datos personales del docente, separados de las credenciales de acceso.
public class TeacherProfile
{
    public int UserId
    {
        get; set;
    }
    public User User { get; set; } = null!;
    public string Cedula { get; set; } = "";
    public string Nombres { get; set; } = "";
    public string Apellidos { get; set; } = "";
    public string? Telefono { get; set; }
    public string? TituloAcademico { get; set; }
    public List<TeacherCarrera> Carreras { get; set; } = [];
}

// Código de carrera del servicio de Materias. Es una referencia: no hay clave foránea entre bases.
public class TeacherCarrera
{
    public int UserId
    {
        get; set;
    }
    public TeacherProfile Profile { get; set; } = null!;
    public string CarreraCodigo { get; set; } = "";
}
