using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace EduMonitor.Api;

[ApiController, Route("api/auth")]
public class AuthController(AppDbContext db, IPasswordHasher<AppUser> hasher, IConfiguration config) : ControllerBase
{
    [HttpPost("login"), EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginRequest input)
    {
        var user = await db.Users.SingleOrDefaultAsync(x => x.Email == input.Email.Trim().ToLower());
        if (user is null || hasher.VerifyHashedPassword(user, user.PasswordHash, input.Password) == PasswordVerificationResult.Failed)
            return Unauthorized(new { message = "Correo o contraseña incorrectos." });
        var expires = DateTime.UtcNow.AddHours(1);
        var jwt = new JwtSecurityToken("EduMonitor", "EduMonitor.Web", [
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim(ClaimTypes.Role, user.Role),
            new Claim(ClaimTypes.Name, user.Name), new Claim("version", user.TokenVersion.ToString())],
            expires: expires, signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!)), SecurityAlgorithms.HmacSha256));
        return Ok(new { token = new JwtSecurityTokenHandler().WriteToken(jwt), expiresAt = expires, user = new { user.Id, user.Name, user.Email, user.Role } });
    }
    [Authorize, HttpGet("me")]
    public async Task<IActionResult> Me() => Ok(await db.Users.Where(x => x.Id == int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!)).Select(x => new { x.Id, x.Name, x.Email, x.Role }).SingleAsync());
    [Authorize, HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        var user = await db.Users.FindAsync(int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!));
        user!.TokenVersion++;
        await db.SaveChangesAsync();
        return NoContent();
    }
}

[ApiController, Authorize(Roles="Admin"), Route("api/teachers")]
public class TeachersController(AppDbContext db, IPasswordHasher<AppUser> hasher) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List() => Ok(await db.Users.Where(x => x.Role == "Teacher").OrderBy(x => x.Name).Select(x => new { x.Id, x.Name, x.Email, x.Specialty }).ToListAsync());
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var x = await db.Users.Where(x => x.Id == id && x.Role == "Teacher").Select(x => new { x.Id, x.Name, x.Email, x.Specialty }).SingleOrDefaultAsync();
        return x is null ? NotFound() : Ok(x);
    }
    [HttpPost]
    public async Task<IActionResult> Create(TeacherRequest input)
    {
        if (string.IsNullOrWhiteSpace(input.Password)) return BadRequest(new { message="La contraseña es obligatoria." });
        var email = input.Email.Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(x => x.Email == email)) return Conflict(new { message="Este correo ya está registrado." });
        var teacher = new AppUser { Name=input.Name.Trim(), Email=email, Specialty=input.Specialty.Trim() };
        teacher.PasswordHash = hasher.HashPassword(teacher, input.Password);
        db.Users.Add(teacher); await db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id=teacher.Id }, new { teacher.Id, teacher.Name, teacher.Email, teacher.Specialty });
    }
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, TeacherRequest input)
    {
        var x = await db.Users.SingleOrDefaultAsync(x => x.Id == id && x.Role == "Teacher");
        if (x is null) return NotFound();
        var email = input.Email.Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(x => x.Id != id && x.Email == email)) return Conflict(new { message="Este correo ya está registrado." });
        x.Name=input.Name.Trim(); x.Email=email; x.Specialty=input.Specialty.Trim();
        if (!string.IsNullOrEmpty(input.Password)) { x.PasswordHash=hasher.HashPassword(x,input.Password); x.TokenVersion++; }
        await db.SaveChangesAsync(); return NoContent();
    }
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var x = await db.Users.SingleOrDefaultAsync(x => x.Id == id && x.Role == "Teacher");
        if (x is null) return NotFound();
        if (await db.Courses.AnyAsync(c => c.TeacherId == id)) return Conflict(new { message="Reasigna o elimina los cursos de este docente antes de eliminarlo." });
        db.Users.Remove(x); await db.SaveChangesAsync(); return NoContent();
    }
}

[ApiController, Authorize, Route("api/courses")]
public class CoursesController(AppDbContext db) : ControllerBase
{
    private int UserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private IQueryable<Course> Visible => User.IsInRole("Admin") ? db.Courses : db.Courses.Where(x => x.TeacherId == UserId);
    [HttpGet]
    public async Task<IActionResult> List() => Ok(await Visible.OrderBy(x=>x.Name).Select(x => new { x.Id, x.Name, x.Code, x.Description, x.TeacherId, teacherName=x.Teacher.Name, studentCount=x.Enrollments.Count }).ToListAsync());
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var x = await Visible.Where(x=>x.Id==id).Select(x => new { x.Id, x.Name, x.Code, x.Description, x.TeacherId, teacherName=x.Teacher.Name, studentCount=x.Enrollments.Count }).SingleOrDefaultAsync();
        return x is null ? NotFound() : Ok(x);
    }
    [HttpPost, Authorize(Roles="Admin")]
    public async Task<IActionResult> Create(CourseRequest input)
    {
        if (!await db.Users.AnyAsync(x=>x.Id==input.TeacherId && x.Role=="Teacher")) return BadRequest(new { message="Selecciona un docente válido." });
        var code = input.Code.ToUpperInvariant();
        if (await db.Courses.AnyAsync(x=>x.Code==code)) return Conflict(new { message="El código del curso ya existe." });
        var x = new Course { Name=input.Name.Trim(), Code=code, Description=input.Description?.Trim() ?? "", TeacherId=input.TeacherId };
        db.Courses.Add(x); await db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get),new { id=x.Id },new { x.Id, x.Name, x.Code, x.Description, x.TeacherId });
    }
    [HttpPut("{id:int}"), Authorize(Roles="Admin")]
    public async Task<IActionResult> Update(int id, CourseRequest input)
    {
        var x = await db.Courses.FindAsync(id); if (x is null) return NotFound();
        if (!await db.Users.AnyAsync(t=>t.Id==input.TeacherId && t.Role=="Teacher")) return BadRequest(new { message="Selecciona un docente válido." });
        var code=input.Code.ToUpperInvariant();
        if (await db.Courses.AnyAsync(c=>c.Id!=id && c.Code==code)) return Conflict(new { message="El código del curso ya existe." });
        x.Name=input.Name.Trim(); x.Code=code; x.Description=input.Description?.Trim() ?? ""; x.TeacherId=input.TeacherId;
        await db.SaveChangesAsync(); return NoContent();
    }
    [HttpDelete("{id:int}"), Authorize(Roles="Admin")]
    public async Task<IActionResult> Delete(int id)
    {
        var x = await db.Courses.FindAsync(id); if(x is null) return NotFound();
        db.Courses.Remove(x); await db.SaveChangesAsync(); return NoContent();
    }
    [HttpGet("{id:int}/students")]
    public async Task<IActionResult> Students(int id)
    {
        if (!await Visible.AnyAsync(x=>x.Id==id)) return NotFound();
        return Ok(await db.Enrollments.Where(x=>x.CourseId==id).OrderBy(x=>x.Student.Name).Select(x=>new { x.Student.Id, x.Student.Name, x.Student.Email, x.Student.StudentCode, x.EnrolledAt }).ToListAsync());
    }
    [HttpPost("{id:int}/students"), Authorize(Roles="Admin")]
    public async Task<IActionResult> Enroll(int id, EnrollmentRequest input)
    {
        if (!await db.Courses.AnyAsync(x=>x.Id==id) || !await db.Students.AnyAsync(x=>x.Id==input.StudentId)) return NotFound();
        if (await db.Enrollments.AnyAsync(x=>x.CourseId==id && x.StudentId==input.StudentId)) return Conflict(new { message="El estudiante ya está matriculado en este curso." });
        db.Enrollments.Add(new Enrollment { CourseId=id, StudentId=input.StudentId }); await db.SaveChangesAsync(); return NoContent();
    }
    [HttpDelete("{id:int}/students/{studentId:int}"), Authorize(Roles="Admin")]
    public async Task<IActionResult> Unenroll(int id, int studentId)
    {
        var x=await db.Enrollments.FindAsync(id,studentId); if(x is null) return NotFound();
        db.Enrollments.Remove(x); await db.SaveChangesAsync(); return NoContent();
    }
}

[ApiController, Authorize(Roles="Admin"), Route("api/students")]
public class StudentsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List() => Ok(await db.Students.AsNoTracking().OrderBy(x=>x.Name).ToListAsync());
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id) { var x=await db.Students.FindAsync(id); return x is null ? NotFound() : Ok(x); }
    [HttpPost]
    public async Task<IActionResult> Create(StudentRequest input)
    {
        var email=input.Email.Trim().ToLowerInvariant(); var code=input.StudentCode.ToUpperInvariant();
        if(await db.Students.AnyAsync(x=>x.Email==email || x.StudentCode==code)) return Conflict(new { message="Ya existe un estudiante con ese correo o código." });
        var x=new Student { Name=input.Name.Trim(), Email=email, StudentCode=code };
        db.Students.Add(x); await db.SaveChangesAsync(); return CreatedAtAction(nameof(Get),new { id=x.Id },x);
    }
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, StudentRequest input)
    {
        var x=await db.Students.FindAsync(id); if(x is null) return NotFound();
        var email=input.Email.Trim().ToLowerInvariant(); var code=input.StudentCode.ToUpperInvariant();
        if(await db.Students.AnyAsync(s=>s.Id!=id && (s.Email==email || s.StudentCode==code))) return Conflict(new { message="Ya existe un estudiante con ese correo o código." });
        x.Name=input.Name.Trim(); x.Email=email; x.StudentCode=code; await db.SaveChangesAsync(); return NoContent();
    }
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var x=await db.Students.FindAsync(id); if(x is null) return NotFound();
        db.Students.Remove(x); await db.SaveChangesAsync(); return NoContent();
    }
}
