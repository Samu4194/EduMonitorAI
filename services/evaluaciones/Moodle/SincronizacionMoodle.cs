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
    // Elementos que ya no existen en Moodle y se retiraron de EduMonitor.
    public int Eliminados
    {
        get; set;
    }
    public List<string> Conflictos { get; } = [];
    // Cosas que Moodle no entregó o que no cuadraron: no impiden sincronizar, pero el docente debe saberlas.
    public List<string> Avisos { get; } = [];
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

        var resumen = new ResumenMoodle();
        resumen.Avisos.AddRange(aula.Avisos ?? []);
        // Si el libro de Moodle tiene una categoría por tipo de actividad, esa es la ponderación del curso (Moodle manda).
        if (aula.Pesos is { } pm)
        {
            var actual = await calificaciones.Ponderacion(cursoId, ct);
            if ((actual.Tareas, actual.Ape, actual.Examen) != (pm.Tareas, pm.Ape, pm.Examen))
            {
                await calificaciones.GuardarPonderacion(cursoId, new PonderacionRequest(pm.Tareas, pm.Ape, pm.Examen, actual.NotaMinima), ct);
                resumen.Actualizados++;
            }
        }
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
        var temasDeMoodle = aula.Secciones.ToDictionary(s => s.Id, s => s.Temas);
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
            var asociados = AsociarTemas(a, temasPorUnidad[a.Seccion], temasDeMoodle[a.Seccion]);
            var aid = await Resolver(key, async () => (await contenidos.CrearActividad(new(cursoId, a.Nombre, a.Tipo, null, a.Fecha, asociados), ct)).Id);
            if (mapa.ContainsKey(key))
            {
                if (!actividades.TryGetValue(aid, out var anterior) || anterior.Tipo != a.Tipo)
                    throw new ApiException(409, "Actividad vinculada ausente o incompatible.");
                if (anterior.Nombre != a.Nombre || anterior.FechaEntrega != a.Fecha || !anterior.Temas.Select(t => t.Id).ToHashSet().SetEquals(asociados))
                {
                    await contenidos.EditarActividad(aid, new(a.Nombre, a.Tipo, anterior.Descripcion, a.Fecha, asociados), ct);
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
        // Moodle manda: lo que ya no existe allá (una actividad o un tema borrados) se retira también aquí, junto con sus notas.
        var ausentes = mapa.Keys.Except(vistos).ToList();
        if (ausentes.Count > 0)
        {
            // Freno de seguridad: si de Moodle no llegó nada de contenido, lo más probable es una lectura incompleta y no se borra nada.
            if (aula.Secciones.Count == 0 && aula.Actividades.Count == 0)
                throw new ApiException(409, "Moodle no devolvió contenido del curso, así que no se retiró nada. Revisa el curso en Moodle e inténtalo de nuevo.");
            var conjunto = ausentes.ToHashSet();
            var todosLosTemas = unidades.SelectMany(u => u.Temas).ToDictionary(t => t.Id);
            // 1) Actividades (se van con sus notas). Antes que los temas, porque un tema usado por una actividad no se puede eliminar.
            foreach (var k in ausentes.Where(k => k.StartsWith("cm/")))
            {
                var nombre = actividades.TryGetValue(mapa[k], out var act) ? act.Nombre : "una actividad";
                await contenidos.EliminarActividad(mapa[k], ct);
                resumen.Eliminados++;
                resumen.Avisos.Add($"Se retiró «{nombre}» (ya no existe en Moodle) junto con sus notas.");
            }
            // 2) Unidades completas (arrastran sus temas y contenidos).
            var unidadesFuera = ausentes.Where(k => System.Text.RegularExpressions.Regex.IsMatch(k, @"^seccion/\d+$")).ToList();
            foreach (var k in unidadesFuera)
            {
                var nombre = unidades.FirstOrDefault(u => u.Id == mapa[k])?.Nombre ?? "una unidad";
                await contenidos.EliminarUnidad(mapa[k], ct);
                resumen.Eliminados++;
                resumen.Avisos.Add($"Se retiró la unidad «{nombre}» (ya no existe en Moodle).");
            }
            // 3) Temas sueltos cuya unidad sigue existiendo (sus contenidos se van con ellos).
            foreach (var k in ausentes.Where(k => System.Text.RegularExpressions.Regex.IsMatch(k, @"^seccion/\d+/tema/\d+$")))
            {
                if (conjunto.Contains(k[..k.IndexOf("/tema/", StringComparison.Ordinal)]))
                    continue;
                var nombre = todosLosTemas.TryGetValue(mapa[k], out var tema) ? tema.Nombre : "un tema";
                await contenidos.EliminarTema(mapa[k], ct);
                resumen.Eliminados++;
                resumen.Avisos.Add($"Se retiró el tema «{nombre}» (ya no existe en Moodle).");
            }
            db.RemoveRange(await db.Set<CorrespondenciaMoodle>().Where(x => x.CursoId == cursoId && ausentes.Contains(x.Clave)).ToListAsync(ct));
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return resumen;
    }

    // A qué temas de su unidad se relaciona una actividad de Moodle:
    //  - un examen evalúa toda la unidad;
    //  - si la configuración lo indica (Moodle:TemasPorActividad:código) o el código antiguo lo dice (…-T2), ese tema;
    //  - si el nombre de la actividad menciona un tema de la unidad, ese tema;
    //  - sin otra pista, la actividad evalúa la unidad completa (así nunca queda sin tema).
    private List<int> AsociarTemas(ActividadMoodle a, Dictionary<int, int> temas, List<TemaMoodle> temasMoodle)
    {
        if (a.Tipo == "Examen")
            return [.. temas.Values];
        if (configuracion.GetValue<int?>($"Moodle:TemasPorActividad:{a.Codigo}") is int configurado)
            return temas.TryGetValue(configurado, out var temaId) ? [temaId] : throw new ApiException(409, "Falta el tema configurado para la actividad.");
        if (a.NumeroTema is int n && temas.TryGetValue(n, out var porNumero))
            return [porNumero];
        var nombre = Normalizar(a.Nombre);
        var porNombre = temasMoodle.Where(t => nombre.Contains(Normalizar(t.Nombre))).Select(t => temas[t.Numero]).ToList();
        return porNombre.Count > 0 ? porNombre : [.. temas.Values];
    }

    private static string Normalizar(string s)
    {
        var d = s.Normalize(System.Text.NormalizationForm.FormD);
        return new string(d.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray()).ToLowerInvariant();
    }
}
