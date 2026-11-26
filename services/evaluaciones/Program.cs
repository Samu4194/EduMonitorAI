using EduMonitor.Evaluaciones.Data;
using EduMonitor.Evaluaciones.Services;
using EduMonitor.Infrastructure;
using Microsoft.EntityFrameworkCore;
var builder = WebApplication.CreateBuilder(args);
builder.AddEduMonitor();
builder.Services.AddDbContext<EvaluacionesDb>(o => o.UseSqlServer(builder.Configuration.GetConnectionString("Default"), sql => sql.CommandTimeout(5)));
builder.Services.AddScoped<FuenteAcademica>();
builder.Services.AddScoped<AccesoCursos>();
builder.Services.AddSingleton<AlmacenArchivos>();
builder.Services.AddScoped<ContenidosService>();
builder.Services.AddScoped<CalificacionesService>();
builder.Services.AddHttpClient<EduMonitor.Evaluaciones.Moodle.MoodleClient>(c => c.Timeout = TimeSpan.FromSeconds(5))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddScoped<EduMonitor.Evaluaciones.Moodle.SincronizacionMoodle>();
var app = builder.Build();
app.UseEduMonitor<EvaluacionesDb>();
if (builder.Configuration.GetValue<bool>("Database:Initialize")) { using var scope = app.Services.CreateScope(); await scope.ServiceProvider.GetRequiredService<EvaluacionesDb>().Database.MigrateAsync(); }
app.Run();
public partial class Program
{
}
