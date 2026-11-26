using EduMonitor.Seguimiento.Reglas;
namespace EduMonitor.Seguimiento.Agente;

// Respaldo del agente: redacta la recomendación con plantillas pedagógicas según los hallazgos. No usa IA ni red, así que siempre
// responde. Garantiza que el sistema nunca se quede sin recomendaciones aunque Ollama esté apagado y no haya internet.
public static class GeneradorPlantillas
{
    public static Borrador Generar(ContextoIA c)
    {
        var hallazgos = c.Hallazgos.OrderByDescending(h => Textos.DeNombre(h.Nivel)).ToList();
        var causas = new List<string>();
        var docente = new List<string>();
        var estudiante = new List<string>();
        var bajas = c.Notas.Where(n => n.Nota < c.NotaMinima).OrderBy(n => n.Nota).Take(3).Select(n => n.Actividad).ToList();
        // Entre comillas angulares: los nombres de actividad pueden traer comas o puntos medios («Tarea 1 · Posición») y se confundirían con el texto.
        var temas = bajas.Count > 0 ? string.Join(", ", bajas.Select(b => $"«{b}»")) : "las actividades con menor nota";

        foreach (var h in hallazgos)
            switch (h.Codigo)
            {
                case MotorReglas.RendimientoBajo:
                    causas.Add("Posibles vacíos en los contenidos evaluados hasta ahora.");
                    docente.Add($"Reunirse con el estudiante para revisar su avance y acordar un plan de recuperación con metas por actividad.");
                    estudiante.Add($"Repasa los contenidos de {temas} y vuelve a resolver los ejercicios que no salieron bien.");
                    break;
                case MotorReglas.NotaCritica:
                    causas.Add("Dificultad puntual con los temas de las actividades de menor nota.");
                    docente.Add($"Dar retroalimentación detallada de {temas} y señalar qué debe corregir.");
                    estudiante.Add("Pide al docente que te explique los errores de las actividades con nota crítica.");
                    break;
                case MotorReglas.TendenciaDescendente:
                    causas.Add("Disminución reciente del ritmo de estudio o de la asistencia (por confirmar con el estudiante).");
                    docente.Add("Conversar sobre qué cambió en las últimas semanas y ofrecer tutorías de refuerzo.");
                    estudiante.Add("Retoma un horario semanal de estudio con bloques de 45 minutos y repasa lo visto en clase.");
                    break;
                case MotorReglas.TipoDebil:
                    causas.Add($"Dificultad en {TipoDe(h)} (formato, tiempo de resolución o comprensión de lo pedido).");
                    docente.Add($"Compartir ejemplos resueltos y una guía para resolver {TipoDe(h)}.");
                    estudiante.Add($"Practica {TipoDe(h)} similares antes de la siguiente entrega.");
                    break;
                case MotorReglas.RiesgoDeReprobar:
                    causas.Add("El tiempo restante exige un desempeño alto y sostenido para llegar a la nota mínima.");
                    // Un promedio requerido mayor que 10 significa que aprobar ya no es posible solo con lo que falta.
                    var posible = c.Proyeccion?.PromedioRequerido is <= 10m;
                    docente.Add(posible
                        ? $"Priorizar con el estudiante las actividades pendientes: necesita un promedio de {Textos.N(c.Proyeccion!.PromedioRequerido!.Value)} en ellas."
                        : "Evaluar con el estudiante las opciones de recuperación disponibles: con las actividades que faltan ya no se alcanzaría la nota mínima.");
                    estudiante.Add(posible
                        ? $"Planifica las actividades que faltan apuntando a un promedio de {Textos.N(c.Proyeccion!.PromedioRequerido!.Value)}."
                        : "Conversa con el docente sobre las opciones de recuperación disponibles.");
                    break;
            }
        if (docente.Count == 0)
        {
            causas.Add("Sin causas identificadas con los datos disponibles.");
            docente.Add("Hacer seguimiento y conversar con el estudiante sobre su avance.");
            estudiante.Add("Consulta al docente ante cualquier duda y mantén el ritmo de estudio.");
        }
        causas = causas.Distinct().Take(3).ToList();
        docente = docente.Distinct().Take(Interprete.MaxLista).ToList();
        estudiante = estudiante.Distinct().Take(Interprete.MaxLista).ToList();

        var titulos = string.Join("; ", hallazgos.Select(h => h.Titulo.TrimEnd('.')).Distinct().Take(3));
        var resumen = hallazgos.Count > 0 ? $"Riesgo académico {c.Nivel.ToLowerInvariant()}: {titulos.ToLowerInvariant()}." : "Sin alertas académicas relevantes.";
        var actual = c.ResultadoActual is { } v ? $"tu resultado actual es {Textos.N(v)} sobre 10 y la nota mínima para aprobar es {Textos.N(c.NotaMinima)}" : $"la nota mínima para aprobar es {Textos.N(c.NotaMinima)}";
        var mensaje = $"Revisando tu avance en {c.Materia}, {actual}. {Principal(hallazgos)} Te sugiero: {Minuscula(estudiante[0])}"
            + (estudiante.Count > 1 ? $" Además: {Minuscula(estudiante[1])}" : "")
            + " Cuenta conmigo para resolver tus dudas: con constancia y apoyo puedes mejorar tu resultado.";
        return new Borrador(resumen, causas, docente, estudiante, mensaje);
    }

    static string TipoDe(HallazgoIA h) => h.Titulo.Replace("Debilidad en ", "");
    static string Minuscula(string s) => s.Length == 0 ? s : char.ToLowerInvariant(s[0]) + s[1..];
    static string Principal(List<HallazgoIA> hallazgos) => hallazgos.FirstOrDefault()?.Codigo switch
    {
        MotorReglas.RendimientoBajo => "Todavía estás por debajo de lo necesario para aprobar, pero aún hay tiempo para recuperarte.",
        MotorReglas.NotaCritica => "Algunas actividades salieron con notas bajas y conviene revisarlas juntos.",
        MotorReglas.TendenciaDescendente => "Tus últimas notas bajaron respecto a las anteriores.",
        MotorReglas.TipoDebil => "Hay un tipo de actividad que te está costando más que el resto.",
        MotorReglas.RiesgoDeReprobar => "Para aprobar necesitarás un buen desempeño en lo que resta del curso.",
        _ => ""
    };
}
