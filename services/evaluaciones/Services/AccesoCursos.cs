using EduMonitor.Evaluaciones.DTOs;
using EduMonitor.Infrastructure;
namespace EduMonitor.Evaluaciones.Services;

// Materias decide quién puede ver un curso: responde 404 si el curso no existe o no es del docente (el JWT se reenvía).
// Así este servicio no repite las reglas de asignación y un docente nunca llega a los datos de un grupo ajeno.
public sealed class AccesoCursos(DependencyClient dependencies, IHttpContextAccessor http)
{
    public const string PeriodoFinalizado = "Finalizado";

    // Un curso de un período finalizado es de solo lectura: se consulta, pero ya no cambian su contenido, sus actividades ni sus notas.
    // Todas las escrituras pasan por aquí, así que la regla se aplica en un solo lugar.
    public async Task<CursoReference> Curso(int cursoId, CancellationToken ct)
    {
        var curso = await dependencies.Get<CursoReference>("Materias", $"/api/cursos/{cursoId}", ct);
        if (curso.PeriodoEstado == PeriodoFinalizado && !HttpMethods.IsGet(http.HttpContext?.Request.Method ?? HttpMethods.Get))
            throw new ApiException(409, $"El período {curso.Periodo} finalizó: el curso es de solo lectura.");
        return curso;
    }
    public Task<List<EstudianteReference>> Estudiantes(int cursoId, CancellationToken ct) => dependencies.Get<List<EstudianteReference>>("Estudiantes", $"/api/estudiantes/curso/{cursoId}", ct);
}
