using System.Security.Claims;
using EduMonitor.Infrastructure;
using EduMonitor.Materias.Data;
using EduMonitor.Materias.DTOs;
using EduMonitor.Materias.Models;
using Microsoft.EntityFrameworkCore;
namespace EduMonitor.Materias.Services;

public sealed class MateriasService(MateriasDb db, DependencyClient dependencies)
{
    private IQueryable<Materia> Visible(ClaimsPrincipal user) => user.IsInRole("Admin") ? db.Materias : db.Materias.Where(x => x.TeacherId == user.UserId());
    public Task<List<Materia>> List(ClaimsPrincipal user, CancellationToken ct) => Visible(user).AsNoTracking().OrderBy(x => x.Name).ToListAsync(ct);
    public async Task<Materia> Get(int id, ClaimsPrincipal user, CancellationToken ct) => await Visible(user).AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new ApiException(404, "Materia no encontrada o no asignada a tu cuenta.");
    public async Task<Materia> Save(int? id, MateriaRequest input, CancellationToken ct)
    {
        var code = input.Code.ToUpperInvariant();
        if (await db.Materias.AnyAsync(x => x.Code == code && x.Id != id, ct))
            throw new ApiException(409, "El código de materia ya está registrado.");
        var materia = id.HasValue ? await db.Materias.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new ApiException(404, "Materia no encontrada.") : new Materia();
        if (input.TeacherId.HasValue)
            await dependencies.Get<TeacherReference>("Usuarios", $"/api/docentes/{input.TeacherId}", ct);
        materia.Code = code;
        materia.Name = input.Name.Trim();
        materia.AcademicPeriod = input.AcademicPeriod.Trim();
        materia.TeacherId = input.TeacherId;
        if (!id.HasValue)
            db.Materias.Add(materia);
        await db.SaveChangesAsync(ct);
        return materia;
    }
    public async Task<Materia> Assign(int id, int teacherId, CancellationToken ct)
    {
        var materia = await db.Materias.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new ApiException(404, "Materia no encontrada.");
        await dependencies.Get<TeacherReference>("Usuarios", $"/api/docentes/{teacherId}", ct);
        materia.TeacherId = teacherId;
        await db.SaveChangesAsync(ct);
        return materia;
    }
}
