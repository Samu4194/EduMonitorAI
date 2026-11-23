using System.ComponentModel.DataAnnotations;
using EduMonitor.Infrastructure;
namespace EduMonitor.Estudiantes.DTOs;

// Registro individual: equivale a una fila del CSV. Sin materia, el estudiante queda en todas las materias de ese nivel y paralelo.
public record StudentRequest(
    [Required, RegularExpression(@"^\d{10}$", ErrorMessage = "La cédula debe tener 10 dígitos."), CedulaEcuatoriana] string Cedula,
    [Required, StringLength(100, MinimumLength = 2)] string Apellidos,
    [Required, StringLength(100, MinimumLength = 2)] string Nombres,
    [Required, EmailAddress, StringLength(254)] string Correo,
    [Required, RegularExpression(@"^[A-Za-z0-9]{2,10}$", ErrorMessage = "El código de carrera no es válido.")] string Carrera,
    [Range(1, 9)] int Nivel,
    [Required, RegularExpression(@"^[A-Za-z]{1,2}$", ErrorMessage = "El paralelo debe tener una o dos letras.")] string Paralelo,
    [StringLength(120)] string? Materia = null);
// Edición de los datos personales. Las materias del estudiante se gestionan con la carga CSV y con el retiro por materia.
public record StudentEdit(
    [Required, RegularExpression(@"^\d{10}$", ErrorMessage = "La cédula debe tener 10 dígitos."), CedulaEcuatoriana] string Cedula,
    [Required, StringLength(100, MinimumLength = 2)] string Apellidos,
    [Required, StringLength(100, MinimumLength = 2)] string Nombres,
    [Required, EmailAddress, StringLength(254)] string Correo);

// Creados y Actualizados cuentan estudiantes (una persona puede venir en varias filas). Reactivados son estudiantes dados de baja que volvieron a
// venir en el archivo (también cuentan como actualizados). Inscripciones son los cursos agregados o reincorporados.
public record PrecargaResultado(int Creados, int Actualizados, int Total, int Reactivados, int Inscripciones);
// La lista que ve el docente no incluye la cédula: solo lo necesario para identificar al estudiante.
public record ListaEstudiante(int Id, string Apellidos, string Nombres, string Correo, int? MoodleUsuarioId, string? MoodleCodigo);
public record InscripcionView(int Id, int CursoId, int Nivel, string Paralelo, string MateriaCodigo, string MateriaNombre, int Creditos, bool Activo);
// Creditos = suma de las materias activas del estudiante (todas, aunque el listado venga filtrado), frente al máximo por período.
public record StudentView(int Id, string Cedula, string Apellidos, string Nombres, string Correo, string CarreraCodigo, bool Activo, DateTime? BajaEn, int Creditos, int MaxCreditos, List<InscripcionView> Inscripciones);

// Respuestas del servicio de Materias que Estudiantes necesita para validar carreras, paralelos, materias y cursos.
public record CarreraReference(string Codigo, string Nombre, int Niveles);
public record CursoReference(int Id, string Carrera, int Nivel, string Paralelo, string MateriaCodigo, string MateriaNombre, int Creditos, int MallaVersion = 1);
