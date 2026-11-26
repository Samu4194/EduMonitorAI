using System.Security.Claims;
using EduMonitor.Infrastructure;
using EduMonitor.Seguimiento.Agente;
using EduMonitor.Seguimiento.Data;
using EduMonitor.Seguimiento.DTOs;
using EduMonitor.Seguimiento.Models;
using EduMonitor.Seguimiento.Reglas;
using Microsoft.EntityFrameworkCore;
namespace EduMonitor.Seguimiento.Services;

// Trabajo para el generador en segundo plano: todo un análisis o la regeneración de una sola recomendación.
public record Trabajo(int AnalisisId, int? RecomendacionId = null);

// HU-11. Un análisis toma la fotografía de las notas del curso, ejecuta el motor de reglas y deja listas las recomendaciones por redactar.
// La redacción con IA es lenta (decenas de segundos por estudiante): se hace en segundo plano y el docente ve el avance.
public sealed class AnalisisService(AccesoCursos acceso, SeguimientoDb db, MotorReglas motor, OpcionesAgente opciones, ColaTrabajos cola, IHttpContextAccessor http)
{
    // Pasado este tiempo, un análisis que quedó "Generando" se considera abandonado y deja de bloquear uno nuevo.
    static readonly TimeSpan Abandonado = TimeSpan.FromMinutes(30);

    public Task<AnalisisResumen> Iniciar(int cursoId, CancellationToken ct) => Iniciar(cursoId, false, ct);

    // soloAlertas: calcula las alertas (reglas, al instante) sin pedirle a la IA una recomendación para cada estudiante. Las recomendaciones se piden
    // después, estudiante por estudiante, desde el asistente; las que el docente ya tenía en borrador o aprobadas pasan al análisis nuevo en vez de descartarse.
    public async Task<AnalisisResumen> Iniciar(int cursoId, bool soloAlertas, CancellationToken ct)
    {
        var curso = await acceso.Curso(cursoId, escritura: true, ct);
        var usuario = http.HttpContext!.User.UserId();
        var corte = DateTime.UtcNow - Abandonado;
        var enCurso = await db.Analisis.Where(a => a.CursoId == cursoId && a.Estado == EstadoAnalisis.Generando).ToListAsync(ct);
        if (enCurso.Any(a => a.CreadoEn >= corte))
            throw new ApiException(409, "Ya hay un análisis de este curso en proceso. Espera a que termine.");
        foreach (var vieja in enCurso)
        {
            vieja.Estado = EstadoAnalisis.Completado;
            vieja.TerminadoEn = DateTime.UtcNow;
        }

        var matriz = await acceso.Matriz(cursoId, ct);
        var estudiantes = (await acceso.Estudiantes(cursoId, ct)).ToDictionary(e => e.Id);
        if (matriz.Estudiantes.Count == 0)
            throw new ApiException(409, "El curso no tiene estudiantes inscritos: no hay nada que analizar.");
        if (matriz.Actividades.Count == 0 || matriz.Estudiantes.All(e => e.Notas.Count == 0))
            throw new ApiException(409, "Todavía no hay calificaciones registradas. Califica al menos una actividad para analizar el curso.");

        var datos = new CursoDato(matriz.Ponderacion.NotaMinima, new Pesos(matriz.Ponderacion.Tareas, matriz.Ponderacion.Ape, matriz.Ponderacion.Examen),
            matriz.Actividades.Select(a => new ActividadDato(a.Id, a.Nombre, a.Tipo, a.FechaEntrega)).ToList(),
            matriz.Estudiantes.Select(e => new EstudianteDato(e.EstudianteId, e.Resultado, e.Notas)).ToList());
        var resultado = motor.Evaluar(datos);
        var minimo = Textos.DeNombre(opciones.NivelMinimo);

        var analisis = new Analisis
        {
            CursoId = cursoId, DocenteId = usuario, Materia = curso.MateriaNombre, Paralelo = curso.Paralelo, Periodo = curso.Periodo ?? "",
            NotaMinima = matriz.Ponderacion.NotaMinima, TotalEstudiantes = matriz.Estudiantes.Count
        };
        var filas = new List<(EstudianteAnalisis Fila, ResultadoEstudiante Datos)>();
        foreach (var (fila, r) in matriz.Estudiantes.Zip(resultado.Estudiantes))
        {
            estudiantes.TryGetValue(fila.EstudianteId, out var persona);
            var e = new EstudianteAnalisis
            {
                EstudianteId = fila.EstudianteId, Apellidos = fila.Apellidos, Nombres = fila.Nombres, Correo = persona?.Correo ?? "",
                Resultado = r.Resultado, Estado = fila.Estado, Pendientes = r.Pendientes, Nivel = r.Nivel.Nombre(),
                ContextoJson = ContextoIA.De(curso.MateriaNombre, matriz.Ponderacion.NotaMinima, r).Serializar()
            };
            analisis.Estudiantes.Add(e);
            filas.Add((e, r));
        }
        analisis.ARecomendar = soloAlertas ? 0 : filas.Count(f => f.Datos.Nivel >= minimo && f.Datos.Nivel != Nivel.Ninguno);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        db.Analisis.Add(analisis);
        await db.SaveChangesAsync(ct);
        foreach (var (fila, r) in filas)
            foreach (var h in r.Hallazgos)
                db.Hallazgos.Add(new Hallazgo { AnalisisId = analisis.Id, EstudianteAnalisisId = fila.Id, Codigo = h.Codigo, Nivel = h.Nivel.Nombre(), Titulo = Cortar(h.Titulo, 200), Detalle = Cortar(h.Detalle, 1000) });
        foreach (var h in resultado.HallazgosCurso)
            db.Hallazgos.Add(new Hallazgo { AnalisisId = analisis.Id, Codigo = h.Codigo, Nivel = h.Nivel.Nombre(), Titulo = Cortar(h.Titulo, 200), Detalle = Cortar(h.Detalle, 1000) });

        // Una recomendación vigente por estudiante: las del análisis anterior que no se comunicaron ni se rechazaron quedan reemplazadas.
        var anteriores = await db.Recomendaciones.Where(r => r.CursoId == cursoId &&
            (r.Estado == EstadoRecomendacion.Generando || r.Estado == EstadoRecomendacion.Borrador || r.Estado == EstadoRecomendacion.Aprobada)).ToListAsync(ct);
        var idsAnteriores = anteriores.Select(r => r.EstudianteAnalisisId).ToList();
        var estudianteDe = await db.EstudiantesAnalisis.Where(e => idsAnteriores.Contains(e.Id)).ToDictionaryAsync(e => e.Id, e => e.EstudianteId, ct);
        var filaNueva = filas.ToDictionary(f => f.Fila.EstudianteId, f => f.Fila.Id);
        foreach (var r in anteriores)
        {
            r.ActualizadoEn = DateTime.UtcNow;
            if (soloAlertas && r.Estado is EstadoRecomendacion.Borrador or EstadoRecomendacion.Aprobada
                && estudianteDe.TryGetValue(r.EstudianteAnalisisId, out var estudianteId) && filaNueva.TryGetValue(estudianteId, out var nuevaFila))
            {
                r.AnalisisId = analisis.Id;
                r.EstudianteAnalisisId = nuevaFila;
                db.Eventos.Add(new EventoRecomendacion { RecomendacionId = r.Id, Evento = "Trasladada", UsuarioId = usuario, Detalle = "Se actualizaron las alertas del curso; la recomendación sigue vigente." });
                continue;
            }
            r.Estado = EstadoRecomendacion.Reemplazada;
            db.Eventos.Add(new EventoRecomendacion { RecomendacionId = r.Id, Evento = "Reemplazada", UsuarioId = usuario, Detalle = "Se generó un análisis nuevo del curso." });
        }
        if (!soloAlertas)
            foreach (var (fila, r) in filas.Where(f => f.Datos.Nivel >= minimo && f.Datos.Nivel != Nivel.Ninguno))
                db.Recomendaciones.Add(new Recomendacion { AnalisisId = analisis.Id, EstudianteAnalisisId = fila.Id, CursoId = cursoId });
        if (analisis.ARecomendar == 0)
        {
            analisis.Estado = EstadoAnalisis.Completado;
            analisis.TerminadoEn = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        if (analisis.Estado == EstadoAnalisis.Generando)
            cola.Encolar(new Trabajo(analisis.Id));
        return Resumen(analisis, filas.Select(f => f.Datos.Nivel.Nombre()).ToList());
    }

    // Los nombres de actividad vienen de docentes o de Moodle: un detalle muy largo no debe hacer fallar todo el análisis por exceder la columna.
    static string Cortar(string t, int max) => t.Length <= max ? t : t[..(max - 1)] + "…";

    public static AnalisisResumen Resumen(Analisis a, IReadOnlyCollection<string> niveles) => new(a.Id, a.Estado, a.CreadoEn, a.TerminadoEn, a.TotalEstudiantes, a.ARecomendar, a.Generadas,
        a.NotaMinima, niveles.Count(n => n == "Alto"), niveles.Count(n => n == "Medio"), niveles.Count(n => n == "Bajo"), niveles.Count(n => n == "Sin alertas"), a.Materia, a.Paralelo, a.Periodo);
}
