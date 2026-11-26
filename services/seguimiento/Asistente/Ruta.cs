using EduMonitor.Seguimiento.DTOs;
using EduMonitor.Seguimiento.Agente;
using EduMonitor.Seguimiento.Models;
using EduMonitor.Seguimiento.Reglas;
namespace EduMonitor.Seguimiento.Asistente;

public sealed record ActividadRuta(int Id, string Nombre, string Tipo, string? Fecha, decimal Aporte, List<string> Temas);
public sealed record TemaRuta(string Tema, decimal? Promedio, int Calificadas, List<string> Evidencia);
// Temas: los que evalúa la actividad (o actividades) de la etapa, con los débiles del estudiante primero. Debiles: cuáles de ellos son débiles.
public sealed record EtapaRuta(int Numero, string Titulo, string? Plazo, List<ActividadRuta> Actividades, List<string> Temas, decimal? Meta, List<string> Acciones, List<string>? Debiles = null);

// Ruta de mejora de un estudiante. Los números (metas, promedios, qué actividades pesan más) los calcula el sistema; la IA solo redacta las
// acciones de estudio de cada etapa. Si la IA no responde, las acciones salen de plantillas y la ruta sigue siendo completa.
//   Estado: Aprobado | Asegurado | Alcanzable | Exigente | MuyExigente | Imposible | SinPendientes | SinNotas
public sealed record RutaDatos(string Tipo, string Estudiante, decimal NotaMinima, decimal? Resultado, string Estado, string Situacion, decimal? PromedioRequerido,
    decimal? MaximoPosible, int Pendientes, List<TemaRuta> Temas, List<EtapaRuta> Etapas, string Resumen, List<string> AccionesDocente, string Mensaje, string Origen, string? Modelo);

public static class RutaMejora
{
    public const int MaxEtapas = 4;

    public static RutaDatos Construir(DatosCurso d, Persona p, string alias, OpcionesReglas reglas)
    {
        var r = d.De(p.Id) ?? throw new InvalidOperationException("El estudiante no tiene resultado calculado.");
        var min = d.NotaMinima;
        var notas = d.Notas(p.Id);
        var temas = d.Temas(p.Id);
        var debiles = temas.Where(t => t.Promedio is { } v && v < min).OrderBy(t => t.Promedio).Take(4).ToList();
        var porId = d.Actividades.ToDictionary(a => a.Id);
        var rutaTemas = debiles.Select(t => new TemaRuta(t.Tema, t.Promedio, t.Calificadas,
            [.. d.Actividades.Where(a => a.Temas.Any(x => x.Id == t.TemaId) && notas.TryGetValue(a.Id, out var n) && n < min).Select(a => a.Nombre).Take(3)])).ToList();
        var nombresDebiles = debiles.Select(t => t.Tema).ToList();
        var pendientes = d.Actividades.Where(a => !notas.ContainsKey(a.Id)).ToList();
        var proy = r.Proyeccion;

        var estado = d.Resultado(p.Id) is null ? "SinNotas"
            : pendientes.Count == 0 ? (d.Resultado(p.Id) >= min ? "Aprobado" : "SinPendientes")
            : proy is null ? "Alcanzable"
            : proy.MaximoPosible < min ? "Imposible"
            : proy.PromedioRequerido is null or <= 0m ? "Asegurado"
            : proy.PromedioRequerido <= reglas.ExigenciaMedia ? "Alcanzable"
            : proy.PromedioRequerido <= reglas.ExigenciaAlta ? "Exigente" : "MuyExigente";
        var requerido = proy?.PromedioRequerido;
        var etapas = new List<EtapaRuta>();

        if (pendientes.Count > 0 && estado is not ("Imposible" or "SinNotas"))
        {
            var ordenadas = pendientes.OrderBy(a => a.FechaEntrega ?? DateOnly.MaxValue).ThenByDescending(d.Aporte).ThenBy(a => a.Id).ToList();
            var grupos = ordenadas.Count <= MaxEtapas ? ordenadas.Select(a => new List<ActividadTemasReference> { a }).ToList()
                : [.. ordenadas.Take(MaxEtapas - 1).Select(a => new List<ActividadTemasReference> { a }), ordenadas.Skip(MaxEtapas - 1).ToList()];
            decimal? meta = estado == "Asegurado" ? null : requerido is { } rq ? Math.Min(10m, rq) : null;
            for (var i = 0; i < grupos.Count; i++)
            {
                var g = grupos[i];
                // Lo que debe estudiar para esta etapa: los temas que evalúa la actividad, con los débiles del estudiante primero.
                var evalua = g.SelectMany(a => a.Temas.Select(t => t.Nombre)).Distinct().ToList();
                var debilesDeLaEtapa = evalua.Where(nombresDebiles.Contains).ToList();
                var delTema = evalua.OrderBy(n => debilesDeLaEtapa.Contains(n) ? 0 : 1).ToList();
                if (delTema.Count == 0)
                    delTema = nombresDebiles.Take(2).ToList();   // actividad sin tema: se refuerzan los más débiles en general
                var titulo = g.Count == 1 ? $"{g[0].Tipo}: {g[0].Nombre}" : $"Actividades restantes ({g.Count})";
                var fecha = g.Count == 1 ? g[0].FechaEntrega : null;
                etapas.Add(new EtapaRuta(i + 1, titulo, fecha is null ? null : $"hasta el {Fecha(fecha.Value)}",
                    [.. g.Select(a => new ActividadRuta(a.Id, a.Nombre, a.Tipo, a.FechaEntrega?.ToString("yyyy-MM-dd"), Math.Round(d.Aporte(a), 1), [.. a.Temas.Select(t => t.Nombre)]))],
                    delTema, meta, PlantillaAcciones(g, delTema), debilesDeLaEtapa));
            }
        }
        else if (estado is "Imposible" or "SinPendientes")
        {
            etapas.Add(new EtapaRuta(1, "Plan de recuperación", null, [], [.. nombresDebiles.Take(3)], null,
            [
                "Conversa con el docente sobre las opciones de recuperación que existen para este curso.",
                nombresDebiles.Count > 0 ? $"Repasa {Lista(nombresDebiles.Take(3))} con ejercicios resueltos y pide retroalimentación." : "Repasa con el docente los contenidos que más te costaron."
            ]));
        }

        var situacion = Situacion(estado, d.Resultado(p.Id), min, requerido, proy?.MaximoPosible, pendientes.Count);
        var docente = new List<string>();
        if (nombresDebiles.Count > 0)
            docente.Add($"Ofrecer una tutoría de refuerzo sobre {Lista(nombresDebiles.Take(3))}.");
        var bajas = d.Actividades.Where(a => notas.TryGetValue(a.Id, out var n) && n < min).Select(a => a.Nombre).Take(3).ToList();
        if (bajas.Count > 0)
            docente.Add($"Dar retroalimentación puntual de las actividades con nota baja: {Lista(bajas)}.");
        if (estado is "Exigente" or "MuyExigente")
            docente.Add("Acordar metas por actividad con el estudiante y revisar su avance después de cada entrega.");
        if (estado is "Imposible" or "SinPendientes")
            docente.Add("Informar al estudiante las opciones de recuperación vigentes y acordar un plan.");
        if (docente.Count == 0)
            docente.Add("Hacer seguimiento del avance y resolver sus dudas.");

        var resumen = estado switch
        {
            "Aprobado" => $"{alias} ya cumple la nota mínima y no tiene actividades pendientes.",
            "SinNotas" => $"{alias} todavía no tiene notas registradas: aún no se puede trazar una ruta.",
            "Imposible" => $"{alias} ya no alcanza la nota mínima con las actividades que faltan; corresponde una ruta de recuperación.",
            "SinPendientes" => $"{alias} terminó las actividades por debajo de la nota mínima; corresponde una ruta de recuperación.",
            "Asegurado" => $"{alias} ya asegura la nota mínima con lo que lleva.",
            _ => $"{alias} necesita un promedio de {Textos.N(requerido ?? 0)} en las {pendientes.Count} actividades que faltan." + (nombresDebiles.Count > 0 ? $" Temas a reforzar: {Lista(nombresDebiles.Take(3))}." : "")
        };
        var mensaje = $"Revisando tu avance en {d.Curso.MateriaNombre}: {situacion}" + (etapas.Count > 0 ? $" Te propongo esta ruta: {string.Join("; ", etapas.Select(e => $"{e.Numero}) {e.Titulo}"))}." : "") +
            (nombresDebiles.Count > 0 ? $" Empieza reforzando {Lista(nombresDebiles.Take(2))}." : "") + " Cuenta conmigo para resolver tus dudas: con constancia puedes mejorar tu resultado.";
        return new RutaDatos("ruta", alias, min, d.Resultado(p.Id), estado, situacion, requerido, proy?.MaximoPosible, pendientes.Count, rutaTemas, etapas, resumen, docente, mensaje, OrigenRecomendacion.Plantilla, null);
    }

    static string Situacion(string estado, decimal? resultado, decimal min, decimal? requerido, decimal? maximo, int pendientes)
    {
        var actual = resultado is { } v ? $"tu resultado actual es {Textos.N(v)} sobre 10 y la nota mínima es {Textos.N(min)}." : $"la nota mínima es {Textos.N(min)}.";
        var faltan = pendientes == 1 ? "la actividad que falta" : $"las {pendientes} actividades que faltan";
        return estado switch
        {
            "Aprobado" => $"{Cap(actual)} Ya cumples la nota mínima y no tienes actividades pendientes.",
            "SinNotas" => "Todavía no tienes notas registradas.",
            "Asegurado" => $"{Cap(actual)} Con lo que llevas ya aseguras la nota mínima.",
            "Imposible" => $"{Cap(actual)} Aunque obtuvieras 10 en {faltan}, llegarías a {Textos.N(maximo ?? 0)}: conviene hablar con el docente sobre la recuperación.",
            "SinPendientes" => $"{Cap(actual)} Ya no quedan actividades por rendir, así que corresponde conversar con el docente sobre la recuperación.",
            _ => $"{Cap(actual)} Necesitas un promedio de {Textos.N(requerido ?? 0)} en {faltan}."
        };
    }

    static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
    static string Fecha(DateOnly f) => $"{f.Day} {new[] { "ene", "feb", "mar", "abr", "may", "jun", "jul", "ago", "sep", "oct", "nov", "dic" }[f.Month - 1]}";
    static string Lista(IEnumerable<string> items)
    {
        var l = items.Select(i => $"«{i}»").ToList();
        return l.Count <= 1 ? string.Join("", l) : $"{string.Join(", ", l.Take(l.Count - 1))} y {l[^1]}";
    }

    static List<string> PlantillaAcciones(List<ActividadTemasReference> actividades, List<string> temas)
    {
        var sobre = temas.Count > 0 ? Lista(temas) : "los contenidos de la actividad";
        var tipos = actividades.Select(a => a.Tipo).Distinct().ToList();
        var acciones = new List<string>();
        if (tipos.Contains("Examen"))
        {
            acciones.Add($"Estudia {sobre} en 3 sesiones de 45 minutos, alternando teoría y ejercicios.");
            acciones.Add("Resuelve una prueba o ejercicios mixtos con tiempo limitado y corrige tus errores.");
            acciones.Add("Lleva tus dudas al docente antes del examen.");
        }
        if (tipos.Contains("APE"))
        {
            acciones.Add($"Repasa {sobre} y ensaya el procedimiento completo antes de la práctica.");
            acciones.Add("Anota tus dudas y consúltalas al docente antes de la fecha de entrega.");
        }
        if (tipos.Contains("Tarea"))
        {
            acciones.Add($"Repasa {sobre} y resuelve de 3 a 5 ejercicios similares antes de entregar.");
            acciones.Add("Entrega a tiempo y revisa la retroalimentación del docente.");
        }
        return [.. acciones.Take(Interprete.MaxLista)];
    }
}

// Valida lo que devuelve el modelo para la ruta. Si algo no cumple se descarta la parte inválida y se conserva la plantilla.
public static class InterpreteRuta
{
    public sealed record Redaccion(string? Resumen, List<List<string>> AccionesPorEtapa, List<string> AccionesDocente, string? Mensaje);

    public static Redaccion? Interpretar(string? contenido, int etapas, out string? motivo)
    {
        motivo = null;
        var b = Interprete.InterpretarJson(contenido, out var raiz, out motivo);
        if (!b)
            return null;
        var resumen = Interprete.Texto(raiz, "resumen", 600);
        var mensaje = Interprete.Texto(raiz, "mensajeEstudiante", 1200);
        var docente = Interprete.Lista(raiz, "accionesDocente");
        var porEtapa = new List<List<string>>();
        foreach (var p in raiz.EnumerateObject())
            if (p.Name.Equals("etapas", StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == System.Text.Json.JsonValueKind.Array)
                foreach (var e in p.Value.EnumerateArray())
                    if (e.ValueKind == System.Text.Json.JsonValueKind.Object)
                        porEtapa.Add(Interprete.Lista(e, "acciones"));
                    else if (e.ValueKind == System.Text.Json.JsonValueKind.Array)
                        porEtapa.Add([.. e.EnumerateArray().Where(x => x.ValueKind == System.Text.Json.JsonValueKind.String).Select(x => x.GetString()!.Trim()).Where(x => x.Length >= 5).Take(Interprete.MaxLista)]);
        var todo = string.Join("\n", new[] { resumen, mensaje }.Concat(docente).Concat(porEtapa.SelectMany(x => x)));
        if (Interprete.Inadecuado(todo))
        {
            motivo = "incluye enlaces, correos o caracteres de otro idioma";
            return null;
        }
        if (porEtapa.Count == 0 && mensaje.Length < 40)
        {
            motivo = "no trae acciones por etapa ni mensaje";
            return null;
        }
        return new Redaccion(resumen.Length >= 10 ? resumen : null, porEtapa.Take(etapas).ToList(), docente, mensaje.Length >= 40 ? mensaje : null);
    }

    // Mezcla lo redactado por el modelo con la ruta de plantilla: lo que el modelo no entregó bien se queda como estaba.
    public static RutaDatos Aplicar(RutaDatos ruta, Redaccion r, string origen, string modelo)
    {
        var etapas = ruta.Etapas.Select((e, i) => i < r.AccionesPorEtapa.Count && r.AccionesPorEtapa[i].Count > 0 ? e with { Acciones = r.AccionesPorEtapa[i] } : e).ToList();
        return ruta with
        {
            Etapas = etapas,
            Resumen = r.Resumen ?? ruta.Resumen,
            AccionesDocente = r.AccionesDocente.Count > 0 ? r.AccionesDocente : ruta.AccionesDocente,
            Mensaje = r.Mensaje ?? ruta.Mensaje,
            Origen = origen,
            Modelo = modelo
        };
    }
}
