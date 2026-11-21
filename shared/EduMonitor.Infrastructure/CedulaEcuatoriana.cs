using System.ComponentModel.DataAnnotations;
namespace EduMonitor.Infrastructure;

// Validación de la cédula de identidad ecuatoriana (persona natural): 10 dígitos, provincia 01-24 o 30,
// tercer dígito de 0 a 5 y dígito verificador por el algoritmo módulo 10.
public static class CedulaEcuatoriana
{
    // Devuelve null si la cédula es válida, o el motivo en español.
    public static string? Error(string? cedula)
    {
        if (cedula is null || cedula.Length != 10 || !cedula.All(c => c is >= '0' and <= '9'))
            return "La cédula debe tener exactamente 10 dígitos numéricos.";
        var provincia = (cedula[0] - '0') * 10 + (cedula[1] - '0');
        if (provincia is not (>= 1 and <= 24) and not 30)
            return "La cédula no es válida: el código de provincia no existe.";
        if (cedula[2] - '0' > 5)
            return "La cédula no es válida: el tercer dígito no corresponde a una persona natural.";
        return DigitoVerificador(cedula[..9]) == cedula[9] - '0' ? null : "La cédula no es válida: el dígito verificador no coincide.";
    }
    public static bool EsValida(string? cedula) => Error(cedula) is null;

    // Módulo 10: coeficientes 2,1,2,1,2,1,2,1,2; si el producto pasa de 9 se le resta 9.
    public static int DigitoVerificador(string primerosNueve)
    {
        var suma = 0;
        for (var i = 0; i < 9; i++)
        {
            var producto = (primerosNueve[i] - '0') * (i % 2 == 0 ? 2 : 1);
            suma += producto > 9 ? producto - 9 : producto;
        }
        return (10 - suma % 10) % 10;
    }
}

// Se aplica después del formato (10 dígitos): si el formato falla, ese mensaje ya lo da RegularExpression y aquí no se repite.
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class CedulaEcuatorianaAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext context)
    {
        if (value is not string cedula || cedula.Length != 10 || !cedula.All(char.IsAsciiDigit))
            return ValidationResult.Success;
        var error = CedulaEcuatoriana.Error(cedula);
        return error is null ? ValidationResult.Success : new ValidationResult(error);
    }
}
