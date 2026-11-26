using EduMonitor.Seguimiento.DTOs;
using EduMonitor.Seguimiento.Reglas;
using EduMonitor.Seguimiento.Services;
namespace EduMonitor.Seguimiento.Asistente;

public sealed record TemaNota(int TemaId, string Tema, string Unidad, decimal? Promedio, int Calificadas, int Pendientes);
// Rendimiento de un tema en todo el curso: promedio de las notas de las actividades relacionadas y cuántos estudiantes quedan por debajo del mínimo.
public sealed record TemaCurso(int TemaId, string Tema, string Unidad, int Actividades, decimal? Promedio, int Calificadas, int NotasBajas, List<int> EstudiantesBajoMinimo);

// Fotografía en vivo de un curso para el asistente: notas, temas de cada actividad y lo que detecta el motor de reglas en este momento.
// No se guarda: se calcula con cada pregunta, así el docente siempre consulta las notas actuales.
public sealed class DatosCurso(CursoReference curso, MatrizReference matriz, List<ActividadTemasReference> actividades, List<Persona> lista, ResultadoCurso reglas, Pesos pesos)
{
    readonly Dictionary<int, ResultadoEstudiante> resultados = reglas.Estudiantes.ToDictionary(e => e.EstudianteId);
    readonly Dictionary<int, FilaReference> filas = matriz.Estudiantes.ToDictionary(e => e.EstudianteId);

    public CursoReference Curso => curso;
    public decimal NotaMinima => matriz.Ponderacion.NotaMinima;
    public Pesos Pesos => pesos;
    public IReadOnlyList<ActividadTemasReference> Actividades => actividades;
    public IReadOnlyList<Persona> Lista => lista;
    public ResultadoCurso Reglas => reglas;
    public IReadOnlyList<FilaReference> Filas => matriz.Estudiantes;

    public Persona? Persona(int id) => lista.FirstOrDefault(p => p.Id == id);
    public ResultadoEstudiante? De(int id) => resultados.GetValueOrDefault(id);
    public IReadOnlyDictionary<int, decimal> Notas(int estudianteId) => filas.TryGetValue(estudianteId, out var f) ? f.Notas : new Dictionary<int, decimal>();
    public decimal? Resultado(int estudianteId) => filas.TryGetValue(estudianteId, out var f) ? f.Resultado : null;

    // Promedio por tema de un estudiante. Se calcula igual que Evaluaciones: promedio de sus notas en las actividades del tema.
    public List<TemaNota> Temas(int estudianteId)
    {
        var notas = Notas(estudianteId);
        var salida = new List<TemaNota>();
        foreach (var grupo in actividades.SelectMany(a => a.Temas.Select(t => (Tema: t, Actividad: a))).GroupBy(x => x.Tema.Id))
        {
            var primero = grupo.First().Tema;
            var valores = grupo.Where(x => notas.ContainsKey(x.Actividad.Id)).Select(x => notas[x.Actividad.Id]).ToList();
            salida.Add(new TemaNota(primero.Id, primero.Nombre, primero.UnidadNombre, valores.Count > 0 ? Math.Round(valores.Average(), 2, MidpointRounding.AwayFromZero) : null,
                valores.Count, grupo.Count() - valores.Count));
        }
        return [.. salida.OrderBy(t => t.Promedio ?? 11m).ThenBy(t => t.Tema)];
    }

    public List<TemaCurso> TemasDelCurso()
    {
        var salida = new List<TemaCurso>();
        foreach (var grupo in actividades.SelectMany(a => a.Temas.Select(t => (Tema: t, Actividad: a))).GroupBy(x => x.Tema.Id))
        {
            var primero = grupo.First().Tema;
            var ids = grupo.Select(x => x.Actividad.Id).ToList();
            var valores = matriz.Estudiantes.SelectMany(e => ids.Where(e.Notas.ContainsKey).Select(i => e.Notas[i])).ToList();
            var bajo = lista.Where(p => Temas(p.Id).FirstOrDefault(t => t.TemaId == primero.Id)?.Promedio is { } v && v < NotaMinima).Select(p => p.Id).ToList();
            salida.Add(new TemaCurso(primero.Id, primero.Nombre, primero.UnidadNombre, ids.Count, valores.Count > 0 ? Math.Round(valores.Average(), 2, MidpointRounding.AwayFromZero) : null,
                valores.Count, valores.Count(v => v < NotaMinima), bajo));
        }
        return [.. salida.OrderBy(t => t.Promedio ?? 11m).ThenBy(t => t.Tema)];
    }

    // Peso de cada actividad en la nota final (en %): el del tipo repartido entre las actividades de ese tipo.
    // Los tipos sin actividades no cuentan: el docente aún no los planificó (misma regla que el cálculo de Evaluaciones).
    public decimal Aporte(ActividadTemasReference a)
    {
        var tipos = actividades.GroupBy(x => x.Tipo).Where(g => pesos.De(g.Key) > 0).ToList();
        var total = tipos.Sum(g => pesos.De(g.Key));
        var delTipo = actividades.Count(x => x.Tipo == a.Tipo);
        return total <= 0 || delTipo == 0 ? 0m : pesos.De(a.Tipo) / total / delTipo * 100m;
    }
}

public sealed class CargadorDatos(AccesoCursos acceso, MotorReglas motor)
{
    public async Task<DatosCurso> Cargar(int cursoId, CancellationToken ct)
    {
        var curso = await acceso.Curso(cursoId, escritura: false, ct);
        var matrizT = acceso.Matriz(cursoId, ct);
        var listaT = acceso.Estudiantes(cursoId, ct);
        var actividadesT = acceso.Actividades(cursoId, ct);
        await Task.WhenAll(matrizT, listaT, actividadesT);
        var matriz = await matrizT;
        var pesos = new Pesos(matriz.Ponderacion.Tareas, matriz.Ponderacion.Ape, matriz.Ponderacion.Examen);
        var datos = new CursoDato(matriz.Ponderacion.NotaMinima, pesos,
            matriz.Actividades.Select(a => new ActividadDato(a.Id, a.Nombre, a.Tipo, a.FechaEntrega)).ToList(),
            matriz.Estudiantes.Select(e => new EstudianteDato(e.EstudianteId, e.Resultado, e.Notas)).ToList());
        var lista = (await listaT).Select(e => new Persona(e.Id, e.Apellidos, e.Nombres, e.Correo)).ToList();
        return new DatosCurso(curso, matriz, await actividadesT, lista, motor.Evaluar(datos), pesos);
    }
}
