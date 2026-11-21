using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text;
using EduMonitor.Estudiantes.DTOs;
using EduMonitor.Infrastructure;
namespace EduMonitor.Estudiantes.Services;

// Lectura y validación del CSV de precarga: cedula, apellidos, nombres, correo, carrera, nivel, paralelo y, opcional, materia.
// Sin materia, la fila inscribe al estudiante en todas las materias de ese nivel y paralelo; con materia, solo en esa.
public static class Precarga
{
    public const int MaxFilas = 5000;
    private static readonly string[] Columnas = ["cedula", "apellidos", "nombres", "correo"];
    private static readonly Dictionary<string, int> Romanos = new() { ["I"] = 1, ["II"] = 2, ["III"] = 3, ["IV"] = 4, ["V"] = 5, ["VI"] = 6, ["VII"] = 7, ["VIII"] = 8, ["IX"] = 9 };

    // Destino elegido al cargar (carrera por código, nivel, paralelo y materia por código). Completa lo que la fila no trae y debe coincidir con lo que sí trae.
    public sealed record Destino(string? Carrera, int? Nivel, string? Paralelo, string? Materia);

    // Una persona con los cursos en los que quedará inscrita (puede venir en varias filas del archivo).
    public sealed record Persona(int Linea, string Cedula, string Apellidos, string Nombres, string Correo, string Carrera, List<CursoReference> Cursos);

    // Cursos de un nivel y paralelo, o solo el de la materia indicada (por código o por nombre). Devuelve el motivo si no se puede.
    public static (List<CursoReference> Cursos, string? Error) ResolverCursos(CarreraReference carrera, int nivel, string paralelo, string? materia, List<CursoReference> catalogo)
    {
        var delParalelo = catalogo.Where(c => c.Carrera == carrera.Codigo && c.Nivel == nivel && c.Paralelo == paralelo).ToList();
        if (delParalelo.Count == 0)
            return ([], $"no existe el paralelo \"{paralelo}\" del nivel {nivel} de {carrera.Nombre} en el período activo");
        if (string.IsNullOrWhiteSpace(materia))
            return (delParalelo, null);
        var texto = materia.Trim();
        var curso = delParalelo.FirstOrDefault(c => string.Equals(c.MateriaCodigo, texto, StringComparison.OrdinalIgnoreCase) || Clave(c.MateriaNombre) == Clave(texto));
        return curso is null ? ([], $"la materia \"{texto}\" no existe en el nivel {nivel} de {carrera.Nombre}") : ([curso], null);
    }

    // La lectura de CSV es común con la carga de mallas: ver EduMonitor.Infrastructure.Csv.
    public static string Clave(string texto) => Csv.Clave(texto);
    public static string Decodificar(byte[] bytes) => Csv.Decodificar(bytes);
    public static List<List<string>> LeerCsv(string texto) => Csv.Leer(texto);

    // Devuelve las personas válidas (una por cédula, con todos sus cursos) y los errores con el número de línea del archivo.
    public static (List<Persona> Personas, List<string> Errores) Validar(string texto, List<CarreraReference> carreras, List<CursoReference> catalogo, Destino? destino = null)
    {
        var errores = new List<string>();
        var lineas = LeerCsv(texto).Where(f => f.Any(c => !string.IsNullOrWhiteSpace(c))).ToList();
        if (lineas.Count == 0)
            throw new ApiException(400, "El archivo está vacío.");
        var cabecera = lineas[0].Select(c => Clave(c).Replace("email", "correo")).ToList();
        var faltan = Columnas.Where(c => !cabecera.Contains(c)).ToList();
        if (faltan.Count > 0)
            throw new ApiException(400, $"Faltan columnas en el archivo: {string.Join(", ", faltan)}. Usa: {string.Join(", ", Columnas)} y, opcional, carrera, nivel, paralelo y materia (o elige ese destino al cargar).");
        if (lineas.Count - 1 > MaxFilas)
            throw new ApiException(400, $"El archivo tiene más de {MaxFilas} filas. Divídelo en partes.");
        var personas = new Dictionary<string, Persona>();
        // materia -> fila y paralelo, por estudiante: una misma materia no puede repetirse ni cursarse en dos paralelos
        var cursosVistos = new Dictionary<(string Cedula, string Materia), (int Linea, string Paralelo)>();
        for (var i = 1; i < lineas.Count; i++)
        {
            var linea = i + 1;
            string Valor(string columna)
            {
                var pos = cabecera.IndexOf(columna);
                return pos >= 0 && pos < lineas[i].Count ? lineas[i][pos].Trim() : "";
            }
            // Se respeta lo escrito: una cédula con letras, espacios o guiones es un error, no se "limpia".
            var cedula = Valor("cedula");
            if (cedula.Length == 9 && cedula.All(char.IsAsciiDigit))
                cedula = "0" + cedula; // Excel elimina el cero inicial de las cédulas; después se valida igual
            var apellidos = Valor("apellidos");
            var nombres = Valor("nombres");
            var correo = Valor("correo").ToLowerInvariant();
            var problemas = new List<string>();
            var cedulaValida = CedulaEcuatoriana.Error(cedula) is null;
            if (CedulaEcuatoriana.Error(cedula) is { } errorCedula)
                problemas.Add(errorCedula.TrimEnd('.').Replace("La cédula", "la cédula"));
            if (apellidos.Length is < 2 or > 100)
                problemas.Add("apellidos de 2 a 100 caracteres");
            if (nombres.Length is < 2 or > 100)
                problemas.Add("nombres de 2 a 100 caracteres");
            if (correo.Length > 254 || !new EmailAddressAttribute().IsValid(correo))
                problemas.Add("correo no válido");
            // Carrera, nivel, paralelo y materia pueden venir en la fila o del destino elegido al cargar. Si vienen de los dos lados deben coincidir.
            var textoCarrera = Valor("carrera");
            var carrera = textoCarrera.Length == 0 ? null : carreras.FirstOrDefault(c => string.Equals(c.Codigo, textoCarrera, StringComparison.OrdinalIgnoreCase) || Clave(c.Nombre) == Clave(textoCarrera));
            if (textoCarrera.Length == 0 && destino?.Carrera is { Length: > 0 } carreraDestino)
                carrera = carreras.FirstOrDefault(c => string.Equals(c.Codigo, carreraDestino, StringComparison.OrdinalIgnoreCase));
            else if (carrera is not null && destino?.Carrera is { Length: > 0 } otraCarrera && !string.Equals(carrera.Codigo, otraCarrera, StringComparison.OrdinalIgnoreCase))
                problemas.Add($"la fila es de {carrera.Codigo} pero estás cargando en {otraCarrera}");
            if (carrera is null)
                problemas.Add(textoCarrera.Length == 0 ? "falta la carrera (elige el destino al cargar o agrega la columna carrera)" : $"la carrera \"{textoCarrera}\" no existe");
            var textoNivel = Valor("nivel");
            var nivel = int.TryParse(textoNivel, out var n) ? n : Romanos.GetValueOrDefault(textoNivel.ToUpperInvariant());
            if (textoNivel.Length == 0 && destino?.Nivel is { } nivelDestino)
                nivel = nivelDestino;
            else if (textoNivel.Length > 0 && destino?.Nivel is { } otroNivel && nivel != otroNivel)
                problemas.Add($"la fila es del nivel {textoNivel} pero estás cargando en el nivel {otroNivel}");
            if (nivel is < 1 or > 9)
                problemas.Add(textoNivel.Length == 0 ? "falta el nivel (elige el destino al cargar o agrega la columna nivel)" : $"el nivel \"{textoNivel}\" no es válido (1 a 9)");
            var paralelo = Valor("paralelo").ToUpperInvariant();
            if (paralelo.Length == 0 && destino?.Paralelo is { Length: > 0 } paraleloDestino)
                paralelo = paraleloDestino.ToUpperInvariant();
            else if (paralelo.Length > 0 && destino?.Paralelo is { Length: > 0 } otroParalelo && paralelo != otroParalelo.ToUpperInvariant())
                problemas.Add($"la fila es del paralelo {paralelo} pero estás cargando en el paralelo {otroParalelo.ToUpperInvariant()}");
            if (paralelo.Length == 0)
                problemas.Add("falta el paralelo (elige el destino al cargar o agrega la columna paralelo)");
            var textoMateria = Valor("materia");
            var cursos = new List<CursoReference>();
            if (carrera is not null && nivel is >= 1 and <= 9 && paralelo.Length > 0)
            {
                var (resueltos, error) = ResolverCursos(carrera, nivel, paralelo, textoMateria.Length == 0 ? destino?.Materia : textoMateria, catalogo);
                if (error is not null)
                    problemas.Add(error);
                else if (textoMateria.Length > 0 && destino?.Materia is { Length: > 0 } materiaDestino && !string.Equals(resueltos[0].MateriaCodigo, materiaDestino, StringComparison.OrdinalIgnoreCase))
                    problemas.Add($"la fila es de la materia \"{textoMateria}\" pero estás cargando {materiaDestino}");
                cursos = resueltos;
            }
            // Datos del estudiante coherentes entre todas sus filas, y ningún curso repetido
            if (cedulaValida && personas.TryGetValue(cedula, out var previa))
            {
                if (previa.Apellidos != apellidos || previa.Nombres != nombres || previa.Correo != correo)
                    problemas.Add($"los datos del estudiante no coinciden con los de la fila {previa.Linea} (misma cédula)");
                if (carrera is not null && previa.Carrera != carrera.Codigo)
                    problemas.Add($"un estudiante no puede estar en dos carreras: en la fila {previa.Linea} es de {previa.Carrera}");
                // Una materia se cursa en un solo paralelo
                foreach (var c in cursos.Where(c => cursosVistos.ContainsKey((cedula, c.MateriaCodigo))).Take(3))
                {
                    var (lineaPrevia, paraleloPrevio) = cursosVistos[(cedula, c.MateriaCodigo)];
                    problemas.Add(paraleloPrevio == c.Paralelo
                        ? $"{c.MateriaNombre} ya aparece para esa cédula en la fila {lineaPrevia}"
                        : $"{c.MateriaNombre} ya está en el paralelo \"{paraleloPrevio}\" (fila {lineaPrevia}): una materia se cursa en un solo paralelo");
                }
            }
            if (problemas.Count > 0)
            {
                errores.Add($"Fila {linea}: {string.Join("; ", problemas)}.");
                continue;
            }
            foreach (var c in cursos)
                cursosVistos[(cedula, c.MateriaCodigo)] = (linea, c.Paralelo);
            if (personas.TryGetValue(cedula, out var existente))
                existente.Cursos.AddRange(cursos);
            else
                personas[cedula] = new Persona(linea, cedula, apellidos, nombres, correo, carrera!.Codigo, [.. cursos]);
        }
        return (personas.Values.ToList(), errores);
    }
}
