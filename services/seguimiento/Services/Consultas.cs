using System.Security.Claims;
using System.Text;
using System.Text.Json;
using EduMonitor.Seguimiento.DTOs;
using EduMonitor.Seguimiento.Data;
using EduMonitor.Seguimiento.Models;
using Microsoft.EntityFrameworkCore;
namespace EduMonitor.Seguimiento.Services;

// Lecturas del módulo: panel del curso (HU-11/12/13) y reporte de seguimiento (HU-14). No cambian nada.
public sealed class Consultas(AccesoCursos acceso, SeguimientoDb db, IHttpContextAccessor http)
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    static List<string> Lista(string json) => JsonSerializer.Deserialize<List<string>>(json, Json) ?? [];

    public static ComunicacionView Ver(Comunicacion c, EstudianteAnalisis e) =>
        new(c.Id, c.RecomendacionId, c.Canal, c.Destinatario, c.Asunto, c.Cuerpo, c.Estado, c.Error, c.CreadoEn, $"{e.Apellidos} {e.Nombres}");

    // Último análisis del curso con todo lo que el docente necesita para decidir.
    async Task<(Analisis? Analisis, List<HallazgoView> Curso, List<EstudianteSeguimiento> Estudiantes)> Cargar(int cursoId, CancellationToken ct)
    {
        var analisis = await db.Analisis.Where(a => a.CursoId == cursoId).OrderByDescending(a => a.CreadoEn).ThenByDescending(a => a.Id).FirstOrDefaultAsync(ct);
        if (analisis is null)
            return (null, [], []);
        var estudiantes = await db.EstudiantesAnalisis.Where(e => e.AnalisisId == analisis.Id).ToListAsync(ct);
        var hallazgos = await db.Hallazgos.Where(h => h.AnalisisId == analisis.Id).OrderBy(h => h.Id).ToListAsync(ct);
        var recs = await db.Recomendaciones.Where(r => r.AnalisisId == analisis.Id).ToListAsync(ct);
        var ids = recs.Select(r => r.Id).ToList();
        var comunicaciones = await db.Comunicaciones.Where(c => ids.Contains(c.RecomendacionId) && c.Estado != EstadoComunicacion.Error)
            .OrderByDescending(c => c.Id).ToListAsync(ct);
        var porNivel = new Dictionary<string, int> { ["Alto"] = 3, ["Medio"] = 2, ["Bajo"] = 1 };
        HallazgoView V(Hallazgo h) => new(h.Id, h.Codigo, h.Nivel, h.Titulo, h.Detalle);
        var filas = estudiantes.Select(e =>
        {
            var rec = recs.SingleOrDefault(r => r.EstudianteAnalisisId == e.Id);
            ComunicacionView? com = null;
            if (rec is not null && comunicaciones.FirstOrDefault(c => c.RecomendacionId == rec.Id) is { } c)
                com = Ver(c, e);
            return new EstudianteSeguimiento(e.Id, e.EstudianteId, e.Apellidos, e.Nombres, e.Correo, e.Resultado, e.Estado, e.Pendientes, e.Nivel,
                hallazgos.Where(h => h.EstudianteAnalisisId == e.Id).OrderByDescending(h => porNivel.GetValueOrDefault(h.Nivel)).Select(V).ToList(),
                rec is null ? null : new RecomendacionView(rec.Id, rec.Estado, rec.Origen, rec.Modelo, rec.Resumen, Lista(rec.CausasJson), Lista(rec.AccionesDocenteJson),
                    Lista(rec.AccionesEstudianteJson), rec.MensajeEstudiante, rec.Editada, rec.Version, rec.CreadoEn, rec.ActualizadoEn, rec.RevisadaEn, rec.MotivoRechazo, com, rec.RutaJson is null ? null : JsonSerializer.Deserialize<JsonElement>(rec.RutaJson, Json)));
        }).OrderByDescending(e => porNivel.GetValueOrDefault(e.Nivel)).ThenBy(e => e.Resultado ?? 11m).ThenBy(e => e.Apellidos).ToList();
        return (analisis, hallazgos.Where(h => h.EstudianteAnalisisId is null).OrderByDescending(h => porNivel.GetValueOrDefault(h.Nivel)).Select(V).ToList(), filas);
    }

    public async Task<PanelView> Panel(int cursoId, CancellationToken ct)
    {
        var curso = await acceso.Curso(cursoId, escritura: false, ct);
        var (analisis, hallazgosCurso, estudiantes) = await Cargar(cursoId, ct);
        return new PanelView(cursoId, acceso.SoloLectura(curso), analisis is null ? null : AnalisisService.Resumen(analisis, estudiantes.Select(e => e.Nivel).ToList()), hallazgosCurso, estudiantes);
    }

    public async Task<List<ComunicacionView>> Comunicaciones(int cursoId, CancellationToken ct)
    {
        await acceso.Curso(cursoId, escritura: false, ct);
        var lista = await db.Comunicaciones.Where(c => c.CursoId == cursoId).OrderByDescending(c => c.Id).Take(200).ToListAsync(ct);
        var ids = lista.Select(c => c.RecomendacionId).Distinct().ToList();
        var nombres = await db.Recomendaciones.Where(r => ids.Contains(r.Id)).Select(r => new { r.Id, r.EstudianteAnalisis.Apellidos, r.EstudianteAnalisis.Nombres }).ToDictionaryAsync(r => r.Id, ct);
        return lista.Select(c => new ComunicacionView(c.Id, c.RecomendacionId, c.Canal, c.Destinatario, c.Asunto, c.Cuerpo, c.Estado, c.Error, c.CreadoEn,
            nombres.TryGetValue(c.RecomendacionId, out var n) ? $"{n.Apellidos} {n.Nombres}" : "")).ToList();
    }

    // HU-14. Reporte del último análisis: panorama del curso, estudiantes en alerta y estado de cada recomendación.
    public async Task<ReporteView> Reporte(int cursoId, CancellationToken ct)
    {
        var curso = await acceso.Curso(cursoId, escritura: false, ct);
        var (analisis, hallazgosCurso, estudiantes) = await Cargar(cursoId, ct);
        if (analisis is null)
            throw new EduMonitor.Infrastructure.ApiException(404, "Todavía no hay un análisis de este curso. Genera uno desde «Alertas y recomendaciones».");
        var recs = estudiantes.Where(e => e.Recomendacion is not null).Select(e => e.Recomendacion!.Estado).ToList();
        var conteo = new ConteoRecomendaciones(recs.Count(x => x == EstadoRecomendacion.Borrador), recs.Count(x => x == EstadoRecomendacion.Aprobada),
            recs.Count(x => x == EstadoRecomendacion.Comunicada), recs.Count(x => x == EstadoRecomendacion.Rechazada), recs.Count(x => x == EstadoRecomendacion.Generando));
        var docente = http.HttpContext!.User.FindFirstValue(ClaimTypes.Name) ?? "";
        return new ReporteView(cursoId, analisis.Materia, analisis.Paralelo, analisis.Periodo, docente, DateTime.UtcNow,
            AnalisisService.Resumen(analisis, estudiantes.Select(e => e.Nivel).ToList()), conteo, hallazgosCurso, estudiantes);
    }

    // CSV de una fila por estudiante, listo para Excel (UTF-8 con BOM, separador «;» como pide Excel en configuración regional hispana).
    public static byte[] Csv(ReporteView r)
    {
        const string Fin = "\r\n";
        // Excel interpreta como fórmula una celda que empieza con = + - @: se antepone un apóstrofo para que se lea como texto.
        static string C(string? t)
        {
            var s = (t ?? "").Replace("\r", " ").Replace("\n", " ");
            if (s.Length > 0 && "=+-@\t".Contains(s[0]))
                s = "'" + s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
        var sb = new StringBuilder();
        sb.Append(string.Join(';', new[] { "Apellidos", "Nombres", "Correo", "Resultado", "Estado académico", "Pendientes", "Nivel de alerta", "Hallazgos", "Recomendación", "Origen", "Acciones para el docente", "Acciones para el estudiante", "Comunicación" }.Select(C))).Append(Fin);
        foreach (var e in r.Estudiantes)
        {
            var rec = e.Recomendacion;
            sb.Append(string.Join(';', new[]
            {
                e.Apellidos, e.Nombres, e.Correo, e.Resultado is { } v ? v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) : "", e.Estado, e.Pendientes.ToString(), e.Nivel,
                string.Join(" | ", e.Hallazgos.Select(h => h.Titulo)),
                rec?.Estado ?? "", rec?.Origen ?? "", string.Join(" | ", rec?.AccionesDocente ?? []), string.Join(" | ", rec?.AccionesEstudiante ?? []),
                rec?.Comunicacion is { } c ? $"{c.Estado} · {c.CreadoEn:yyyy-MM-dd HH:mm} UTC" : ""
            }.Select(C))).Append(Fin);
        }
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }
}
