using System.Linq.Expressions;
using System.Security.Claims;
using EduMonitor.Infrastructure;
using EduMonitor.Materias.Data;
using EduMonitor.Materias.DTOs;
using EduMonitor.Materias.Models;
using Microsoft.EntityFrameworkCore;
namespace EduMonitor.Materias.Services;

public sealed partial class MateriasService(MateriasDb db, DependencyClient dependencies)
{
    private static string Normalizar(string codigo) => codigo.Trim().ToUpperInvariant();
    private static bool EsAdmin(ClaimsPrincipal user) => user.IsInRole("Admin");
    // El administrador ve todas las carreras. Un docente solo ve las suyas (claims "carrera" del token).
    private IQueryable<Carrera> CarrerasVisibles(ClaimsPrincipal user)
    {
        if (EsAdmin(user))
            return db.Carreras;
        var mias = user.Carreras().Select(Normalizar).ToList();
        return db.Carreras.Where(c => mias.Contains(c.Codigo) && c.Activa);
    }
    // Un docente solo ve los cursos que tiene asignados.
    private IQueryable<Curso> CursosVisibles(ClaimsPrincipal user)
    {
        if (EsAdmin(user))
            return db.Cursos;
        var yo = user.UserId();
        return db.Cursos.Where(c => c.TeacherId == yo && c.Paralelo.Carrera.Activa);
    }
    private static readonly Expression<Func<Curso, CursoView>> VistaCurso = c => new CursoView(c.Id, c.MateriaId, c.Materia.Codigo, c.Materia.Nombre, c.Materia.Creditos, c.Materia.HorasTotales,
        c.Materia.Carrera.Codigo, c.Materia.Carrera.Nombre, c.Paralelo.Nivel, c.Paralelo.Letra, c.Paralelo.PeriodoId, c.Paralelo.Periodo.Nombre, c.TeacherId, c.Paralelo.Malla.Version, c.Paralelo.Periodo.Estado);
    private Task<int?> PeriodoActivoId(CancellationToken ct) => db.Periodos.Where(p => p.Activo).Select(p => (int?)p.Id).FirstOrDefaultAsync(ct);

    public async Task<List<CarreraView>> Carreras(string? estado, ClaimsPrincipal user, CancellationToken ct)
    {
        var query = CarrerasVisibles(user).AsNoTracking();
        switch ((estado ?? "activas").Trim().ToLowerInvariant())
        {
            case "activas":
                query = query.Where(c => c.Activa);
                break;
            case "bajas":
                query = EsAdmin(user) ? query.Where(c => !c.Activa) : query.Where(c => false);
                break;
            case "todas":
                break;
            default:
                throw new ApiException(400, "El estado debe ser activas, bajas o todas.");
        }
        var carreras = await query.Include(c => c.Mallas).OrderBy(c => c.Nombre).ToListAsync(ct);
        var porMalla = await db.Materias.AsNoTracking().GroupBy(m => m.MallaId).Select(g => new { Malla = g.Key, Total = g.Count() }).ToDictionaryAsync(x => x.Malla, x => x.Total, ct);
        return carreras.Select(c =>
        {
            var vigente = c.Mallas.OrderByDescending(m => m.Version).FirstOrDefault();
            return new CarreraView(c.Id, c.Codigo, c.Nombre, c.Modalidad, c.Niveles, c.HorasTotales, c.CreditosTotales, vigente is null ? 0 : porMalla.GetValueOrDefault(vigente.Id), c.PrefijoUta, vigente?.Version ?? 0, c.Mallas.Count, c.Activa, c.BajaEn);
        }).ToList();
    }
    // malla: nulo = la vigente, un número = esa versión, "todas" = todas las versiones.
    public async Task<List<MateriaView>> MateriasDe(string codigo, int? nivel, string? malla, ClaimsPrincipal user, CancellationToken ct)
    {
        var code = Normalizar(codigo);
        var carrera = await CarrerasVisibles(user).AsNoTracking().Include(c => c.Mallas).SingleOrDefaultAsync(c => c.Codigo == code, ct) ?? throw new ApiException(404, "Carrera no encontrada o no asignada a tu cuenta.");
        var query = db.Materias.AsNoTracking().Where(m => m.CarreraId == carrera.Id);
        if (!string.Equals(malla?.Trim(), "todas", StringComparison.OrdinalIgnoreCase))
        {
            if (malla is not null && !int.TryParse(malla, out _))
                throw new ApiException(400, "La malla debe ser un número de versión o \"todas\".");
            var version = malla is null ? carrera.Mallas.Select(m => m.Version).DefaultIfEmpty(0).Max() : int.Parse(malla);
            query = query.Where(m => m.Malla.Version == version);
        }
        if (nivel.HasValue)
            query = query.Where(m => m.Nivel == nivel);
        return await query.OrderBy(m => m.Malla.Version).ThenBy(m => m.Nivel).ThenBy(m => m.OrdenNivel).Select(m => new MateriaView(m.Id, m.Codigo, m.CodigoUta, m.Nombre, m.Nivel, m.Unidad, m.Tipo,
            m.HorasContactoDocente, m.HorasPracticoExperimental, m.HorasAutonomas, m.HorasTotales, m.Creditos, m.CreditosDerivado, m.Malla.Version)).ToListAsync(ct);
    }

    public Task<List<PeriodoView>> Periodos(CancellationToken ct) => db.Periodos.AsNoTracking().OrderByDescending(p => p.Id).Select(p => new PeriodoView(p.Id, p.Nombre, p.Activo, p.Estado)).ToListAsync(ct);
    public async Task<PeriodoView> CrearPeriodo(PeriodoRequest input, CancellationToken ct)
    {
        var nombre = input.Nombre.Trim();
        if (await db.Periodos.AnyAsync(p => p.Nombre == nombre, ct))
            throw new ApiException(409, "El período ya está registrado.");
        var periodo = new Periodo { Nombre = nombre };
        db.Periodos.Add(periodo);
        await db.SaveChangesAsync(ct);
        return new PeriodoView(periodo.Id, periodo.Nombre, periodo.Activo, periodo.Estado);
    }
    public async Task<PeriodoView> ActivarPeriodo(int id, CancellationToken ct)
    {
        var periodos = await db.Periodos.ToListAsync(ct);
        var elegido = periodos.SingleOrDefault(p => p.Id == id) ?? throw new ApiException(404, "Período no encontrado.");
        foreach (var p in periodos)
            p.Activo = p.Id == id;
        await db.SaveChangesAsync(ct);
        return new PeriodoView(elegido.Id, elegido.Nombre, elegido.Activo, elegido.Estado);
    }
    // Iniciar el período bloquea la carga de mallas; finalizarlo la libera para preparar el siguiente.
    public async Task<PeriodoView> CambiarEstadoPeriodo(int id, bool iniciar, CancellationToken ct)
    {
        var periodo = await db.Periodos.SingleOrDefaultAsync(p => p.Id == id, ct) ?? throw new ApiException(404, "Período no encontrado.");
        if (iniciar)
        {
            if (!periodo.Activo)
                throw new ApiException(409, "Solo se puede iniciar el período activo. Actívalo primero.");
            if (periodo.Estado != Periodo.Preparacion)
                throw new ApiException(409, $"El período está {periodo.Estado.ToLowerInvariant()}: solo se inicia uno en preparación.");
            periodo.Estado = Periodo.EnCurso;
        }
        else
        {
            if (periodo.Estado != Periodo.EnCurso)
                throw new ApiException(409, "Solo se puede finalizar un período en curso.");
            periodo.Estado = Periodo.Finalizado;
        }
        await db.SaveChangesAsync(ct);
        return new PeriodoView(periodo.Id, periodo.Nombre, periodo.Activo, periodo.Estado);
    }

    public async Task<List<ParaleloView>> Paralelos(string? carrera, int? nivel, int? periodoId, ClaimsPrincipal user, CancellationToken ct)
    {
        var periodo = periodoId ?? await PeriodoActivoId(ct);
        if (periodo is null)
            return [];
        var visibles = CarrerasVisibles(user).Select(c => c.Id);
        var query = db.Paralelos.AsNoTracking().Where(p => p.PeriodoId == periodo && p.Carrera.Activa && visibles.Contains(p.CarreraId));
        if (!string.IsNullOrWhiteSpace(carrera))
        {
            var code = Normalizar(carrera);
            query = query.Where(p => p.Carrera.Codigo == code);
        }
        if (nivel.HasValue)
            query = query.Where(p => p.Nivel == nivel);
        return await query.OrderBy(p => p.Carrera.Codigo).ThenBy(p => p.Nivel).ThenBy(p => p.Letra).Select(p => new ParaleloView(p.Id, p.PeriodoId, p.Carrera.Codigo, p.Nivel, p.Letra, p.Malla.Version)).ToListAsync(ct);
    }
    public async Task<ParaleloView> CrearParalelo(ParaleloRequest input, CancellationToken ct)
    {
        var periodo = await db.Periodos.SingleOrDefaultAsync(p => p.Activo, ct) ?? throw new ApiException(409, "No hay un período activo. Activa uno antes de crear paralelos.");
        var code = Normalizar(input.Carrera);
        var carrera = await db.Carreras.Include(c => c.Mallas).ThenInclude(m => m.Materias).SingleOrDefaultAsync(c => c.Codigo == code, ct) ?? throw new ApiException(404, "Carrera no encontrada.");
        ExigirActiva(carrera);
        if (input.Nivel > carrera.Niveles)
            throw new ApiException(400, $"{carrera.Nombre} tiene {carrera.Niveles} niveles.");
        // El paralelo se dicta con una malla: la vigente, salvo que se pida otra (para quienes siguen con una malla anterior).
        var malla = (input.Malla.HasValue ? carrera.Mallas.SingleOrDefault(m => m.Version == input.Malla) : carrera.Mallas.OrderByDescending(m => m.Version).FirstOrDefault())
            ?? throw new ApiException(input.Malla.HasValue ? 404 : 409, input.Malla.HasValue ? $"{carrera.Nombre} no tiene la malla {input.Malla}." : $"{carrera.Nombre} todavía no tiene malla. Cárgala antes de crear paralelos.");
        var letra = input.Letra.ToUpperInvariant();
        if (await db.Paralelos.AnyAsync(p => p.PeriodoId == periodo.Id && p.CarreraId == carrera.Id && p.Nivel == input.Nivel && p.Letra == letra, ct))
            throw new ApiException(409, $"El paralelo {letra} del nivel {input.Nivel} de {carrera.Nombre} ya existe en este período.");
        var paralelo = CatalogoSeed.AgregarParalelo(db, periodo, carrera, malla, input.Nivel, letra, malla.Materias.Where(m => m.Nivel == input.Nivel));
        await db.SaveChangesAsync(ct);
        return new ParaleloView(paralelo.Id, periodo.Id, carrera.Codigo, paralelo.Nivel, paralelo.Letra, malla.Version);
    }

    public async Task<List<CursoView>> Cursos(string? carrera, int? nivel, string? paralelo, int? periodoId, ClaimsPrincipal user, CancellationToken ct)
    {
        var periodo = periodoId ?? await PeriodoActivoId(ct);
        if (periodo is null)
            return [];
        var query = CursosVisibles(user).AsNoTracking().Where(c => c.Paralelo.PeriodoId == periodo && c.Paralelo.Carrera.Activa);
        if (!string.IsNullOrWhiteSpace(carrera))
        {
            var code = Normalizar(carrera);
            query = query.Where(c => c.Paralelo.Carrera.Codigo == code);
        }
        if (nivel.HasValue)
            query = query.Where(c => c.Paralelo.Nivel == nivel);
        if (!string.IsNullOrWhiteSpace(paralelo))
        {
            var letra = Normalizar(paralelo);
            query = query.Where(c => c.Paralelo.Letra == letra);
        }
        return await query.OrderBy(c => c.Paralelo.Carrera.Codigo).ThenBy(c => c.Paralelo.Nivel).ThenBy(c => c.Paralelo.Letra).ThenBy(c => c.Materia.OrdenNivel).Select(VistaCurso).ToListAsync(ct);
    }
    public async Task<CursoView> Curso(int id, ClaimsPrincipal user, CancellationToken ct) =>
        await CursosVisibles(user).AsNoTracking().Where(c => c.Id == id).Select(VistaCurso).SingleOrDefaultAsync(ct) ?? throw new ApiException(404, "Curso no encontrado o no asignado a tu cuenta.");
    public async Task<CursoView> Asignar(int id, int? teacherId, CancellationToken ct)
    {
        var curso = await db.Cursos.Include(c => c.Materia).ThenInclude(m => m.Carrera).SingleOrDefaultAsync(c => c.Id == id, ct) ?? throw new ApiException(404, "Curso no encontrado.");
        if (teacherId.HasValue)
        {
            ExigirActiva(curso.Materia.Carrera);
            // Usuarios confirma que el docente existe y a qué carreras pertenece. Un docente solo puede dictar materias de su carrera.
            var docente = await dependencies.Get<TeacherReference>("Usuarios", $"/api/docentes/{teacherId}", ct);
            if (!docente.Carreras.Contains(curso.Materia.Carrera.Codigo, StringComparer.OrdinalIgnoreCase))
                throw new ApiException(409, $"El docente no pertenece a la carrera {curso.Materia.Carrera.Nombre}.");
        }
        curso.TeacherId = teacherId;
        await db.SaveChangesAsync(ct);
        return await db.Cursos.AsNoTracking().Where(c => c.Id == id).Select(VistaCurso).SingleAsync(ct);
    }
}
