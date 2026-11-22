using EduMonitor.Evaluaciones.Data;
using EduMonitor.Evaluaciones.DTOs;
using EduMonitor.Evaluaciones.Models;
using EduMonitor.Infrastructure;
using Microsoft.EntityFrameworkCore;
namespace EduMonitor.Evaluaciones.Services;

// HU-07 (calificaciones y ponderaciones) y HU-08 (seguimiento del rendimiento).
public sealed class CalificacionesService(EvaluacionesDb db, AccesoCursos acceso)
{
    // ---------- Ponderaciones ----------
    private async Task<PonderacionView> LeerPonderacion(int cursoId, CancellationToken ct)
    {
        var p = await db.Ponderaciones.AsNoTracking().SingleOrDefaultAsync(x => x.CursoId == cursoId, ct);
        return p is null ? Calculo.Predeterminada : new PonderacionView(p.Tareas, p.Ape, p.Examen, p.NotaMinima, true);
    }
    public async Task<PonderacionView> Ponderacion(int cursoId, CancellationToken ct)
    {
        await acceso.Curso(cursoId, ct);
        return await LeerPonderacion(cursoId, ct);
    }
    public async Task<PonderacionView> GuardarPonderacion(int cursoId, PonderacionRequest input, CancellationToken ct)
    {
        await acceso.Curso(cursoId, ct);
        if (input.Tareas + input.Ape + input.Examen != 100m)
            throw new ApiException(400, $"Los porcentajes de tareas, APE y exámenes deben sumar 100 (suman {input.Tareas + input.Ape + input.Examen}).");
        var p = await db.Ponderaciones.SingleOrDefaultAsync(x => x.CursoId == cursoId, ct);
        if (p is null)
            db.Ponderaciones.Add(p = new Ponderacion { CursoId = cursoId });
        (p.Tareas, p.Ape, p.Examen, p.NotaMinima) = (input.Tareas, input.Ape, input.Examen, input.NotaMinima);
        await db.SaveChangesAsync(ct);
        return new PonderacionView(p.Tareas, p.Ape, p.Examen, p.NotaMinima, true);
    }

    // ---------- Calificaciones ----------
    private async Task<(List<Actividad> Actividades, Dictionary<int, Dictionary<int, decimal>> Notas)> Cargar(int cursoId, CancellationToken ct)
    {
        var actividades = await db.Actividades.AsNoTracking().Include(a => a.Temas).Where(a => a.CursoId == cursoId)
            .OrderBy(a => a.FechaEntrega == null).ThenBy(a => a.FechaEntrega).ThenBy(a => a.Id).ToListAsync(ct);
        var ids = actividades.Select(a => a.Id).ToList();
        var filas = await db.Calificaciones.AsNoTracking().Where(c => ids.Contains(c.ActividadId)).Select(c => new { c.EstudianteId, c.ActividadId, c.Nota }).ToListAsync(ct);
        // estudiante -> (actividad -> nota)
        var notas = filas.GroupBy(f => f.EstudianteId).ToDictionary(g => g.Key, g => g.ToDictionary(f => f.ActividadId, f => f.Nota));
        return (actividades, notas);
    }
    private static readonly Dictionary<int, decimal> SinNotas = [];

    // Devuelve el grupo del curso con sus notas y el resultado ponderado. Solo aparecen los estudiantes del paralelo del curso.
    public async Task<MatrizView> Matriz(int cursoId, CancellationToken ct)
    {
        await acceso.Curso(cursoId, ct);
        var roster = await acceso.Estudiantes(cursoId, ct);
        var pon = await LeerPonderacion(cursoId, ct);
        var (actividades, notas) = await Cargar(cursoId, ct);
        var refs = actividades.Select(a => (a.Id, a.Tipo)).ToList();
        var filas = roster.Select(e =>
        {
            var propias = notas.GetValueOrDefault(e.Id) ?? SinNotas;
            var r = Calculo.Calcular(refs, propias, pon, true);
            return new FilaCalificaciones(e.Id, e.Apellidos, e.Nombres, propias.Where(n => actividades.Any(a => a.Id == n.Key)).ToDictionary(n => n.Key, n => n.Value), r.Valor, Calculo.Estado(r.Valor, pon.NotaMinima), r.Definitivo, r.Pendientes);
        }).ToList();
        return new MatrizView(cursoId, pon, actividades.Select(a => new ActividadColumna(a.Id, a.Nombre, a.Tipo, a.FechaEntrega)).ToList(), filas);
    }

    // Guarda varias notas de una actividad de forma atómica: si una sola es inválida no se guarda ninguna.
    public async Task<GuardarResultado> Guardar(int actividadId, GuardarNotas input, CancellationToken ct)
    {
        var actividad = await db.Actividades.SingleOrDefaultAsync(a => a.Id == actividadId, ct) ?? throw new ApiException(404, "La actividad no existe o no está autorizada.");
        await acceso.Curso(actividad.CursoId, ct);
        var roster = (await acceso.Estudiantes(actividad.CursoId, ct)).Select(e => e.Id).ToHashSet();
        if (input.Notas.Select(n => n.EstudianteId).Distinct().Count() != input.Notas.Count)
            throw new ApiException(400, "Hay estudiantes repetidos en la solicitud.");
        foreach (var n in input.Notas)
        {
            if (!roster.Contains(n.EstudianteId))
                throw new ApiException(400, "Algún estudiante no pertenece al paralelo de este curso.");
            if (n.Nota is { } v && (v < 0 || v > Calculo.NotaMaxima))
                throw new ApiException(400, $"La calificación debe estar entre 0 y {Calculo.NotaMaxima:0}.");
        }
        var ids = input.Notas.Select(n => n.EstudianteId).ToList();
        var actuales = await db.Calificaciones.Where(c => c.ActividadId == actividadId && ids.Contains(c.EstudianteId)).ToDictionaryAsync(c => c.EstudianteId, ct);
        var ahora = DateTime.UtcNow;
        foreach (var n in input.Notas)
        {
            actuales.TryGetValue(n.EstudianteId, out var fila);
            if (n.Nota is null)
            {
                if (fila is not null)
                    db.Calificaciones.Remove(fila);
                continue;
            }
            var valor = Calculo.Redondear(n.Nota.Value);
            if (fila is null)
                db.Calificaciones.Add(new Calificacion { ActividadId = actividadId, EstudianteId = n.EstudianteId, Nota = valor, Actualizada = ahora });
            else if (fila.Nota != valor)
                (fila.Nota, fila.Actualizada) = (valor, ahora);
        }
        await db.SaveChangesAsync(ct);
        var guardadas = await db.Calificaciones.CountAsync(c => c.ActividadId == actividadId && roster.Contains(c.EstudianteId), ct);
        return new GuardarResultado(guardadas, roster.Count - guardadas);
    }

    // ---------- Rendimiento (HU-08) ----------
    private async Task<List<Tema>> TemasDelCurso(int cursoId, CancellationToken ct) =>
        await db.Temas.AsNoTracking().Include(t => t.Unidad).Where(t => t.Unidad.CursoId == cursoId).OrderBy(t => t.Unidad.Orden).ThenBy(t => t.Orden).ToListAsync(ct);

    public async Task<RendimientoView> Rendimiento(int cursoId, int? unidadId, int? temaId, CancellationToken ct)
    {
        await acceso.Curso(cursoId, ct);
        var roster = await acceso.Estudiantes(cursoId, ct);
        var pon = await LeerPonderacion(cursoId, ct);
        var (todas, notas) = await Cargar(cursoId, ct);
        var temas = await TemasDelCurso(cursoId, ct);
        if (unidadId.HasValue && !await db.Unidades.AnyAsync(u => u.Id == unidadId && u.CursoId == cursoId, ct))
            throw new ApiException(400, "La unidad no pertenece a este curso.");
        if (temaId.HasValue && temas.All(t => t.Id != temaId))
            throw new ApiException(400, "El tema no pertenece a este curso.");
        // Los filtros de unidad y tema limitan las actividades consideradas; el resultado se recalcula solo con ellas.
        var temasFiltrados = temas.Where(t => (!unidadId.HasValue || t.UnidadId == unidadId) && (!temaId.HasValue || t.Id == temaId)).ToList();
        var filtrado = unidadId.HasValue || temaId.HasValue;
        var ids = temasFiltrados.Select(t => t.Id).ToHashSet();
        var actividades = filtrado ? todas.Where(a => a.Temas.Any(x => ids.Contains(x.TemaId))).ToList() : todas;
        var refs = actividades.Select(a => (a.Id, a.Tipo)).ToList();

        var filas = roster.Select(e =>
        {
            var propias = notas.GetValueOrDefault(e.Id) ?? SinNotas;
            var r = Calculo.Calcular(refs, propias, pon, !filtrado);
            var desglose = Calculo.Desglose(refs, propias, pon);
            return new EstudianteRendimiento(e.Id, e.Apellidos, e.Nombres, r.Valor, Calculo.Estado(r.Valor, pon.NotaMinima), r.Definitivo, r.Calificadas, r.Pendientes, r.NotaMasBaja, desglose, desglose.Sum(x => x.Puntos));
        }).ToList();

        var conResultado = filas.Where(f => f.Resultado is not null).ToList();
        var enRiesgo = conResultado.Count(f => f.Estado == Calculo.EnRiesgo);
        var todasLasNotas = roster.SelectMany(e => refs.Where(a => (notas.GetValueOrDefault(e.Id) ?? SinNotas).ContainsKey(a.Id)).Select(a => notas[e.Id][a.Id])).ToList();
        var pendientes = filas.Sum(f => f.Pendientes);
        var resumen = new ResumenRendimiento(roster.Count, conResultado.Count,
            conResultado.Count > 0 ? Calculo.Redondear(conResultado.Average(f => f.Resultado!.Value)) : null,
            enRiesgo,
            conResultado.Count > 0 ? Math.Round(100m * enRiesgo / conResultado.Count, 1, MidpointRounding.AwayFromZero) : null,
            todasLasNotas.Count > 0 ? todasLasNotas.Min() : null,
            conResultado.Count > 0 ? conResultado.Min(f => f.Resultado) : null,
            actividades.Count, pendientes, actividades.Count > 0 && roster.Count > 0 && pendientes == 0, pon.NotaMinima);

        var filasTema = temasFiltrados.Select(t =>
        {
            var deTema = todas.Where(a => a.Temas.Any(x => x.TemaId == t.Id)).Select(a => a.Id).ToList();
            var valores = roster.SelectMany(e => deTema.Where(a => (notas.GetValueOrDefault(e.Id) ?? SinNotas).ContainsKey(a)).Select(a => notas[e.Id][a])).ToList();
            return new TemaRendimiento(t.Id, t.Nombre, t.UnidadId, t.Unidad.Nombre, deTema.Count,
                valores.Count > 0 ? Calculo.Redondear(valores.Average()) : null, valores.Count, deTema.Count * roster.Count - valores.Count, valores.Count(v => v < pon.NotaMinima));
        }).ToList();
        return new RendimientoView(cursoId, resumen, filas.OrderBy(f => f.Apellidos).ThenBy(f => f.Nombres).ToList(), filasTema);
    }

    public async Task<EstudianteDetalle> Estudiante(int cursoId, int estudianteId, CancellationToken ct)
    {
        await acceso.Curso(cursoId, ct);
        var roster = await acceso.Estudiantes(cursoId, ct);
        var estudiante = roster.SingleOrDefault(e => e.Id == estudianteId) ?? throw new ApiException(404, "El estudiante no pertenece a este paralelo.");
        var pon = await LeerPonderacion(cursoId, ct);
        var (actividades, notas) = await Cargar(cursoId, ct);
        var temas = await TemasDelCurso(cursoId, ct);
        var propias = notas.GetValueOrDefault(estudianteId) ?? SinNotas;
        var r = Calculo.Calcular(actividades.Select(a => (a.Id, a.Tipo)).ToList(), propias, pon, true);
        var nombreTema = temas.ToDictionary(t => t.Id, t => t.Nombre);
        var detalle = actividades.Select(a => new NotaDetalle(a.Id, a.Nombre, a.Tipo, propias.TryGetValue(a.Id, out var n) ? n : null, a.Temas.Select(x => nombreTema.GetValueOrDefault(x.TemaId, "")).ToList())).ToList();
        var porTema = temas.Select(t =>
        {
            var deTema = actividades.Where(a => a.Temas.Any(x => x.TemaId == t.Id)).Select(a => a.Id).ToList();
            var valores = deTema.Where(propias.ContainsKey).Select(a => propias[a]).ToList();
            return new TemaPromedio(t.Id, t.Nombre, valores.Count > 0 ? Calculo.Redondear(valores.Average()) : null, valores.Count, deTema.Count - valores.Count);
        }).Where(t => t.Calificadas + t.Pendientes > 0).ToList();
        var desglose = Calculo.Desglose(actividades.Select(a => (a.Id, a.Tipo)).ToList(), propias, pon);
        return new EstudianteDetalle(estudiante.Id, estudiante.Apellidos, estudiante.Nombres, estudiante.Correo, r.Valor, Calculo.Estado(r.Valor, pon.NotaMinima), r.Definitivo, r.Pendientes, detalle, porTema, pon.NotaMinima, desglose, desglose.Sum(x => x.Puntos));
    }
}
