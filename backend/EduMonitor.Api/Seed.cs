using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
namespace EduMonitor.Api;
public static class Seed
{
    public static async Task Initialize(AppDbContext db, IPasswordHasher<AppUser> hasher, IConfiguration config)
    {
        if (await db.Users.AnyAsync(x => x.Role == "Admin")) return;
        var password = config["Seed:AdminPassword"];
        if (string.IsNullOrWhiteSpace(password) || password.Length < 10) throw new InvalidOperationException("Define Seed__AdminPassword con al menos 10 caracteres.");
        var admin = new AppUser { Name="Administrador", Email=(config["Seed:AdminEmail"] ?? "admin@edumonitor.local").Trim().ToLowerInvariant(), Role="Admin" };
        admin.PasswordHash = hasher.HashPassword(admin, password);
        db.Users.Add(admin);
        await db.SaveChangesAsync();
    }
}
