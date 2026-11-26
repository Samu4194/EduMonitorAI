using System.Data;
using EduMonitor.Evaluaciones.Data;
using EduMonitor.Evaluaciones.Services;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace EduMonitor.Evaluaciones.Controllers;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
internal sealed class EdicionAcademicaManualAttribute : ActionFilterAttribute
{
    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var http = context.HttpContext;
        if (HttpMethods.IsGet(http.Request.Method))
        {
            await next();
            return;
        }

        // El bloqueo de rango de SQL Server impide que se vincule el curso entre la comprobación y la escritura.
        var db = http.RequestServices.GetRequiredService<EvaluacionesDb>();
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, http.RequestAborted);
        http.RequestServices.GetRequiredService<AccesoCursos>().EdicionManual = true;
        var resultado = await next();
        if (resultado.Exception == null && !resultado.Canceled)
            await tx.CommitAsync(http.RequestAborted);
    }
}
