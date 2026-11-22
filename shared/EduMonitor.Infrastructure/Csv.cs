using System.Globalization;
using System.Text;
namespace EduMonitor.Infrastructure;

// Lectura mínima de CSV compartida por las cargas masivas (estudiantes y mallas curriculares).
public static class Csv
{
    // Quita tildes y mayúsculas para comparar encabezados y nombres.
    public static string Clave(string texto)
    {
        var d = texto.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        return new string(d.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).Replace("  ", " ");
    }

    // Excel suele guardar el CSV en UTF-8 o en ANSI. Si no es UTF-8 válido se lee como Latin-1.
    public static string Decodificar(byte[] bytes)
    {
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes).TrimStart((char)0xFEFF);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }

    // Lector CSV: separador coma o punto y coma, campos entre comillas y saltos de línea Windows o Unix.
    public static List<List<string>> Leer(string texto)
    {
        var primera = texto.Split('\n', 2)[0];
        var separador = primera.Count(c => c == ';') > primera.Count(c => c == ',') ? ';' : ',';
        var filas = new List<List<string>>();
        var fila = new List<string>();
        var campo = new StringBuilder();
        var entreComillas = false;
        for (var i = 0; i < texto.Length; i++)
        {
            var c = texto[i];
            if (entreComillas)
            {
                if (c == '"' && i + 1 < texto.Length && texto[i + 1] == '"')
                {
                    campo.Append('"');
                    i++;
                }
                else if (c == '"')
                    entreComillas = false;
                else
                    campo.Append(c);
            }
            else if (c == '"')
                entreComillas = true;
            else if (c == separador)
            {
                fila.Add(campo.ToString());
                campo.Clear();
            }
            else if (c == '\n' || c == '\r')
            {
                if (c == '\r' && i + 1 < texto.Length && texto[i + 1] == '\n')
                    i++;
                fila.Add(campo.ToString());
                campo.Clear();
                filas.Add(fila);
                fila = [];
            }
            else
                campo.Append(c);
        }
        if (campo.Length > 0 || fila.Count > 0)
        {
            fila.Add(campo.ToString());
            filas.Add(fila);
        }
        return filas;
    }
}
