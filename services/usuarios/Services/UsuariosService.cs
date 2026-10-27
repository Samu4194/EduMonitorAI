using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using EduMonitor.Infrastructure;
using EduMonitor.Usuarios.Data;
using EduMonitor.Usuarios.DTOs;
using EduMonitor.Usuarios.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
namespace EduMonitor.Usuarios.Services;

public sealed class UsuariosService(UsuariosDb db, IPasswordHasher<User> hasher, IConfiguration config)
{
    private static UserView View(User u) => new(u.Id, u.Name, u.Email, u.Role);
    public async Task<object> Login(LoginRequest input, CancellationToken ct)
    {
        var email = input.Email.Trim().ToLowerInvariant();
        var user = await db.Users.SingleOrDefaultAsync(x => x.Email == email, ct);
        if (user is null || hasher.VerifyHashedPassword(user, user.PasswordHash, input.Password) == PasswordVerificationResult.Failed)
            throw new ApiException(401, "Correo o contraseña incorrectos.");
        var expires = DateTime.UtcNow.AddHours(1);
        var token = new JwtSecurityToken("EduMonitor", "EduMonitor.Web", [
            new Claim(ClaimTypes.NameIdentifier,user.Id.ToString()),new Claim(ClaimTypes.Role,user.Role),
            new Claim(ClaimTypes.Name,user.Name),new Claim("version",user.TokenVersion.ToString())], expires: expires,
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!)), SecurityAlgorithms.HmacSha256));
        return new
        {
            token = new JwtSecurityTokenHandler().WriteToken(token),
            expiresAt = expires,
            user = View(user)
        };
    }
    public async Task<UserView> Me(int id, CancellationToken ct) => View(await db.Users.SingleAsync(x => x.Id == id, ct));
    public async Task Logout(int id, CancellationToken ct)
    {
        var user = await db.Users.SingleAsync(x => x.Id == id, ct);
        user.TokenVersion++;
        await db.SaveChangesAsync(ct);
    }
    public Task<List<UserView>> List(CancellationToken ct) => db.Users.AsNoTracking().Where(x => x.Role == "Teacher").OrderBy(x => x.Name).Select(x => new UserView(x.Id, x.Name, x.Email, x.Role)).ToListAsync(ct);
    public async Task<UserView> Get(int id, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.Role == "Teacher", ct);
        return user is null ? throw new ApiException(404, "Docente no encontrado.") : View(user);
    }
    public async Task<UserView> Save(int? id, TeacherRequest input, CancellationToken ct)
    {
        if (id is null && string.IsNullOrWhiteSpace(input.Password))
            throw new ApiException(400, "La contraseña es obligatoria al registrar un docente.");
        var email = input.Email.Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(x => x.Email == email && x.Id != id, ct))
            throw new ApiException(409, "El correo ya está registrado.");
        var user = id.HasValue ? await db.Users.SingleOrDefaultAsync(x => x.Id == id && x.Role == "Teacher", ct) ?? throw new ApiException(404, "Docente no encontrado.") : new User();
        user.Name = input.Name.Trim();
        user.Email = email;
        if (!string.IsNullOrEmpty(input.Password))
        {
            user.PasswordHash = hasher.HashPassword(user, input.Password);
            user.TokenVersion++;
        }
        if (!id.HasValue)
            db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return View(user);
    }
    public async Task Seed(CancellationToken ct = default)
    {
        if (await db.Users.AnyAsync(x => x.Role == "Admin", ct))
            return;
        var password = config["Seed:AdminPassword"];
        if (string.IsNullOrWhiteSpace(password) || password.Length < 10)
            throw new InvalidOperationException("Configura Seed__AdminPassword con al menos 10 caracteres.");
        var user = new User { Name = "Administrador", Email = (config["Seed:AdminEmail"] ?? "admin@edumonitor.local").Trim().ToLowerInvariant(), Role = "Admin" };
        user.PasswordHash = hasher.HashPassword(user, password);
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
    }
}
