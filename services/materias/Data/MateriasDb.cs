using EduMonitor.Materias.Models;
using Microsoft.EntityFrameworkCore;
namespace EduMonitor.Materias.Data;

public class MateriasDb(DbContextOptions<MateriasDb> options) : DbContext(options)
{
    public DbSet<Materia> Materias => Set<Materia>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Materia>().Property(x => x.Code).HasMaxLength(30);
        b.Entity<Materia>().HasIndex(x => x.Code).IsUnique();
        b.Entity<Materia>().Property(x => x.Name).HasMaxLength(120);
        b.Entity<Materia>().Property(x => x.AcademicPeriod).HasMaxLength(80);
    }
}
