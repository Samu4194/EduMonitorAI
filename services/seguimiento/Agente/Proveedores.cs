using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using EduMonitor.Seguimiento.Models;
namespace EduMonitor.Seguimiento.Agente;

public class ProveedorOpciones
{
    public bool Habilitado { get; set; } = true;
    public string BaseUrl { get; set; } = "";
    public string Modelo { get; set; } = "";
    // Modelo para el chat del asistente (p. ej. una variante con ventana de contexto mayor). Vacío = el mismo de las recomendaciones.
    public string ModeloChat { get; set; } = "";
    public string? ApiKey { get; set; }
    public int TimeoutSegundos { get; set; } = 60;
    // Pausa mínima entre llamadas: los planes gratuitos de la nube limitan las peticiones por minuto.
    public int MinIntervaloMs { get; set; }
}

// Sección "Agente" de appsettings. Orden = a quién se le pide primero; si falla, se pasa al siguiente y, al final, a las plantillas.
public class OpcionesAgente
{
    // La nube va primero (mejor calidad) y la local queda de respaldo para cuando no hay internet o se agota el límite gratuito.
    public List<string> Orden { get; set; } = ["nube", "local"];
    public int FallosParaOmitir { get; set; } = 2;
    // El agente redacta recomendaciones para los estudiantes con este nivel de alerta o mayor (los de nivel Bajo solo aparecen en la tabla).
    public string NivelMinimo { get; set; } = "Medio";
    public ProveedorOpciones Local { get; set; } = new() { BaseUrl = "http://host.docker.internal:11434/v1", Modelo = "qwen2.5:3b", TimeoutSegundos = 150 };
    public ProveedorOpciones Nube { get; set; } = new() { BaseUrl = "https://generativelanguage.googleapis.com/v1beta/openai", Modelo = "gemini-3.5-flash-lite", TimeoutSegundos = 40, MinIntervaloMs = 4500 };
}

public record MensajeChat(string Rol, string Contenido);

public record EstadoProveedor(string Clave, string Origen, string Modelo, bool Configurado, bool Disponible, string Detalle);

public interface IProveedorIA
{
    string Clave { get; }
    string Origen { get; }
    string Modelo { get; }
    bool Configurado { get; }
    Task<EstadoProveedor> Estado(CancellationToken ct);
    // Devuelve el texto de la respuesta (se le pide JSON). Lanza si el proveedor no responde o responde con error.
    Task<string> Completar(string sistema, string usuario, CancellationToken ct);
    // Chat del asistente: devuelve la respuesta en fragmentos a medida que el modelo la escribe. Lanza si el proveedor no responde.
    IAsyncEnumerable<string> Conversar(string sistema, IReadOnlyList<MensajeChat> historial, CancellationToken ct) => throw new NotSupportedException();
    // Carga el modelo en memoria sin esperar respuesta (el primer uso en frío tarda decenas de segundos). No hace nada en la nube.
    Task Calentar(CancellationToken ct) => Task.CompletedTask;
    // Pausa breve tras una caída de conexión: mientras dura, el agente salta este proveedor sin esperar su tiempo de espera en cada pregunta.
    bool EnPausa => false;
    void Pausar() { }
}

// Un solo conector sirve para los dos proveedores porque ambos hablan el protocolo "Chat Completions" de OpenAI:
//  - Local: Ollama (http://localhost:11434/v1), sin internet y sin enviar nada fuera de la máquina.
//  - Nube: Google Gemini (endpoint de compatibilidad con OpenAI), con clave de API gratuita.
// Cambiar de proveedor es cambiar la URL, el modelo y la clave en la configuración.
public sealed class ProveedorCompatibleOpenAI(string clave, string origen, ProveedorOpciones o, HttpClient http) : IProveedorIA
{
    readonly SemaphoreSlim turno = new(1, 1);
    DateTime ultima = DateTime.MinValue;
    DateTime pausadoHasta = DateTime.MinValue;
    // Solo la nube se pausa: sin internet (o con el límite gratuito agotado) fallaría en cada pregunta; el modelo local, en cambio, solo tarda en arrancar.
    public bool EnPausa => DateTime.UtcNow < pausadoHasta;
    public void Pausar()
    {
        if (origen == OrigenRecomendacion.Nube)
            pausadoHasta = DateTime.UtcNow.AddSeconds(60);
    }
    public string Clave => clave;
    public string Origen => origen;
    public string Modelo => o.Modelo;
    // La nube necesita clave; el modelo local no.
    public bool Configurado => o.Habilitado && !string.IsNullOrWhiteSpace(o.BaseUrl) && !string.IsNullOrWhiteSpace(o.Modelo) && (origen == OrigenRecomendacion.Local || !string.IsNullOrWhiteSpace(o.ApiKey));

    HttpRequestMessage Pedido(HttpMethod metodo, string ruta)
    {
        var r = new HttpRequestMessage(metodo, o.BaseUrl.TrimEnd('/') + ruta);
        if (!string.IsNullOrWhiteSpace(o.ApiKey))
            r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", o.ApiKey);
        return r;
    }

    public async Task<EstadoProveedor> Estado(CancellationToken ct)
    {
        EstadoProveedor E(bool disponible, string detalle) => new(clave, origen, o.Modelo, Configurado, disponible, detalle);
        if (!o.Habilitado)
            return E(false, "Desactivado en la configuración.");
        if (!Configurado)
            return E(false, origen == OrigenRecomendacion.Nube ? "Falta la clave de API (Agente__Nube__ApiKey)." : "Falta la URL o el modelo.");
        try
        {
            using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limite.CancelAfter(TimeSpan.FromSeconds(4));
            using var respuesta = await http.SendAsync(Pedido(HttpMethod.Get, "/models"), limite.Token);
            if (!respuesta.IsSuccessStatusCode)
                return E(false, $"El servicio respondió HTTP {(int)respuesta.StatusCode}. Revisa la clave o la URL.");
            var datos = await respuesta.Content.ReadFromJsonAsync<JsonElement>(limite.Token);
            var ids = datos.TryGetProperty("data", out var lista) && lista.ValueKind == JsonValueKind.Array
                ? lista.EnumerateArray().Select(x => x.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "").ToList() : [];
            // Ollama lista solo los modelos descargados; Gemini los lista como "models/gemini-...".
            var presente = ids.Any(id => id.Equals(o.Modelo, StringComparison.OrdinalIgnoreCase) || id.EndsWith("/" + o.Modelo, StringComparison.OrdinalIgnoreCase)
                || (origen == OrigenRecomendacion.Local && id.Equals(o.Modelo + ":latest", StringComparison.OrdinalIgnoreCase)));
            // Ollama con la lista vacía = sin modelos descargados. Solo en la nube se tolera una lista vacía (algunos servicios no la devuelven).
            return presente || (ids.Count == 0 && origen != OrigenRecomendacion.Local) ? E(true, "Disponible.") : E(false, origen == OrigenRecomendacion.Local ? $"El modelo no está descargado. Ejecuta: ollama pull {o.Modelo}" : $"El modelo «{o.Modelo}» no aparece en el servicio.");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            return E(false, origen == OrigenRecomendacion.Local ? "Ollama no responde. Inícialo para usar el modelo local." : "Sin conexión con el servicio en la nube.");
        }
    }

    public async Task<string> Completar(string sistema, string usuario, CancellationToken ct)
    {
        await turno.WaitAsync(ct);
        try
        {
            var espera = ultima.AddMilliseconds(o.MinIntervaloMs) - DateTime.UtcNow;
            if (espera > TimeSpan.Zero)
                await Task.Delay(espera, ct);
            using var pedido = Pedido(HttpMethod.Post, "/chat/completions");
            pedido.Content = new StringContent(JsonSerializer.Serialize(new
            {
                model = o.Modelo,
                temperature = 0.3,
                stream = false,
                response_format = new { type = "json_object" },
                messages = new object[] { new { role = "system", content = sistema }, new { role = "user", content = usuario } }
            }), Encoding.UTF8, "application/json");
            using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limite.CancelAfter(TimeSpan.FromSeconds(o.TimeoutSegundos));
            try
            {
                using var respuesta = await http.SendAsync(pedido, limite.Token);
                ultima = DateTime.UtcNow;
                if (!respuesta.IsSuccessStatusCode)
                    throw new HttpRequestException($"{clave}: HTTP {(int)respuesta.StatusCode}");
                var datos = await respuesta.Content.ReadFromJsonAsync<JsonElement>(limite.Token);
                return datos.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                ultima = DateTime.UtcNow;
                throw new TimeoutException($"{clave}: sin respuesta en {o.TimeoutSegundos} s");
            }
        }
        finally { turno.Release(); }
    }

    // La variante del chat puede no existir todavía (no se ejecutó Preparar-IA): entonces se usa el modelo normal.
    string? modeloChatUsable;
    DateTime modeloChatRevisado = DateTime.MinValue;
    async Task<string> ModeloParaChat(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(o.ModeloChat) || o.ModeloChat == o.Modelo)
            return o.Modelo;
        if (modeloChatUsable is not null && DateTime.UtcNow - modeloChatRevisado < TimeSpan.FromMinutes(2))
            return modeloChatUsable;
        var usable = o.Modelo;
        try
        {
            using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limite.CancelAfter(TimeSpan.FromSeconds(4));
            using var respuesta = await http.SendAsync(Pedido(HttpMethod.Get, "/models"), limite.Token);
            if (respuesta.IsSuccessStatusCode)
            {
                var datos = await respuesta.Content.ReadFromJsonAsync<JsonElement>(limite.Token);
                if (datos.TryGetProperty("data", out var lista) && lista.ValueKind == JsonValueKind.Array &&
                    lista.EnumerateArray().Any(x => x.TryGetProperty("id", out var id) && (id.GetString() ?? "").StartsWith(o.ModeloChat, StringComparison.OrdinalIgnoreCase)))
                    usable = o.ModeloChat;
            }
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or JsonException) { /* se usa el modelo normal */ }
        modeloChatUsable = usable;
        modeloChatRevisado = DateTime.UtcNow;
        return usable;
    }

    public async IAsyncEnumerable<string> Conversar(string sistema, IReadOnlyList<MensajeChat> historial, [EnumeratorCancellation] CancellationToken ct)
    {
        await turno.WaitAsync(ct);
        try
        {
            var espera = ultima.AddMilliseconds(o.MinIntervaloMs) - DateTime.UtcNow;
            if (espera > TimeSpan.Zero)
                await Task.Delay(espera, ct);
            var mensajes = new List<object> { new { role = "system", content = sistema } };
            mensajes.AddRange(historial.Select(m => (object)new { role = m.Rol, content = m.Contenido }));
            using var pedido = Pedido(HttpMethod.Post, "/chat/completions");
            pedido.Content = new StringContent(JsonSerializer.Serialize(new { model = await ModeloParaChat(ct), temperature = 0.4, stream = true, messages = mensajes }), Encoding.UTF8, "application/json");
            // El plazo vale para el primer fragmento y se reinicia con cada uno: una respuesta larga no se corta mientras el modelo siga escribiendo.
            using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limite.CancelAfter(TimeSpan.FromSeconds(o.TimeoutSegundos));
            HttpResponseMessage respuesta;
            try { respuesta = await http.SendAsync(pedido, HttpCompletionOption.ResponseHeadersRead, limite.Token); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new TimeoutException($"{clave}: sin respuesta en {o.TimeoutSegundos} s"); }
            using (respuesta)
            {
                if (!respuesta.IsSuccessStatusCode)
                    throw new HttpRequestException($"{clave}: HTTP {(int)respuesta.StatusCode}");
                await using var flujo = await respuesta.Content.ReadAsStreamAsync(limite.Token);
                using var lector = new StreamReader(flujo, Encoding.UTF8);
                while (true)
                {
                    string? linea;
                    try { linea = await lector.ReadLineAsync(limite.Token); }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new TimeoutException($"{clave}: sin respuesta en {o.TimeoutSegundos} s"); }
                    if (linea is null)
                        break;
                    if (!linea.StartsWith("data:", StringComparison.Ordinal))
                        continue;
                    var dato = linea[5..].Trim();
                    if (dato == "[DONE]")
                        break;
                    limite.CancelAfter(TimeSpan.FromSeconds(o.TimeoutSegundos));
                    string? delta = null;
                    using (var doc = JsonDocument.Parse(dato))
                        if (doc.RootElement.TryGetProperty("choices", out var opciones) && opciones.GetArrayLength() > 0 &&
                            opciones[0].TryGetProperty("delta", out var d) && d.TryGetProperty("content", out var c))
                            delta = c.GetString();
                    if (!string.IsNullOrEmpty(delta))
                        yield return delta;
                }
            }
            ultima = DateTime.UtcNow;
        }
        finally { turno.Release(); }
    }

    public async Task Calentar(CancellationToken ct)
    {
        if (origen != OrigenRecomendacion.Local || !Configurado || !await turno.WaitAsync(0, ct))
            return;
        try
        {
            using var pedido = Pedido(HttpMethod.Post, "/chat/completions");
            pedido.Content = new StringContent(JsonSerializer.Serialize(new { model = await ModeloParaChat(ct), max_tokens = 1, stream = false, messages = new object[] { new { role = "user", content = "Hola" } } }), Encoding.UTF8, "application/json");
            using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limite.CancelAfter(TimeSpan.FromSeconds(o.TimeoutSegundos));
            using var respuesta = await http.SendAsync(pedido, limite.Token);
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException) { /* calentar es opcional */ }
        finally { turno.Release(); }
    }
}

// Proveedores en el orden configurado. Los que no están configurados (p. ej. la nube sin clave) se listan, pero el agente los salta.
public sealed class ProveedoresIA
{
    public IReadOnlyList<IProveedorIA> Lista { get; }
    public ProveedoresIA(OpcionesAgente o, IHttpClientFactory fabrica)
    {
        var todos = new Dictionary<string, IProveedorIA>(StringComparer.OrdinalIgnoreCase)
        {
            ["local"] = new ProveedorCompatibleOpenAI("local", OrigenRecomendacion.Local, o.Local, fabrica.CreateClient("ia-local")),
            ["nube"] = new ProveedorCompatibleOpenAI("nube", OrigenRecomendacion.Nube, o.Nube, fabrica.CreateClient("ia-nube"))
        };
        Lista = o.Orden.Where(todos.ContainsKey).Distinct(StringComparer.OrdinalIgnoreCase).Select(k => todos[k]).ToList();
    }
    public ProveedoresIA(IReadOnlyList<IProveedorIA> lista) => Lista = lista;
}
