using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using EduMonitor.Evaluaciones.DTOs;
using EduMonitor.Evaluaciones.Services;
using EduMonitor.Infrastructure;

namespace EduMonitor.Evaluaciones.Moodle;

public record AlumnoMoodle(int Id, string Codigo, string Nombres, string Apellidos, string Correo);
public record TemaMoodle(int Numero, string Nombre, string Contenido);
public record SeccionMoodle(int Id, int Numero, string Nombre, List<TemaMoodle> Temas);
// NumeroTema: pista del formato antiguo del aula (código terminado en -T1, -T2…) que relaciona una tarea con la temática del mismo número.
public record ActividadMoodle(int Cm, int Instancia, int Seccion, string Nombre, string Modulo, string Tipo, string Codigo, DateOnly? Fecha = null, int? NumeroTema = null);
// Ponderación por tipo (en %) cuando el libro de calificaciones de Moodle tiene una categoría por tipo de actividad.
public record PesosMoodle(decimal Tareas, decimal Ape, decimal Examen);

// Cómo se reconoce el tipo de una actividad de Moodle (Tarea, APE o Examen). Se puede ajustar en la configuración (Moodle:Tipos).
public sealed record ReglasTipos(Regex Examen, Regex Ape)
{
    public static ReglasTipos Predeterminadas { get; } = new(
        new Regex(@"\b(examen|prueba|evaluaci[oó]n|parcial|quimestre|test)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
        new Regex(@"\b(ape|pr[aá]ctic[ao]s?|laboratorio|experiment\w*|proyecto|taller)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));

    public static ReglasTipos De(IConfiguration? config)
    {
        Regex Leer(string clave, Regex defecto) =>
            string.IsNullOrWhiteSpace(config?[$"Moodle:Tipos:{clave}"]) ? defecto : new Regex(config![$"Moodle:Tipos:{clave}"]!, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
        return new(Leer("Examen", Predeterminadas.Examen), Leer("Ape", Predeterminadas.Ape));
    }
}

// Lo que EduMonitor entiende de un curso de Moodle. Moodle es la fuente de verdad: aquí solo se lee y se normaliza.
// Principios (los mismos de un sistema de solo lectura):
//  - Una nota 0 es 0; una celda vacía es «no disponible» (null) y nunca se convierte en 0.
//  - La escala de Moodle (0-100, 0-10, 0-20…) se normaliza a 0-10.
//  - Lo que Moodle no entrega o no cuadra no tumba todo el curso: la nota queda «no disponible» y se avisa. Solo se rechaza lo ambiguo
//    (estudiantes con el mismo identificador, actividades con datos contradictorios, un curso sin matrícula o sin actividades calificadas).
public record AulaMoodle(List<AlumnoMoodle> Alumnos, List<SeccionMoodle> Secciones, List<ActividadMoodle> Actividades,
    Dictionary<(int Usuario, int Cm), decimal?> Notas, List<string>? Avisos = null, PesosMoodle? Pesos = null, List<AlumnoMoodle>? Profesores = null)
{
    private static void Exigir(bool ok, string mensaje)
    {
        if (!ok)
            throw new ApiException(409, mensaje);
    }
    private static string? Texto(JsonElement e, string campo)
    {
        if (!e.TryGetProperty(campo, out var v) || v.ValueKind != JsonValueKind.String)
            return null;
        var t = v.GetString()?.Trim();
        return string.IsNullOrEmpty(t) ? null : t;
    }
    private static string Recortar(string t, int max) => t.Length <= max ? t : t[..(max - 1)].TrimEnd() + "…";
    private static int Entero(JsonElement e, string campo) => e.GetProperty(campo).GetInt32();
    private static decimal? Decimal(JsonElement e, string campo) =>
        e.TryGetProperty(campo, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDecimal() : null;
    private static bool Verdadero(JsonElement e, string campo) => e.TryGetProperty(campo, out var v) && v.ValueKind == JsonValueKind.True;

    // Texto sin etiquetas HTML, para usarlo como contenido de un tema.
    private static string SinHtml(string html)
    {
        var t = Regex.Replace(html, "<[^>]+>", " ");
        return Regex.Replace(WebUtilityDecode(t), @"\s+", " ").Trim();
    }
    private static string WebUtilityDecode(string t) => System.Net.WebUtility.HtmlDecode(t);

    // Las temáticas se publican como pares <h4>Temática N: nombre</h4><p>contenido</p> en el resumen de la sección.
    // Si el resumen no sigue ese formato (lo normal en un curso cualquiera) la sección entera es un único tema.
    private static List<TemaMoodle> Temas(string nombreSeccion, string resumen)
    {
        try
        {
            var partes = XElement.Parse("<section>" + resumen + "</section>").Elements().ToArray();
            if (partes.Length >= 2 && partes.Length % 2 == 0)
            {
                var temas = new List<TemaMoodle>();
                for (var i = 0; i < partes.Length; i += 2)
                {
                    var m = Regex.Match(partes[i].Value.Trim(), @"^Temática\s+(\d+)\s*:\s*(.+)$");
                    var contenido = partes[i + 1].Value.Trim();
                    if (partes[i].Name != "h4" || partes[i + 1].Name != "p" || !m.Success || m.Groups[2].Value.Length is < 3 or > 150 || contenido.Length is < 3 or > 300)
                        goto Unico;
                    temas.Add(new(int.Parse(m.Groups[1].Value), m.Groups[2].Value, contenido));
                }
                if (temas.All(t => t.Numero > 0) && temas.DistinctBy(t => t.Numero).Count() == temas.Count)
                    return temas;
            }
        }
        catch (System.Xml.XmlException) { /* el resumen no es XML bien formado: se usa la sección como un solo tema */ }
        Unico:
        ;
        var texto = SinHtml(resumen);
        return [new(1, Recortar(nombreSeccion, 150), texto.Length >= 3 ? Recortar(texto, 300) : Recortar(nombreSeccion, 300))];
    }

    // Fecha de entrega o de cierre, si la actividad la tiene. Moodle la manda como marca de tiempo; el día se toma en hora de Ecuador (UTC-5).
    private static DateOnly? Fecha(JsonElement modulo)
    {
        if (!modulo.TryGetProperty("dates", out var fechas) || fechas.ValueKind != JsonValueKind.Array)
            return null;
        foreach (var clave in new[] { "duedate", "timeclose", "submissionend", "deadline" })
            foreach (var f in fechas.EnumerateArray())
                if (f.TryGetProperty("dataid", out var id) && id.GetString() == clave && f.TryGetProperty("timestamp", out var t) && t.ValueKind == JsonValueKind.Number && t.GetInt64() > 0)
                    return DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(t.GetInt64()).UtcDateTime.AddHours(-5));
        return null;
    }

    private static (string Tipo, int? NumeroTema) TipoDe(string modulo, string nombre, string? codigo, ReglasTipos reglas)
    {
        // Formato antiguo del aula de ejemplo: el código lo dice (…-T1, …-APE, …-EX).
        if (codigo is not null)
        {
            if (Regex.IsMatch(codigo, @"-APE$", RegexOptions.IgnoreCase)) return ("APE", null);
            if (Regex.IsMatch(codigo, @"-EX$", RegexOptions.IgnoreCase)) return ("Examen", null);
            var t = Regex.Match(codigo, @"-T([1-9]\d*)$", RegexOptions.IgnoreCase);
            if (t.Success) return ("Tarea", int.Parse(t.Groups[1].Value));
        }
        if (modulo == "quiz" || reglas.Examen.IsMatch(nombre)) return ("Examen", null);
        if (reglas.Ape.IsMatch(nombre)) return ("APE", null);
        return ("Tarea", null);
    }

    public static AulaMoodle Leer(int curso, string codigo, JsonElement contenidos, JsonElement usuarios, JsonElement informe, ReglasTipos? reglas = null)
    {
        reglas ??= ReglasTipos.Predeterminadas;
        var avisos = new List<string>();
        void Avisar(string texto)
        {
            if (avisos.Count < 40)
                avisos.Add(texto);
        }

        // Matrícula: los usuarios con rol de estudiante. Sin código institucional se usa el usuario de Moodle.
        var alumnos = usuarios.EnumerateArray().Where(u => u.TryGetProperty("roles", out var roles) && roles.ValueKind == JsonValueKind.Array &&
            roles.EnumerateArray().Any(r => Texto(r, "shortname") == "student")).Select(u =>
        {
            var id = Entero(u, "id");
            var cod = Texto(u, "idnumber") ?? Texto(u, "username") ?? $"U{id}";
            var correo = Texto(u, "email");
            if (correo is null)
                Avisar($"El estudiante {cod} no tiene correo en Moodle.");
            return new AlumnoMoodle(id, Recortar(cod, 100), Recortar(Texto(u, "firstname") ?? "(sin nombre)", 100), Recortar(Texto(u, "lastname") ?? "(sin apellido)", 100),
                Recortar(correo ?? $"{cod.ToLowerInvariant()}@moodle.invalid", 254));
        }).ToList();
        Exigir(alumnos.Count > 0 && alumnos.DistinctBy(x => x.Id).Count() == alumnos.Count &&
            alumnos.DistinctBy(x => x.Codigo, StringComparer.OrdinalIgnoreCase).Count() == alumnos.Count, "Matrícula ausente o ambigua.");

        // Profesores del curso (con o sin permiso de edición): sirven para comprobar que quien califica en Moodle es el docente asignado en EduMonitor.
        var profesores = usuarios.EnumerateArray().Where(u => u.TryGetProperty("roles", out var roles) && roles.ValueKind == JsonValueKind.Array &&
            roles.EnumerateArray().Any(r => Texto(r, "shortname") is "editingteacher" or "teacher")).Select(u =>
        {
            var id = Entero(u, "id");
            return new AlumnoMoodle(id, Recortar(Texto(u, "idnumber") ?? Texto(u, "username") ?? $"U{id}", 100), Recortar(Texto(u, "firstname") ?? "", 100),
                Recortar(Texto(u, "lastname") ?? "", 100), Recortar(Texto(u, "email") ?? "", 254));
        }).ToList();

        // Secciones y módulos del curso
        var seccionesTodas = new List<(int Id, int Numero, string Nombre, string Resumen)>();
        var modulos = new Dictionary<int, (int Instancia, int Seccion, string Nombre, string Modulo, JsonElement Json)>();
        foreach (var s in contenidos.EnumerateArray())
        {
            var n = Entero(s, "section");
            var id = Entero(s, "id");
            var nombre = Recortar(Texto(s, "name") ?? $"Sección {n}", 120);
            seccionesTodas.Add((id, n, nombre, Texto(s, "summary") ?? ""));
            if (s.TryGetProperty("modules", out var mods) && mods.ValueKind == JsonValueKind.Array)
                foreach (var m in mods.EnumerateArray())
                    modulos[Entero(m, "id")] = (Entero(m, "instance"), id, Recortar(Texto(m, "name") ?? $"Actividad {Entero(m, "id")}", 120), Texto(m, "modname") ?? "", m);
        }

        // Libro de calificaciones: una fila por estudiante con sus ítems. Solo las actividades que aparecen en él se importan.
        var filas = informe.GetProperty("usergrades").EnumerateArray().ToDictionary(f => Entero(f, "userid"));
        foreach (var a in alumnos.Where(a => !filas.ContainsKey(a.Id)))
            Avisar($"Moodle no entregó el libro de calificaciones de {a.Codigo}: sus notas quedan como no disponibles.");
        foreach (var fila in filas.Values)
            Exigir(Entero(fila, "courseid") == curso, "El informe pertenece a otro curso.");

        var actividades = new Dictionary<int, ActividadMoodle>();
        var notas = new Dictionary<(int, int), decimal?>();
        var categorias = new Dictionary<int, decimal>();       // categoría de calificación -> peso en el curso
        var tipoDeCategoria = new Dictionary<int, HashSet<string>>();
        foreach (var alumno in alumnos)
        {
            if (!filas.TryGetValue(alumno.Id, out var fila))
                continue;
            var items = fila.GetProperty("gradeitems").EnumerateArray().ToArray();
            foreach (var item in items.Where(i => Texto(i, "itemtype") == "category"))
                if (Decimal(item, "weightraw") is { } peso && item.TryGetProperty("iteminstance", out var inst) && inst.ValueKind == JsonValueKind.Number)
                    categorias[inst.GetInt32()] = peso;
            foreach (var item in items.Where(i => Texto(i, "itemtype") == "mod"))
            {
                var cm = Entero(item, "cmid");
                if (!modulos.TryGetValue(cm, out var mod))
                {
                    Avisar($"Moodle calificó el módulo {cm}, que no aparece en el contenido del curso: se omitió.");
                    continue;
                }
                var key = Texto(item, "idnumber");
                Exigir(Texto(item, "itemmodule") == mod.Modulo && Entero(item, "iteminstance") == mod.Instancia, "Actividad sin correspondencia inequívoca.");
                var (tipo, numeroTema) = TipoDe(mod.Modulo, mod.Nombre, key, reglas);
                var a = new ActividadMoodle(cm, mod.Instancia, mod.Seccion, mod.Nombre, mod.Modulo, tipo, Recortar(key ?? $"cm{cm}", 100), Fecha(mod.Json), numeroTema);
                Exigir(!actividades.TryGetValue(cm, out var otra) || otra == a, "El informe contiene identificadores contradictorios.");
                actividades[cm] = a;
                if (item.TryGetProperty("categoryid", out var cat) && cat.ValueKind == JsonValueKind.Number)
                {
                    if (!tipoDeCategoria.TryGetValue(cat.GetInt32(), out var tipos))
                        tipoDeCategoria[cat.GetInt32()] = tipos = [];
                    tipos.Add(tipo);
                }

                // Nota normalizada a 0-10. Una celda vacía o que Moodle aún debe recalcular queda «no disponible»: nunca 0.
                var min = Decimal(item, "grademin") ?? 0m;
                var max = Decimal(item, "grademax") ?? 10m;
                var raw = item.TryGetProperty("graderaw", out var g) && g.ValueKind == JsonValueKind.Number ? g.GetDecimal() : (decimal?)null;
                decimal? valor = null;
                if (raw is { } r && max > min && !Verdadero(item, "gradeneedsupdate"))
                {
                    valor = Math.Round((r - min) / (max - min) * 10m, 4, MidpointRounding.AwayFromZero);
                    if (valor is < 0 or > 10)
                    {
                        Avisar($"La nota de {alumno.Codigo} en «{mod.Nombre}» queda fuera de la escala: se ajustó a {(valor < 0 ? 0 : 10)}.");
                        valor = Math.Clamp(valor.Value, 0m, 10m);
                    }
                }
                else if (raw is not null)
                    Avisar($"La nota de {alumno.Codigo} en «{mod.Nombre}» no está lista en Moodle: queda como no disponible.");
                notas[(alumno.Id, cm)] = valor;
            }
        }
        Exigir(actividades.Count > 0, "Moodle no tiene actividades calificadas en este curso.");

        // Una celda ausente (la actividad existe pero Moodle no entregó la nota de ese estudiante) es «no disponible», no un error.
        foreach (var alumno in alumnos)
            foreach (var cm in actividades.Keys)
                if (!notas.ContainsKey((alumno.Id, cm)))
                {
                    notas[(alumno.Id, cm)] = null;
                    if (filas.ContainsKey(alumno.Id))
                        Avisar($"Moodle no entregó la nota de {alumno.Codigo} en «{actividades[cm].Nombre}»: queda como no disponible.");
                }

        // Unidades: las secciones que tienen al menos una actividad calificada; cada una con sus temas.
        var usadas = actividades.Values.Select(a => a.Seccion).ToHashSet();
        var secciones = seccionesTodas.Where(s => usadas.Contains(s.Id)).Select(s => new SeccionMoodle(s.Id, s.Numero, s.Nombre, Temas(s.Nombre, s.Resumen))).ToList();
        Exigir(secciones.Count > 0 && secciones.DistinctBy(s => s.Id).Count() == secciones.Count, "Faltan unidades o actividades en Moodle.");
        Exigir(actividades.Values.DistinctBy(a => a.Codigo).Count() == actividades.Count, "Hay códigos de actividad duplicados.");

        // Ponderación: solo si el libro tiene una categoría por tipo de actividad (Tareas, APE, Exámenes).
        var pesos = PesosDeCategorias(categorias, tipoDeCategoria);
        if (pesos is null)
            Avisar("El libro de calificaciones de Moodle no tiene una categoría por tipo de actividad: se usa la ponderación de EduMonitor para este curso.");

        // El total de Moodle es informativo: si difiere de lo que calcula EduMonitor se avisa (la fuente de las notas sigue siendo Moodle).
        var referencia = pesos is null ? Calculo.Predeterminada : new PonderacionView(pesos.Tareas, pesos.Ape, pesos.Examen, Calculo.Predeterminada.NotaMinima, true);
        foreach (var alumno in alumnos.Where(a => filas.ContainsKey(a.Id)).Take(200))
        {
            var items = filas[alumno.Id].GetProperty("gradeitems").EnumerateArray().ToArray();
            var calificadas = actividades.Values.Where(a => notas[(alumno.Id, a.Cm)] is not null).ToDictionary(a => a.Cm, a => Calculo.Redondear(notas[(alumno.Id, a.Cm)]!.Value));
            var total = items.FirstOrDefault(i => Texto(i, "itemtype") == "course");
            if (calificadas.Count != actividades.Count || total.ValueKind != JsonValueKind.Object || Decimal(total, "graderaw") is not { } graderaw)
                continue;
            var max = Decimal(total, "grademax") ?? 10m;
            var min = Decimal(total, "grademin") ?? 0m;
            if (max <= min)
                continue;
            var deMoodle = Calculo.Redondear((graderaw - min) / (max - min) * 10m);
            var local = Calculo.Calcular(actividades.Values.Select(a => (a.Cm, a.Tipo)).ToArray(), calificadas, referencia, true).Valor;
            if (local is { } l && Math.Abs(l - deMoodle) > 0.05m)
                Avisar($"El total de Moodle de {alumno.Codigo} ({deMoodle:0.00}) difiere del que calcula EduMonitor ({l:0.00}): revisa la ponderación del curso.");
        }
        return new(alumnos, secciones, actividades.Values.ToList(), notas, avisos, pesos, profesores);
    }

    // Peso por tipo (en %) si cada categoría del libro agrupa actividades de un solo tipo y están los tres tipos.
    // Los pesos se reparten proporcionalmente, así sirve tanto si Moodle los guarda como 0,3 como 30.
    private static PesosMoodle? PesosDeCategorias(Dictionary<int, decimal> pesos, Dictionary<int, HashSet<string>> tipos)
    {
        var porTipo = new Dictionary<string, decimal>();
        foreach (var (categoria, delTipo) in tipos)
        {
            if (delTipo.Count != 1 || !pesos.TryGetValue(categoria, out var peso) || peso <= 0)
                return null;
            var t = delTipo.Single();
            porTipo[t] = porTipo.GetValueOrDefault(t) + peso;
        }
        if (porTipo.Count != 3 || !porTipo.ContainsKey("Tarea") || !porTipo.ContainsKey("APE") || !porTipo.ContainsKey("Examen"))
            return null;
        var suma = porTipo.Values.Sum();
        decimal P(string t) => Math.Round(porTipo[t] / suma * 100m, 2, MidpointRounding.AwayFromZero);
        var (a, b) = (P("Tarea"), P("APE"));
        return new PesosMoodle(a, b, 100m - a - b);
    }
}
