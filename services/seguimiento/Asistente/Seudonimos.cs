using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
namespace EduMonitor.Seguimiento.Asistente;

public sealed record Persona(int Id, string Apellidos, string Nombres, string Correo)
{
    public string Completo => $"{Apellidos} {Nombres}".Trim();
}

// Resultado de buscar estudiantes dentro de lo que escribió el docente.
//  Resueltos: se identificaron sin dudas (por nombre completo).
//  Dudas: cada elemento es un grupo de estudiantes entre los que el sistema NO puede elegir solo; se le pregunta al docente.
public sealed record Busqueda(List<Persona> Resueltos, List<List<Persona>> Dudas);

// Seudónimos y búsqueda de estudiantes. El modelo de IA nunca recibe un nombre ni una cédula reales: recibe «Estudiante A», «Estudiante B»…
// El servidor traduce al enviar y al mostrar, así el docente ve nombres reales y la nube solo ve letras.
public static partial class Seudonimos
{
    static readonly HashSet<string> Conectores = ["del", "los", "las", "von", "van", "san", "mac"];
    public const int MaxAlias = 52;

    public static string Normalizar(string s)
    {
        var d = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var c in d)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    // Palabras de un nombre que sirven para reconocerlo: sin conectores ni palabras de menos de 3 letras.
    static List<string> Fichas(Persona p) =>
        [.. PalabrasRegex().Matches(Normalizar(p.Completo)).Select(m => m.Value).Where(t => t.Length >= 3 && !Conectores.Contains(t)).Distinct()];

    [GeneratedRegex(@"[a-z0-9]+")]
    private static partial Regex PalabrasRegex();
    [GeneratedRegex(@"\p{L}[\p{L}\p{M}]*|\d+")]
    private static partial Regex PalabraOriginal();
    [GeneratedRegex(@"(?<!\d)\d{10}(?!\d)")]
    private static partial Regex CedulaRegex();
    [GeneratedRegex(@"Estudiante ([A-Z]{1,2})(?![A-Za-z])")]
    private static partial Regex AliasRegex();
    // Plural: «Estudiantes D y E», «Estudiantes A, B y C».
    [GeneratedRegex(@"Estudiantes ([A-Z]{1,2}(?:(?:, | y | e )[A-Z]{1,2})*)(?![A-Za-z])")]
    private static partial Regex AliasPluralRegex();
    // Final de un fragmento que podría ser el comienzo de un seudónimo (singular o plural): se retiene hasta saber cómo sigue.
    [GeneratedRegex(@"(?:E|Es|Est|Estu|Estud|Estudi|Estudia|Estudian|Estudiant)$|Estudiantes?(?: (?:[A-Z]{1,2}(?:(?:, ?| y ?| e ?)(?:[A-Z]{1,2})?)*)?)?$")]
    private static partial Regex ColaAlias();

    public static string Alias(int indice) => indice < 26 ? $"Estudiante {(char)('A' + indice)}" : $"Estudiante A{(char)('A' + indice - 26)}";

    // ---------- Buscar a quién se refiere el docente ----------

    public static Busqueda Buscar(string texto, IReadOnlyList<Persona> lista)
    {
        var resueltos = new List<Persona>();
        var dudas = new List<List<Persona>>();
        var palabras = PalabraOriginal().Matches(texto).Select(m => (Norm: Normalizar(m.Value), Mayuscula: char.IsUpper(m.Value[0]))).ToList();
        var presentes = palabras.Select(p => p.Norm).ToHashSet();
        var aciertos = lista
            .Select(p => (Persona: p, Fichas: Fichas(p), Coinciden: Fichas(p).Where(presentes.Contains).ToList()))
            .Where(x => x.Coinciden.Count > 0).ToList();

        // Coincidencias fuertes: al menos dos palabras del nombre. Si dos personas comparten palabras, se agrupan y se queda la que más coincide;
        // si hay empate no se adivina: se pregunta.
        var fuertes = aciertos.Where(x => x.Coinciden.Count >= 2).ToList();
        var usadas = new HashSet<string>();
        var pendientes = new List<(Persona Persona, List<string> Fichas, List<string> Coinciden)>(fuertes.Select(x => (x.Persona, x.Fichas, x.Coinciden)));
        while (pendientes.Count > 0)
        {
            var grupo = new List<(Persona Persona, List<string> Fichas, List<string> Coinciden)> { pendientes[0] };
            pendientes.RemoveAt(0);
            for (var cambio = true; cambio;)
            {
                cambio = false;
                foreach (var otro in pendientes.ToList())
                    if (grupo.Any(g => g.Coinciden.Intersect(otro.Coinciden).Any()))
                    {
                        grupo.Add(otro);
                        pendientes.Remove(otro);
                        cambio = true;
                    }
            }
            var mejor = grupo.Max(g => g.Coinciden.Count);
            var cima = grupo.Where(g => g.Coinciden.Count == mejor).ToList();
            foreach (var g in grupo)
                foreach (var t in g.Coinciden)
                    usadas.Add(t);
            if (cima.Count == 1)
                resueltos.Add(cima[0].Persona);
            else
                dudas.Add([.. cima.Select(c => c.Persona)]);
        }

        // Coincidencias débiles: una sola palabra, con mayúscula inicial y de 4 letras o más. Nunca se asumen: se pide confirmación.
        var debiles = aciertos.Where(x => x.Coinciden.Count == 1 && !usadas.Contains(x.Coinciden[0]) && x.Coinciden[0].Length >= 4 &&
            palabras.Any(p => p.Norm == x.Coinciden[0] && p.Mayuscula)).GroupBy(x => x.Coinciden[0]);
        foreach (var g in debiles)
            dudas.Add([.. g.Select(x => x.Persona)]);
        return new Busqueda(resueltos, dudas);
    }

    // Búsqueda para el selector de la pantalla: por nombre, sin importar tildes ni mayúsculas.
    public static List<Persona> Filtrar(string consulta, IReadOnlyList<Persona> lista, int max = 8)
    {
        var q = Normalizar(consulta).Trim();
        if (q.Length < 2)
            return [];
        var partes = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return [.. lista.Where(p =>
        {
            var nombre = Normalizar(p.Completo);
            return partes.All(nombre.Contains);
        }).Take(max)];
    }

    // ---------- Ocultar: lo que sale hacia el modelo ----------

    // Cambia por seudónimos las palabras del nombre de un estudiante. Los nombres de estudiantes no identificados (con mayúscula inicial)
    // también se tapan, y cualquier número de 10 dígitos (parece una cédula) se omite: nada personal llega al modelo.
    public static string Ocultar(string texto, IReadOnlyDictionary<int, string> aliasDe, IReadOnlyList<Persona> lista)
    {
        texto = CedulaRegex().Replace(texto, "[número omitido]");
        var fichasDe = lista.ToDictionary(p => p.Id, Fichas);
        var palabras = PalabraOriginal().Matches(texto).ToList();
        var sb = new StringBuilder();
        var cursor = 0;
        for (var i = 0; i < palabras.Count;)
        {
            // Una «racha» es una secuencia de palabras de nombre separadas solo por espacios: «Clara Mena Ríos».
            int j = i;
            var racha = new List<string>();
            while (j < palabras.Count && EsDeNombre(palabras[j].Value, aliasDe, fichasDe) && (j == i || SoloEspacios(texto, palabras[j - 1], palabras[j])))
            {
                racha.Add(Normalizar(palabras[j].Value));
                j++;
            }
            if (racha.Count == 0)
            {
                i++;
                continue;
            }
            var reemplazo = ElegirReemplazo(racha, aliasDe, fichasDe, lista, palabras.Skip(i).Take(racha.Count).Any(w => char.IsUpper(w.Value[0])));
            if (reemplazo is null)
            {
                i = j;
                continue;
            }
            sb.Append(texto, cursor, palabras[i].Index - cursor).Append(reemplazo);
            cursor = palabras[j - 1].Index + palabras[j - 1].Length;
            i = j;
        }
        sb.Append(texto, cursor, texto.Length - cursor);
        return sb.ToString();
    }

    static bool SoloEspacios(string texto, Match anterior, Match siguiente) =>
        string.IsNullOrWhiteSpace(texto[(anterior.Index + anterior.Length)..siguiente.Index]);

    static bool EsDeNombre(string palabra, IReadOnlyDictionary<int, string> aliasDe, Dictionary<int, List<string>> fichasDe)
    {
        var n = Normalizar(palabra);
        return n.Length >= 3 && fichasDe.Any(f => f.Value.Contains(n) && (aliasDe.ContainsKey(f.Key) || (char.IsUpper(palabra[0]) && n.Length >= 4)));
    }

    static string? ElegirReemplazo(List<string> racha, IReadOnlyDictionary<int, string> aliasDe, Dictionary<int, List<string>> fichasDe, IReadOnlyList<Persona> lista, bool conMayuscula)
    {
        var mejor = aliasDe.Where(a => fichasDe.ContainsKey(a.Key)).Select(a => (a.Key, a.Value, Puntos: racha.Count(t => fichasDe[a.Key].Contains(t)))).Where(x => x.Puntos > 0)
            .OrderByDescending(x => x.Puntos).FirstOrDefault();
        if (mejor.Value is not null)
            return mejor.Value;
        // Palabras de un estudiante no identificado: solo se tapan si parecen un nombre propio (mayúscula inicial).
        return conMayuscula ? "[nombre omitido]" : null;
    }

    // ---------- Mostrar: lo que vuelve del modelo ----------

    // Cambia «Estudiante A» por el nombre real. Los seudónimos que no pertenecen a la conversación se dejan igual.
    public static string Mostrar(string texto, IReadOnlyDictionary<string, string> nombrePorAlias)
    {
        // Primero el plural: «Estudiantes D y E» → «Nombre1 y Nombre2». Si alguna letra no pertenece a la conversación, se deja como vino.
        texto = AliasPluralRegex().Replace(texto, m =>
        {
            var nombres = LetrasRegex().Matches(m.Groups[1].Value).Select(l => nombrePorAlias.TryGetValue($"Estudiante {l.Value}", out var n) ? n : null).ToList();
            if (nombres.Count == 0 || nombres.Any(n => n is null))
                return m.Value;
            return nombres.Count == 1 ? nombres[0]! : string.Join(", ", nombres.Take(nombres.Count - 1)) + " y " + nombres[^1];
        });
        return AliasRegex().Replace(texto, m => nombrePorAlias.TryGetValue($"Estudiante {m.Groups[1].Value}", out var n) ? n : m.Value);
    }

    [GeneratedRegex(@"[A-Z]{1,2}")]
    private static partial Regex LetrasRegex();

    public static IEnumerable<string> AliasEn(string texto) => AliasRegex().Matches(texto).Select(m => $"Estudiante {m.Groups[1].Value}")
        .Concat(AliasPluralRegex().Matches(texto).SelectMany(m => LetrasRegex().Matches(m.Groups[1].Value).Select(l => $"Estudiante {l.Value}"))).Distinct();

    // Traduce un texto que llega por fragmentos sin cortar un seudónimo por la mitad («Estudian» + «te A»).
    public sealed class Traductor(IReadOnlyDictionary<string, string> nombrePorAlias)
    {
        string retenido = "";
        public string Procesar(string fragmento)
        {
            var todo = retenido + fragmento;
            var cola = ColaAlias().Match(todo);
            var corte = cola.Success ? cola.Index : todo.Length;
            retenido = todo[corte..];
            return Mostrar(todo[..corte], nombrePorAlias);
        }
        public string Terminar()
        {
            var resto = Mostrar(retenido, nombrePorAlias);
            retenido = "";
            return resto;
        }
    }
}
