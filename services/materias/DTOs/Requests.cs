using System.ComponentModel.DataAnnotations;
namespace EduMonitor.Materias.DTOs;

public record MateriaRequest([Required, RegularExpression(@"^[A-Za-z0-9-]{2,30}$")] string Code, [Required, StringLength(120, MinimumLength = 2)] string Name, [Required, StringLength(80, MinimumLength = 2)] string AcademicPeriod, [Range(1, int.MaxValue)] int? TeacherId);
public record AssignmentRequest([Range(1, int.MaxValue)] int TeacherId);
public record TeacherReference(int Id);
