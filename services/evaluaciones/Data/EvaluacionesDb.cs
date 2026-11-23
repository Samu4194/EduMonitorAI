using EduMonitor.Evaluaciones.Models;
using Microsoft.EntityFrameworkCore;
namespace EduMonitor.Evaluaciones.Data;

public class EvaluacionesDb(DbContextOptions<EvaluacionesDb> options) : DbContext(options)
{
    public DbSet<Unidad> Unidades => Set<Unidad>();
    public DbSet<Tema> Temas => Set<Tema>();
    public DbSet<Contenido> Contenidos => Set<Contenido>();
    public DbSet<Archivo> Archivos => Set<Archivo>();
    public DbSet<Actividad> Actividades => Set<Actividad>();
    public DbSet<ActividadTema> ActividadTemas => Set<ActividadTema>();
    public DbSet<Calificacion> Calificaciones => Set<Calificacion>();
    public DbSet<Ponderacion> Ponderaciones => Set<Ponderacion>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<VinculoMoodle>(e => {
            e.HasKey(x => x.CursoId);
            e.Property(x => x.CursoId).ValueGeneratedNever();
            e.HasIndex(x => x.MoodleId).IsUnique();
            e.Property(x => x.Codigo).HasMaxLength(100);
            e.HasIndex(x => x.Codigo).IsUnique();
        });
        b.Entity<CorrespondenciaMoodle>(e => {
            e.HasKey(x => new { x.CursoId, x.Clave });
            e.Property(x => x.Clave).HasMaxLength(180);
            e.HasOne<VinculoMoodle>().WithMany().HasForeignKey(x => x.CursoId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Unidad>(e =>
        {
            e.Property(x => x.Nombre).HasMaxLength(120);
            e.Property(x => x.Descripcion).HasMaxLength(500);
            e.HasIndex(x => new { x.CursoId, x.Orden });
            e.HasMany(x => x.Temas).WithOne(x => x.Unidad).HasForeignKey(x => x.UnidadId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<Tema>(e =>
        {
            e.Property(x => x.Nombre).HasMaxLength(150);
            e.Property(x => x.Descripcion).HasMaxLength(500);
            e.HasMany(x => x.Contenidos).WithOne().HasForeignKey(x => x.TemaId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<Contenido>().Property(x => x.Descripcion).HasMaxLength(300);
        b.Entity<Archivo>(e =>
        {
            e.Property(x => x.Nombre).HasMaxLength(200);
            e.Property(x => x.Tipo).HasMaxLength(120);
            e.HasIndex(x => new { x.TemaId, x.Nombre }).IsUnique();
            e.HasOne(x => x.Tema).WithMany().HasForeignKey(x => x.TemaId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<Actividad>(e =>
        {
            e.Property(x => x.Nombre).HasMaxLength(120);
            e.Property(x => x.Tipo).HasMaxLength(10);
            e.Property(x => x.Descripcion).HasMaxLength(500);
            e.HasIndex(x => x.CursoId);
            e.HasMany(x => x.Temas).WithOne(x => x.Actividad).HasForeignKey(x => x.ActividadId).OnDelete(DeleteBehavior.Cascade);
        });
        // Un tema en uso por una actividad no se puede borrar: la restricción lo impide aunque falle la validación del servicio.
        b.Entity<ActividadTema>(e =>
        {
            e.HasKey(x => new { x.ActividadId, x.TemaId });
            e.HasOne(x => x.Tema).WithMany().HasForeignKey(x => x.TemaId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Calificacion>(e =>
        {
            e.Property(x => x.Nota).HasPrecision(4, 2);
            e.HasIndex(x => new { x.ActividadId, x.EstudianteId }).IsUnique();
            e.HasOne(x => x.Actividad).WithMany().HasForeignKey(x => x.ActividadId).OnDelete(DeleteBehavior.Cascade);
            e.ToTable(t => t.HasCheckConstraint("CK_Calificaciones_Nota", "[Nota] >= 0 AND [Nota] <= 10"));
        });
        b.Entity<Ponderacion>(e =>
        {
            e.HasKey(x => x.CursoId);
            e.Property(x => x.CursoId).ValueGeneratedNever();
            e.Property(x => x.Tareas).HasPrecision(5, 2);
            e.Property(x => x.Ape).HasPrecision(5, 2);
            e.Property(x => x.Examen).HasPrecision(5, 2);
            e.Property(x => x.NotaMinima).HasPrecision(4, 2);
        });
    }
}
