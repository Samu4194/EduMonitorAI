using System.Reflection;
using EduMonitor.Evaluaciones.Controllers;
using Microsoft.AspNetCore.Mvc.Routing;
using Xunit;

namespace EduMonitor.IntegrationTests;

public class MoodleEdicionManualTests
{
    [Theory]
    [InlineData(typeof(UnidadesController))]
    [InlineData(typeof(TemasController))]
    [InlineData(typeof(ContenidosController))]
    [InlineData(typeof(ActividadesController))]
    [InlineData(typeof(CalificacionesController))]
    public void CadaEscrituraAcademicaExigeComprobarElVinculo(Type controller)
    {
        foreach (var action in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            var ruta = action.GetCustomAttribute<HttpMethodAttribute>();
            if (ruta == null || ruta.HttpMethods.All(m => m == "GET"))
                continue;

            // Los archivos de apoyo se administran localmente, incluso en un curso vinculado.
            var archivoLocal = controller == typeof(TemasController) && ruta.Template!.Contains("archivos");
            Assert.Equal(!archivoLocal, Protegida(controller, action));
        }
    }

    [Fact]
    public void LaImportacionAutorizadaUsaSuPropiaRutaDeEscritura()
    {
        var ruta = typeof(MoodleController).GetMethod(nameof(MoodleController.Importar))!;
        Assert.False(Protegida(typeof(MoodleController), ruta));
        Assert.Equal("Teacher", ruta.GetCustomAttribute<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>()!.Roles);
    }

    private static bool Protegida(Type controller, MethodInfo action) =>
        controller.CustomAttributes.Concat(action.CustomAttributes)
            .Any(attr => attr.AttributeType.FullName == "EduMonitor.Evaluaciones.Controllers.EdicionAcademicaManualAttribute");
}
