using System.Text.Json;
using EduMonitor.Seguimiento.Reglas;
namespace EduMonitor.Seguimiento.Agente;

// Lo único que el agente conoce de un estudiante. A propósito NO incluye nombre, cédula, correo ni el identificador:
// aunque la redacción la haga un modelo en la nube, ningún dato personal sale del sistema.
public record NotaIA(string Actividad, string Tipo, decimal Nota);
public record TipoIA(string Tipo, decimal Promedio, int Calificadas, int TotalActividades);
public record HallazgoIA(string Codigo, string Nivel, string Titulo, string Detalle);
public record ProyeccionIA(int ActividadesPendientes, decimal? PromedioRequerido, decimal? MaximoPosible);
public record ContextoIA(string Materia, decimal NotaMinima, decimal? ResultadoActual, string Nivel, List<NotaIA> Notas, List<TipoIA> PorTipo,
    List<HallazgoIA> Hallazgos, ProyeccionIA? Proyeccion)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = false };

    public static ContextoIA De(string materia, decimal notaMinima, ResultadoEstudiante r) => new(materia, notaMinima, r.Resultado, r.Nivel.Nombre(),
        r.Notas.Select(n => new NotaIA(n.Actividad, n.Tipo, n.Nota)).ToList(),
        r.PorTipo.Select(t => new TipoIA(t.Tipo, t.Promedio, t.Calificadas, t.Total)).ToList(),
        r.Hallazgos.Select(h => new HallazgoIA(h.Codigo, h.Nivel.Nombre(), h.Titulo, h.Detalle)).ToList(),
        r.Proyeccion is null ? null : new ProyeccionIA(r.Proyeccion.Pendientes, r.Proyeccion.PromedioRequerido, r.Proyeccion.MaximoPosible));
    public string Serializar() => JsonSerializer.Serialize(this, Json);
    public static ContextoIA? Leer(string json) => JsonSerializer.Deserialize<ContextoIA>(json, Json);
}

// Lo que produce el agente para un estudiante (antes de que el docente lo revise).
public record Borrador(string Resumen, List<string> Causas, List<string> AccionesDocente, List<string> AccionesEstudiante, string MensajeEstudiante);
