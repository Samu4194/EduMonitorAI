using System.Data;
using EduMonitor.Evaluaciones.Data;
using EduMonitor.Evaluaciones.DTOs;
using EduMonitor.Evaluaciones.Models;
using EduMonitor.Evaluaciones.Services;
using EduMonitor.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EduMonitor.Evaluaciones.Moodle;

public record EstadoVinculoMoodle(bool Vinculado, Uri? UrlCurso);

public sealed class ResumenMoodle
{
    public int Creados
    {
        get; set;
    }
    public int Actualizados
    {
        get; set;
    }
    public int Omitidos
    {
        get; set;
    }
    public List<string> Conflictos { get; } = [];
}

public sealed class SincronizacionMoodle(EvaluacionesDb db, AccesoCursos acceso, MoodleClient cliente,
    ContenidosService contenidos, CalificacionesService calificaciones, IConfiguration configuracion)
{
    public async Task<EstadoVinculoMoodle> Estado(int cursoId, CancellationToken ct)
    {
        await acceso.Curso(cursoId, ct);
        var id = await db.Set<VinculoMoodle>().Where(v => v.CursoId == cursoId)
            .Select(v => (int?)v.MoodleId).SingleOrDefaultAsync(ct);
        return new(id.HasValue, id.HasValue ? cliente.UrlCurso(id.Value) : null);
    }

    public async Task<VinculoMoodle> Vincular(int cursoId, string codigo, CancellationToken ct)
    {
        await acceso.Curso(cursoId, ct);
        var id = await cliente.Resolver(codigo, ct);
        await cliente.Aula(id, codigo, ct);
        var vinculo = await db.Set<VinculoMoodle>().SingleOrDefaultAsync(v => v.CursoId == cursoId || v.MoodleId == id, ct);
        if (vinculo != null)
        {
            if (vinculo.CursoId != cursoId || vinculo.MoodleId != id || vinculo.Codigo != codigo)
                throw new ApiException(409, "Uno de los cursos ya tiene otra correspondencia.");
            return vinculo;
        }
        vinculo = new()
        {
            CursoId = cursoId,
            MoodleId = id,
            Codigo = codigo
        };
        db.Add(vinculo);
        await db.SaveChangesAsync(ct);
        return vinculo;
    }

    public async Task<AulaMoodle> Leer(int cursoId, CancellationToken ct)
    {
        await acceso.Curso(cursoId, ct);
        var vinculo = await db.Set<VinculoMoodle>().AsNoTracking().SingleOrDefaultAsync(x => x.CursoId == cursoId, ct)
            ?? throw new ApiException(409, "El administrador debe vincular este curso con Moodle.");
        return await cliente.Aula(vinculo.MoodleId, vinculo.Codigo, ct);
    }

    public async Task<ResumenMoodle> Importar(int cursoId, CancellationToken ct)
    {
        await acceso.Curso(cursoId, ct);
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        // Serializar por curso antes de consultar Moodle evita aplicar una lectura más antigua tras otra más reciente.
        var vinculo = await db.Set<VinculoMoodle>()
            .FromSqlInterpolated($"SELECT * FROM [VinculoMoodle] WITH (UPDLOCK) WHERE [CursoId] = {cursoId}")
            .AsNoTracking().SingleOrDefaultAsync(ct) ?? throw new ApiException(409, "El administrador debe vincular este curso con Moodle.");
        var aula = await cliente.Aula(vinculo.MoodleId, vinculo.Codigo, ct);
        var lista = await acceso.Estudiantes(cursoId, ct);
        var inscritos = lista.Where(x => x.MoodleUsuarioId.HasValue).ToDictionary(x => x.MoodleUsuarioId!.Value);
        if (!inscritos.Keys.ToHashSet().SetEquals(aula.Alumnos.Select(a => a.Id)) ||
            aula.Alumnos.Any(a => inscritos[a.Id].MoodleCodigo != a.Codigo))
            throw new ApiException(409, "La matrícula Moodle y las correspondencias locales difieren. Solicita la revisión del administrador.");

        var pesos = await calificaciones.Ponderacion(cursoId, ct);
        if ((pesos.Tareas, pesos.Ape, pesos.Examen, pesos.NotaMinima) != (30m, 30m, 40m, 7m))
            throw new ApiException(409, "El curso tiene otra ponderación o nota mínima. Se conservó su configuración.");
        var resumen = new ResumenMoodle();
        var mapa = await db.Set<CorrespondenciaMoodle>().Where(x => x.CursoId == cursoId).ToDictionaryAsync(x => x.Clave, x => x.LocalId, ct);
        var vistos = new HashSet<string>();
        async Task<int> Resolver(string clave, Func<Task<int>> crear)
        {
            vistos.Add(clave);
            if (mapa.TryGetValue(clave, out var local))
                return local;
            local = await crear();
            db.Add(new CorrespondenciaMoodle { CursoId = cursoId, Clave = clave, LocalId = local });
            resumen.Creados++;
            return local;
        }
        var unidades = await contenidos.Unidades(cursoId, ct);
        var actividades = (await contenidos.Actividades(cursoId, null, null, ct)).ToDictionary(x => x.Id);
        var temasPorUnidad = new Dictionary<int, Dictionary<int, int>>();
        foreach (var seccion in aula.Secciones)
        {
            var key = $"seccion/{seccion.Id}";
            var uid = await Resolver(key, async () => (await contenidos.CrearUnidad(new(cursoId, seccion.Nombre, null), ct)).Id);
            var unidad = unidades.SingleOrDefault(u => u.Id == uid);
            if (mapa.ContainsKey(key))
            {
                if (unidad == null)
                    throw new ApiException(409, "Unidad vinculada ausente.");
                if (unidad.Nombre != seccion.Nombre)
                {
                    await contenidos.EditarUnidad(uid, new(seccion.Nombre, unidad.Descripcion), ct);
                    resumen.Actualizados++;
                }
                else
                    resumen.Omitidos++;
            }
            var temasUnidad = new Dictionary<int, int>();
            foreach (var tema in seccion.Temas)
            {
                var tk = $"{key}/tema/{tema.Numero}";
                var tid = await Resolver(tk, async () => (await contenidos.CrearTema(uid, new(tema.Nombre, null, null), ct)).Id);
                var previo = unidad?.Temas.SingleOrDefault(t => t.Id == tid);
                if (mapa.ContainsKey(tk))
                {
                    if (previo == null)
                        throw new ApiException(409, "Tema vinculado ausente.");
                    if (previo.Nombre != tema.Nombre)
                    {
                        await contenidos.EditarTema(tid, new(tema.Nombre, previo.Descripcion), ct);
                        resumen.Actualizados++;
                    }
                    else
                        resumen.Omitidos++;
                }
                temasUnidad.Add(tema.Numero, tid);
                var ck = tk + "/contenido";
                var cid = await Resolver(ck, async () => (await contenidos.CrearContenido(tid, new(tema.Contenido), ct)).Id);
                if (mapa.ContainsKey(ck))
                {
                    var anterior = previo?.Contenidos.SingleOrDefault(c => c.Id == cid) ?? throw new ApiException(409, "Contenido vinculado ausente.");
                    if (anterior.Descripcion != tema.Contenido)
                    {
                        await contenidos.EditarContenido(cid, new(tema.Contenido), ct);
                        resumen.Actualizados++;
                    }
                    else
                        resumen.Omitidos++;
                }
            }
            temasPorUnidad.Add(seccion.Id, temasUnidad);
        }
        foreach (var a in aula.Actividades)
        {
            var key = $"cm/{a.Cm}/{a.Codigo}";
            // El cuestionario de unidad cubre sus temas. En esta aula T1/T2 corresponden a la temática numerada.
            // Las APE del aula de Física tienen una correspondencia explícita por código estable.
            var temas = temasPorUnidad[a.Seccion];
            List<int> asociados = a.Tipo switch
            {
                "Examen" => temas.Values.ToList(),
                "APE" when configuracion.GetValue<int?>($"Moodle:TemasPorActividad:{a.Codigo}") is int numero =>
                    temas.TryGetValue(numero, out var temaId) ? [temaId] : throw new ApiException(409, "Falta el tema configurado para la APE."),
                "Tarea" when int.TryParse(a.Codigo.Split('-')[^1].AsSpan(1), out var n) && temas.TryGetValue(n, out var id) => [id],
                _ => []
            };
            var aid = await Resolver(key, async () => (await contenidos.CrearActividad(new(cursoId, a.Nombre, a.Tipo, null, null, asociados), ct)).Id);
            if (mapa.ContainsKey(key))
            {
                if (!actividades.TryGetValue(aid, out var anterior) || anterior.Tipo != a.Tipo)
                    throw new ApiException(409, "Actividad vinculada ausente o incompatible.");
                if (anterior.Nombre != a.Nombre || !anterior.Temas.Select(t => t.Id).ToHashSet().SetEquals(asociados))
                {
                    await contenidos.EditarActividad(aid, new(a.Nombre, a.Tipo, anterior.Descripcion, anterior.FechaEntrega, asociados), ct);
                    resumen.Actualizados++;
                }
                else
                    resumen.Omitidos++;
            }
            var anteriores = await db.Calificaciones.Where(n => n.ActividadId == aid).ToDictionaryAsync(n => n.EstudianteId, n => n.Nota, ct);
            var cambios = new List<NotaEntrada>();
            foreach (var alumno in aula.Alumnos)
            {
                var eid = inscritos[alumno.Id].Id;
                var existe = anteriores.TryGetValue(eid, out var anterior);
                var nota = aula.Notas[(alumno.Id, a.Cm)];
                if (nota is null)
                {
                    if (existe)
                        resumen.Conflictos.Add($"{alumno.Codigo} · {a.Nombre}: Moodle indica pendiente. Se conservó {anterior:0.00}.");
                    resumen.Omitidos++;
                }
                else if (existe && anterior == Calculo.Redondear(nota.Value))
                    resumen.Omitidos++;
                else
                {
                    cambios.Add(new(eid, nota));
                    if (existe)
                        resumen.Actualizados++;
                    else
                        resumen.Creados++;
                }
            }
            if (cambios.Count > 0)
                await calificaciones.Guardar(aid, new(cambios), ct);
        }
        if (mapa.Keys.Except(vistos).Any())
            throw new ApiException(409, "Moodle omitió elementos previamente vinculados. No se aplicaron cambios.");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return resumen;
    }
}
