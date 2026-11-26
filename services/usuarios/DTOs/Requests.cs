using System.ComponentModel.DataAnnotations;
using EduMonitor.Infrastructure;
namespace EduMonitor.Usuarios.DTOs;

public record LoginRequest([Required, EmailAddress, StringLength(254)] string Email, [Required, StringLength(128)] string Password);
public record TeacherRequest(
    [Required, RegularExpression(@"^\d{10}$", ErrorMessage = "La cédula debe tener 10 dígitos."), CedulaEcuatoriana] string Cedula,
    [Required, StringLength(100, MinimumLength = 2)] string Nombres,
    [Required, StringLength(100, MinimumLength = 2)] string Apellidos,
    [Required, EmailAddress, StringLength(254)] string Email,
    [RegularExpression(@"^[0-9+\- ]{7,20}$", ErrorMessage = "El teléfono solo admite números, espacios, + y -.")] string? Telefono,
    [StringLength(150)] string? TituloAcademico,
    [Required, MinLength(1, ErrorMessage = "Selecciona al menos una carrera.")] List<string> Carreras,
    [StringLength(128, MinimumLength = 10)] string? Password);
public record UserView(int Id, string Name, string Email, string Role, string[] Carreras);
public record TeacherView(int Id, string Name, string Email, string Cedula, string Nombres, string Apellidos, string? Telefono, string? TituloAcademico, string[] Carreras, bool Activo = true);
