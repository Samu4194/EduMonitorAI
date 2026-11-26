using EduMonitor.Seguimiento.DTOs;
using EduMonitor.Seguimiento.Reglas;
namespace EduMonitor.Seguimiento.Asistente;

public sealed record PreguntaPre(string Id, string Grupo, string Texto, bool RequiereEstudiante);

// Tarjeta de datos exactos que se muestra junto a la respuesta. La calcula el sistema, no el modelo.
public sealed record Tabla(string Titulo, List<string> Columnas, List<List<string>> Filas, List<string> Notas)
{
    public string Tipo => "tabla";
}

// Lo que produce una pregunta precargada: la tarjeta para el docente, los datos (compactos y con seudónimos) para el modelo y qué se le pide.
public sealed record Hechos(Tabla? Tarjeta, string Datos, string Instruccion);

public static class Catalogo
{
    public const string Resumen = "resumen", Actividad = "actividad", Temas = "temas", Evaluaciones = "evaluaciones", Atencion = "atencion",
        Ruta = "ruta", Rutas = "rutas", Apoyar = "apoyar", Tutoria = "tutoria";
    public const string MarcaEstudiante = "[estudiante]";

    public static readonly IReadOnlyList<PreguntaPre> Todas =
    [
        new(Ruta, "Estudiantes", $"Ruta de mejora de {MarcaEstudiante}", true),
        new(Rutas, "Estudiantes", "Rutas de mejora para todos los estudiantes en riesgo", false),
        new(Atencion, "Estudiantes", "¿Quiénes necesitan atención urgente y por qué?", false),
        new(Apoyar, "Estudiantes", $"¿Cómo apoyo a {MarcaEstudiante}?", true),
        new(Resumen, "Curso", "Resume el estado del curso", false),
        new(Actividad, "Curso", "¿Qué actividad salió peor y qué hago?", false),
        new(Temas, "Curso", "¿Qué temas debo reforzar en la próxima clase?", false),
        new(Evaluaciones, "Evaluación", "¿Es adecuada la dificultad de mis evaluaciones?", false),
        new(Tutoria, "Planificación", "Propón una tutoría de refuerzo", false),
    ];

    public static PreguntaPre? Buscar(string? id) => Todas.FirstOrDefault(p => p.Id == id);
}

// Datos exactos de cada pregunta precargada y del curso en general. Los estudiantes aparecen con su seudónimo («Estudiante A»).
public static class Datos
{
    static string N(decimal? v) => v is null ? "—" : Textos.N(v.Value);
    static string Pct(int parte, int total) => total == 0 ? "0 %" : $"{Math.Round(100m * parte / total, 0)} %";
    static List<string> Nombres(IEnumerable<string> items) => [.. items];

    sealed record StatsActividad(ActividadTemasReference Actividad, decimal? Promedio, int Calificadas, int BajoMinimo);

    static List<StatsActividad> Actividades(DatosCurso d) => [.. d.Actividades.Select(a =>
    {
        var v = d.Filas.Where(f => f.Notas.ContainsKey(a.Id)).Select(f => f.Notas[a.Id]).ToList();
        return new StatsActividad(a, v.Count > 0 ? Math.Round(v.Average(), 2, MidpointRounding.AwayFromZero) : null, v.Count, v.Count(x => x < d.NotaMinima));
    })];

    static string TemasDe(ActividadTemasReference a) => a.Temas.Count == 0 ? "sin tema" : string.Join(", ", a.Temas.Select(t => t.Nombre));

    // ---------- Resumen del curso: va siempre como contexto del chat ----------
    public static string ContextoCurso(DatosCurso d, Func<Persona, string> alias)
    {
        var conResultado = d.Filas.Where(f => f.Resultado is not null).ToList();
        var riesgo = conResultado.Count(f => f.Resultado < d.NotaMinima);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Curso: {d.Curso.MateriaNombre}, paralelo {d.Curso.Paralelo}. Escala 0 a 10; nota mínima para aprobar: {N(d.NotaMinima)}.");
        sb.AppendLine($"Estudiantes: {d.Lista.Count}; con resultado calculado: {conResultado.Count}; promedio del grupo: {N(conResultado.Count > 0 ? Math.Round(conResultado.Average(f => f.Resultado!.Value), 2) : null)}; por debajo de la nota mínima: {riesgo} ({Pct(riesgo, conResultado.Count)}).");
        sb.AppendLine($"Ponderación: tareas {d.Pesos.Tarea} %, APE {d.Pesos.Ape} %, exámenes {d.Pesos.Examen} %.");
        var acts = Actividades(d);
        if (acts.Count > 0)
        {
            sb.AppendLine("Actividades (promedio; notas registradas; notas bajo el mínimo; temas):");
            foreach (var a in acts.Take(14))
                sb.AppendLine($"- {a.Actividad.Nombre} ({a.Actividad.Tipo}): {N(a.Promedio)}; {a.Calificadas}; {a.BajoMinimo}; {TemasDe(a.Actividad)}");
        }
        var temas = d.TemasDelCurso();
        if (temas.Count > 0)
        {
            sb.AppendLine("Temas (promedio; estudiantes por debajo del mínimo):");
            foreach (var t in temas.Take(8))
                sb.AppendLine($"- {t.Tema}: {N(t.Promedio)}; {t.EstudiantesBajoMinimo.Count}");
        }
        foreach (var h in d.Reglas.HallazgosCurso.Take(4))
            sb.AppendLine($"Alerta del curso: {h.Titulo}. {h.Detalle}");
        var enAlerta = d.Reglas.Estudiantes.Where(e => e.Nivel >= Nivel.Medio).OrderByDescending(e => e.Nivel).ThenBy(e => e.Resultado ?? 11m).Take(8).ToList();
        if (enAlerta.Count > 0)
        {
            sb.AppendLine("Estudiantes en alerta (resultado; nivel; hallazgo principal):");
            foreach (var e in enAlerta)
                if (d.Persona(e.EstudianteId) is { } p)
                    sb.AppendLine($"- {alias(p)}: {N(e.Resultado)}; {e.Nivel.Nombre()}; {e.Hallazgos.FirstOrDefault()?.Titulo ?? "—"}");
        }
        return sb.ToString().TrimEnd();
    }

    // ---------- Un estudiante ----------
    public static string DeEstudiante(DatosCurso d, Persona p, string alias)
    {
        var r = d.De(p.Id);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"{alias}: resultado {N(d.Resultado(p.Id))} sobre 10; nivel de alerta {r?.Nivel.Nombre() ?? "Sin alertas"}; actividades pendientes {r?.Pendientes ?? 0}.");
        var notas = d.Notas(p.Id);
        var lista = d.Actividades.Where(a => notas.ContainsKey(a.Id)).Select(a => $"{a.Nombre} ({a.Tipo}) {N(notas[a.Id])}").Take(14).ToList();
        if (lista.Count > 0)
            sb.AppendLine($"Notas de {alias}: {string.Join("; ", lista)}.");
        var temas = d.Temas(p.Id).Where(t => t.Promedio is not null).Take(6).Select(t => $"{t.Tema} {N(t.Promedio)}").ToList();
        if (temas.Count > 0)
            sb.AppendLine($"Promedio por tema de {alias} (de menor a mayor): {string.Join("; ", temas)}.");
        foreach (var h in r?.Hallazgos.Take(5) ?? [])
            sb.AppendLine($"Hallazgo de {alias} ({h.Nivel.Nombre()}): {h.Titulo}. {h.Detalle}");
        if (r?.Proyeccion is { PromedioRequerido: { } req, MaximoPosible: { } max })
            sb.AppendLine($"Proyección de {alias}: necesita un promedio de {N(req)} en las {r.Proyeccion.Pendientes} actividades pendientes; máximo posible {N(max)}.");
        return sb.ToString().TrimEnd();
    }

    // Tarjeta con las notas exactas de UN estudiante (la calcula el sistema; el modelo solo la comenta).
    public static Tabla NotasDe(DatosCurso d, Persona p, string alias)
    {
        var notas = d.Notas(p.Id);
        string[] orden = ["Tarea", "APE", "Examen"];
        var filas = d.Actividades.OrderBy(a => Array.IndexOf(orden, a.Tipo) is var i && i >= 0 ? i : 9).ThenBy(a => a.Id)
            .Select(a => new List<string> { a.Nombre, a.Tipo, notas.TryGetValue(a.Id, out var n) ? N(n) : "—" }).ToList();
        filas.Add(["Resultado actual", "", N(d.Resultado(p.Id))]);
        var r = d.De(p.Id);
        var pie = new List<string> { $"Nivel de alerta: {r?.Nivel.Nombre() ?? "Sin alertas"}. Nota mínima para aprobar: {N(d.NotaMinima)}." };
        if ((r?.Pendientes ?? 0) > 0)
            pie.Add($"Actividades sin nota todavía: {r!.Pendientes}.");
        return new Tabla($"Notas de {alias}", ["Actividad", "Tipo", "Nota"], filas, pie);
    }

    // ---------- Preguntas precargadas ----------
    public static Hechos? De(string id, DatosCurso d, List<Persona> estudiantes, Func<Persona, string> alias) => id switch
    {
        Catalogo.Resumen => ResumenCurso(d, alias),
        Catalogo.Actividad => ActividadDificil(d),
        Catalogo.Temas => TemasAReforzar(d),
        Catalogo.Evaluaciones => Evaluaciones(d),
        Catalogo.Atencion => Atencion(d, alias),
        Catalogo.Apoyar => estudiantes.Count == 0 ? null : Apoyar(d, estudiantes[0], alias),
        Catalogo.Tutoria => Tutoria(d, alias),
        _ => null
    };

    static Hechos ResumenCurso(DatosCurso d, Func<Persona, string> alias)
    {
        var conResultado = d.Filas.Where(f => f.Resultado is not null).ToList();
        var riesgo = conResultado.Count(f => f.Resultado < d.NotaMinima);
        var pendientes = d.Filas.Sum(f => f.Pendientes);
        var alto = d.Reglas.Estudiantes.Count(e => e.Nivel == Nivel.Alto);
        var medio = d.Reglas.Estudiantes.Count(e => e.Nivel == Nivel.Medio);
        var filas = new List<List<string>>
        {
            new() { "Estudiantes", d.Lista.Count.ToString() },
            new() { "Con resultado calculado", conResultado.Count.ToString() },
            new() { "Promedio del grupo", N(conResultado.Count > 0 ? Math.Round(conResultado.Average(f => f.Resultado!.Value), 2) : null) },
            new() { $"Por debajo de {N(d.NotaMinima)}", $"{riesgo} ({Pct(riesgo, conResultado.Count)})" },
            new() { "Alertas altas / medias", $"{alto} / {medio}" },
            new() { "Actividades / notas pendientes", $"{d.Actividades.Count} / {pendientes}" },
        };
        var debiles = d.TemasDelCurso().Where(t => t.Promedio is not null).Take(3).Select(t => $"{t.Tema} ({N(t.Promedio)})").ToList();
        if (debiles.Count > 0)
            filas.Add(["Temas más débiles", string.Join(", ", debiles)]);
        var notas = d.Reglas.HallazgosCurso.Select(h => $"{h.Titulo}. {h.Detalle}").ToList();
        return new Hechos(new Tabla("Estado del curso", ["Dato", "Valor"], filas, notas), ContextoCurso(d, alias),
            "Resume el estado del curso con dos secciones: **Panorama** (3 viñetas con las cifras clave) y **Prioridades** (2 viñetas con lo más importante a atender), usando solo los datos.");
    }

    static Hechos ActividadDificil(DatosCurso d)
    {
        var acts = Actividades(d).Where(a => a.Promedio is not null).OrderBy(a => a.Promedio).Take(5).ToList();
        var filas = acts.Select(a => new List<string> { a.Actividad.Nombre, a.Actividad.Tipo, N(a.Promedio), $"{a.BajoMinimo} de {a.Calificadas}", TemasDe(a.Actividad) }).ToList();
        var datos = acts.Count == 0 ? "Todavía no hay notas registradas en ninguna actividad."
            : "Actividades con menor promedio (promedio; notas bajo el mínimo de las registradas; temas):\n" + string.Join("\n", acts.Select(a => $"- {a.Actividad.Nombre} ({a.Actividad.Tipo}): {N(a.Promedio)}; {a.BajoMinimo} de {a.Calificadas}; {TemasDe(a.Actividad)}"));
        return new Hechos(new Tabla("Actividades con menor promedio", ["Actividad", "Tipo", "Promedio", "Bajo el mínimo", "Temas"], filas,
            acts.Count == 0 ? ["Aún no hay notas registradas."] : []), $"Nota mínima: {N(d.NotaMinima)}.\n{datos}",
            "Explica qué actividad salió peor, da posibles causas pedagógicas (como hipótesis, no certezas) y propón 3 acciones concretas para el docente.");
    }

    static Hechos TemasAReforzar(DatosCurso d)
    {
        var temas = d.TemasDelCurso().Where(t => t.Promedio is not null).Take(6).ToList();
        var filas = temas.Select(t => new List<string> { t.Tema, t.Unidad, N(t.Promedio), t.NotasBajas.ToString(), t.EstudiantesBajoMinimo.Count.ToString() }).ToList();
        var datos = temas.Count == 0 ? "No hay temas relacionados con actividades calificadas."
            : "Temas de menor a mayor promedio (promedio; notas bajo el mínimo; estudiantes bajo el mínimo):\n" + string.Join("\n", temas.Select(t => $"- {t.Tema} ({t.Unidad}): {N(t.Promedio)}; {t.NotasBajas}; {t.EstudiantesBajoMinimo.Count}"));
        var notas = new List<string>();
        if (d.Actividades.Any(a => a.Temas.Count == 0))
            notas.Add($"{d.Actividades.Count(a => a.Temas.Count == 0)} actividad(es) no están relacionadas con ningún tema y no entran en este cálculo.");
        return new Hechos(new Tabla("Temas con menor rendimiento", ["Tema", "Unidad", "Promedio", "Notas bajas", "Estudiantes bajo el mínimo"], filas, notas),
            $"Nota mínima: {N(d.NotaMinima)}.\n{datos}", "Indica qué temas conviene reforzar en la próxima clase, en orden de prioridad, y cómo hacerlo con 3 acciones concretas.");
    }

    static Hechos Evaluaciones(DatosCurso d)
    {
        var acts = Actividades(d);
        var filas = new List<List<string>>();
        var lineas = new List<string>();
        foreach (var tipo in new[] { "Tarea", "APE", "Examen" })
        {
            var delTipo = acts.Where(a => a.Actividad.Tipo == tipo).ToList();
            if (delTipo.Count == 0)
                continue;
            var notas = d.Filas.SelectMany(f => delTipo.Where(a => f.Notas.ContainsKey(a.Actividad.Id)).Select(a => f.Notas[a.Actividad.Id])).ToList();
            var bajo = notas.Count(n => n < d.NotaMinima);
            var prom = notas.Count > 0 ? Math.Round(notas.Average(), 2, MidpointRounding.AwayFromZero) : (decimal?)null;
            filas.Add([tipo, delTipo.Count.ToString(), N(prom), notas.Count == 0 ? "—" : $"{bajo} de {notas.Count} ({Pct(bajo, notas.Count)})"]);
            lineas.Add($"- {tipo}: {delTipo.Count} actividad(es); promedio {N(prom)}; notas bajo el mínimo {bajo} de {notas.Count} ({Pct(bajo, notas.Count)}).");
        }
        var con = acts.Where(a => a.Promedio is not null).ToList();
        var notasFin = new List<string>();
        if (con.Count > 0)
        {
            var peor = con.MinBy(a => a.Promedio)!;
            var mejor = con.MaxBy(a => a.Promedio)!;
            notasFin.Add($"Menor promedio: {peor.Actividad.Nombre} ({N(peor.Promedio)}). Mayor promedio: {mejor.Actividad.Nombre} ({N(mejor.Promedio)}).");
            lineas.Add($"Menor promedio: {peor.Actividad.Nombre} ({N(peor.Promedio)}). Mayor promedio: {mejor.Actividad.Nombre} ({N(mejor.Promedio)}).");
        }
        return new Hechos(new Tabla("Dificultad por tipo de evaluación", ["Tipo", "Actividades", "Promedio", "Notas bajo el mínimo"], filas, notasFin),
            $"Nota mínima: {N(d.NotaMinima)}.\n{(lineas.Count == 0 ? "Todavía no hay actividades calificadas." : string.Join("\n", lineas))}",
            "Valora si la dificultad de las evaluaciones parece adecuada (promedio, porcentaje bajo el mínimo y diferencia entre actividades) y qué ajustar. Aclara que son indicios, no una conclusión definitiva.");
    }

    static Hechos Atencion(DatosCurso d, Func<Persona, string> alias)
    {
        var lista = d.Reglas.Estudiantes.Where(e => e.Nivel >= Nivel.Bajo).OrderByDescending(e => e.Nivel).ThenBy(e => e.Resultado ?? 11m).Take(8).ToList();
        var filas = new List<List<string>>();
        var lineas = new List<string>();
        foreach (var e in lista)
            if (d.Persona(e.EstudianteId) is { } p)
            {
                var a = alias(p);
                var principal = e.Hallazgos.FirstOrDefault();
                filas.Add([a, e.Nivel.Nombre(), N(e.Resultado), principal?.Titulo ?? "—"]);
                lineas.Add($"- {a}: resultado {N(e.Resultado)}; nivel {e.Nivel.Nombre()}; {string.Join(" | ", e.Hallazgos.Take(3).Select(h => $"{h.Titulo}. {h.Detalle}"))}");
            }
        return new Hechos(new Tabla("Estudiantes que necesitan atención", ["Estudiante", "Nivel", "Resultado", "Motivo principal"], filas,
                lista.Count == 0 ? ["Ningún estudiante tiene alertas en este momento."] : []),
            lista.Count == 0 ? "Ningún estudiante tiene alertas en este momento." : $"Nota mínima: {N(d.NotaMinima)}.\n{string.Join("\n", lineas)}",
            "Explica en una frase por estudiante por qué necesita atención y sugiere una primera acción para cada uno.");
    }

    static Hechos Apoyar(DatosCurso d, Persona p, Func<Persona, string> alias)
    {
        var a = alias(p);
        var r = d.De(p.Id);
        var filas = new List<List<string>> { new() { "Resultado actual", N(d.Resultado(p.Id)) }, new() { "Nivel de alerta", r?.Nivel.Nombre() ?? "Sin alertas" }, new() { "Actividades pendientes", (r?.Pendientes ?? 0).ToString() } };
        var temas = d.Temas(p.Id).Where(t => t.Promedio is not null).Take(3).Select(t => $"{t.Tema} ({N(t.Promedio)})").ToList();
        if (temas.Count > 0)
            filas.Add(["Temas más débiles", string.Join(", ", temas)]);
        return new Hechos(new Tabla($"{a}: situación", ["Dato", "Valor"], filas, r?.Hallazgos.Select(h => $"{h.Titulo}. {h.Detalle}").ToList() ?? []), DeEstudiante(d, p, a) + $"\nNota mínima: {N(d.NotaMinima)}.",
            $"Propón cómo apoyar a {a}: un diagnóstico breve basado en los datos y 3 o 4 acciones concretas para el docente.");
    }

    static Hechos Tutoria(DatosCurso d, Func<Persona, string> alias)
    {
        var temas = d.TemasDelCurso().Where(t => t.EstudiantesBajoMinimo.Count > 0).OrderByDescending(t => t.EstudiantesBajoMinimo.Count).ThenBy(t => t.Promedio).Take(4).ToList();
        var filas = new List<List<string>>();
        var lineas = new List<string>();
        foreach (var t in temas)
        {
            var quienes = t.EstudiantesBajoMinimo.Select(d.Persona).OfType<Persona>().Take(6).Select(alias).ToList();
            filas.Add([t.Tema, t.EstudiantesBajoMinimo.Count.ToString(), N(t.Promedio), string.Join(", ", quienes)]);
            lineas.Add($"- {t.Tema}: {t.EstudiantesBajoMinimo.Count} estudiante(s) bajo el mínimo (promedio {N(t.Promedio)}): {string.Join(", ", quienes)}");
        }
        return new Hechos(new Tabla("Temas para una tutoría de refuerzo", ["Tema", "Estudiantes bajo el mínimo", "Promedio", "Quiénes"], filas,
                temas.Count == 0 ? ["Ningún tema tiene estudiantes por debajo del mínimo. Si hay actividades sin tema, relaciónalas para ver este análisis."] : []),
            temas.Count == 0 ? "Ningún tema tiene estudiantes por debajo de la nota mínima." : $"Nota mínima: {N(d.NotaMinima)}.\n{string.Join("\n", lineas)}",
            "Propón una tutoría de refuerzo de 60 a 90 minutos: objetivo, estructura por bloques con tiempos, ejercicios sugeridos y cómo comprobar si funcionó. Basa los temas en los datos.");
    }
}
