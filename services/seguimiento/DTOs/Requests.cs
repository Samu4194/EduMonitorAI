using System.ComponentModel.DataAnnotations;
namespace EduMonitor.Seguimiento.DTOs;

// ---- Entradas ----
public record EditarRecomendacion(
    [Required, StringLength(600, MinimumLength = 10, ErrorMessage = "El resumen debe tener entre 10 y 600 caracteres.")] string Resumen,
    [Required, MinLength(1, ErrorMessage = "Indica al menos una causa probable."), MaxLength(4)] List<string> Causas,
    [Required, MinLength(1, ErrorMessage = "Indica al menos una acción para el docente."), MaxLength(4)] List<string> AccionesDocente,
    [Required, MinLength(1, ErrorMessage = "Indica al menos una acción para el estudiante."), MaxLength(4)] List<string> AccionesEstudiante,
    [Required, StringLength(1500, MinimumLength = 40, ErrorMessage = "El mensaje al estudiante debe tener entre 40 y 1500 caracteres.")] string MensajeEstudiante);
public record AprobarRequest(bool Comunicar);
public record RechazarRequest([Required, StringLength(500, MinimumLength = 5, ErrorMessage = "Indica el motivo (entre 5 y 500 caracteres).")] string Motivo);

// ---- Respuestas de otros servicios ----
public record CursoReference(int Id, string MateriaNombre, string Carrera, int Nivel, string Paralelo, int? TeacherId, string? Periodo = null, string? PeriodoEstado = null);
public record EstudianteReference(int Id, string Apellidos, string Nombres, string Correo);
public record TemaReference(int Id, string Nombre, int UnidadId, string UnidadNombre);
public record ActividadTemasReference(int Id, string Nombre, string Tipo, DateOnly? FechaEntrega, List<TemaReference> Temas, int Calificadas);
public record PonderacionReference(decimal Tareas, decimal Ape, decimal Examen, decimal NotaMinima);
public record ActividadReference(int Id, string Nombre, string Tipo, DateOnly? FechaEntrega);
public record FilaReference(int EstudianteId, string Apellidos, string Nombres, Dictionary<int, decimal> Notas, decimal? Resultado, string Estado, bool Definitivo, int Pendientes);
public record MatrizReference(int CursoId, PonderacionReference Ponderacion, List<ActividadReference> Actividades, List<FilaReference> Estudiantes);

// ---- Salidas ----
public record EstadoAgenteView(List<Agente.EstadoProveedor> Proveedores, bool Plantillas, string NivelMinimo, bool CorreoReal);
public record HallazgoView(int Id, string Codigo, string Nivel, string Titulo, string Detalle);
public record ComunicacionView(int Id, int RecomendacionId, string Canal, string Destinatario, string Asunto, string Cuerpo, string Estado, string? Error, DateTime CreadoEn, string Estudiante);
public record RecomendacionView(int Id, string Estado, string Origen, string? Modelo, string Resumen, List<string> Causas, List<string> AccionesDocente,
    List<string> AccionesEstudiante, string MensajeEstudiante, bool Editada, int Version, DateTime CreadoEn, DateTime ActualizadoEn, DateTime? RevisadaEn, string? MotivoRechazo,
    ComunicacionView? Comunicacion, System.Text.Json.JsonElement? Ruta = null);
public record EstudianteSeguimiento(int EstudianteAnalisisId, int EstudianteId, string Apellidos, string Nombres, string Correo, decimal? Resultado, string Estado, int Pendientes,
    string Nivel, List<HallazgoView> Hallazgos, RecomendacionView? Recomendacion);
public record AnalisisResumen(int Id, string Estado, DateTime CreadoEn, DateTime? TerminadoEn, int TotalEstudiantes, int ARecomendar, int Generadas, decimal NotaMinima,
    int Alto, int Medio, int Bajo, int SinAlertas, string Materia, string Paralelo, string Periodo);
public record PanelView(int CursoId, bool SoloLectura, AnalisisResumen? Analisis, List<HallazgoView> HallazgosCurso, List<EstudianteSeguimiento> Estudiantes);

// HU-14
public record ConteoRecomendaciones(int Borrador, int Aprobada, int Comunicada, int Rechazada, int Generando);
public record ReporteView(int CursoId, string Materia, string Paralelo, string Periodo, string Docente, DateTime GeneradoEn, AnalisisResumen Analisis,
    ConteoRecomendaciones Recomendaciones, List<HallazgoView> HallazgosCurso, List<EstudianteSeguimiento> Estudiantes);
