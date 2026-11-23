using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Text;
using EduMonitor.Estudiantes.Data;
using EduMonitor.Estudiantes.Services;
using EduMonitor.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduMonitor.Estudiantes.Controllers;

public record OrigenMoodle(int Id, string Codigo, string Nombres, string Apellidos, string Correo);
public record IdentidadMoodle([Required] string Codigo, [Required, CedulaEcuatoriana, RegularExpression(@"^\d{10}$")] string Cedula,
    bool VincularExistente = false);
public record MatriculaMoodle([Required, MinLength(1), MaxLength(5000)] List<IdentidadMoodle> Personas);

[ApiController, Authorize(Roles = "Admin"), Route("api/estudiantes/moodle/{cursoId:int}")]
public class MatriculaMoodleController(EstudiantesDb db, EstudiantesService estudiantes, DependencyClient api) : ControllerBase
{
    private async Task<List<OrigenMoodle>> Origen(int cursoId, CancellationToken ct) =>
        await api.Get<List<OrigenMoodle>>("Evaluaciones", $"/api/moodle/cursos/{cursoId}/origen", ct);

    [HttpGet]
    public async Task<IActionResult> Preparar(int cursoId, CancellationToken ct)
    {
        var origen = await Origen(cursoId, ct);
        var ids = origen.Select(x => x.Id).ToList();
        var locales = await db.Students.Where(s => s.MoodleUsuarioId != null && ids.Contains(s.MoodleUsuarioId.Value))
            .ToDictionaryAsync(s => s.MoodleUsuarioId!.Value, ct);
        return Ok(origen.Select(p => new { p.Codigo, p.Nombres, p.Apellidos, Cedula = locales.GetValueOrDefault(p.Id)?.Cedula }));
    }

    [HttpPost]
    public async Task<IActionResult> Matricular(int cursoId, MatriculaMoodle input, CancellationToken ct)
    {
        var curso = await api.Get<CursoDestino>("Materias", $"/api/cursos/{cursoId}", ct);
        if (curso.PeriodoEstado == "Finalizado")
            throw new ApiException(409, "El período está finalizado.");
        var origen = await Origen(cursoId, ct);
        if (input.Personas.DistinctBy(p => p.Cedula).Count() != input.Personas.Count ||
           input.Personas.DistinctBy(p => p.Codigo).Count() != input.Personas.Count ||
           !origen.Select(p => p.Codigo).ToHashSet().SetEquals(input.Personas.Select(p => p.Codigo)))
            throw new ApiException(409, "Debe confirmar una cédula distinta para cada estudiante de la matrícula actual.");
        var personas = input.Personas.ToDictionary(p => p.Codigo);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var cambios = new List<OrigenMoodle>();
        int creados = 0, actualizados = 0;
        foreach (var p in origen)
        {
            var dato = personas[p.Codigo];
            var candidatos = await db.Students.Include(s => s.Inscripciones).Where(s => s.Cedula == dato.Cedula ||
                s.MoodleUsuarioId == p.Id || s.MoodleCodigo == p.Codigo || s.Correo == p.Correo).ToListAsync(ct);
            if (candidatos.Count > 1)
                throw new ApiException(409, $"Correspondencias ambiguas para {p.Codigo}.");
            var s = candidatos.SingleOrDefault();
            if (s != null)
            {
                if (s.Cedula != dato.Cedula || !s.Activo ||
                    (s.MoodleUsuarioId != null && (s.MoodleUsuarioId != p.Id || s.MoodleCodigo != p.Codigo)) ||
                    (s.MoodleUsuarioId == null && !dato.VincularExistente))
                    throw new ApiException(409, $"Verifica la correspondencia y la autorización para asociar {p.Codigo} a la ficha existente.");
                var ins = s.Inscripciones.SingleOrDefault(i => i.CursoId == cursoId);
                if (ins?.Activo == false)
                    throw new ApiException(409, $"{p.Codigo} tiene una matrícula retirada. Revisa su estado.");
                if (s.MoodleUsuarioId == p.Id && ins != null && s.Nombres == p.Nombres && s.Apellidos == p.Apellidos &&
                    s.Correo == p.Correo.ToLowerInvariant())
                    continue;
                actualizados++;
            }
            else
                creados++;
            cambios.Add(p);
        }
        if (cambios.Count > 0)
        {
            static string Celda(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";
            var filas = cambios.Select(p => string.Join(",", new[] { personas[p.Codigo].Cedula, p.Apellidos, p.Nombres, p.Correo }.Select(Celda)));
            var csv = "cedula,apellidos,nombres,correo\n" + string.Join("\n", filas);
            await estudiantes.Cargar(Encoding.UTF8.GetBytes(csv), new(curso.Carrera, curso.Nivel, curso.Paralelo, curso.MateriaCodigo), ct);
            foreach (var p in cambios)
            {
                var cedula = personas[p.Codigo].Cedula;
                var s = await db.Students.Include(s => s.Inscripciones).SingleAsync(s => s.Cedula == cedula, ct);
                if (!s.Inscripciones.Any(i => i.CursoId == cursoId && i.Activo))
                    throw new ApiException(409, "El destino de la matrícula no es el curso vinculado.");
                s.MoodleCodigo = p.Codigo;
                s.MoodleUsuarioId = p.Id;
            }
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        return Ok(new
        {
            creados,
            actualizados,
            omitidos = origen.Count - cambios.Count,
            conflictos = Array.Empty<string>()
        });
    }

    private record CursoDestino(string Carrera, int Nivel, string Paralelo, string MateriaCodigo, string PeriodoEstado);
}
