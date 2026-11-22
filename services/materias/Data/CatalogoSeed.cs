using System.Text.Json;
using EduMonitor.Materias.Models;
using Microsoft.EntityFrameworkCore;
namespace EduMonitor.Materias.Data;

// Carga el catálogo de las mallas curriculares (embebido en el servicio) cuando la base está vacía.
public static class CatalogoSeed
{
    private sealed record CarreraSeed(string Codigo, string Nombre, string PrefijoUta, string Modalidad, int Niveles, int HorasTotales, int CreditosTotales);
    private sealed record MateriaSeed(string Carrera, string Codigo, string CodigoUta, string Nombre, int Nivel, int OrdenNivel, string Unidad, string Tipo,
        int? HorasContactoDocente, int? HorasPracticoExperimental, int? HorasAutonomas, int HorasTotales, int Creditos, bool CreditosDerivado);

    private static readonly JsonSerializerOptions Opciones = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    public const string PeriodoInicial = "Agosto - Diciembre 2026";
    private static readonly string[] LetrasIniciales = ["A", "B"];

    private static List<T> Leer<T>(string archivo)
    {
        using var stream = typeof(CatalogoSeed).Assembly.GetManifestResourceStream($"EduMonitor.Materias.Data.Seed.{archivo}")
            ?? throw new InvalidOperationException($"Falta el recurso {archivo}.");
        return JsonSerializer.Deserialize<List<T>>(stream, Opciones)!;
    }

    public static async Task Aplicar(MateriasDb db, CancellationToken ct = default)
    {
        if (!await db.Carreras.AnyAsync(ct))
        {
            var carreras = Leer<CarreraSeed>("carreras.json").Select(x => new Carrera
            {
                Codigo = x.Codigo, Nombre = x.Nombre, PrefijoUta = x.PrefijoUta, Modalidad = x.Modalidad,
                Niveles = x.Niveles, HorasTotales = x.HorasTotales, CreditosTotales = x.CreditosTotales
            }).ToDictionary(x => x.Codigo);
            // El catálogo inicial es la versión 1 de la malla de cada carrera
            var mallas = carreras.Values.ToDictionary(c => c.Codigo, c => new Malla { Version = 1, Carrera = c });
            foreach (var malla in mallas.Values)
                malla.Carrera.Mallas.Add(malla);
            foreach (var m in Leer<MateriaSeed>("materias.json"))
                carreras[m.Carrera].Materias.Add(new Materia
                {
                    Malla = mallas[m.Carrera], Codigo = m.Codigo, CodigoUta = m.CodigoUta, Nombre = m.Nombre, Nivel = m.Nivel, OrdenNivel = m.OrdenNivel, Unidad = m.Unidad, Tipo = m.Tipo,
                    HorasContactoDocente = m.HorasContactoDocente, HorasPracticoExperimental = m.HorasPracticoExperimental, HorasAutonomas = m.HorasAutonomas,
                    HorasTotales = m.HorasTotales, Creditos = m.Creditos, CreditosDerivado = m.CreditosDerivado
                });
            db.Carreras.AddRange(carreras.Values);
            await db.SaveChangesAsync(ct);
        }
        if (!await db.Periodos.AnyAsync(ct))
        {
            db.Periodos.Add(new Periodo { Nombre = PeriodoInicial, Activo = true });
            await db.SaveChangesAsync(ct);
        }
        // Paralelos de demostración (A y B por nivel) con un curso por cada materia del nivel.
        if (!await db.Paralelos.AnyAsync(ct))
        {
            var periodo = await db.Periodos.FirstAsync(x => x.Activo, ct);
            foreach (var carrera in await db.Carreras.Include(x => x.Mallas).ThenInclude(x => x.Materias).ToListAsync(ct))
            {
                var vigente = carrera.Mallas.OrderByDescending(x => x.Version).First();
                for (var nivel = 1; nivel <= carrera.Niveles; nivel++)
                    foreach (var letra in LetrasIniciales)
                        AgregarParalelo(db, periodo, carrera, vigente, nivel, letra, vigente.Materias.Where(m => m.Nivel == nivel));
            }
            await db.SaveChangesAsync(ct);
        }
    }

    // Agrega el paralelo y un curso (sin docente) por cada materia de esa malla y nivel. El que llama guarda los cambios.
    public static Paralelo AgregarParalelo(MateriasDb db, Periodo periodo, Carrera carrera, Malla malla, int nivel, string letra, IEnumerable<Materia> materiasDelNivel)
    {
        var paralelo = new Paralelo { Periodo = periodo, Carrera = carrera, Malla = malla, Nivel = nivel, Letra = letra };
        db.Paralelos.Add(paralelo);
        foreach (var materia in materiasDelNivel)
            db.Cursos.Add(new Curso { Materia = materia, Paralelo = paralelo });
        return paralelo;
    }
}
