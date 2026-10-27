using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using EduMonitor.Api;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
var frontend = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "../../frontend/dist"));
if (Directory.Exists(frontend))
{
    builder.Environment.WebRootPath = frontend;
    builder.Environment.WebRootFileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(frontend);
}
builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlServer(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddScoped<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
var key = builder.Configuration["Jwt:Key"] ?? throw new InvalidOperationException("Configura Jwt__Key (al menos 32 caracteres). Ejecuta scripts/Start.ps1.");
if (Encoding.UTF8.GetByteCount(key) < 32) throw new InvalidOperationException("Jwt__Key debe tener al menos 32 bytes.");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true, ValidateIssuerSigningKey = true,
        ValidIssuer = "EduMonitor", ValidAudience = "EduMonitor.Web",
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)), ClockSkew = TimeSpan.FromSeconds(10)
    };
    o.Events = new JwtBearerEvents { OnTokenValidated = async ctx =>
    {
        var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
        var id = int.Parse(ctx.Principal!.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
        if (user is null || user.TokenVersion.ToString() != ctx.Principal!.FindFirstValue("version")) ctx.Fail("Sesión revocada.");
    }};
});
builder.Services.AddAuthorization();
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins("http://localhost:5173", "http://127.0.0.1:5173").AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "local", _ => new FixedWindowRateLimiterOptions { PermitLimit=20, Window=TimeSpan.FromMinutes(1), QueueLimit=0 }));
});
var app = builder.Build();
app.UseExceptionHandler();
app.Use(async (ctx, next) =>
{
    try { await next(); }
    catch (DbUpdateException e) when (e.InnerException is Microsoft.Data.SqlClient.SqlException sql && (sql.Number == 2601 || sql.Number == 2627 || sql.Number == 547))
    {
        ctx.Response.StatusCode = 409;
        await ctx.Response.WriteAsJsonAsync(new { message = "El registro está duplicado o relacionado con otro registro. Actualiza la página e inténtalo de nuevo." });
    }
});
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapControllers();
app.Map("/api/{**path}", () => Results.NotFound(new { message="Ruta de API no encontrada." }));
app.MapFallbackToFile("{*path:nonfile}", "index.html");
app.MapGet("/api/health", async (AppDbContext db) => await db.Database.CanConnectAsync() ? Results.Ok(new { status = "ok" }) : Results.Problem("Base de datos no disponible", statusCode:503));
if (args.Contains("--initialize"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await Seed.Initialize(db, scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>(), builder.Configuration);
    return;
}
app.Run();
public partial class Program { }
