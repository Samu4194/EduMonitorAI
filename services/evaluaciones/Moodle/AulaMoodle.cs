using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using EduMonitor.Evaluaciones.Services;
using EduMonitor.Infrastructure;

namespace EduMonitor.Evaluaciones.Moodle;

public record AlumnoMoodle(int Id, string Codigo, string Nombres, string Apellidos, string Correo);
public record TemaMoodle(int Numero, string Nombre, string Contenido);
public record SeccionMoodle(int Id, int Numero, string Nombre, List<TemaMoodle> Temas);
public record ActividadMoodle(int Cm, int Instancia, int Seccion, string Nombre, string Modulo, string Tipo, string Codigo);
public record AulaMoodle(List<AlumnoMoodle> Alumnos, List<SeccionMoodle> Secciones, List<ActividadMoodle> Actividades,
    Dictionary<(int Usuario, int Cm), decimal?> Notas)
{
    private static void Exigir(bool ok, string mensaje)
    {
        if (!ok)
            throw new ApiException(409, mensaje);
    }
    private static string Texto(JsonElement e, string campo, int max)
    {
        var valor = e.GetProperty(campo).GetString()?.Trim();
        Exigir(!string.IsNullOrEmpty(valor) && valor.Length <= max, $"Campo Moodle inválido: {campo}.");
        return valor!;
    }
    private static int Entero(JsonElement e, string campo) => e.GetProperty(campo).GetInt32();

    public static AulaMoodle Leer(int curso, string codigo, JsonElement contenidos, JsonElement usuarios, JsonElement informe)
    {
        var alumnos = usuarios.EnumerateArray().Where(u => u.GetProperty("roles").EnumerateArray()
            .Any(r => Texto(r, "shortname", 100) == "student")).Select(u => new AlumnoMoodle(Entero(u, "id"),
            Texto(u, "idnumber", 100), Texto(u, "firstname", 100), Texto(u, "lastname", 100), Texto(u, "email", 254))).ToList();
        Exigir(alumnos.Count > 0 && alumnos.DistinctBy(x => x.Id).Count() == alumnos.Count &&
            alumnos.DistinctBy(x => x.Codigo, StringComparer.OrdinalIgnoreCase).Count() == alumnos.Count, "Matrícula ausente o ambigua.");
        var secciones = new List<SeccionMoodle>();
        var modulos = new Dictionary<int, (int Instancia, int Seccion, string Nombre, string Modulo)>();
        foreach (var s in contenidos.EnumerateArray())
        {
            var n = Entero(s, "section");
            var mods = s.GetProperty("modules").EnumerateArray().Where(m => Texto(m, "modname", 80) is "assign" or "quiz").ToArray();
            if (n == 0 && mods.Length == 0)
                continue;
            var nombre = Texto(s, "name", 120);
            var seccionId = Entero(s, "id");
            var html = XElement.Parse("<section>" + Texto(s, "summary", 12000) + "</section>");
            var partes = html.Elements().ToArray();
            Exigir(partes.Length >= 2 && partes.Length % 2 == 0, $"Revisa los temas de {nombre}.");
            var temas = new List<TemaMoodle>();
            for (int i = 0; i < partes.Length; i += 2)
            {
                var m = Regex.Match(partes[i].Value.Trim(), @"^Temática\s+(\d+)\s*:\s*(.+)$");
                var contenido = partes[i + 1].Value.Trim();
                Exigir(partes[i].Name == "h4" && partes[i + 1].Name == "p" && m.Success &&
                    m.Groups[2].Value.Length is >= 3 and <= 150 && contenido.Length is >= 3 and <= 300, $"Formato de temática incompatible en {nombre}.");
                temas.Add(new(int.Parse(m.Groups[1].Value), m.Groups[2].Value, contenido));
            }
            Exigir(temas.All(t => t.Numero > 0) && temas.DistinctBy(t => t.Numero).Count() == temas.Count, "Número de temática ambiguo.");
            secciones.Add(new(seccionId, n, nombre, temas));
            foreach (var m in mods)
                modulos.Add(Entero(m, "id"), (Entero(m, "instance"), seccionId, Texto(m, "name", 120), Texto(m, "modname", 80)));
        }
        Exigir(secciones.Count > 0 && secciones.DistinctBy(s => s.Id).Count() == secciones.Count && modulos.Count > 0, "Faltan unidades o actividades en Moodle.");
        var actividades = new Dictionary<int, ActividadMoodle>();
        var notas = new Dictionary<(int, int), decimal?>();
        var filas = informe.GetProperty("usergrades").EnumerateArray().ToDictionary(f => Entero(f, "userid"));
        Exigir(filas.Keys.ToHashSet().SetEquals(alumnos.Select(x => x.Id)), "El informe de notas no cubre la matrícula completa.");
        foreach (var alumno in alumnos)
        {
            var fila = filas[alumno.Id];
            Exigir(Entero(fila, "courseid") == curso, "El informe pertenece a otro curso.");
            var items = fila.GetProperty("gradeitems").EnumerateArray().ToArray();
            Exigir(items.All(i => Texto(i, "itemtype", 25) is "mod" or "category" or "course"),
                "El libro de Moodle contiene elementos de calificación sin correspondencia.");
            var cats = items.Where(i => Texto(i, "itemtype", 25) == "category").ToDictionary(i => Entero(i, "iteminstance"));
            var tipos = new Dictionary<int, string>();
            var calificadas = new Dictionary<int, decimal>();
            foreach (var item in items.Where(i => Texto(i, "itemtype", 25) == "mod"))
            {
                var cm = Entero(item, "cmid");
                var mod = modulos[cm];
                var key = Texto(item, "idnumber", 100);
                var seccion = secciones.Single(s => s.Id == mod.Seccion);
                var prefijo = $"{codigo}-U{seccion.Numero}-";
                Exigir(key.StartsWith(prefijo, StringComparison.Ordinal) && Texto(item, "itemmodule", 30) == mod.Modulo &&
                    Entero(item, "iteminstance") == mod.Instancia && Entero(item, "itemnumber") == 0, "Actividad sin correspondencia inequívoca.");
                var sufijo = key[prefijo.Length..];
                var tipo = (mod.Modulo, sufijo) switch
                {
                    ("assign", "APE") => "APE",
                    ("quiz", "EX") => "Examen",
                    ("assign", _) when Regex.IsMatch(sufijo, @"^T[1-9]\d*$") => "Tarea",
                    _ => throw new ApiException(409, $"Tipo no reconocido: {key}.")
                };
                var cat = Entero(item, "categoryid");
                Exigir(!tipos.TryGetValue(cat, out var previo) || previo == tipo, "Categoría de calificación ambigua.");
                tipos[cat] = tipo;
                Exigir(cats[cat].GetProperty("weightraw").GetDecimal() == (tipo == "Examen" ? .4m : .3m) &&
                    item.GetProperty("grademin").GetDecimal() == 0 && item.GetProperty("grademax").GetDecimal() == 10 &&
                    !item.GetProperty("gradeneedsupdate").GetBoolean() && !item.GetProperty("gradeishidden").GetBoolean(),
                    "Escala, ponderación o visibilidad de calificaciones incompatible.");
                var a = new ActividadMoodle(cm, mod.Instancia, mod.Seccion, mod.Nombre, mod.Modulo, tipo, key);
                Exigir(!actividades.TryGetValue(cm, out var otra) || otra == a, "El informe contiene identificadores contradictorios.");
                actividades[cm] = a;
                var raw = item.GetProperty("graderaw");
                decimal? valor = raw.ValueKind == JsonValueKind.Null ? null : raw.GetDecimal();
                Exigir(valor is null or >= 0 and <= 10, "Nota fuera de escala.");
                notas.Add((alumno.Id, cm), valor);
                if (valor.HasValue)
                    calificadas.Add(cm, Calculo.Redondear(valor.Value));
            }
            Exigir(tipos.Count == 3 && tipos.Values.Distinct().Count() == 3 && cats.Count == 3 &&
                modulos.Keys.All(cm => notas.ContainsKey((alumno.Id, cm))), "Faltan categorías o celdas del libro de notas.");
            var total = items.Single(i => Texto(i, "itemtype", 25) == "course").GetProperty("graderaw");
            var esperado = Calculo.Calcular(actividades.Values.Select(a => (a.Cm, a.Tipo)).ToArray(), calificadas, Calculo.Predeterminada, true).Valor;
            // Moodle puede incluir las celdas vacías como cero en el total provisional.
            // EduMonitor conserva su propia regla para pendientes y compara el total definitivo.
            Exigir(calificadas.Count != modulos.Count ||
                (total.ValueKind == JsonValueKind.Number && esperado == Calculo.Redondear(total.GetDecimal())),
                $"El total Moodle de {alumno.Codigo} no coincide con las reglas del curso.");
        }
        Exigir(actividades.Values.DistinctBy(a => a.Codigo).Count() == actividades.Count, "Hay códigos de actividad duplicados.");
        return new(alumnos, secciones, actividades.Values.ToList(), notas);
    }
}
