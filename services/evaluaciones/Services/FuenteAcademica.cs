using EduMonitor.Evaluaciones.Data;
using EduMonitor.Evaluaciones.Models;
using Microsoft.EntityFrameworkCore;
namespace EduMonitor.Evaluaciones.Services;

// De dónde salen los datos académicos (unidades, temas, actividades y notas).
//   Moodle: Moodle es la fuente de verdad. EduMonitor solo los lee (sincronización) y nadie los edita a mano. Es lo normal.
//   Manual: el docente los registra y edita en EduMonitor. Queda como opción para desarrollo, pruebas y cursos sin Moodle.
// Se guarda en la base para poder cambiarlo sin reiniciar los servicios.
public sealed class FuenteAcademica(EvaluacionesDb db, IConfiguration config)
{
    public const string Moodle = "Moodle", Manual = "Manual";
    const string Clave = "FuenteAcademica";

    public async Task<string> Obtener(CancellationToken ct)
    {
        var valor = await db.Set<Ajuste>().AsNoTracking().Where(a => a.Clave == Clave).Select(a => a.Valor).SingleOrDefaultAsync(ct);
        valor ??= config["Academico:FuenteInicial"];
        return string.Equals(valor, Manual, StringComparison.OrdinalIgnoreCase) ? Manual : Moodle;
    }

    public async Task Cambiar(string fuente, CancellationToken ct)
    {
        var ajuste = await db.Set<Ajuste>().SingleOrDefaultAsync(a => a.Clave == Clave, ct);
        if (ajuste is null)
            db.Add(ajuste = new Ajuste { Clave = Clave });
        ajuste.Valor = fuente;
        await db.SaveChangesAsync(ct);
    }
}
