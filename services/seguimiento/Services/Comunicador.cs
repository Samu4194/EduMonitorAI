using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using EduMonitor.Seguimiento.Models;
namespace EduMonitor.Seguimiento.Services;

// Sección "Smtp" de appsettings. Sin Host el correo no sale de la máquina: queda registrado como "Simulado".
public class OpcionesCorreo
{
    public string Host { get; set; } = "";
    public int Puerto { get; set; } = 587;
    public string? Usuario { get; set; }
    public string? Clave { get; set; }
    public string Remitente { get; set; } = "";
    public bool UsarSsl { get; set; } = true;
    public int TimeoutSegundos { get; set; } = 8;
    public bool Real => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(Remitente);
}

// HU-13. Comunica al estudiante la recomendación que el docente aprobó. El agente nunca escribe al estudiante por su cuenta.
public sealed class Comunicador(OpcionesCorreo correo, ILogger<Comunicador> logger)
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static (string Asunto, string Cuerpo) Componer(Recomendacion r, Analisis a, string docente)
    {
        var acciones = JsonSerializer.Deserialize<List<string>>(r.AccionesEstudianteJson, Json) ?? [];
        var cuerpo = new StringBuilder();
        cuerpo.AppendLine($"Estimado/a {r.EstudianteAnalisis.Nombres}:").AppendLine().AppendLine(r.MensajeEstudiante.Trim());
        if (RutaComoTexto(r.RutaJson) is { Length: > 0 } ruta)
            cuerpo.AppendLine().Append(ruta);
        else if (acciones.Count > 0)
        {
            cuerpo.AppendLine().AppendLine("Acciones sugeridas:");
            foreach (var x in acciones)
                cuerpo.AppendLine($"  - {x}");
        }
        cuerpo.AppendLine().AppendLine("Atentamente,").AppendLine(docente).AppendLine($"Docente de {a.Materia} · Paralelo {a.Paralelo} · {a.Periodo}");
        return ($"Seguimiento académico: {a.Materia}", cuerpo.ToString().TrimEnd());
    }

    // La ruta de mejora que nació del asistente, como texto para el correo (etapas, metas y acciones de estudio).
    public static string RutaComoTexto(string? rutaJson)
    {
        if (string.IsNullOrWhiteSpace(rutaJson))
            return "";
        try
        {
            using var doc = JsonDocument.Parse(rutaJson);
            if (!doc.RootElement.TryGetProperty("etapas", out var etapas) || etapas.ValueKind != JsonValueKind.Array || etapas.GetArrayLength() == 0)
                return "";
            var sb = new StringBuilder("Tu ruta de mejora:").AppendLine();
            foreach (var e in etapas.EnumerateArray())
            {
                var titulo = e.TryGetProperty("titulo", out var t) ? t.GetString() : "";
                var plazo = e.TryGetProperty("plazo", out var p) && p.ValueKind == JsonValueKind.String ? $" ({p.GetString()})" : "";
                var meta = e.TryGetProperty("meta", out var m) && m.ValueKind == JsonValueKind.Number ? $" · nota objetivo: {m.GetDecimal().ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("es-EC"))}" : "";
                sb.AppendLine($"  {(e.TryGetProperty("numero", out var n) ? n.GetInt32() : 0)}. {titulo}{plazo}{meta}");
                if (e.TryGetProperty("temas", out var temas) && temas.ValueKind == JsonValueKind.Array && temas.GetArrayLength() > 0)
                    sb.AppendLine($"     Temas que evalúa: {string.Join(", ", temas.EnumerateArray().Select(x => x.GetString()))}");
                if (e.TryGetProperty("acciones", out var acciones) && acciones.ValueKind == JsonValueKind.Array)
                    foreach (var a in acciones.EnumerateArray())
                        sb.AppendLine($"     - {a.GetString()}");
            }
            return sb.ToString();
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
        {
            return "";
        }
    }

    public async Task<Comunicacion> Enviar(Recomendacion r, Analisis a, string docente, int usuarioId, CancellationToken ct)
    {
        var (asunto, cuerpo) = Componer(r, a, docente);
        // Lo que se guarda como constancia cabe en la columna (4000); el correo sale completo.
        var c = new Comunicacion
        {
            RecomendacionId = r.Id, CursoId = r.CursoId, Destinatario = r.EstudianteAnalisis.Correo, Asunto = asunto, Cuerpo = cuerpo.Length <= 4000 ? cuerpo : cuerpo[..3999] + "…", EnviadoPor = usuarioId
        };
        if (string.IsNullOrWhiteSpace(c.Destinatario))
        {
            c.Estado = EstadoComunicacion.Error;
            c.Error = "El estudiante no tiene correo registrado.";
            return c;
        }
        if (!correo.Real)
        {
            // Modo simulado: sirve para presentar el flujo completo sin un servidor de correo ni escribirle a nadie por error.
            c.Estado = EstadoComunicacion.Simulado;
            return c;
        }
        try
        {
            using var smtp = new SmtpClient(correo.Host, correo.Puerto) { EnableSsl = correo.UsarSsl, Timeout = correo.TimeoutSegundos * 1000 };
            if (!string.IsNullOrWhiteSpace(correo.Usuario))
                smtp.Credentials = new NetworkCredential(correo.Usuario, correo.Clave);
            using var mensaje = new MailMessage(correo.Remitente, c.Destinatario, asunto, cuerpo) { BodyEncoding = Encoding.UTF8, SubjectEncoding = Encoding.UTF8 };
            await smtp.SendMailAsync(mensaje, ct);
            c.Estado = EstadoComunicacion.Enviado;
        }
        catch (Exception e) when (e is SmtpException or FormatException or InvalidOperationException or TimeoutException || (e is OperationCanceledException && !ct.IsCancellationRequested))
        {
            logger.LogWarning("No se pudo enviar el correo de la recomendación {Id}: {Motivo}", r.Id, e.Message);
            c.Estado = EstadoComunicacion.Error;
            c.Error = "No se pudo enviar el correo. Revisa la configuración SMTP e inténtalo de nuevo.";
        }
        return c;
    }
}
