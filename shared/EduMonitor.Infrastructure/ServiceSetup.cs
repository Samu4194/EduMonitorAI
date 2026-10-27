using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace EduMonitor.Infrastructure;

public static class ServiceSetup
{
    public static void AddEduMonitor(this WebApplicationBuilder builder, bool verifySessionRemotely = true)
    {
        builder.Services.AddControllers();
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<Errors>();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddHttpClient<DependencyClient>(c => c.Timeout = TimeSpan.FromSeconds(3));
        var key = builder.Configuration["Jwt:Key"];
        if (string.IsNullOrWhiteSpace(key) || Encoding.UTF8.GetByteCount(key) < 32)
            throw new InvalidOperationException("Configura Jwt__Key con al menos 32 bytes.");
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
        {
            o.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = "EduMonitor",
                ValidAudience = "EduMonitor.Web",
                ClockSkew = TimeSpan.FromSeconds(10),
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key))
            };
            if (verifySessionRemotely)
                o.Events = new JwtBearerEvents
                {
                    OnTokenValidated = async ctx =>
                    {
                        // La firma se comprueba localmente. Usuarios confirma además que la sesión no fue revocada.
                        await ctx.HttpContext.RequestServices.GetRequiredService<DependencyClient>().Get<SessionInfo>("Usuarios", "/api/auth/session", ctx.HttpContext.RequestAborted);
                    }
                };
        });
        builder.Services.AddAuthorization();
    }
    public static void UseEduMonitor<T>(this WebApplication app) where T : DbContext
    {
        app.UseExceptionHandler();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        app.MapGet("/health/live", () => Results.Ok(new { status = "ok" }));
        app.MapGet("/health/ready", async (HttpContext context) =>
        {
            var db = context.RequestServices.GetRequiredService<T>();
            return await db.Database.CanConnectAsync(context.RequestAborted)
                ? Results.Ok(new { status = "ok" })
                : Results.Json(new { message = "SQL Server no disponible" }, statusCode: 503);
        });
    }
    public static int UserId(this ClaimsPrincipal user) => int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    public record SessionInfo(int Id, string Name, string Email, string Role);
}

internal sealed class Errors(ILogger<Errors> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception error, CancellationToken ct)
    {
        var (status, message) = error switch
        {
            ApiException api => (api.Status, api.Message),
            DbUpdateException { InnerException: SqlException { Number: 2601 or 2627 } } => (409, "El código o correo ya está registrado."),
            SqlException => (503, "La base de datos no está disponible. Inténtalo de nuevo en unos segundos."),
            DbUpdateException { InnerException: SqlException } => (503, "No se pudo guardar la información. Comprueba la disponibilidad de la base de datos."),
            _ => (500, "No se pudo completar la operación. Consulta el registro técnico con el identificador del error.")
        };
        logger.LogError(error, "Solicitud fallida {TraceId}, estado {Status}", ctx.TraceIdentifier, status);
        ctx.Response.StatusCode = status;
        await ctx.Response.WriteAsJsonAsync(new
        {
            message,
            traceId = ctx.TraceIdentifier
        }, ct);
        return true;
    }
}
