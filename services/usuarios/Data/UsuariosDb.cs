using EduMonitor.Usuarios.Models;
using Microsoft.EntityFrameworkCore;
namespace EduMonitor.Usuarios.Data;

public class UsuariosDb(DbContextOptions<UsuariosDb> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<TeacherProfile> TeacherProfiles => Set<TeacherProfile>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>().Property(x => x.Name).HasMaxLength(200);
        b.Entity<User>().Property(x => x.Email).HasMaxLength(254);
        b.Entity<User>().Property(x => x.Role).HasMaxLength(20);
        b.Entity<User>().HasIndex(x => x.Email).IsUnique();
        // Las cuentas que ya existían quedan activas al agregar la columna.
        b.Entity<User>().Property(x => x.Activo).HasDefaultValue(true);
        b.Entity<User>().HasOne(x => x.Profile).WithOne(x => x.User).HasForeignKey<TeacherProfile>(x => x.UserId);
        b.Entity<TeacherProfile>().HasKey(x => x.UserId);
        b.Entity<TeacherProfile>().Property(x => x.Cedula).HasMaxLength(10);
        b.Entity<TeacherProfile>().HasIndex(x => x.Cedula).IsUnique();
        b.Entity<TeacherProfile>().Property(x => x.Nombres).HasMaxLength(100);
        b.Entity<TeacherProfile>().Property(x => x.Apellidos).HasMaxLength(100);
        b.Entity<TeacherProfile>().Property(x => x.Telefono).HasMaxLength(20);
        b.Entity<TeacherProfile>().Property(x => x.TituloAcademico).HasMaxLength(150);
        b.Entity<TeacherCarrera>().HasKey(x => new { x.UserId, x.CarreraCodigo });
        b.Entity<TeacherCarrera>().Property(x => x.CarreraCodigo).HasMaxLength(10);
        b.Entity<TeacherCarrera>().HasOne(x => x.Profile).WithMany(x => x.Carreras).HasForeignKey(x => x.UserId);
    }
}
