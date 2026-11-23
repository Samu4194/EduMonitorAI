using EduMonitor.Infrastructure;
using EduMonitor.Estudiantes.Data;
using EduMonitor.Estudiantes.DTOs;
using EduMonitor.Estudiantes.Models;
using Microsoft.EntityFrameworkCore;
namespace EduMonitor.Estudiantes.Services;

public sealed class EstudiantesService(EstudiantesDb db, DependencyClient dependencies)
{
    // Máximo de créditos que un estudiante puede sumar en el período con sus materias activas. Menos créditos no es un problema.
    public const int MaxCreditos = 15;
    private static int CreditosActivos(IEnumerable<Inscripcion> inscripciones) => inscripciones.Where(i => i.Activo).Sum(i => i.Creditos);
    private static InscripcionView VistaInscripcion(Inscripcion i) => new(i.Id, i.CursoId, i.Nivel, i.Paralelo, i.MateriaCodigo, i.MateriaNombre, i.Creditos, i.Activo);
    private static StudentView Vista(Student s, IEnumerable<Inscripcion>? inscripciones = null) => new(s.Id, s.Cedula, s.Apellidos, s.Nombres, s.Correo, s.CarreraCodigo, s.Activo, s.BajaEn,
        CreditosActivos(s.Inscripciones), MaxCreditos,
        (inscripciones ?? s.Inscripciones).OrderBy(i => i.Nivel).ThenBy(i => i.Paralelo).ThenBy(i => i.MateriaNombre).Select(VistaInscripcion).ToList());

    // estado: "activos" (por defecto), "bajas" o "todos". Nivel, paralelo y materia (código) filtran por las inscripciones del estudiante,
    // y la respuesta solo trae las inscripciones que cumplen el filtro.
    public async Task<List<StudentView>> List(string? carrera, int? nivel, string? paralelo, string? materia, string? estado, CancellationToken ct)
    {
        var query = db.Students.AsNoTracking().Include(x => x.Inscripciones).AsQueryable();
        switch ((estado ?? "activos").Trim().ToLowerInvariant())
        {
            case "activos":
                query = query.Where(x => x.Activo);
                break;
            case "bajas":
                query = query.Where(x => !x.Activo);
                break;
            case "todos":
                break;
            default:
                throw new ApiException(400, "El estado debe ser activos, bajas o todos.");
        }
        if (!string.IsNullOrWhiteSpace(carrera))
        {
            var code = carrera.Trim().ToUpperInvariant();
            query = query.Where(x => x.CarreraCodigo == code);
        }
        var letra = string.IsNullOrWhiteSpace(paralelo) ? null : paralelo.Trim().ToUpperInvariant();
        var codigoMateria = string.IsNullOrWhiteSpace(materia) ? null : materia.Trim().ToUpperInvariant();
        var filtraInscripciones = nivel.HasValue || letra is not null || codigoMateria is not null;
        if (filtraInscripciones)
            query = query.Where(x => x.Inscripciones.Any(i => (!nivel.HasValue || i.Nivel == nivel) && (letra == null || i.Paralelo == letra) && (codigoMateria == null || i.MateriaCodigo == codigoMateria)));
        var estudiantes = await query.OrderBy(x => x.CarreraCodigo).ThenBy(x => x.Apellidos).ThenBy(x => x.Nombres).ToListAsync(ct);
        return estudiantes.Select(s => Vista(s, filtraInscripciones
            ? s.Inscripciones.Where(i => (!nivel.HasValue || i.Nivel == nivel) && (letra == null || i.Paralelo == letra) && (codigoMateria == null || i.MateriaCodigo == codigoMateria))
            : null)).ToList();
    }
    public async Task<StudentView> Get(int id, CancellationToken ct) =>
        Vista(await db.Students.AsNoTracking().Include(x => x.Inscripciones).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new ApiException(404, "Estudiante no encontrado."));

    // Carreras y cursos del período activo, consultados al servicio de Materias con el JWT del administrador.
    private async Task<(List<CarreraReference> Carreras, List<CursoReference> Cursos)> Catalogo(CancellationToken ct) =>
        (await dependencies.Get<List<CarreraReference>>("Materias", "/api/carreras", ct), await dependencies.Get<List<CursoReference>>("Materias", "/api/cursos", ct));

    private static Inscripcion Inscribir(CursoReference c) => new() { CursoId = c.Id, Nivel = c.Nivel, Paralelo = c.Paralelo, MateriaCodigo = c.MateriaCodigo, MateriaNombre = c.MateriaNombre, Creditos = c.Creditos, MallaVersion = c.MallaVersion };

    // Registro individual (una fila del CSV). Sin materia, inscribe en todas las materias del nivel y paralelo.
    public async Task<StudentView> Crear(StudentRequest input, CancellationToken ct)
    {
        var (carreras, catalogo) = await Catalogo(ct);
        var carrera = carreras.FirstOrDefault(c => string.Equals(c.Codigo, input.Carrera, StringComparison.OrdinalIgnoreCase)) ?? throw new ApiException(400, $"La carrera \"{input.Carrera}\" no existe o está dada de baja.");
        var (cursos, error) = Precarga.ResolverCursos(carrera, input.Nivel, input.Paralelo.ToUpperInvariant(), input.Materia, catalogo);
        if (error is not null)
            throw new ApiException(400, char.ToUpperInvariant(error[0]) + error[1..] + ".");
        await ValidarCedula(input.Cedula, null, ct);
        if (cursos.Sum(c => c.Creditos) > MaxCreditos)
            throw new ApiException(400, $"Las materias suman {cursos.Sum(c => c.Creditos)} créditos y el máximo por período es {MaxCreditos}. Indica una materia.");
        var student = new Student { Cedula = input.Cedula, Apellidos = input.Apellidos.Trim(), Nombres = input.Nombres.Trim(), Correo = input.Correo.Trim().ToLowerInvariant(), CarreraCodigo = carrera.Codigo };
        foreach (var c in cursos)
            student.Inscripciones.Add(Inscribir(c));
        db.Students.Add(student);
        await db.SaveChangesAsync(ct);
        return Vista(student);
    }
    private async Task ValidarCedula(string cedula, int? id, CancellationToken ct)
    {
        var repetido = await db.Students.Where(x => x.Cedula == cedula && x.Id != id).Select(x => (bool?)x.Activo).FirstOrDefaultAsync(ct);
        if (repetido is not null)
            throw new ApiException(409, repetido == false ? "Esa cédula pertenece a un estudiante dado de baja. Reactívalo en lugar de registrarlo de nuevo." : "La cédula ya está registrada.");
    }
    public async Task<StudentView> Editar(int id, StudentEdit input, CancellationToken ct)
    {
        var student = await db.Students.Include(x => x.Inscripciones).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new ApiException(404, "Estudiante no encontrado.");
        await ValidarCedula(input.Cedula, id, ct);
        student.Cedula = input.Cedula;
        student.Apellidos = input.Apellidos.Trim();
        student.Nombres = input.Nombres.Trim();
        student.Correo = input.Correo.Trim().ToLowerInvariant();
        await db.SaveChangesAsync(ct);
        return Vista(student);
    }

    // Todo o nada: si alguna fila tiene errores no se guarda ninguna. Las cédulas que ya existen se actualizan y se les agregan los cursos nuevos;
    // no se quita ninguna inscripción anterior (para eso está el retiro por materia).
    public async Task<PrecargaResultado> Cargar(byte[] archivo, Precarga.Destino? destino, CancellationToken ct)
    {
        var (carreras, catalogo) = await Catalogo(ct);
        // El destino se valida una sola vez, antes de las filas: así un paralelo o una materia inexistentes no se repiten en cada fila.
        if (destino is { Carrera: { Length: > 0 } codigoCarrera, Nivel: { } nivelDestino, Paralelo: { Length: > 0 } letra, Materia: { Length: > 0 } materiaDestino })
        {
            var carreraDestino = carreras.FirstOrDefault(c => string.Equals(c.Codigo, codigoCarrera, StringComparison.OrdinalIgnoreCase)) ?? throw new ApiException(400, $"La carrera \"{codigoCarrera}\" no existe o está dada de baja.");
            var (_, errorDestino) = Precarga.ResolverCursos(carreraDestino, nivelDestino, letra.ToUpperInvariant(), materiaDestino, catalogo);
            if (errorDestino is not null)
                throw new ApiException(400, char.ToUpperInvariant(errorDestino[0]) + errorDestino[1..] + ".");
        }
        var (personas, errores) = Precarga.Validar(Precarga.Decodificar(archivo), carreras, catalogo, destino);
        if (errores.Count > 0)
        {
            var mostrados = string.Join("\n", errores.Take(10));
            throw new ApiException(400, $"El archivo tiene {errores.Count} fila(s) con errores y no se guardó nada:\n{mostrados}{(errores.Count > 10 ? $"\n... y {errores.Count - 10} más." : "")}");
        }
        if (personas.Count == 0)
            throw new ApiException(400, "El archivo no tiene estudiantes.");
        var cedulas = personas.Select(f => f.Cedula).ToList();
        var existentes = (await db.Students.Include(s => s.Inscripciones).Where(s => cedulas.Contains(s.Cedula)).ToListAsync(ct)).ToDictionary(s => s.Cedula);
        // Reglas frente a lo ya guardado: una sola carrera por estudiante y una materia en un solo paralelo
        var conflictos = new List<string>();
        foreach (var f in personas)
        {
            var guardado = existentes.GetValueOrDefault(f.Cedula);
            if (guardado is not null && guardado.CarreraCodigo != f.Carrera)
                conflictos.Add($"Fila {f.Linea} ({f.Cedula}): el estudiante ya está registrado en {guardado.CarreraCodigo} y un estudiante no puede estar en dos carreras.");
            var activas = guardado?.Inscripciones.Where(i => i.Activo).ToList() ?? [];
            var choqueParalelo = false;
            // Un estudiante continúa con la malla en la que empezó, aunque la carrera tenga una malla nueva
            var mallaActual = guardado?.Inscripciones.Select(i => (int?)i.MallaVersion).FirstOrDefault();
            foreach (var c in f.Cursos.Where(c => mallaActual is not null && c.MallaVersion != mallaActual).Take(1))
            {
                choqueParalelo = true;
                conflictos.Add($"Fila {f.Linea} ({f.Cedula}): el estudiante cursa con la malla {mallaActual} y {c.MateriaNombre} pertenece a la malla {c.MallaVersion}; un estudiante continúa con la malla en la que empezó.");
            }
            foreach (var c in f.Cursos)
                if (activas.FirstOrDefault(i => i.MateriaCodigo == c.MateriaCodigo && i.CursoId != c.Id) is { } otra)
                {
                    choqueParalelo = true;
                    conflictos.Add($"Fila {f.Linea} ({f.Cedula}): {c.MateriaNombre} ya la cursa en el paralelo \"{otra.Paralelo}\". Retírala de ese paralelo antes de pasarla al \"{c.Paralelo}\".");
                }
            // Créditos: las materias activas que ya tiene más las nuevas, sin contar dos veces las que ya cursa.
            // Si la materia ya está en otro paralelo ya se informó de eso, y contarla otra vez sería engañoso.
            var total = activas.Sum(i => i.Creditos) + f.Cursos.Where(c => !activas.Any(i => i.CursoId == c.Id)).Sum(c => c.Creditos);
            if (total > MaxCreditos && !choqueParalelo)
            {
                var nuevas = f.Cursos.Where(c => !activas.Any(i => i.CursoId == c.Id)).ToList();
                conflictos.Add($"Fila {f.Linea} ({f.Cedula}): no se puede inscribir en {string.Join(", ", nuevas.Select(c => c.MateriaNombre))} porque ya tiene {activas.Sum(i => i.Creditos)} créditos y sumaría {total}; el máximo por período es {MaxCreditos}.");
            }
        }
        if (conflictos.Count > 0)
            throw new ApiException(409, $"El archivo tiene {conflictos.Count} conflicto(s) con lo ya registrado y no se guardó nada:\n{string.Join("\n", conflictos.Take(10))}{(conflictos.Count > 10 ? $"\n... y {conflictos.Count - 10} más." : "")}");
        int creados = 0, actualizados = 0, reactivados = 0, inscripciones = 0;
        foreach (var f in personas)
        {
            if (existentes.TryGetValue(f.Cedula, out var student))
            {
                actualizados++;
                if (!student.Activo)
                {
                    (student.Activo, student.BajaEn) = (true, null);
                    reactivados++;
                }
            }
            else
            {
                student = new Student { Cedula = f.Cedula };
                db.Students.Add(student);
                creados++;
            }
            student.Apellidos = f.Apellidos;
            student.Nombres = f.Nombres;
            student.Correo = f.Correo;
            student.CarreraCodigo = f.Carrera;
            foreach (var c in f.Cursos)
            {
                var actual = student.Inscripciones.FirstOrDefault(i => i.CursoId == c.Id);
                if (actual is null)
                {
                    student.Inscripciones.Add(Inscribir(c));
                    inscripciones++;
                }
                else if (!actual.Activo)
                {
                    (actual.Activo, actual.BajaEn) = (true, null);
                    inscripciones++;
                }
            }
        }
        await db.SaveChangesAsync(ct);
        return new PrecargaResultado(creados, actualizados, personas.Count, reactivados, inscripciones);
    }

    // Baja y reactivación son idempotentes: repetirlas no cambia el resultado.
    public async Task<StudentView> CambiarEstado(int id, bool activo, CancellationToken ct)
    {
        var student = await db.Students.Include(x => x.Inscripciones).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new ApiException(404, "Estudiante no encontrado.");
        if (student.Activo != activo)
        {
            student.Activo = activo;
            student.BajaEn = activo ? null : DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        return Vista(student);
    }
    // Retirar de una materia: el estudiante deja de aparecer en ese curso, pero sigue activo en las demás y conserva sus notas.
    public async Task<StudentView> CambiarEstadoInscripcion(int inscripcionId, bool activo, CancellationToken ct)
    {
        var inscripcion = await db.Inscripciones.Include(i => i.Student).ThenInclude(s => s.Inscripciones).SingleOrDefaultAsync(i => i.Id == inscripcionId, ct) ?? throw new ApiException(404, "Inscripción no encontrada.");
        if (inscripcion.Activo != activo)
        {
            if (activo && inscripcion.Student.Inscripciones.FirstOrDefault(i => i.Activo && i.MateriaCodigo == inscripcion.MateriaCodigo && i.Id != inscripcion.Id) is { } otra)
                throw new ApiException(409, $"{inscripcion.MateriaNombre} ya la cursa en el paralelo \"{otra.Paralelo}\". Retírala de ese paralelo antes de reincorporarla al \"{inscripcion.Paralelo}\".");
            if (activo && CreditosActivos(inscripcion.Student.Inscripciones) + inscripcion.Creditos > MaxCreditos)
                throw new ApiException(409, $"Con {inscripcion.MateriaNombre} el estudiante sumaría {CreditosActivos(inscripcion.Student.Inscripciones) + inscripcion.Creditos} créditos y el máximo por período es {MaxCreditos}. Retira otra materia antes.");
            inscripcion.Activo = activo;
            inscripcion.BajaEn = activo ? null : DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        return Vista(inscripcion.Student);
    }

    public async Task<List<ListaEstudiante>> Lista(int cursoId, CancellationToken ct)
    {
        // Se reenvía el JWT del usuario: Materias responde 404 si el curso no es de ese docente.
        await dependencies.Get<CursoReference>("Materias", $"/api/cursos/{cursoId}", ct);
        return await db.Inscripciones.AsNoTracking().Where(i => i.CursoId == cursoId && i.Activo && i.Student.Activo)
            .OrderBy(i => i.Student.Apellidos).ThenBy(i => i.Student.Nombres).Select(i => new ListaEstudiante(i.StudentId, i.Student.Apellidos, i.Student.Nombres, i.Student.Correo, i.Student.MoodleUsuarioId, i.Student.MoodleCodigo)).ToListAsync(ct);
    }
}

