using System.Text.RegularExpressions;
using EduMonitor.Infrastructure;
using EduMonitor.Materias.DTOs;
using EduMonitor.Materias.Models;
using Microsoft.EntityFrameworkCore;
namespace EduMonitor.Materias.Services;

// Alta de carreras y carga de mallas curriculares por CSV.
public sealed partial class MateriasService
{
    private static readonly HashSet<string> Conectores = ["de", "del", "la", "las", "el", "los", "y", "e", "en", "para", "a", "al", "con"];
    private static readonly string[] Unidades = ["BASICA", "PROFESIONAL", "TITULACION"];
    private static readonly string[] Tipos = ["REGULAR", "SERVICIO_COMUNITARIO", "PRACTICAS_LABORALES", "TITULACION"];
    private static readonly Dictionary<string, int> Romanos = new() { ["I"] = 1, ["II"] = 2, ["III"] = 3, ["IV"] = 4, ["V"] = 5, ["VI"] = 6, ["VII"] = 7, ["VIII"] = 8, ["IX"] = 9, ["X"] = 10, ["XI"] = 11, ["XII"] = 12 };
    public const int HorasPorCredito = 48;
    public const int MaxFilasMalla = 600;

    // Iniciales del nombre sin conectores: "Mecánica Automotriz" -> MAU. Es solo la base: el código final se hace único.
    public static string CodigoBase(string nombre)
    {
        var palabras = Regex.Matches(Csv.Clave(nombre), "[a-z]+").Select(m => m.Value).Where(p => !Conectores.Contains(p)).ToList();
        if (palabras.Count == 0)
            return "CAR";
        var letras = palabras.Count >= 3 ? string.Concat(palabras.Take(3).Select(p => p[0]))
            : palabras.Count == 2 ? $"{palabras[0][0]}{palabras[1][0]}{(palabras[0].Length > 1 ? palabras[0][1] : palabras[1][^1])}"
            : palabras[0][..Math.Min(3, palabras[0].Length)];
        return letras.ToUpperInvariant();
    }

    // El código y el prefijo UTA los asigna el sistema: son únicos entre todas las carreras y, como las carreras no se eliminan, no se vuelven a usar.
    // El prefijo es el siguiente número de 4 dígitos después del más alto que ya existe (0809 -> 0810).
    public async Task<CodigoSugerido> SugerirCodigo(string nombre, CancellationToken ct)
    {
        var prefijos = await db.Carreras.AsNoTracking().Select(c => c.PrefijoUta).ToListAsync(ct);
        var siguiente = prefijos.Select(p => int.TryParse(p, out var n) ? n : 0).DefaultIfEmpty(0).Max() + 1;
        if (siguiente > 9999)
            throw new ApiException(409, "No quedan prefijos UTA disponibles.");
        var baseCodigo = CodigoBase(nombre);
        var usados = (await db.Carreras.AsNoTracking().Where(c => c.Codigo.StartsWith(baseCodigo)).Select(c => c.Codigo).ToListAsync(ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var codigo = baseCodigo;
        for (var n = 2; usados.Contains(codigo); n++)
            codigo = $"{baseCodigo}{n}";
        return new CodigoSugerido(codigo, siguiente.ToString("D4"));
    }

    private static void ExigirActiva(Carrera carrera)
    {
        if (!carrera.Activa)
            throw new ApiException(409, $"{carrera.Nombre} está dada de baja: reactívala para continuar.");
    }

    // Baja y reactivación son idempotentes. No se da de baja una carrera con paralelos en un período que no haya finalizado:
    // primero se finaliza el período, para no dejar cursos, docentes y estudiantes en el aire.
    public async Task<CarreraView> CambiarEstadoCarrera(string codigo, bool activa, CancellationToken ct)
    {
        var code = Normalizar(codigo);
        var carrera = await db.Carreras.SingleOrDefaultAsync(c => c.Codigo == code, ct) ?? throw new ApiException(404, "Carrera no encontrada.");
        if (carrera.Activa != activa)
        {
            if (!activa)
            {
                var periodo = await db.Periodos.AsNoTracking().FirstOrDefaultAsync(p => p.Activo, ct);
                if (periodo is not null && periodo.Estado != Periodo.Finalizado && await db.Paralelos.AnyAsync(p => p.PeriodoId == periodo.Id && p.CarreraId == carrera.Id, ct))
                    throw new ApiException(409, $"{carrera.Nombre} tiene paralelos en el período {periodo.Nombre} ({(periodo.Estado == Periodo.EnCurso ? "en curso" : "en preparación")}). Finaliza el período antes de darla de baja.");
            }
            carrera.Activa = activa;
            carrera.BajaEn = activa ? null : DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
        return await VistaCarrera(carrera.Id, ct);
    }

    private async Task<CarreraView> VistaCarrera(int id, CancellationToken ct)
    {
        var c = await db.Carreras.AsNoTracking().Include(x => x.Mallas).SingleAsync(x => x.Id == id, ct);
        var vigente = c.Mallas.OrderByDescending(m => m.Version).FirstOrDefault();
        var materias = vigente is null ? 0 : await db.Materias.CountAsync(m => m.MallaId == vigente.Id, ct);
        return new CarreraView(c.Id, c.Codigo, c.Nombre, c.Modalidad, c.Niveles, c.HorasTotales, c.CreditosTotales, materias, c.PrefijoUta, vigente?.Version ?? 0, c.Mallas.Count, c.Activa, c.BajaEn);
    }

    private async Task ValidarDatosCarrera(CarreraRequest input, int? idActual, CancellationToken ct)
    {
        var nombre = input.Nombre.Trim();
        if (await db.Carreras.AnyAsync(c => c.Nombre == nombre && c.Id != idActual, ct))
            throw new ApiException(409, "Ya existe una carrera con ese nombre.");
    }

    public async Task<CarreraView> CrearCarrera(CarreraRequest input, CancellationToken ct)
    {
        await ValidarDatosCarrera(input, null, ct);
        var asignado = await SugerirCodigo(input.Nombre, ct);
        var carrera = new Carrera { Codigo = asignado.Codigo, PrefijoUta = asignado.PrefijoUta, Nombre = input.Nombre.Trim(), Modalidad = input.Modalidad.Trim(), Niveles = input.Niveles };
        db.Carreras.Add(carrera);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            throw new ApiException(409, "No se pudo asignar un código único a la carrera. Inténtalo de nuevo.");
        }
        return await VistaCarrera(carrera.Id, ct);
    }

    // El código y el prefijo UTA no cambian nunca. Los niveles solo pueden aumentar: reducirlos dejaría paralelos y materias fuera de la carrera.
    public async Task<CarreraView> EditarCarrera(string codigo, CarreraRequest input, CancellationToken ct)
    {
        var code = Normalizar(codigo);
        var carrera = await db.Carreras.SingleOrDefaultAsync(c => c.Codigo == code, ct) ?? throw new ApiException(404, "Carrera no encontrada.");
        ExigirActiva(carrera);
        await ValidarDatosCarrera(input, carrera.Id, ct);
        if (input.Niveles < carrera.Niveles)
            throw new ApiException(409, $"{carrera.Nombre} tiene {carrera.Niveles} niveles: no se pueden reducir.");
        carrera.Nombre = input.Nombre.Trim();
        carrera.Modalidad = input.Modalidad.Trim();
        carrera.Niveles = input.Niveles;
        await db.SaveChangesAsync(ct);
        return await VistaCarrera(carrera.Id, ct);
    }

    private sealed record FilaMalla(int Nivel, string Nombre, string CodigoUta, string Unidad, string Tipo, int? Contacto, int? Practico, int? Autonomas, int Horas, int Creditos, bool Derivado);

    private static string Valor(string texto) => string.Join('_', Csv.Clave(texto).Split([' ', '-'], StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();

    // Todo o nada. La malla se carga o actualiza solo con el período en preparación. Si la malla vigente todavía no se usa en ningún paralelo se
    // reemplaza; si ya se usa se crea una versión nueva: los estudiantes registrados siguen con su malla y los nuevos entran con la nueva.
    public async Task<MallaResultado> CargarMalla(string codigo, byte[] archivo, CancellationToken ct)
    {
        var activo = await db.Periodos.AsNoTracking().FirstOrDefaultAsync(p => p.Activo, ct);
        if (activo?.Estado == Periodo.EnCurso)
            throw new ApiException(409, $"El período {activo.Nombre} está en curso: la malla solo se puede cargar o actualizar mientras el período está en preparación.");
        var code = Normalizar(codigo);
        var carrera = await db.Carreras.Include(c => c.Mallas).SingleOrDefaultAsync(c => c.Codigo == code, ct) ?? throw new ApiException(404, "Carrera no encontrada.");
        ExigirActiva(carrera);
        var (filas, errores) = LeerMalla(Csv.Decodificar(archivo), carrera);
        if (errores.Count > 0)
            throw new ApiException(400, $"El archivo tiene {errores.Count} error(es) y no se guardó nada:\n{string.Join("\n", errores.Take(10))}{(errores.Count > 10 ? $"\n... y {errores.Count - 10} más." : "")}");

        await using var transaccion = await db.Database.BeginTransactionAsync(ct);
        var vigente = carrera.Mallas.OrderByDescending(m => m.Version).FirstOrDefault();
        bool nueva;
        Malla malla;
        if (vigente is not null && !await db.Paralelos.AnyAsync(p => p.MallaId == vigente.Id, ct))
        {
            // Sin paralelos todavía: nadie la usa, se reemplaza
            nueva = false;
            malla = vigente;
            db.Materias.RemoveRange(await db.Materias.Where(m => m.MallaId == vigente.Id).ToListAsync(ct));
            await db.SaveChangesAsync(ct);
        }
        else
        {
            nueva = true;
            malla = new Malla { Carrera = carrera, Version = (vigente?.Version ?? 0) + 1 };
            db.Mallas.Add(malla);
        }
        var prefijo = malla.Version == 1 ? carrera.Codigo : $"{carrera.Codigo}-V{malla.Version}";
        foreach (var grupo in filas.GroupBy(f => f.Nivel))
        {
            var orden = 0;
            foreach (var f in grupo)
            {
                orden++;
                db.Materias.Add(new Materia
                {
                    Carrera = carrera, Malla = malla, Codigo = $"{prefijo}-{f.Nivel}{orden:00}", CodigoUta = f.CodigoUta, Nombre = f.Nombre, Nivel = f.Nivel, OrdenNivel = orden,
                    Unidad = f.Unidad, Tipo = f.Tipo, HorasContactoDocente = f.Contacto, HorasPracticoExperimental = f.Practico, HorasAutonomas = f.Autonomas,
                    HorasTotales = f.Horas, Creditos = f.Creditos, CreditosDerivado = f.Derivado
                });
            }
        }
        carrera.HorasTotales = filas.Sum(f => f.Horas);
        carrera.CreditosTotales = filas.Sum(f => f.Creditos);
        await db.SaveChangesAsync(ct);
        await transaccion.CommitAsync(ct);
        return new MallaResultado(malla.Version, nueva, filas.Count, carrera.HorasTotales, carrera.CreditosTotales);
    }

    private static (List<FilaMalla> Filas, List<string> Errores) LeerMalla(string texto, Carrera carrera)
    {
        var errores = new List<string>();
        var lineas = Csv.Leer(texto).Where(f => f.Any(c => !string.IsNullOrWhiteSpace(c))).ToList();
        if (lineas.Count == 0)
            throw new ApiException(400, "El archivo está vacío.");
        var cabecera = lineas[0].Select(c => Valor(c).ToLowerInvariant()).ToList();
        var faltan = new[] { "nivel", "nombre", "unidad" }.Where(c => !cabecera.Contains(c)).ToList();
        if (faltan.Count > 0)
            throw new ApiException(400, $"Faltan columnas en el archivo: {string.Join(", ", faltan)}. Usa: nivel, nombre, unidad y, según la materia, tipo, codigo_uta, horas_contacto_docente, horas_practico_experimental, horas_autonomas, horas_totales y creditos.");
        if (lineas.Count - 1 > MaxFilasMalla)
            throw new ApiException(400, $"El archivo tiene más de {MaxFilasMalla} filas.");
        var filas = new List<FilaMalla>();
        var nombres = new HashSet<(int, string)>();
        var utas = new HashSet<string>();
        for (var i = 1; i < lineas.Count; i++)
        {
            string Celda(string columna)
            {
                var pos = cabecera.IndexOf(columna);
                return pos >= 0 && pos < lineas[i].Count ? lineas[i][pos].Trim() : "";
            }
            var problemas = new List<string>();
            int? Horas(string columna)
            {
                var valor = Celda(columna);
                if (valor.Length == 0)
                    return null;
                if (!int.TryParse(valor, out var h) || h is < 0 or > 1000)
                {
                    problemas.Add($"{columna} debe ser un número de horas entre 0 y 1000");
                    return null;
                }
                return h;
            }
            var textoNivel = Celda("nivel");
            var nivel = int.TryParse(textoNivel, out var n) ? n : Romanos.GetValueOrDefault(textoNivel.ToUpperInvariant());
            if (nivel < 1 || nivel > carrera.Niveles)
                problemas.Add($"el nivel \"{textoNivel}\" no es válido ({carrera.Nombre} tiene {carrera.Niveles} niveles)");
            var nombre = Celda("nombre");
            if (nombre.Length is < 3 or > 150)
                problemas.Add("nombre de 3 a 150 caracteres");
            var unidad = Valor(Celda("unidad"));
            if (!Unidades.Contains(unidad))
                problemas.Add($"unidad \"{Celda("unidad")}\" no válida (usa {string.Join(", ", Unidades)})");
            var tipo = Celda("tipo").Length == 0 ? "REGULAR" : Valor(Celda("tipo"));
            if (!Tipos.Contains(tipo))
                problemas.Add($"tipo \"{Celda("tipo")}\" no válido (usa {string.Join(", ", Tipos)})");
            var uta = Celda("codigo_uta");
            if (uta.Length > 0 && !Regex.IsMatch(uta, @"^\d{8}$"))
                problemas.Add("codigo_uta son 8 dígitos");
            else if (uta.Length > 0 && !uta.StartsWith(carrera.PrefijoUta, StringComparison.Ordinal))
                problemas.Add($"codigo_uta {uta} debe empezar con {carrera.PrefijoUta}, el prefijo UTA de {carrera.Nombre}");
            else if (uta.Length > 0 && !utas.Add(uta))
                problemas.Add($"codigo_uta {uta} está repetido en el archivo");
            int? contacto = Horas("horas_contacto_docente"), practico = Horas("horas_practico_experimental"), autonomas = Horas("horas_autonomas"), declaradas = Horas("horas_totales");
            int total = 0;
            if (tipo == "REGULAR")
            {
                if (contacto is null || practico is null || autonomas is null)
                    problemas.Add("una materia regular necesita horas_contacto_docente, horas_practico_experimental y horas_autonomas");
                else
                {
                    total = contacto.Value + practico.Value + autonomas.Value;
                    if (declaradas is not null && declaradas != total)
                        problemas.Add($"horas_totales ({declaradas}) no coincide con la suma de sus componentes ({total})");
                }
            }
            else
            {
                if (declaradas is null or 0)
                    problemas.Add("este tipo de materia necesita horas_totales");
                else
                    total = declaradas.Value;
                (contacto, practico, autonomas) = (null, null, null);
            }
            var derivado = Celda("creditos").Length == 0;
            var creditos = 0;
            if (!derivado && (!int.TryParse(Celda("creditos"), out creditos) || creditos is < 1 or > 20))
                problemas.Add("creditos debe ser un número entre 1 y 20");
            else if (derivado && total > 0)
            {
                creditos = (int)Math.Round(total / (double)HorasPorCredito, MidpointRounding.AwayFromZero);
                if (creditos < 1)
                    problemas.Add($"{total} horas no alcanzan 1 crédito ({HorasPorCredito} horas por crédito)");
            }
            if (nivel >= 1 && nombre.Length > 0 && !nombres.Add((nivel, Csv.Clave(nombre))))
                problemas.Add($"\"{nombre}\" está repetida en el nivel {nivel}");
            if (problemas.Count > 0)
            {
                errores.Add($"Fila {i + 1}: {string.Join("; ", problemas)}.");
                continue;
            }
            filas.Add(new FilaMalla(nivel, nombre, uta, unidad, tipo, contacto, practico, autonomas, total, creditos, derivado));
        }
        if (errores.Count == 0)
        {
            var sinMaterias = Enumerable.Range(1, carrera.Niveles).Where(nv => filas.All(f => f.Nivel != nv)).ToList();
            if (sinMaterias.Count > 0)
                errores.Add($"La malla debe tener materias en todos los niveles: faltan {string.Join(", ", sinMaterias)}.");
        }
        return (filas, errores);
    }
}
