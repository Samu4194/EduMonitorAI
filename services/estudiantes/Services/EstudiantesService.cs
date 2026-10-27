using EduMonitor.Infrastructure;
using EduMonitor.Estudiantes.Data;
using EduMonitor.Estudiantes.DTOs;
using EduMonitor.Estudiantes.Models;
using Microsoft.EntityFrameworkCore;
namespace EduMonitor.Estudiantes.Services;

public sealed class EstudiantesService(EstudiantesDb db, DependencyClient dependencies)
{
    public Task<List<Student>> List(CancellationToken ct) => db.Students.AsNoTracking().OrderBy(x => x.LastNames).ThenBy(x => x.FirstNames).ToListAsync(ct);
    public async Task<Student> Get(int id, CancellationToken ct) => await db.Students.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new ApiException(404, "Estudiante no encontrado.");
    public async Task<Student> Save(int? id, StudentRequest input, CancellationToken ct)
    {
        var code = input.StudentCode.ToUpperInvariant();
        if (await db.Students.AnyAsync(x => x.StudentCode == code && x.Id != id, ct))
            throw new ApiException(409, "El código de estudiante ya está registrado.");
        var student = id.HasValue ? await db.Students.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new ApiException(404, "Estudiante no encontrado.") : new Student();
        student.StudentCode = code;
        student.FirstNames = input.FirstNames.Trim();
        student.LastNames = input.LastNames.Trim();
        if (!id.HasValue)
            db.Students.Add(student);
        await db.SaveChangesAsync(ct);
        return student;
    }
    public async Task Enroll(EnrollmentRequest input, CancellationToken ct)
    {
        // Si Materias no responde, no se guarda una relación sin verificar.
        await dependencies.Get<MateriaReference>("Materias", $"/api/materias/{input.MateriaId}", ct);
        if (!await db.Students.AnyAsync(x => x.Id == input.StudentId, ct))
            throw new ApiException(404, "Estudiante no encontrado.");
        if (await db.Enrollments.AnyAsync(x => x.MateriaId == input.MateriaId && x.StudentId == input.StudentId, ct))
            throw new ApiException(409, "El estudiante ya está matriculado en esta materia.");
        db.Enrollments.Add(new Enrollment { MateriaId = input.MateriaId, StudentId = input.StudentId });
        await db.SaveChangesAsync(ct);
    }
    public async Task<List<EnrolledStudent>> Roster(int materiaId, CancellationToken ct)
    {
        // Se reenvía el JWT del usuario: Materias comprueba la asignación actual del docente.
        await dependencies.Get<MateriaReference>("Materias", $"/api/materias/{materiaId}", ct);
        return await db.Enrollments.Where(x => x.MateriaId == materiaId).OrderBy(x => x.Student.LastNames).Select(x => new EnrolledStudent(x.StudentId, x.Student.StudentCode, x.Student.FirstNames, x.Student.LastNames, x.EnrolledAt)).ToListAsync(ct);
    }
}
