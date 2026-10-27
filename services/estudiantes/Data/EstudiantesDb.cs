using EduMonitor.Estudiantes.Models;
using Microsoft.EntityFrameworkCore;
namespace EduMonitor.Estudiantes.Data;

public class EstudiantesDb(DbContextOptions<EstudiantesDb> options) : DbContext(options)
{
    public DbSet<Student> Students => Set<Student>();
    public DbSet<Enrollment> Enrollments => Set<Enrollment>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Student>().Property(x => x.StudentCode).HasMaxLength(30);
        b.Entity<Student>().HasIndex(x => x.StudentCode).IsUnique();
        b.Entity<Student>().Property(x => x.FirstNames).HasMaxLength(100);
        b.Entity<Student>().Property(x => x.LastNames).HasMaxLength(100);
        b.Entity<Enrollment>().HasKey(x => new { x.MateriaId, x.StudentId });
        b.Entity<Enrollment>().HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);
    }
}
