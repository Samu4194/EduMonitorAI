using EduMonitor.Infrastructure;
using EduMonitor.Materias.Data;
using EduMonitor.Materias.Services;
using Microsoft.EntityFrameworkCore;
var builder = WebApplication.CreateBuilder(args);
builder.AddEduMonitor();
builder.Services.AddDbContext<MateriasDb>(o => o.UseSqlServer(builder.Configuration.GetConnectionString("Default"), sql => sql.CommandTimeout(5)));
builder.Services.AddScoped<MateriasService>();
var app = builder.Build();
app.UseEduMonitor<MateriasDb>();
if (builder.Configuration.GetValue<bool>("Database:Initialize")) { using var scope = app.Services.CreateScope(); await scope.ServiceProvider.GetRequiredService<MateriasDb>().Database.MigrateAsync(); }
app.Run();
public partial class Program
{
}
