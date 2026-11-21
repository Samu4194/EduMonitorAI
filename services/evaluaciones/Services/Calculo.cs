using EduMonitor.Evaluaciones.DTOs;
using EduMonitor.Evaluaciones.Models;
namespace EduMonitor.Evaluaciones.Services;

// Reglas de calificación del proyecto (escala 0 a 10, nota mínima 7 por defecto, ponderación por tipo de actividad).
// Es código puro, sin base de datos, para poder comprobarlo con casos de prueba.
public static class Calculo
{
    public const decimal NotaMaxima = 10m;
    public static readonly PonderacionView Predeterminada = new(30m, 30m, 40m, 7m, false);
    public const string SinCalificaciones = "Sin calificaciones", Optimo = "Óptimo", EnRiesgo = "En riesgo";

    public static decimal Peso(PonderacionView p, string tipo) => tipo switch
    {
        TipoActividad.Tarea => p.Tareas,
        TipoActividad.Ape => p.Ape,
        TipoActividad.Examen => p.Examen,
        _ => 0m
    };
    public static decimal Redondear(decimal valor) => Math.Round(valor, 2, MidpointRounding.AwayFromZero);

    public record Resultado(decimal? Valor, bool Definitivo, int Calificadas, int Pendientes, decimal? NotaMasBaja);

    // Una actividad sin nota es "pendiente": no cuenta como cero ni baja el promedio. Con notas parciales el resultado se calcula con los
    // tipos que ya tienen nota (los pesos se reparten entre ellos) y se marca como provisional hasta que no quede nada pendiente.
    // exigirTipos: el resultado solo es definitivo si cada tipo con porcentaje tiene al menos una actividad. No aplica al filtrar por tema.
    public static Resultado Calcular(IReadOnlyCollection<(int Id, string Tipo)> actividades, IReadOnlyDictionary<int, decimal> notas, PonderacionView pon, bool exigirTipos)
    {
        var calificadas = actividades.Where(a => notas.ContainsKey(a.Id)).ToList();
        decimal suma = 0, pesos = 0;
        foreach (var grupo in calificadas.GroupBy(a => a.Tipo))
        {
            var peso = Peso(pon, grupo.Key);
            if (peso <= 0)
                continue;
            suma += peso * grupo.Average(a => notas[a.Id]);
            pesos += peso;
        }
        decimal? valor = pesos > 0 ? Redondear(suma / pesos) : null;
        var pendientes = actividades.Count - calificadas.Count;
        var tiposCubiertos = !exigirTipos || TipoActividad.Todos.Where(t => Peso(pon, t) > 0).All(t => actividades.Any(a => a.Tipo == t));
        var definitivo = valor is not null && actividades.Count > 0 && pendientes == 0 && tiposCubiertos;
        decimal? masBaja = calificadas.Count > 0 ? calificadas.Min(a => notas[a.Id]) : null;
        return new Resultado(valor, definitivo, calificadas.Count, pendientes, masBaja);
    }
    // Desglose por tipo: cuántos puntos de los 10 lleva ganados el estudiante en tareas, APE y exámenes.
    // Cada tipo aporta su promedio (de lo ya calificado) por su porcentaje; un tipo sin notas todavía aporta 0.
    public static List<PuntosTipo> Desglose(IReadOnlyCollection<(int Id, string Tipo)> actividades, IReadOnlyDictionary<int, decimal> notas, PonderacionView pon) =>
        TipoActividad.Todos.Select(tipo =>
        {
            var delTipo = actividades.Where(a => a.Tipo == tipo).ToList();
            var valores = delTipo.Where(a => notas.ContainsKey(a.Id)).Select(a => notas[a.Id]).ToList();
            var peso = Peso(pon, tipo);
            decimal? promedio = valores.Count > 0 ? Redondear(valores.Average()) : null;
            return new PuntosTipo(tipo, peso, Redondear(peso / 10m), promedio, valores.Count > 0 ? Redondear(valores.Average() * peso / 100m) : 0m, valores.Count, delTipo.Count);
        }).ToList();

    public static string Estado(decimal? resultado, decimal notaMinima) => resultado is null ? SinCalificaciones : resultado < notaMinima ? EnRiesgo : Optimo;
}
