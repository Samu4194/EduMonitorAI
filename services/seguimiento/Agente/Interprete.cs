using System.Text.Json;
using System.Text.RegularExpressions;
namespace EduMonitor.Seguimiento.Agente;

// Instrucciones del agente. Van aparte del código para poder revisarlas y ajustarlas sin tocar la lógica.
public static class Prompts
{
    public const string Sistema = """
        Eres un asistente pedagógico de una universidad. Ayudas a un docente a apoyar a un estudiante que tiene dificultades académicas.
        Recibirás los datos académicos de UN estudiante en JSON (sin nombre) y los hallazgos que detectó un sistema de reglas.
        Responde SOLO con un objeto JSON, sin texto adicional, con exactamente estas claves:
        - "resumen": string, máximo 2 frases, qué le ocurre al estudiante según los datos.
        - "causasProbables": arreglo de 1 a 3 strings. Son hipótesis a conversar con el estudiante, no afirmaciones.
        - "accionesDocente": arreglo de 1 a 4 strings, acciones concretas que puede tomar el docente.
        - "accionesEstudiante": arreglo de 1 a 4 strings, acciones concretas que puede hacer el estudiante.
        - "mensajeEstudiante": string de 50 a 110 palabras, dirigido al estudiante en segunda persona (tú), tono cordial, respetuoso y motivador.
          Sin saludo ni despedida (el sistema los agrega).
        Reglas obligatorias: usa únicamente los datos recibidos; no inventes notas, fechas, actividades ni nombres; no hagas diagnósticos
        médicos ni psicológicos; no prometas cambios de nota ni de aprobación; no incluyas enlaces ni correos; escribe en español.
        """;

    public static string Usuario(ContextoIA contexto) => $"Datos académicos del estudiante (escala de 0 a 10):\n{contexto.Serializar()}";
}

// Valida lo que devolvió el modelo. Un modelo puede responder fuera de formato, con texto de más o con contenido inadecuado:
// nada de eso llega al docente; si la respuesta no pasa la validación se descarta y se usa el siguiente proveedor o la plantilla.
public static partial class Interprete
{
    public const int MaxLista = 4, MaxTexto = 320;

    [GeneratedRegex(@"https?://|www\.|[\w.+-]+@[\w-]+\.[\w.]+", RegexOptions.IgnoreCase)]
    private static partial Regex Prohibido();

    // Los modelos pequeños a veces mezclan caracteres chinos, cirílicos o árabes en medio de una respuesta en español.
    [GeneratedRegex(@"[Ѐ-ӿ֐-ۿऀ-෿฀-໿぀-ヿ㐀-鿿가-힯]")]
    private static partial Regex OtroAlfabeto();

    // Texto con enlaces, correos o caracteres de otro alfabeto: no se le muestra al docente.
    public static bool Inadecuado(string texto) => Prohibido().IsMatch(texto) || OtroAlfabeto().IsMatch(texto);

    // Extrae el objeto JSON de la respuesta del modelo (tolera texto alrededor y bloques ```json).
    public static bool InterpretarJson(string? contenido, out JsonElement raiz, out string? motivo)
    {
        motivo = null;
        raiz = default;
        if (string.IsNullOrWhiteSpace(contenido))
        {
            motivo = "respuesta vacía";
            return false;
        }
        // Algunos modelos envuelven el JSON en ```json ... ```: se toma desde la primera llave hasta la última.
        var ini = contenido.IndexOf('{');
        var fin = contenido.LastIndexOf('}');
        if (ini < 0 || fin <= ini)
        {
            motivo = "no contiene JSON";
            return false;
        }
        try { raiz = JsonDocument.Parse(contenido[ini..(fin + 1)]).RootElement; }
        catch (JsonException) { motivo = "JSON inválido"; return false; }
        if (raiz.ValueKind != JsonValueKind.Object)
        {
            motivo = "no es un objeto";
            return false;
        }
        return true;
    }

    public static Borrador? Interpretar(string? contenido, out string? motivo)
    {
        if (!InterpretarJson(contenido, out var raiz, out motivo))
            return null;
        var resumen = Texto(raiz, "resumen", 600);
        var mensaje = Texto(raiz, "mensajeEstudiante", 1200);
        var causas = Lista(raiz, "causasProbables");
        var docente = Lista(raiz, "accionesDocente");
        var estudiante = Lista(raiz, "accionesEstudiante");
        if (resumen.Length < 10 || mensaje.Length < 40 || causas.Count == 0 || docente.Count == 0 || estudiante.Count == 0)
        {
            motivo = "faltan campos o están muy cortos";
            return null;
        }
        var todo = string.Join("\n", new[] { resumen, mensaje }.Concat(causas).Concat(docente).Concat(estudiante));
        if (Prohibido().IsMatch(todo))
        {
            motivo = "incluye enlaces o correos";
            return null;
        }
        if (OtroAlfabeto().IsMatch(todo))
        {
            motivo = "mezcla caracteres de otro idioma";
            return null;
        }
        return new Borrador(resumen, causas, docente, estudiante, mensaje);
    }

    public static string Texto(JsonElement o, string clave, int max)
    {
        foreach (var p in o.EnumerateObject())
            if (p.NameEquals(clave) || p.Name.Equals(clave, StringComparison.OrdinalIgnoreCase))
            {
                var t = p.Value.ValueKind == JsonValueKind.String ? (p.Value.GetString() ?? "").Trim() : "";
                return t.Length > max ? t[..max].TrimEnd() + "…" : t;
            }
        return "";
    }
    public static List<string> Lista(JsonElement o, string clave)
    {
        foreach (var p in o.EnumerateObject())
            if (p.Name.Equals(clave, StringComparison.OrdinalIgnoreCase))
            {
                IEnumerable<string> items = p.Value.ValueKind switch
                {
                    JsonValueKind.Array => p.Value.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString() ?? ""),
                    JsonValueKind.String => [p.Value.GetString() ?? ""],
                    _ => []
                };
                return items.Select(x => x.Trim().TrimStart('-', '•', '*', ' ')).Where(x => x.Length >= 5)
                    .Select(x => x.Length > MaxTexto ? x[..MaxTexto].TrimEnd() + "…" : x).Take(MaxLista).ToList();
            }
        return [];
    }
}
