using System.ComponentModel.DataAnnotations;
namespace EduMonitor.Estudiantes.DTOs;

public record StudentRequest([Required, RegularExpression(@"^[A-Za-z0-9-]{2,30}$")] string StudentCode, [Required, StringLength(100, MinimumLength = 2)] string FirstNames, [Required, StringLength(100, MinimumLength = 2)] string LastNames);
public record EnrollmentRequest([Range(1, int.MaxValue)] int MateriaId, [Range(1, int.MaxValue)] int StudentId);
public record MateriaReference(int Id);
public record EnrolledStudent(int Id, string StudentCode, string FirstNames, string LastNames, DateTime EnrolledAt);
