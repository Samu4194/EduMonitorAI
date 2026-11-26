using EduMonitor.Infrastructure;
using EduMonitor.Seguimiento.DTOs;
namespace EduMonitor.Seguimiento.Services;

// Seguimiento no guarda cursos, estudiantes ni notas: los pide a Materias, Estudiantes y Evaluaciones con el JWT del docente.
// Materias responde 404 si el curso no existe o no es del docente, así que este servicio no repite las reglas de asignación.
public sealed class AccesoCursos(DependencyClient dependencies, IHttpContextAccessor http)
{
    public const string PeriodoFinalizado = "Finalizado";

    // escritura = true para operaciones que cambian algo: un período finalizado es de solo lectura.
    public async Task<CursoReference> Curso(int cursoId, bool escritura, CancellationToken ct)
    {
        var curso = await dependencies.Get<CursoReference>("Materias", $"/api/cursos/{cursoId}", ct);
        if (escritura && curso.PeriodoEstado == PeriodoFinalizado)
            throw new ApiException(409, $"El período {curso.Periodo} finalizó: el seguimiento del curso es de solo lectura.");
        return curso;
    }
    public bool SoloLectura(CursoReference curso) => curso.PeriodoEstado == PeriodoFinalizado || !(http.HttpContext?.User.IsInRole("Teacher") ?? false);
    public Task<MatrizReference> Matriz(int cursoId, CancellationToken ct) => dependencies.Get<MatrizReference>("Evaluaciones", $"/api/calificaciones/curso/{cursoId}", ct);
    public Task<List<EstudianteReference>> Estudiantes(int cursoId, CancellationToken ct) => dependencies.Get<List<EstudianteReference>>("Estudiantes", $"/api/estudiantes/curso/{cursoId}", ct);
    // Actividades con los temas a los que se relacionan (el seguimiento los usa para decir qué reforzar).
    public Task<List<ActividadTemasReference>> Actividades(int cursoId, CancellationToken ct) => dependencies.Get<List<ActividadTemasReference>>("Evaluaciones", $"/api/actividades?cursoId={cursoId}", ct);
}
