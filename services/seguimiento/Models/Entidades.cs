namespace EduMonitor.Seguimiento.Models;

public static class EstadoAnalisis
{
    public const string Generando = "Generando", Completado = "Completado";
}

// Ciclo de vida de una recomendación (HU-13): el agente propone, el docente decide.
//   Generando -> Borrador -> Aprobada -> Comunicada
//                         \-> Rechazada
//   Borrador/Aprobada -> Reemplazada (cuando se genera un análisis nuevo del curso)
public static class EstadoRecomendacion
{
    public const string Generando = "Generando", Borrador = "Borrador", Aprobada = "Aprobada", Comunicada = "Comunicada", Rechazada = "Rechazada", Reemplazada = "Reemplazada";
}

public static class OrigenRecomendacion
{
    public const string Plantilla = "Plantilla", Local = "Local", Nube = "Nube";
}

public static class EstadoComunicacion
{
    public const string Enviado = "Enviado", Simulado = "Simulado", Error = "Error";
}

// Una corrida del agente sobre un curso: fotografía de las notas en ese momento, más lo que el agente detectó y recomendó.
// Se guarda la fotografía porque las notas cambian: el docente debe poder revisar con qué datos se hizo cada recomendación.
public class Analisis
{
    public int Id { get; set; }
    public int CursoId { get; set; }
    public int DocenteId { get; set; }
    public string Materia { get; set; } = "";
    public string Paralelo { get; set; } = "";
    public string Periodo { get; set; } = "";
    public decimal NotaMinima { get; set; }
    public string Estado { get; set; } = EstadoAnalisis.Generando;
    public DateTime CreadoEn { get; set; } = DateTime.UtcNow;
    public DateTime? TerminadoEn { get; set; }
    public int TotalEstudiantes { get; set; }
    // Cuántas recomendaciones debe redactar el agente y cuántas lleva.
    public int ARecomendar { get; set; }
    public int Generadas { get; set; }
    public List<EstudianteAnalisis> Estudiantes { get; set; } = [];
    public List<Hallazgo> Hallazgos { get; set; } = [];
}

public class EstudianteAnalisis
{
    public int Id { get; set; }
    public int AnalisisId { get; set; }
    public int EstudianteId { get; set; }
    public string Apellidos { get; set; } = "";
    public string Nombres { get; set; } = "";
    public string Correo { get; set; } = "";
    public decimal? Resultado { get; set; }
    public string Estado { get; set; } = "";
    public int Pendientes { get; set; }
    public string Nivel { get; set; } = "Sin alertas";
    // Contexto anónimo que se le entrega al agente (JSON). Nunca contiene nombre, cédula ni correo.
    public string ContextoJson { get; set; } = "{}";
}

// EstudianteAnalisisId nulo = hallazgo del curso (actividad difícil, curso en riesgo).
public class Hallazgo
{
    public int Id { get; set; }
    public int AnalisisId { get; set; }
    public int? EstudianteAnalisisId { get; set; }
    public string Codigo { get; set; } = "";
    public string Nivel { get; set; } = "";
    public string Titulo { get; set; } = "";
    public string Detalle { get; set; } = "";
}

public class Recomendacion
{
    public int Id { get; set; }
    public int AnalisisId { get; set; }
    public int EstudianteAnalisisId { get; set; }
    public EstudianteAnalisis EstudianteAnalisis { get; set; } = null!;
    public int CursoId { get; set; }
    public string Estado { get; set; } = EstadoRecomendacion.Generando;
    public string Origen { get; set; } = OrigenRecomendacion.Plantilla;
    public string? Modelo { get; set; }
    public string Resumen { get; set; } = "";
    // Listas serializadas como JSON (arreglo de textos).
    public string CausasJson { get; set; } = "[]";
    public string AccionesDocenteJson { get; set; } = "[]";
    public string AccionesEstudianteJson { get; set; } = "[]";
    public string MensajeEstudiante { get; set; } = "";
    // El docente editó el texto generado.
    public bool Editada { get; set; }
    public int Version { get; set; } = 1;
    public DateTime CreadoEn { get; set; } = DateTime.UtcNow;
    public DateTime ActualizadoEn { get; set; } = DateTime.UtcNow;
    public int? RevisadaPor { get; set; }
    public DateTime? RevisadaEn { get; set; }
    public string? MotivoRechazo { get; set; }
    // Ruta de mejora del estudiante (etapas, metas y temas a reforzar) cuando la recomendación nació del asistente. Nulo en las demás.
    public string? RutaJson { get; set; }
    public List<Comunicacion> Comunicaciones { get; set; } = [];
}

public static class RolMensaje
{
    public const string Docente = "docente", Asistente = "asistente";
}

public static class TipoMensaje
{
    public const string Libre = "libre", Precargada = "precargada", Aclaracion = "aclaracion", Ruta = "ruta";
}

// Conversación del docente con el asistente sobre un curso. Es temporal: se borra sola pasados los días de retención configurados.
public class Conversacion
{
    public int Id { get; set; }
    public int CursoId { get; set; }
    public int DocenteId { get; set; }
    public string Titulo { get; set; } = "Conversación nueva";
    // Seudónimos de la conversación, p. ej. {"A":123,"B":456}. El historial guarda «Estudiante A»: el nombre real nunca se almacena aquí.
    public string AliasJson { get; set; } = "{}";
    public DateTime CreadoEn { get; set; } = DateTime.UtcNow;
    public DateTime ActualizadoEn { get; set; } = DateTime.UtcNow;
    public List<Mensaje> Mensajes { get; set; } = [];
}

public class Mensaje
{
    public int Id { get; set; }
    public int ConversacionId { get; set; }
    public string Rol { get; set; } = RolMensaje.Docente;
    public string Tipo { get; set; } = TipoMensaje.Libre;
    public string? PreguntaId { get; set; }
    // Texto con seudónimos («Estudiante A»); se traduce a nombres reales solo al mostrarlo.
    public string Texto { get; set; } = "";
    // Tarjetas de datos exactos (tablas, rutas, opciones de aclaración) como arreglo JSON, también con seudónimos.
    public string TarjetasJson { get; set; } = "[]";
    public string? Origen { get; set; }
    public string? Modelo { get; set; }
    // "curso" = se apoyó en datos del curso; "general" = conocimiento pedagógico general, sin datos.
    public string? Alcance { get; set; }
    // false = alguna cifra del texto no coincide con los datos (se avisó al docente).
    public bool? Verificada { get; set; }
    public bool? Util { get; set; }
    public DateTime CreadoEn { get; set; } = DateTime.UtcNow;
}

// Correo dirigido al estudiante. Con SMTP configurado se envía; sin SMTP queda como "Simulado": se registra lo que se habría enviado.
public class Comunicacion
{
    public int Id { get; set; }
    public int RecomendacionId { get; set; }
    public int CursoId { get; set; }
    public string Canal { get; set; } = "Correo";
    public string Destinatario { get; set; } = "";
    public string Asunto { get; set; } = "";
    public string Cuerpo { get; set; } = "";
    public string Estado { get; set; } = EstadoComunicacion.Simulado;
    public string? Error { get; set; }
    public int EnviadoPor { get; set; }
    public DateTime CreadoEn { get; set; } = DateTime.UtcNow;
}

// Bitácora: quién hizo qué sobre una recomendación.
public class EventoRecomendacion
{
    public int Id { get; set; }
    public int RecomendacionId { get; set; }
    public string Evento { get; set; } = "";
    public int? UsuarioId { get; set; }
    public string? Detalle { get; set; }
    public DateTime Fecha { get; set; } = DateTime.UtcNow;
}
