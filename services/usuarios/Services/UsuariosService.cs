using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using EduMonitor.Infrastructure;
using EduMonitor.Usuarios.Data;
using EduMonitor.Usuarios.DTOs;
using EduMonitor.Usuarios.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
namespace EduMonitor.Usuarios.Services;

//Generar el token JWT
public sealed class UsuariosService(UsuariosDb db, IPasswordHasher<User> hasher, IConfiguration config, DependencyClient dependencies)
{
    private static string[] CarrerasDe(User u) => u.Profile?.Carreras.Select(c => c.CarreraCodigo).Order().ToArray() ?? [];
    private static UserView View(User u) => new(u.Id, u.Name, u.Email, u.Role, CarrerasDe(u));
    private static TeacherView TeacherOf(User u) => new(u.Id, u.Name, u.Email, u.Profile?.Cedula ?? "", u.Profile?.Nombres ?? u.Name, u.Profile?.Apellidos ?? "", u.Profile?.Telefono, u.Profile?.TituloAcademico, CarrerasDe(u), u.Activo);
    private IQueryable<User> WithProfile() => db.Users.Include(x => x.Profile).ThenInclude(p => p!.Carreras);
    public async Task<object> Login(LoginRequest input, CancellationToken ct)
    {
        var email = input.Email.Trim().ToLowerInvariant();
        //Comprobar contraseña
        var user = await WithProfile().SingleOrDefaultAsync(x => x.Email == email, ct);
        if (user is null || hasher.VerifyHashedPassword(user, user.PasswordHash, input.Password) == PasswordVerificationResult.Failed)
            throw new ApiException(401, "Correo o contraseña incorrectos.");
        // Se comprueba después de la contraseña: así la baja no se revela a quien no la conoce.
        if (!user.Activo)
            throw new ApiException(403, "Tu cuenta está dada de baja. Comunícate con el administrador.");
        var expires = DateTime.UtcNow.AddHours(1);
        // Un claim "carrera" por cada carrera del docente: los demás servicios segmentan con ellos.
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()), new(ClaimTypes.Role, user.Role),
            new(ClaimTypes.Name, user.Name), new("version", user.TokenVersion.ToString())
        };
        claims.AddRange(CarrerasDe(user).Select(c => new Claim("carrera", c)));
        var token = new JwtSecurityToken("EduMonitor", "EduMonitor.Web", claims, expires: expires,
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!)), SecurityAlgorithms.HmacSha256));
        return new
        {
            token = new JwtSecurityTokenHandler().WriteToken(token),
            expiresAt = expires,
            user = View(user)
        };
    }
    public async Task<UserView> Me(int id, CancellationToken ct) => View(await WithProfile().AsNoTracking().SingleAsync(x => x.Id == id, ct));
    public async Task Logout(int id, CancellationToken ct)
    {
        var user = await db.Users.SingleAsync(x => x.Id == id, ct);
        user.TokenVersion++;
        await db.SaveChangesAsync(ct);
    }
    // carrera opcional: sirve para elegir docentes de una carrera al asignarlos a un curso.
    // estado: «activos» (por defecto, lo que usan las asignaciones), «bajas» o «todos».
    public async Task<List<TeacherView>> List(string? carrera, string? estado, CancellationToken ct)
    {
        var query = WithProfile().AsNoTracking().Where(x => x.Role == "Teacher");
        query = (estado ?? "activos").ToLowerInvariant() switch
        {
            "todos" => query,
            "bajas" => query.Where(x => !x.Activo),
            _ => query.Where(x => x.Activo)
        };
        if (!string.IsNullOrWhiteSpace(carrera))
        {
            var code = carrera.Trim().ToUpperInvariant();
            query = query.Where(x => x.Profile!.Carreras.Any(c => c.CarreraCodigo == code));
        }
        return (await query.OrderBy(x => x.Name).ToListAsync(ct)).Select(TeacherOf).ToList();
    }
    public async Task<TeacherView> Get(int id, CancellationToken ct)
    {
        var user = await WithProfile().AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.Role == "Teacher", ct);
        return user is null ? throw new ApiException(404, "Docente no encontrado.") : TeacherOf(user);
    }
    // Dar de baja: primero se liberan sus cursos en Materias (idempotente) y después se desactiva la cuenta y se revocan sus sesiones.
    // Si Materias no responde no se hace nada: no puede quedar un docente de baja con cursos asignados.
    public async Task<TeacherView> Baja(int id, CancellationToken ct)
    {
        var user = await WithProfile().SingleOrDefaultAsync(x => x.Id == id && x.Role == "Teacher", ct) ?? throw new ApiException(404, "Docente no encontrado.");
        if (!user.Activo)
            return TeacherOf(user);
        await dependencies.Post<object>("Materias", $"/api/cursos/docente/{id}/liberar", ct);
        user.Activo = false;
        user.TokenVersion++;
        await db.SaveChangesAsync(ct);
        return TeacherOf(user);
    }
    public async Task<TeacherView> Reactivar(int id, CancellationToken ct)
    {
        var user = await WithProfile().SingleOrDefaultAsync(x => x.Id == id && x.Role == "Teacher", ct) ?? throw new ApiException(404, "Docente no encontrado.");
        user.Activo = true;
        await db.SaveChangesAsync(ct);
        return TeacherOf(user);
    }
    public async Task<TeacherView> Save(int? id, TeacherRequest input, CancellationToken ct)
    {
        if (id is null && string.IsNullOrWhiteSpace(input.Password))
            throw new ApiException(400, "La contraseña es obligatoria al registrar un docente.");
        var email = input.Email.Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(x => x.Email == email && x.Id != id, ct))
            throw new ApiException(409, "El correo ya está registrado.");
        if (await db.TeacherProfiles.AnyAsync(x => x.Cedula == input.Cedula && x.UserId != id, ct))
            throw new ApiException(409, "La cédula ya está registrada.");
        var carreras = input.Carreras.Select(c => c.Trim().ToUpperInvariant()).Distinct().ToList();
        if (carreras.Any(c => !Regex.IsMatch(c, "^[A-Z0-9]{2,10}$")))
            throw new ApiException(400, "Algún código de carrera no es válido.");
        var user = id.HasValue ? await WithProfile().SingleOrDefaultAsync(x => x.Id == id && x.Role == "Teacher", ct) ?? throw new ApiException(404, "Docente no encontrado.") : new User();
        var profile = user.Profile ??= new TeacherProfile();
        var anteriores = profile.Carreras.Select(c => c.CarreraCodigo).Order().ToList();
        profile.Cedula = input.Cedula;
        profile.Nombres = input.Nombres.Trim();
        profile.Apellidos = input.Apellidos.Trim();
        profile.Telefono = string.IsNullOrWhiteSpace(input.Telefono) ? null : input.Telefono.Trim();
        profile.TituloAcademico = string.IsNullOrWhiteSpace(input.TituloAcademico) ? null : input.TituloAcademico.Trim();
        profile.Carreras.RemoveAll(c => !carreras.Contains(c.CarreraCodigo));
        foreach (var code in carreras.Where(c => profile.Carreras.All(x => x.CarreraCodigo != c)))
            profile.Carreras.Add(new TeacherCarrera { CarreraCodigo = code });
        user.Name = $"{profile.Apellidos} {profile.Nombres}";
        user.Email = email;
        user.Role = "Teacher";
        // Si cambian la contraseña o las carreras, las sesiones anteriores dejan de valer: el token lleva las carreras.
        if (!string.IsNullOrEmpty(input.Password))
        {
            user.PasswordHash = hasher.HashPassword(user, input.Password);
            user.TokenVersion++;
        }
        else if (id.HasValue && !anteriores.SequenceEqual(carreras.Order()))
            user.TokenVersion++;
        if (!id.HasValue)
            db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return TeacherOf(user);
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
