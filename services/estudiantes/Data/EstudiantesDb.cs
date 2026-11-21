using EduMonitor.Estudiantes.Models;
using Microsoft.EntityFrameworkCore;
namespace EduMonitor.Estudiantes.Data;

public class EstudiantesDb(DbContextOptions<EstudiantesDb> options) : DbContext(options)
{
    public DbSet<Student> Students => Set<Student>();
    public DbSet<Inscripcion> Inscripciones => Set<Inscripcion>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Student>().Property(x => x.Cedula).HasMaxLength(10);
        b.Entity<Student>().HasIndex(x => x.Cedula).IsUnique();
        b.Entity<Student>().Property(x => x.Apellidos).HasMaxLength(100);
        b.Entity<Student>().Property(x => x.Nombres).HasMaxLength(100);
        b.Entity<Student>().Property(x => x.Correo).HasMaxLength(254);
        b.Entity<Student>().Property(x => x.CarreraCodigo).HasMaxLength(10);
        b.Entity<Student>().HasIndex(x => x.CarreraCodigo);
        b.Entity<Student>().HasMany(x => x.Inscripciones).WithOne(x => x.Student).HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Inscripcion>().Property(x => x.Paralelo).HasMaxLength(2);
        b.Entity<Inscripcion>().Property(x => x.MateriaCodigo).HasMaxLength(20);
        b.Entity<Inscripcion>().Property(x => x.MateriaNombre).HasMaxLength(200);
        b.Entity<Inscripcion>().HasIndex(x => new { x.StudentId, x.CursoId }).IsUnique();
        b.Entity<Inscripcion>().HasIndex(x => x.CursoId);
        b.Entity<Inscripcion>().HasIndex(x => new { x.Nivel, x.Paralelo, x.MateriaCodigo });
    }
}
