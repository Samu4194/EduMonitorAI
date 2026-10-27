using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EduMonitor.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace EduMonitor.Tests;

// Cada prueba dispone de su base relacional y del servidor HTTP real en memoria.
public class TestApp : WebApplicationFactory<Program>
{
    private readonly string databaseName = "EduMonitorTests_" + Guid.NewGuid().ToString("N");
    private bool cleaned;
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Jwt:Key", "integration-test-key-32-bytes-minimum-only");
        builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string,string?> { ["Jwt:Key"]="integration-test-key-32-bytes-minimum-only" }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<AppDbContext>();
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            var connection = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("EDUMONITOR_TEST_CONNECTION") ?? "Server=(localdb)\\MSSQLLocalDB;Trusted_Connection=True;TrustServerCertificate=True") { InitialCatalog=databaseName };
            services.AddDbContext<AppDbContext>(o=>o.UseSqlServer(connection.ConnectionString));
        });
    }
    public async Task<HttpClient> Client()
    {
        var client=CreateClient();
        using var scope=Services.CreateScope();
        var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
        var hasher=scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>();
        var admin=new AppUser { Name="Admin",Email="admin@test.local",Role="Admin" };
        admin.PasswordHash=hasher.HashPassword(admin,"AdminTest123!");
        db.Users.Add(admin);await db.SaveChangesAsync();return client;
    }
    protected override void Dispose(bool disposing)
    {
        if(disposing && !cleaned) { cleaned=true; using var scope=Services.CreateScope();var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();if(db.Database.GetDbConnection().Database==databaseName) db.Database.EnsureDeleted(); }
        base.Dispose(disposing);
    }
}

public class ApiTests
{
    private static async Task Login(HttpClient c,string email="admin@test.local",string password="AdminTest123!")
    {
        var response=await c.PostAsJsonAsync("/api/auth/login",new {email,password});
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var json=await response.Content.ReadFromJsonAsync<JsonElement>();
        c.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",json.GetProperty("token").GetString());
    }
    private static async Task<int> Create(HttpClient c,string path,object body)
    {
        var response=await c.PostAsJsonAsync(path,body);Assert.Equal(HttpStatusCode.Created,response.StatusCode);
        var json=await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("id").GetInt32();
    }
    private static object Teacher(string email="teacher@test.local") => new {name="Docente Uno",email,password="TeacherTest123!",specialty="Matemáticas"};
    [Fact]
    public async Task Authentication_RejectsAnonymousBadPasswordAndTamperedToken()
    {
        using var app=new TestApp();using var c=await app.Client();
        Assert.Equal(HttpStatusCode.Unauthorized,(await c.GetAsync("/api/courses")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,(await c.PostAsJsonAsync("/api/auth/login",new {email="admin@test.local",password="incorrecta"})).StatusCode);
        c.DefaultRequestHeaders.Authorization=new("Bearer","invalid.token.value");
        Assert.Equal(HttpStatusCode.Unauthorized,(await c.GetAsync("/api/courses")).StatusCode);
        await Login(c);Assert.Equal(HttpStatusCode.OK,(await c.GetAsync("/api/auth/me")).StatusCode);
    }
    [Fact]
    public async Task CompleteAdminFlow_CrudEnrollmentReassignmentAndDelete()
    {
        using var app=new TestApp();using var c=await app.Client();await Login(c);
        var teacher=await Create(c,"/api/teachers",Teacher());
        var second=await Create(c,"/api/teachers",Teacher("second@test.local"));
        var course=await Create(c,"/api/courses",new {name="Álgebra",code="MAT-01",description="Primer curso",teacherId=teacher});
        var student=await Create(c,"/api/students",new {name="Ana Pérez",email="ana@test.local",studentCode="EST-01"});
        Assert.Equal(HttpStatusCode.NoContent,(await c.PutAsJsonAsync($"/api/teachers/{teacher}",new {name="Docente Editado",email="teacher@test.local",password=(string?)null,specialty="Álgebra"})).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,(await c.PutAsJsonAsync($"/api/students/{student}",new {name="Ana Editada",email="ana@test.local",studentCode="EST-01"})).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,(await c.PostAsJsonAsync($"/api/courses/{course}/students",new {studentId=student})).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await c.PostAsJsonAsync($"/api/courses/{course}/students",new {studentId=student})).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await c.DeleteAsync($"/api/teachers/{teacher}")).StatusCode);
        var roster=await c.GetFromJsonAsync<JsonElement>($"/api/courses/{course}/students");Assert.Equal("Ana Editada",roster[0].GetProperty("name").GetString());
        Assert.Equal(HttpStatusCode.NoContent,(await c.PutAsJsonAsync($"/api/courses/{course}",new {name="Álgebra II",code="MAT-01",description="Actualizado",teacherId=second})).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,(await c.DeleteAsync($"/api/teachers/{teacher}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,(await c.DeleteAsync($"/api/courses/{course}/students/{student}")).StatusCode);
        Assert.Empty(await c.GetFromJsonAsync<Student[]>($"/api/courses/{course}/students") ?? []);
        Assert.Equal(HttpStatusCode.NoContent,(await c.PostAsJsonAsync($"/api/courses/{course}/students",new {studentId=student})).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,(await c.DeleteAsync($"/api/students/{student}")).StatusCode);
        Assert.Empty(await c.GetFromJsonAsync<Student[]>($"/api/courses/{course}/students") ?? []);
        Assert.Equal(HttpStatusCode.NoContent,(await c.DeleteAsync($"/api/courses/{course}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await c.GetAsync($"/api/courses/{course}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,(await c.DeleteAsync($"/api/teachers/{second}")).StatusCode);
    }
    [Fact]
    public async Task Teacher_IsRestrictedToOwnCoursesAndCannotMutate()
    {
        using var app=new TestApp();using var c=await app.Client();await Login(c);
        var first=await Create(c,"/api/teachers",Teacher());var second=await Create(c,"/api/teachers",Teacher("second@test.local"));
        var own=await Create(c,"/api/courses",new {name="Propio",code="OWN",description="",teacherId=first});
        var other=await Create(c,"/api/courses",new {name="Ajeno",code="OTHER",description="",teacherId=second});
        await Login(c,"teacher@test.local","TeacherTest123!");
        var courses=await c.GetFromJsonAsync<JsonElement>("/api/courses");Assert.Equal(1,courses.GetArrayLength());Assert.Equal(own,courses[0].GetProperty("id").GetInt32());
        Assert.Equal(HttpStatusCode.OK,(await c.GetAsync($"/api/courses/{own}/students")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await c.GetAsync($"/api/courses/{other}/students")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await c.GetAsync($"/api/courses/{other}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await c.GetAsync("/api/teachers")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await c.GetAsync("/api/students")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await c.PostAsJsonAsync("/api/teachers",Teacher())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await c.DeleteAsync($"/api/courses/{own}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await c.PostAsJsonAsync($"/api/courses/{own}/students",new {studentId=1})).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await c.PostAsJsonAsync("/api/students",new {name="Prohibido",email="no@test.local",studentCode="NO"})).StatusCode);
    }
    [Fact]
    public async Task ValidationAndDuplicates_ReturnUsefulStatusCodes()
    {
        using var app=new TestApp();using var c=await app.Client();await Login(c);
        await Create(c,"/api/teachers",Teacher());
        Assert.Equal(HttpStatusCode.Conflict,(await c.PostAsJsonAsync("/api/teachers",Teacher("TEACHER@test.local"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await c.PostAsJsonAsync("/api/teachers",new {name="  ",email="invalid",password="short",specialty=""})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await c.PostAsJsonAsync("/api/courses",new {name="Curso",code="OK",description="",teacherId=999})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await c.PostAsJsonAsync("/api/teachers",new {name="Docente",email="new@test.local",password=(string?)null,specialty="Física"})).StatusCode);
        await Create(c,"/api/students",new {name="Estudiante",email="a@test.local",studentCode="ST01"});
        Assert.Equal(HttpStatusCode.Conflict,(await c.PostAsJsonAsync("/api/students",new {name="Otro",email="b@test.local",studentCode="st01"})).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await c.DeleteAsync("/api/students/999")).StatusCode);
    }
    [Fact]
    public async Task LogoutAndPasswordChange_RevokeTokens()
    {
        using var app=new TestApp();using var admin=await app.Client();await Login(admin);
        var id=await Create(admin,"/api/teachers",Teacher());using var teacher=app.CreateClient();await Login(teacher,"teacher@test.local","TeacherTest123!");
        Assert.Equal(HttpStatusCode.NoContent,(await admin.PutAsJsonAsync($"/api/teachers/{id}",new {name="Docente",email="teacher@test.local",password="NewPassword123!",specialty="Física"})).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,(await teacher.GetAsync("/api/courses")).StatusCode);
        await Login(teacher,"teacher@test.local","NewPassword123!");
        Assert.Equal(HttpStatusCode.NoContent,(await teacher.PostAsync("/api/auth/logout",null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,(await teacher.GetAsync("/api/courses")).StatusCode);
        await Login(teacher,"teacher@test.local","NewPassword123!");
        Assert.Equal(HttpStatusCode.NoContent,(await admin.DeleteAsync($"/api/teachers/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,(await teacher.GetAsync("/api/courses")).StatusCode);
    }
}
