using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace EduMonitor.Api;

public class AppUser
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = "Teacher";
    public string Specialty { get; set; } = "";
    public int TokenVersion { get; set; }
}
public class Course
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Code { get; set; } = "";
    public string Description { get; set; } = "";
    public int TeacherId { get; set; }
    public AppUser Teacher { get; set; } = null!;
    public ICollection<Enrollment> Enrollments { get; set; } = [];
}
public class Student
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string StudentCode { get; set; } = "";
}
public class Enrollment
{
    public int CourseId { get; set; }
    public Course Course { get; set; } = null!;
    public int StudentId { get; set; }
    public Student Student { get; set; } = null!;
    public DateTime EnrolledAt { get; set; } = DateTime.UtcNow;
}
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Course> Courses => Set<Course>();
    public DbSet<Student> Students => Set<Student>();
    public DbSet<Enrollment> Enrollments => Set<Enrollment>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<AppUser>().HasIndex(x => x.Email).IsUnique();
        b.Entity<AppUser>().Property(x => x.Email).HasMaxLength(254);
        b.Entity<AppUser>().Property(x => x.Name).HasMaxLength(120);
        b.Entity<AppUser>().Property(x => x.Specialty).HasMaxLength(120);
        b.Entity<AppUser>().Property(x => x.Role).HasMaxLength(20);
        b.Entity<Course>().HasIndex(x => x.Code).IsUnique();
        b.Entity<Course>().Property(x => x.Code).HasMaxLength(30);
        b.Entity<Course>().Property(x => x.Name).HasMaxLength(120);
        b.Entity<Course>().Property(x => x.Description).HasMaxLength(1000);
        b.Entity<Course>().HasOne(x => x.Teacher).WithMany().HasForeignKey(x => x.TeacherId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Student>().HasIndex(x => x.Email).IsUnique();
        b.Entity<Student>().HasIndex(x => x.StudentCode).IsUnique();
        b.Entity<Student>().Property(x => x.Email).HasMaxLength(254);
        b.Entity<Student>().Property(x => x.StudentCode).HasMaxLength(30);
        b.Entity<Student>().Property(x => x.Name).HasMaxLength(120);
        b.Entity<Enrollment>().HasKey(x => new { x.CourseId, x.StudentId });
        b.Entity<Enrollment>().HasOne(x => x.Course).WithMany(x => x.Enrollments).HasForeignKey(x => x.CourseId);
        b.Entity<Enrollment>().HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId);
    }
}
public record LoginRequest([Required, EmailAddress] string Email, [Required] string Password);
public record TeacherRequest(
    [Required, StringLength(120, MinimumLength=2)] string Name,
    [Required, EmailAddress, StringLength(254)] string Email,
    [StringLength(128, MinimumLength=10)] string? Password,
    [Required, StringLength(120)] string Specialty);
public record CourseRequest(
    [Required, StringLength(120, MinimumLength=2)] string Name,
    [Required, RegularExpression(@"^[A-Za-z0-9-]{2,30}$")] string Code,
    [StringLength(1000)] string Description,
    [Range(1, int.MaxValue)] int TeacherId);
public record StudentRequest(
    [Required, StringLength(120, MinimumLength=2)] string Name,
    [Required, EmailAddress, StringLength(254)] string Email,
    [Required, RegularExpression(@"^[A-Za-z0-9-]{2,30}$")] string StudentCode);
public record EnrollmentRequest([Range(1,int.MaxValue)] int StudentId);
