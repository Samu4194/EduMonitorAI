using System.ComponentModel.DataAnnotations;
namespace EduMonitor.Usuarios.DTOs;

public record LoginRequest([Required, EmailAddress, StringLength(254)] string Email, [Required, StringLength(128)] string Password);
public record TeacherRequest([Required, StringLength(120, MinimumLength = 2)] string Name, [Required, EmailAddress, StringLength(254)] string Email, [StringLength(128, MinimumLength = 10)] string? Password);
public record UserView(int Id, string Name, string Email, string Role);
