using EduMonitor.Infrastructure;
using EduMonitor.Seguimiento.Agente;
using EduMonitor.Seguimiento.Asistente;
using EduMonitor.Seguimiento.Data;
using EduMonitor.Seguimiento.Reglas;
using EduMonitor.Seguimiento.Services;
using Microsoft.EntityFrameworkCore;
var builder = WebApplication.CreateBuilder(args);
builder.AddEduMonitor();
builder.Services.AddDbContext<SeguimientoDb>(o => o.UseSqlServer(builder.Configuration.GetConnectionString("Default"), sql => sql.CommandTimeout(5)));
builder.Services.AddScoped<AccesoCursos>();

// Configuración de las reglas (HU-11), del agente (HU-12) y del correo (HU-13).
var reglas = builder.Configuration.GetSection("Reglas").Get<OpcionesReglas>() ?? new OpcionesReglas();
var agente = builder.Configuration.GetSection("Agente").Get<OpcionesAgente>() ?? new OpcionesAgente();
var correo = builder.Configuration.GetSection("Smtp").Get<OpcionesCorreo>() ?? new OpcionesCorreo();
builder.Services.AddSingleton(reglas);
builder.Services.AddSingleton(agente);
builder.Services.AddSingleton(correo);
builder.Services.AddSingleton(new MotorReglas(reglas));
// El plazo de cada llamada a la IA lo controla el propio proveedor; el cliente HTTP no debe cortarlo antes.
builder.Services.AddHttpClient("ia-local", c => c.Timeout = Timeout.InfiniteTimeSpan);
// A la nube se le da poco tiempo para conectar: sin internet debe fallar en segundos y pasar a la IA local, no esperar el plazo completo.
builder.Services.AddHttpClient("ia-nube", c => c.Timeout = Timeout.InfiniteTimeSpan)
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { ConnectTimeout = TimeSpan.FromSeconds(5) });
builder.Services.AddSingleton<ProveedoresIA>();
builder.Services.AddSingleton<AgenteAcademico>();
builder.Services.AddSingleton<Comunicador>();
builder.Services.AddSingleton<ColaTrabajos>();
builder.Services.AddHostedService<GeneradorEnSegundoPlano>();
// Asistente (chat del docente): historial temporal, un turno a la vez por docente y datos del curso en vivo.
builder.Services.AddSingleton(builder.Configuration.GetSection("Asistente").Get<OpcionesAsistente>() ?? new OpcionesAsistente());
builder.Services.AddSingleton<LimiteDocente>();
builder.Services.AddScoped<CargadorDatos>();
builder.Services.AddScoped<ServicioAsistente>();
builder.Services.AddHostedService<DepuradorConversaciones>();
builder.Services.AddScoped<AnalisisService>();
builder.Services.AddScoped<RecomendacionesService>();
builder.Services.AddScoped<Consultas>();
var app = builder.Build();
app.UseEduMonitor<SeguimientoDb>();
if (builder.Configuration.GetValue<bool>("Database:Initialize")) { using var scope = app.Services.CreateScope(); await scope.ServiceProvider.GetRequiredService<SeguimientoDb>().Database.MigrateAsync(); }
app.Run();
public partial class Program
{
}
