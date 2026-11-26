using System.Globalization;
namespace EduMonitor.Seguimiento.Reglas;

// Entradas y salidas del motor de reglas. Son registros inmutables sin base de datos: el motor se comprueba con casos de prueba.
public enum Nivel { Ninguno = 0, Bajo = 1, Medio = 2, Alto = 3 }

public static class Textos
{
    // Los textos generados usan coma decimal (es-EC) sin depender de la cultura instalada en el contenedor.
    static readonly NumberFormatInfo Coma = new() { NumberDecimalSeparator = ",", NumberGroupSeparator = "" };
    public static string N(decimal v) => v.ToString("0.00", Coma);
    public static string Nombre(this Nivel n) => n switch { Nivel.Alto => "Alto", Nivel.Medio => "Medio", Nivel.Bajo => "Bajo", _ => "Sin alertas" };
    public static Nivel DeNombre(string? n) => n switch { "Alto" => Nivel.Alto, "Medio" => Nivel.Medio, "Bajo" => Nivel.Bajo, _ => Nivel.Ninguno };
}

public record Pesos(decimal Tarea, decimal Ape, decimal Examen)
{
    public decimal De(string tipo) => tipo switch { "Tarea" => Tarea, "APE" => Ape, "Examen" => Examen, _ => 0m };
}
public record ActividadDato(int Id, string Nombre, string Tipo, DateOnly? Fecha);
// Resultado lo calcula Evaluaciones (fuente única de la nota); el motor no lo recalcula.
public record EstudianteDato(int Id, decimal? Resultado, IReadOnlyDictionary<int, decimal> Notas);
public record CursoDato(decimal NotaMinima, Pesos Pesos, IReadOnlyList<ActividadDato> Actividades, IReadOnlyList<EstudianteDato> Estudiantes);

public record HallazgoDato(string Codigo, Nivel Nivel, string Titulo, string Detalle);
// Cuánto necesita sacar el estudiante en lo que le falta para llegar a la nota mínima.
public record ProyeccionDato(int Pendientes, decimal? PromedioRequerido, decimal? MaximoPosible);
public record NotaOrdenada(int ActividadId, string Actividad, string Tipo, DateOnly? Fecha, decimal Nota);
public record PromedioTipo(string Tipo, decimal Promedio, int Calificadas, int Total);
public record ResultadoEstudiante(int EstudianteId, decimal? Resultado, Nivel Nivel, IReadOnlyList<HallazgoDato> Hallazgos, IReadOnlyList<NotaOrdenada> Notas,
    IReadOnlyList<PromedioTipo> PorTipo, ProyeccionDato? Proyeccion, int Pendientes);
public record ResultadoCurso(IReadOnlyList<ResultadoEstudiante> Estudiantes, IReadOnlyList<HallazgoDato> HallazgosCurso);

// Umbrales de las reglas. Se leen de la sección "Reglas" de appsettings: ajustarlos no requiere cambiar código.
public class OpcionesReglas
{
    // Resultado por debajo de la nota mínima: Medio; por debajo de (mínima - BrechaAlta): Alto.
    public decimal BrechaAlta { get; set; } = 2m;
    public int MinNotasTendencia { get; set; } = 4;
    public decimal CaidaTendencia { get; set; } = 1.5m;
    public decimal CaidaTendenciaAlta { get; set; } = 3m;
    public decimal NotaCritica { get; set; } = 5m;
    // Un tipo de actividad es débil si su promedio queda más de DebilidadTipo por debajo de la nota mínima (con al menos 2 notas).
    public decimal DebilidadTipo { get; set; } = 1m;
    // Promedio que debería sacar en lo pendiente: por encima de ExigenciaMedia es Medio, por encima de ExigenciaAlta (o imposible) es Alto.
    public decimal ExigenciaMedia { get; set; } = 8m;
    public decimal ExigenciaAlta { get; set; } = 9m;
    public decimal ActividadDificilBrecha { get; set; } = 1.5m;
    public decimal PorcentajeCalificadoActividad { get; set; } = 0.5m;
    public decimal CursoEnRiesgoMedio { get; set; } = 0.4m;
    public decimal CursoEnRiesgoAlto { get; set; } = 0.6m;
}
