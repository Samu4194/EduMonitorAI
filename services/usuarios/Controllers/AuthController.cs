using EduMonitor.Infrastructure;
using EduMonitor.Usuarios.DTOs;
using EduMonitor.Usuarios.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
namespace EduMonitor.Usuarios.Controllers;

[ApiController, Route("api/auth")]
public class AuthController(UsuariosService service) : ControllerBase
{
    [HttpPost("login"), EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginRequest input, CancellationToken ct) => Ok(await service.Login(input, ct));
    [Authorize, HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct) => Ok(await service.Me(User.UserId(), ct));
    [Authorize, HttpGet("session")]
    public async Task<IActionResult> Session(CancellationToken ct) => Ok(await service.Me(User.UserId(), ct));
    [Authorize, HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await service.Logout(User.UserId(), ct);
        return NoContent();
    }
}
