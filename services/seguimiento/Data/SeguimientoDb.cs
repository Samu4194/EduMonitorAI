using EduMonitor.Seguimiento.Models;
using Microsoft.EntityFrameworkCore;
namespace EduMonitor.Seguimiento.Data;

public class SeguimientoDb(DbContextOptions<SeguimientoDb> options) : DbContext(options)
{
    public DbSet<Analisis> Analisis => Set<Analisis>();
    public DbSet<EstudianteAnalisis> EstudiantesAnalisis => Set<EstudianteAnalisis>();
    public DbSet<Hallazgo> Hallazgos => Set<Hallazgo>();
    public DbSet<Recomendacion> Recomendaciones => Set<Recomendacion>();
    public DbSet<Comunicacion> Comunicaciones => Set<Comunicacion>();
    public DbSet<EventoRecomendacion> Eventos => Set<EventoRecomendacion>();
    public DbSet<Conversacion> Conversaciones => Set<Conversacion>();
    public DbSet<Mensaje> Mensajes => Set<Mensaje>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Analisis>(e =>
        {
            e.ToTable("Analisis");
            e.Property(x => x.Materia).HasMaxLength(150);
            e.Property(x => x.Paralelo).HasMaxLength(20);
            e.Property(x => x.Periodo).HasMaxLength(100);
            e.Property(x => x.Estado).HasMaxLength(20);
            e.Property(x => x.NotaMinima).HasPrecision(4, 2);
            e.HasIndex(x => new { x.CursoId, x.CreadoEn });
            e.HasMany(x => x.Estudiantes).WithOne().HasForeignKey(x => x.AnalisisId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Hallazgos).WithOne().HasForeignKey(x => x.AnalisisId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<EstudianteAnalisis>(e =>
        {
            e.ToTable("EstudianteAnalisis");
            e.Property(x => x.Apellidos).HasMaxLength(120);
            e.Property(x => x.Nombres).HasMaxLength(120);
            e.Property(x => x.Correo).HasMaxLength(200);
            e.Property(x => x.Estado).HasMaxLength(30);
            e.Property(x => x.Nivel).HasMaxLength(20);
            e.Property(x => x.Resultado).HasPrecision(4, 2);
            e.HasIndex(x => new { x.AnalisisId, x.EstudianteId }).IsUnique();
        });
        b.Entity<Hallazgo>(e =>
        {
            e.Property(x => x.Codigo).HasMaxLength(40);
            e.Property(x => x.Nivel).HasMaxLength(20);
            e.Property(x => x.Titulo).HasMaxLength(200);
            e.Property(x => x.Detalle).HasMaxLength(1000);
            e.HasOne<EstudianteAnalisis>().WithMany().HasForeignKey(x => x.EstudianteAnalisisId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.AnalisisId);
        });
        b.Entity<Recomendacion>(e =>
        {
            e.Property(x => x.Estado).HasMaxLength(20);
            e.Property(x => x.Origen).HasMaxLength(20);
            e.Property(x => x.Modelo).HasMaxLength(100);
            e.Property(x => x.Resumen).HasMaxLength(1500);
            e.Property(x => x.MensajeEstudiante).HasMaxLength(3000);
            e.Property(x => x.MotivoRechazo).HasMaxLength(500);
            e.HasOne(x => x.EstudianteAnalisis).WithMany().HasForeignKey(x => x.EstudianteAnalisisId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Analisis>().WithMany().HasForeignKey(x => x.AnalisisId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Comunicaciones).WithOne().HasForeignKey(x => x.RecomendacionId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.CursoId, x.Estado });
        });
        b.Entity<Comunicacion>(e =>
        {
            e.Property(x => x.Canal).HasMaxLength(20);
            e.Property(x => x.Destinatario).HasMaxLength(200);
            e.Property(x => x.Asunto).HasMaxLength(200);
            e.Property(x => x.Cuerpo).HasMaxLength(4000);
            e.Property(x => x.Estado).HasMaxLength(20);
            e.Property(x => x.Error).HasMaxLength(500);
            e.HasIndex(x => x.CursoId);
        });
        b.Entity<Conversacion>(e =>
        {
            e.ToTable("Conversaciones");
            e.Property(x => x.Titulo).HasMaxLength(120);
            e.HasIndex(x => new { x.CursoId, x.DocenteId, x.ActualizadoEn });
            e.HasMany(x => x.Mensajes).WithOne().HasForeignKey(x => x.ConversacionId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<Mensaje>(e =>
        {
            e.ToTable("Mensajes");
            e.Property(x => x.Rol).HasMaxLength(20);
            e.Property(x => x.Tipo).HasMaxLength(20);
            e.Property(x => x.PreguntaId).HasMaxLength(40);
            e.Property(x => x.Origen).HasMaxLength(20);
            e.Property(x => x.Modelo).HasMaxLength(100);
            e.Property(x => x.Alcance).HasMaxLength(10);
            e.HasIndex(x => x.ConversacionId);
        });
        b.Entity<EventoRecomendacion>(e =>
        {
            e.Property(x => x.Evento).HasMaxLength(40);
            e.Property(x => x.Detalle).HasMaxLength(500);
            e.HasIndex(x => x.RecomendacionId);
        });
    }
}
