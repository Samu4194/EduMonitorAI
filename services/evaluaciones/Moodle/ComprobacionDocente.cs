namespace EduMonitor.Evaluaciones.Moodle;

// En Moodle califica quien está matriculado como profesor del curso; en EduMonitor ve los resultados el docente asignado al curso.
// Nada obliga a que sean la misma persona, así que se comparan sus correos y, si no coinciden, se avisa (sin bloquear: puede haber profesores de apoyo).
public static class ComprobacionDocente
{
    public record Resultado(bool Coincide, List<string> Avisos);

    public static Resultado Evaluar(string? correoAsignado, IReadOnlyList<AlumnoMoodle>? profesoresMoodle)
    {
        var avisos = new List<string>();
        var profesores = profesoresMoodle ?? [];
        if (string.IsNullOrWhiteSpace(correoAsignado))
        {
            avisos.Add("Este curso no tiene docente asignado en EduMonitor.");
            return new(false, avisos);
        }
        if (profesores.Count == 0)
        {
            avisos.Add("En Moodle no hay ningún profesor matriculado en este curso.");
            return new(false, avisos);
        }
        if (profesores.Any(p => string.Equals(p.Correo, correoAsignado.Trim(), StringComparison.OrdinalIgnoreCase)))
            return new(true, avisos);
        avisos.Add($"El docente asignado en EduMonitor ({correoAsignado.Trim()}) no figura como profesor de este curso en Moodle " +
            $"(profesores: {string.Join(", ", profesores.Select(p => $"{p.Nombres} {p.Apellidos} <{p.Correo}>"))}). Quien califica en Moodle puede ser distinto de quien ve los resultados aquí.");
        return new(false, avisos);
    }
}
