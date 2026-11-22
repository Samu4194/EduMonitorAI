namespace EduMonitor.IntegrationTests;

// Cédulas ecuatorianas válidas (provincia 24, tercer dígito 5) con el prefijo reservado 2459. Pasan la validación del sistema y no se parecen a datos
// de demostración: la limpieza de las pruebas borra solo lo que empieza con ese prefijo.
public static class Cedulas
{
    public const string Prefijo = "2459";
    private static readonly object Candado = new();
    private static readonly HashSet<string> Usadas = [];

    public static string Generar()
    {
        lock (Candado)
            while (true)
            {
                var nueve = Prefijo + Random.Shared.Next(0, 100000).ToString("D5");
                var cedula = nueve + Digito(nueve);
                if (Usadas.Add(cedula))
                    return cedula;
            }
    }
    public static int Digito(string nueve)
    {
        var suma = 0;
        for (var i = 0; i < 9; i++)
        {
            var p = (nueve[i] - '0') * (i % 2 == 0 ? 2 : 1);
            suma += p > 9 ? p - 9 : p;
        }
        return (10 - suma % 10) % 10;
    }
    // Misma longitud y formato, pero con el dígito verificador equivocado.
    public static string ConDigitoErroneo(string valida) => valida[..9] + (valida[9] - '0' + 1) % 10;
}
