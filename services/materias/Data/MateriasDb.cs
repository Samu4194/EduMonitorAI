using EduMonitor.Materias.Models;
using Microsoft.EntityFrameworkCore;
namespace EduMonitor.Materias.Data;

public class MateriasDb(DbContextOptions<MateriasDb> options) : DbContext(options)
{
    public DbSet<Carrera> Carreras => Set<Carrera>();
    public DbSet<Malla> Mallas => Set<Malla>();
    public DbSet<Materia> Materias => Set<Materia>();
    public DbSet<Periodo> Periodos => Set<Periodo>();
    public DbSet<Paralelo> Paralelos => Set<Paralelo>();
    public DbSet<Curso> Cursos => Set<Curso>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Carrera>().Property(x => x.Codigo).HasMaxLength(10);
        b.Entity<Carrera>().HasIndex(x => x.Codigo).IsUnique();
        b.Entity<Carrera>().Property(x => x.Nombre).HasMaxLength(120);
        b.Entity<Carrera>().Property(x => x.Activa).HasDefaultValue(true);
        b.Entity<Carrera>().HasIndex(x => x.PrefijoUta).IsUnique().HasFilter("[PrefijoUta] <> ''");
        b.Entity<Carrera>().Property(x => x.PrefijoUta).HasMaxLength(4);
        b.Entity<Carrera>().Property(x => x.Modalidad).HasMaxLength(30);
        b.Entity<Materia>().Property(x => x.Codigo).HasMaxLength(20);
        b.Entity<Materia>().HasIndex(x => x.Codigo).IsUnique();
        b.Entity<Materia>().Property(x => x.CodigoUta).HasMaxLength(8);
        // El código UTA es único dentro de una malla (una malla nueva puede conservar los códigos oficiales); vacío se permite repetido.
        b.Entity<Materia>().HasIndex(x => new { x.MallaId, x.CodigoUta }).IsUnique().HasFilter("[CodigoUta] <> ''");
        b.Entity<Materia>().Property(x => x.Nombre).HasMaxLength(150);
        b.Entity<Materia>().Property(x => x.Unidad).HasMaxLength(20);
        b.Entity<Materia>().Property(x => x.Tipo).HasMaxLength(30);
        b.Entity<Malla>().HasIndex(x => new { x.CarreraId, x.Version }).IsUnique();
        b.Entity<Malla>().HasOne(x => x.Carrera).WithMany(x => x.Mallas).HasForeignKey(x => x.CarreraId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Materia>().HasOne(x => x.Malla).WithMany(x => x.Materias).HasForeignKey(x => x.MallaId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Materia>().HasOne(x => x.Carrera).WithMany(x => x.Materias).HasForeignKey(x => x.CarreraId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Periodo>().Property(x => x.Nombre).HasMaxLength(60);
        b.Entity<Periodo>().HasIndex(x => x.Nombre).IsUnique();
        b.Entity<Periodo>().Property(x => x.Estado).HasMaxLength(20).HasDefaultValue(Periodo.Preparacion);
        b.Entity<Paralelo>().Property(x => x.Letra).HasMaxLength(2);
        b.Entity<Paralelo>().HasIndex(x => new { x.PeriodoId, x.CarreraId, x.Nivel, x.Letra }).IsUnique();
        b.Entity<Paralelo>().HasOne(x => x.Periodo).WithMany().HasForeignKey(x => x.PeriodoId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Paralelo>().HasOne(x => x.Carrera).WithMany().HasForeignKey(x => x.CarreraId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Paralelo>().HasOne(x => x.Malla).WithMany().HasForeignKey(x => x.MallaId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Curso>().HasIndex(x => new { x.MateriaId, x.ParaleloId }).IsUnique();
        b.Entity<Curso>().HasOne(x => x.Materia).WithMany().HasForeignKey(x => x.MateriaId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Curso>().HasOne(x => x.Paralelo).WithMany().HasForeignKey(x => x.ParaleloId).OnDelete(DeleteBehavior.Restrict);
    }
}
