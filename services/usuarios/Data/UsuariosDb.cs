using EduMonitor.Usuarios.Models;
using Microsoft.EntityFrameworkCore;
namespace EduMonitor.Usuarios.Data;

public class UsuariosDb(DbContextOptions<UsuariosDb> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>().Property(x => x.Name).HasMaxLength(120);
        b.Entity<User>().Property(x => x.Email).HasMaxLength(254);
        b.Entity<User>().Property(x => x.Role).HasMaxLength(20);
        b.Entity<User>().HasIndex(x => x.Email).IsUnique();
    }
}
