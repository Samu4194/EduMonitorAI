using EduMonitor.Infrastructure;
using EduMonitor.Estudiantes.Data;
using EduMonitor.Estudiantes.Services;
using Microsoft.EntityFrameworkCore;
var builder = WebApplication.CreateBuilder(args);
builder.AddEduMonitor();
builder.Services.AddDbContext<EstudiantesDb>(o => o.UseSqlServer(builder.Configuration.GetConnectionString("Default"), sql => sql.CommandTimeout(5)));
builder.Services.AddScoped<EstudiantesService>();
var app = builder.Build();
app.UseEduMonitor<EstudiantesDb>();
if (builder.Configuration.GetValue<bool>("Database:Initialize")) { using var scope = app.Services.CreateScope(); await scope.ServiceProvider.GetRequiredService<EstudiantesDb>().Database.MigrateAsync(); }
app.Run();
public partial class Program
{
}
