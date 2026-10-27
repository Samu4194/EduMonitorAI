using System.Security.Claims;
using System.Threading.RateLimiting;
using EduMonitor.Infrastructure;
using EduMonitor.Usuarios.Data;
using EduMonitor.Usuarios.Models;
using EduMonitor.Usuarios.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
var builder = WebApplication.CreateBuilder(args);
builder.AddEduMonitor(verifySessionRemotely: false);
builder.Services.AddDbContext<UsuariosDb>(o => o.UseSqlServer(builder.Configuration.GetConnectionString("Default"), sql => sql.CommandTimeout(5)));
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddScoped<UsuariosService>();
builder.Services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o => o.Events = new JwtBearerEvents
{
    OnTokenValidated = async ctx =>
    {
        var db = ctx.HttpContext.RequestServices.GetRequiredService<UsuariosDb>();
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == ctx.Principal!.UserId(), ctx.HttpContext.RequestAborted);
        if (user is null || user.TokenVersion.ToString() != ctx.Principal!.FindFirstValue("version"))
            ctx.Fail("Sesión revocada.");
    }
});
builder.Services.AddRateLimiter(o => { o.RejectionStatusCode = 429; o.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "local", _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })); });
var app = builder.Build();
app.UseRateLimiter();
app.UseEduMonitor<UsuariosDb>();
if (builder.Configuration.GetValue<bool>("Database:Initialize"))
{
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<UsuariosDb>().Database.MigrateAsync();
    await scope.ServiceProvider.GetRequiredService<UsuariosService>().Seed();
}
app.Run();
public partial class Program
{
}
