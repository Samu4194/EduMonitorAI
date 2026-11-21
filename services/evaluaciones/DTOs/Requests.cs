using System.ComponentModel.DataAnnotations;
namespace EduMonitor.Evaluaciones.DTOs;

// ---- Entradas ----
public record UnidadCreate(
    [Range(1, int.MaxValue, ErrorMessage = "Elige un curso.")] int CursoId,
    [Required, StringLength(120, MinimumLength = 3, ErrorMessage = "El nombre de la unidad debe tener entre 3 y 120 caracteres.")] string Nombre,
    [StringLength(500, ErrorMessage = "La descripción no puede pasar de 500 caracteres.")] string? Descripcion);
public record UnidadEdit(
    [Required, StringLength(120, MinimumLength = 3, ErrorMessage = "El nombre de la unidad debe tener entre 3 y 120 caracteres.")] string Nombre,
    [StringLength(500, ErrorMessage = "La descripción no puede pasar de 500 caracteres.")] string? Descripcion);
// Contenidos solo se usa al crear: permite registrar el tema con sus contenidos mínimos en un solo paso.
public record TemaCreate(
    [Required, StringLength(150, MinimumLength = 3, ErrorMessage = "El nombre del tema debe tener entre 3 y 150 caracteres.")] string Nombre,
    [StringLength(500, ErrorMessage = "La descripción no puede pasar de 500 caracteres.")] string? Descripcion,
    [MaxLength(30, ErrorMessage = "Un tema admite hasta 30 contenidos mínimos.")] List<string>? Contenidos);
public record TemaEdit(
    [Required, StringLength(150, MinimumLength = 3, ErrorMessage = "El nombre del tema debe tener entre 3 y 150 caracteres.")] string Nombre,
    [StringLength(500, ErrorMessage = "La descripción no puede pasar de 500 caracteres.")] string? Descripcion);
public record ContenidoRequest([Required, StringLength(300, MinimumLength = 3, ErrorMessage = "El contenido mínimo debe tener entre 3 y 300 caracteres.")] string Descripcion);
// -1 sube un lugar y 1 baja un lugar.
public record MoverRequest([Range(-1, 1)] int Direccion);

public record ActividadCreate(
    [Range(1, int.MaxValue, ErrorMessage = "Elige un curso.")] int CursoId,
    [Required, StringLength(120, MinimumLength = 3, ErrorMessage = "El nombre de la actividad debe tener entre 3 y 120 caracteres.")] string Nombre,
    [Required, RegularExpression("^(Tarea|APE|Examen)$", ErrorMessage = "El tipo debe ser Tarea, APE o Examen.")] string Tipo,
    [StringLength(500, ErrorMessage = "La descripción no puede pasar de 500 caracteres.")] string? Descripcion,
    DateOnly? FechaEntrega,
    [MaxLength(30)] List<int>? TemaIds = null);
public record ActividadEdit(
    [Required, StringLength(120, MinimumLength = 3, ErrorMessage = "El nombre de la actividad debe tener entre 3 y 120 caracteres.")] string Nombre,
    [Required, RegularExpression("^(Tarea|APE|Examen)$", ErrorMessage = "El tipo debe ser Tarea, APE o Examen.")] string Tipo,
    [StringLength(500, ErrorMessage = "La descripción no puede pasar de 500 caracteres.")] string? Descripcion,
    DateOnly? FechaEntrega,
    [MaxLength(30)] List<int>? TemaIds = null);

// Nota nula = dejar la actividad pendiente de calificar para ese estudiante.
public record NotaEntrada([Range(1, int.MaxValue)] int EstudianteId, decimal? Nota);
public record GuardarNotas([Required, MinLength(1), MaxLength(300)] List<NotaEntrada> Notas);
public record PonderacionRequest(
    [Range(0, 100, ErrorMessage = "Cada porcentaje debe estar entre 0 y 100.")] decimal Tareas,
    [Range(0, 100, ErrorMessage = "Cada porcentaje debe estar entre 0 y 100.")] decimal Ape,
    [Range(0, 100, ErrorMessage = "Cada porcentaje debe estar entre 0 y 100.")] decimal Examen,
    [Range(0, 10, ErrorMessage = "La nota mínima debe estar entre 0 y 10.")] decimal NotaMinima);

// ---- Respuestas de otros servicios ----
public record CursoReference(int Id, string MateriaNombre, string Carrera, int Nivel, string Paralelo, int? TeacherId, string? Periodo = null, string? PeriodoEstado = null);
public record EstudianteReference(int Id, string Apellidos, string Nombres, string Correo);

// ---- Salidas ----
public record ContenidoView(int Id, string Descripcion);
public record ArchivoView(int Id, int TemaId, string Nombre, string Tipo, int Tamano, DateTime SubidoEn);
public record TemaView(int Id, int UnidadId, string Nombre, string? Descripcion, List<ContenidoView> Contenidos, List<ArchivoView> Archivos);
public record UnidadView(int Id, int CursoId, string Nombre, string? Descripcion, List<TemaView> Temas);
public record TemaRef(int Id, string Nombre, int UnidadId, string UnidadNombre);
public record ActividadView(int Id, int CursoId, string Nombre, string Tipo, string? Descripcion, DateOnly? FechaEntrega, List<TemaRef> Temas, int Calificadas);
public record PonderacionView(decimal Tareas, decimal Ape, decimal Examen, decimal NotaMinima, bool Personalizada);

public record FilaCalificaciones(int EstudianteId, string Apellidos, string Nombres, Dictionary<int, decimal> Notas, decimal? Resultado, string Estado, bool Definitivo, int Pendientes);
public record ActividadColumna(int Id, string Nombre, string Tipo, DateOnly? FechaEntrega);
public record MatrizView(int CursoId, PonderacionView Ponderacion, List<ActividadColumna> Actividades, List<FilaCalificaciones> Estudiantes);
public record GuardarResultado(int Guardadas, int Pendientes);

public record ResumenRendimiento(int TotalEstudiantes, int ConResultado, decimal? PromedioGrupo, int EnRiesgo, decimal? PorcentajeRiesgo, decimal? NotaMasBaja, decimal? ResultadoMasBajo,
    int Actividades, int NotasPendientes, bool Completo, decimal NotaMinima);
// Puntos que un tipo de actividad aporta al total de 10: su promedio por su porcentaje. Maximo = porcentaje / 10 (30 % -> 3 puntos).
public record PuntosTipo(string Tipo, decimal Porcentaje, decimal Maximo, decimal? Promedio, decimal Puntos, int Calificadas, int Actividades);
public record EstudianteRendimiento(int EstudianteId, string Apellidos, string Nombres, decimal? Resultado, string Estado, bool Definitivo, int Calificadas, int Pendientes, decimal? NotaMasBaja, List<PuntosTipo> Desglose, decimal Acumulado);
public record TemaRendimiento(int TemaId, string Tema, int UnidadId, string Unidad, int Actividades, decimal? Promedio, int Calificadas, int Pendientes, int NotasBajas);
public record RendimientoView(int CursoId, ResumenRendimiento Resumen, List<EstudianteRendimiento> Estudiantes, List<TemaRendimiento> Temas);
public record NotaDetalle(int ActividadId, string Actividad, string Tipo, decimal? Nota, List<string> Temas);
public record TemaPromedio(int TemaId, string Tema, decimal? Promedio, int Calificadas, int Pendientes);
public record EstudianteDetalle(int EstudianteId, string Apellidos, string Nombres, string Correo, decimal? Resultado, string Estado, bool Definitivo, int Pendientes, List<NotaDetalle> Notas, List<TemaPromedio> Temas, decimal NotaMinima, List<PuntosTipo> Desglose, decimal Acumulado);
