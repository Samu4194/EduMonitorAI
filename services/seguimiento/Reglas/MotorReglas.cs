namespace EduMonitor.Seguimiento.Reglas;

// HU-11. Motor de reglas del agente académico: detecta dificultades a partir de las notas. Es determinista y no usa IA:
// la misma entrada produce siempre los mismos hallazgos, por eso el docente puede auditar por qué un estudiante está en alerta.
// La IA solo interviene después (HU-12) para redactar recomendaciones sobre estos hallazgos; nunca decide quién está en riesgo.
public sealed class MotorReglas(OpcionesReglas o)
{
    public const string RendimientoBajo = "RENDIMIENTO_BAJO", NotaCritica = "NOTA_CRITICA", TendenciaDescendente = "TENDENCIA_DESCENDENTE", TipoDebil = "TIPO_DEBIL",
        RiesgoDeReprobar = "RIESGO_DE_REPROBAR", ActividadDificil = "ACTIVIDAD_DIFICIL", CursoEnRiesgo = "CURSO_EN_RIESGO";

    public ResultadoCurso Evaluar(CursoDato c)
    {
        var estudiantes = c.Estudiantes.Select(e => Estudiante(c, e)).ToList();
        return new ResultadoCurso(estudiantes, Curso(c, estudiantes));
    }

    ResultadoEstudiante Estudiante(CursoDato c, EstudianteDato e)
    {
        var min = c.NotaMinima;
        var notas = c.Actividades.Where(a => e.Notas.ContainsKey(a.Id))
            .OrderBy(a => a.Fecha ?? DateOnly.MaxValue).ThenBy(a => a.Id)
            .Select(a => new NotaOrdenada(a.Id, a.Nombre, a.Tipo, a.Fecha, e.Notas[a.Id])).ToList();
        var porTipo = c.Actividades.GroupBy(a => a.Tipo).Select(g =>
        {
            var valores = g.Where(a => e.Notas.ContainsKey(a.Id)).Select(a => e.Notas[a.Id]).ToList();
            return new PromedioTipo(g.Key, valores.Count > 0 ? Math.Round(valores.Average(), 2, MidpointRounding.AwayFromZero) : 0m, valores.Count, g.Count());
        }).Where(t => t.Calificadas > 0).ToList();
        var pendientes = c.Actividades.Count - notas.Count;
        var hallazgos = new List<HallazgoDato>();

        if (e.Resultado is { } r && r < min)
        {
            var nivel = r < min - o.BrechaAlta ? Nivel.Alto : Nivel.Medio;
            hallazgos.Add(new(RendimientoBajo, nivel, "Rendimiento por debajo del mínimo",
                $"Su resultado actual es {Textos.N(r)} sobre 10 y la nota mínima para aprobar es {Textos.N(min)}."));
        }

        var criticas = notas.Where(n => n.Nota < o.NotaCritica).ToList();
        if (criticas.Count > 0)
            hallazgos.Add(new(NotaCritica, criticas.Count >= 2 ? Nivel.Alto : Nivel.Medio, criticas.Count == 1 ? "Una nota crítica" : $"{criticas.Count} notas críticas",
                $"Obtuvo menos de {Textos.N(o.NotaCritica)} en: {string.Join(", ", criticas.Select(n => $"{n.Actividad} ({Textos.N(n.Nota)})"))}."));

        // Con menos de 3 notas no habría notas «previas» que comparar con las dos últimas, aunque se configure un mínimo menor.
        if (notas.Count >= Math.Max(3, o.MinNotasTendencia))
        {
            var ultimas = notas.TakeLast(2).Average(n => n.Nota);
            var previas = notas.Take(notas.Count - 2).Average(n => n.Nota);
            var caida = previas - ultimas;
            if (caida >= o.CaidaTendencia)
                hallazgos.Add(new(TendenciaDescendente, caida >= o.CaidaTendenciaAlta && ultimas < min ? Nivel.Alto : Nivel.Medio, "Tendencia descendente",
                    $"El promedio de sus dos últimas notas ({Textos.N(ultimas)}) es {Textos.N(caida)} puntos menor que el de las anteriores ({Textos.N(previas)})."));
        }

        foreach (var t in porTipo.Where(t => t.Calificadas >= 2 && t.Promedio < min - o.DebilidadTipo))
            hallazgos.Add(new(TipoDebil, Nivel.Bajo, $"Debilidad en {Plural(t.Tipo)}",
                $"Su promedio en {Plural(t.Tipo)} es {Textos.N(t.Promedio)} ({t.Calificadas} de {t.Total} calificadas), por debajo de la nota mínima."));

        var proyeccion = Proyectar(c, e, pendientes);
        if (proyeccion is { PromedioRequerido: { } req, MaximoPosible: { } max })
        {
            if (max < min)
                hallazgos.Add(new(RiesgoDeReprobar, Nivel.Alto, "Ya no alcanza la nota mínima",
                    $"Aunque obtuviera 10 en {Pendientes(pendientes)}, su resultado final llegaría a {Textos.N(max)}, menor que {Textos.N(min)}."));
            else if (req > o.ExigenciaAlta)
                hallazgos.Add(new(RiesgoDeReprobar, Nivel.Alto, "Riesgo alto de reprobar",
                    $"Para aprobar necesita un promedio de {Textos.N(req)} en {Pendientes(pendientes)}."));
            else if (req > o.ExigenciaMedia)
                hallazgos.Add(new(RiesgoDeReprobar, Nivel.Medio, "Riesgo de reprobar",
                    $"Para aprobar necesita un promedio de {Textos.N(req)} en {Pendientes(pendientes)}."));
        }

        var nivelFinal = hallazgos.Count == 0 ? Nivel.Ninguno : hallazgos.Max(h => h.Nivel);
        return new ResultadoEstudiante(e.Id, e.Resultado, nivelFinal, hallazgos.OrderByDescending(h => h.Nivel).ThenBy(h => h.Codigo).ToList(), notas, porTipo, proyeccion, pendientes);
    }

    // Resultado final = suma de peso(tipo) x promedio(tipo) / suma de pesos, suponiendo que todo lo pendiente de un tipo sale igual a x.
    // Es lineal (a + b·x), así que x se despeja sin iterar. Los tipos sin actividades no cuentan: el docente aún no los planificó.
    ProyeccionDato? Proyectar(CursoDato c, EstudianteDato e, int pendientes)
    {
        if (pendientes == 0 || e.Notas.Count == 0)
            return null;
        decimal a = 0, b = 0, w = 0;
        foreach (var g in c.Actividades.GroupBy(x => x.Tipo))
        {
            var peso = c.Pesos.De(g.Key);
            if (peso <= 0)
                continue;
            var n = g.Count();
            var suma = g.Where(x => e.Notas.ContainsKey(x.Id)).Sum(x => e.Notas[x.Id]);
            a += peso * suma / n;
            b += peso * (n - g.Count(x => e.Notas.ContainsKey(x.Id))) / n;
            w += peso;
        }
        if (w <= 0 || b <= 0)
            return null;
        a /= w;
        b /= w;
        var requerido = Math.Max(0m, (c.NotaMinima - a) / b);
        return new ProyeccionDato(pendientes, Math.Round(requerido, 2, MidpointRounding.AwayFromZero), Math.Round(a + 10m * b, 2, MidpointRounding.AwayFromZero));
    }

    List<HallazgoDato> Curso(CursoDato c, List<ResultadoEstudiante> estudiantes)
    {
        var lista = new List<HallazgoDato>();
        var total = c.Estudiantes.Count;
        if (total == 0)
            return lista;
        foreach (var a in c.Actividades)
        {
            var notas = c.Estudiantes.Where(e => e.Notas.ContainsKey(a.Id)).Select(e => e.Notas[a.Id]).ToList();
            if (notas.Count == 0 || (decimal)notas.Count / total < o.PorcentajeCalificadoActividad)
                continue;
            var promedio = notas.Average();
            if (promedio < c.NotaMinima)
                lista.Add(new(ActividadDificil, promedio < c.NotaMinima - o.ActividadDificilBrecha ? Nivel.Alto : Nivel.Medio, $"{a.Nombre}: promedio bajo",
                    $"El promedio del curso en «{a.Nombre}» ({a.Tipo}) es {Textos.N(Math.Round(promedio, 2, MidpointRounding.AwayFromZero))}, menor que la nota mínima. {notas.Count} de {total} estudiantes calificados."));
        }
        var conResultado = estudiantes.Where(e => e.Resultado is not null).ToList();
        if (conResultado.Count > 0)
        {
            var enRiesgo = conResultado.Count(e => e.Resultado < c.NotaMinima);
            var parte = (decimal)enRiesgo / conResultado.Count;
            if (parte >= o.CursoEnRiesgoMedio)
                lista.Add(new(CursoEnRiesgo, parte >= o.CursoEnRiesgoAlto ? Nivel.Alto : Nivel.Medio, "Curso con muchos estudiantes en riesgo",
                    $"{enRiesgo} de {conResultado.Count} estudiantes con resultado ({Math.Round(parte * 100m, 0)} %) están por debajo de la nota mínima."));
        }
        return lista.OrderByDescending(h => h.Nivel).ToList();
    }

    static string Pendientes(int n) => n == 1 ? "la actividad que le falta" : $"las {n} actividades que le faltan";
    static string Plural(string tipo) => tipo switch { "Tarea" => "tareas", "APE" => "APE", "Examen" => "exámenes", _ => tipo };
}
