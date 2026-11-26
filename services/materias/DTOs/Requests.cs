using System.ComponentModel.DataAnnotations;
namespace EduMonitor.Materias.DTOs;

public record PeriodoRequest([Required, StringLength(60, MinimumLength = 3)] string Nombre);
public record ParaleloRequest([Required, RegularExpression(@"^[A-Za-z0-9]{2,10}$", ErrorMessage = "El código de carrera no es válido.")] string Carrera, [Range(1, 9)] int Nivel, [Required, RegularExpression(@"^[A-Za-z]{1,2}$", ErrorMessage = "El paralelo debe tener una o dos letras.")] string Letra, [Range(1, 1000)] int? Malla = null);
// teacherId nulo = dejar el curso sin docente.
public record AssignmentRequest([Range(1, int.MaxValue)] int? TeacherId);
public record TeacherReference(int Id, string[] Carreras, bool Activo = true);

// El código de la carrera no se escribe: lo asigna el sistema, es único y no se reutiliza. El prefijo UTA también lo asigna el sistema (único): los códigos UTA de la malla empiezan con él. Las horas y los créditos totales salen de la malla.
public record CarreraRequest([Required, StringLength(120, MinimumLength = 3)] string Nombre, [Required, StringLength(30, MinimumLength = 3)] string Modalidad, [Range(1, 12)] int Niveles);
public record CarreraView(int Id, string Codigo, string Nombre, string Modalidad, int Niveles, int HorasTotales, int CreditosTotales, int Materias, string PrefijoUta, int MallaVersion, int Versiones, bool Activa, DateTime? BajaEn);
public record CodigoSugerido(string Codigo, string PrefijoUta);
public record MallaResultado(int Version, bool NuevaVersion, int Materias, int HorasTotales, int CreditosTotales);
public record MateriaView(int Id, string Codigo, string CodigoUta, string Nombre, int Nivel, string Unidad, string Tipo, int? HorasContactoDocente, int? HorasPracticoExperimental, int? HorasAutonomas, int HorasTotales, int Creditos, bool CreditosDerivado, int MallaVersion);
public record PeriodoView(int Id, string Nombre, bool Activo, string Estado);
public record ParaleloView(int Id, int PeriodoId, string Carrera, int Nivel, string Letra, int MallaVersion);
public record CursoView(int Id, int MateriaId, string MateriaCodigo, string MateriaNombre, int Creditos, int HorasTotales, string Carrera, string CarreraNombre, int Nivel, string Paralelo, int PeriodoId, string Periodo, int? TeacherId, int MallaVersion, string PeriodoEstado, string CodigoMoodle = "");
