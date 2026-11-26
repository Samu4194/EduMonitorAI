using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace EduMonitor.Seguimiento.Asistente;

// Un modelo pequeño puede citar mal una cifra. Antes de mostrar la respuesta se comprueba que cada nota, promedio o porcentaje que menciona
// exista en los datos que se le entregaron; si no, se avisa al docente (los datos exactos siempre van en la tarjeta).
public static partial class Verificador
{
    // Cifras tipo nota (con decimales) o porcentajes. Los enteros sueltos («3 ejercicios», «semana 2») no se revisan: son parte del consejo.
    [GeneratedRegex(@"(?<![\w.,])\d+[.,]\d+(?![\w])|\d+(?:[.,]\d+)?\s?%")]
    private static partial Regex Cifras();
    [GeneratedRegex(@"\d+(?:[.,]\d+)?")]
    private static partial Regex Numeros();

    static decimal? Valor(string texto)
    {
        var limpio = texto.Replace("%", "").Trim().Replace(',', '.');
        return decimal.TryParse(limpio, NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    public static List<string> Revisar(string respuesta, params string[] fuentes)
    {
        var permitidos = fuentes.SelectMany(f => Numeros().Matches(f).Select(m => Valor(m.Value))).OfType<decimal>().ToHashSet();
        var sospechosas = new List<string>();
        foreach (Match m in Cifras().Matches(respuesta))
            if (Valor(m.Value) is { } v && !permitidos.Any(p => Math.Abs(p - v) < 0.011m))
                sospechosas.Add(m.Value.Trim());
        return [.. sospechosas.Distinct()];
    }

    // Variante para la ruta: junta todos los números de la ruta de plantilla como «datos permitidos».
    public static List<string> RevisarRuta(string textoModelo, RutaDatos ruta) => Revisar(textoModelo, JsonSerializer.Serialize(ruta));
}

public static class PromptsAsistente
{
    public const string Sistema = """
        Eres «Asistente académico», un asesor pedagógico que ayuda a un docente universitario a entender cómo va su curso y a apoyar a sus estudiantes.
        Reglas obligatorias:
        - Responde SIEMPRE en español, con tono profesional y cercano. Máximo 220 palabras, salvo que el docente pida más detalle.
        - Formato obligatorio: organiza la respuesta en secciones cortas. Cada sección empieza con un título en negrita en su propia línea (por ejemplo **Panorama**, **Estudiantes a atender**, **Qué hacer**) y sigue con viñetas con guion, una idea por línea y máximo 15 palabras por viñeta. Para estudiantes, temas o actividades usa una viñeta por elemento. Nunca escribas párrafos largos ni enumeres varios elementos en una misma frase.
        - Los datos del curso están en el bloque «DATOS DEL CURSO». Ya fueron verificados por el sistema: usa solo esas cifras, cópialas tal cual y no inventes notas, fechas, actividades, temas ni personas.
        - Los estudiantes aparecen con seudónimos («Estudiante A», «Estudiante B»…). Escribe siempre ese mismo texto y, si mencionas a varios, repite la palabra: «Estudiante A y Estudiante B». Nunca inventes nombres ni uses otros.
        - Empieza tu respuesta con la etiqueta [CURSO] si te apoyas en los datos del curso, o [GENERAL] si respondes con conocimiento pedagógico general sin usar esos datos.
        - Si la pregunta no trata de docencia, estudio o evaluación, responde con una sola frase indicando que solo puedes ayudar con temas académicos.
        - Si faltan datos para responder, dilo y explica qué dato haría falta.
        - Las causas que menciones son hipótesis para conversar con el estudiante, no certezas. No des diagnósticos médicos ni psicológicos.
        - No prometas que una nota vaya a subir ni propongas cambiar calificaciones: tú aconsejas y el docente decide.
        - No incluyas enlaces ni correos.
        """;

    public const string SistemaRuta = """
        Eres un asesor pedagógico. Recibirás la RUTA DE MEJORA de UN estudiante, calculada por el sistema, en JSON: su situación, los temas débiles y las etapas
        (cada etapa es una actividad pendiente o un grupo de ellas, con la nota objetivo). Tu trabajo es redactar el contenido de la ruta.
        Responde SOLO con un objeto JSON, sin texto adicional, con exactamente estas claves:
        - "resumen": string, máximo 2 frases, qué necesita el estudiante para mejorar.
        - "etapas": arreglo con UN objeto por cada etapa recibida, en el mismo orden, cada uno con la clave "acciones": arreglo de 2 a 3 strings con acciones
          concretas de estudio para esa etapa, escritas en imperativo dirigidas al estudiante (tú).
        - "accionesDocente": arreglo de 1 a 3 strings con acciones concretas del docente.
        - "mensajeEstudiante": string de 50 a 110 palabras dirigido al estudiante en segunda persona (tú), tono cordial y motivador, sin saludo ni despedida.
        Reglas obligatorias: no cambies ni inventes cifras (usa solo las del JSON); no inventes actividades, fechas ni temas; no prometas aprobar ni subir la nota;
        no incluyas enlaces ni correos; escribe en español.
        """;

    // Datos de la ruta que se le entregan al modelo: solo hechos, sin textos ya redactados.
    public static string UsuarioRuta(DatosCurso d, RutaDatos r) => "RUTA DE MEJORA (JSON):\n" + JsonSerializer.Serialize(new
    {
        materia = d.Curso.MateriaNombre,
        notaMinima = r.NotaMinima,
        resultadoActual = r.Resultado,
        estado = r.Estado,
        situacion = r.Situacion,
        promedioRequerido = r.PromedioRequerido,
        maximoPosible = r.MaximoPosible,
        actividadesPendientes = r.Pendientes,
        temasDebiles = r.Temas.Select(t => new { t.Tema, t.Promedio, t.Evidencia }),
        etapas = r.Etapas.Select(e => new { e.Numero, e.Titulo, e.Plazo, metaDeNota = e.Meta, temasQueEvalua = e.Temas, temasDebilesDelEstudiante = e.Debiles, actividades = e.Actividades.Select(a => new { a.Nombre, a.Tipo, aporteALaNotaFinalPorcentaje = a.Aporte }) })
    }, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = false });
}
