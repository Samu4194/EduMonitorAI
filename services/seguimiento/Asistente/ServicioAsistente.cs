using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using EduMonitor.Infrastructure;
using EduMonitor.Seguimiento.Agente;
using EduMonitor.Seguimiento.Data;
using EduMonitor.Seguimiento.DTOs;
using EduMonitor.Seguimiento.Models;
using EduMonitor.Seguimiento.Reglas;
using EduMonitor.Seguimiento.Services;
using Microsoft.EntityFrameworkCore;
namespace EduMonitor.Seguimiento.Asistente;

public sealed record EntradaChat(int? ConversacionId, string? Texto, string? PreguntaId, List<int>? EstudianteIds, bool IgnorarDudas = false);
public sealed record EventoChat(string Tipo, object Datos);
public sealed record PersonaView(int Id, string Nombre, string Correo);
public sealed record AliasView(string Alias, int EstudianteId, string Nombre);
public sealed record MensajeView(int Id, string Rol, string Tipo, string Texto, List<JsonElement> Tarjetas, string? Origen, string? Modelo, string? Alcance, bool? Verificada, bool? Util, DateTime CreadoEn);
public sealed record ConversacionView(int Id, string Titulo, DateTime ActualizadoEn, DateTime VenceEn, int Mensajes);
public sealed record ConversacionDetalle(int Id, string Titulo, DateTime VenceEn, List<MensajeView> Mensajes, List<AliasView> Alias);
public sealed record GuardadoView(int RecomendacionId, string Estado, bool Nueva);

// Chat del docente con el asistente académico (HU-12 ampliada). Las notas y los números los calcula el sistema; la IA solo los explica.
// Nada personal sale hacia el modelo: los estudiantes viajan como «Estudiante A, B…» y el servidor los traduce al mostrar la respuesta.
public sealed partial class ServicioAsistente(AccesoCursos acceso, CargadorDatos cargador, SeguimientoDb db, AgenteAcademico agente, OpcionesAsistente cfg,
    OpcionesReglas reglas, IHttpContextAccessor http, ILogger<ServicioAsistente> logger)
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    [GeneratedRegex(@"curso|estudiant|alumn|nota|promedio|actividad|tema|riesgo|grupo|clase|paralelo|examen|tarea|\bape\b|materia|unidad", RegexOptions.IgnoreCase)]
    private static partial Regex PalabrasDelCurso();
    // «Este estudiante», «esa alumna»…: se refiere a alguien de la conversación sin nombrarlo.
    [GeneratedRegex(@"\b(este|ese|esta|esa|aquel|aquella)\s+(estudiante|alumn[oa]|chic[oa]|joven)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ReferenciaAEstudiante();
    // Pide ver las notas de alguien: se responde con una tarjeta exacta, no con lo que el modelo recuerde.
    [GeneratedRegex(@"\b(notas?|calificaci(?:ón|on|ones)|puntaje|resultados?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex PideNotas();

    int DocenteId => http.HttpContext!.User.UserId();
    public Task<CursoReference> Autorizar(int cursoId, CancellationToken ct) => acceso.Curso(cursoId, escritura: false, ct);

    // ---------- Consultas ----------
    public static List<PreguntaPre> Sugerencias() => [.. Catalogo.Todas];

    async Task<List<Persona>> Lista(int cursoId, CancellationToken ct) =>
        [.. (await acceso.Estudiantes(cursoId, ct)).Select(e => new Persona(e.Id, e.Apellidos, e.Nombres, e.Correo))];

    public async Task<List<PersonaView>> BuscarEstudiantes(int cursoId, string? q, CancellationToken ct)
    {
        await Autorizar(cursoId, ct);
        return [.. Seudonimos.Filtrar(q ?? "", await Lista(cursoId, ct)).Select(p => new PersonaView(p.Id, p.Completo, p.Correo))];
    }

    // Traduce los seudónimos de una conversación a nombres reales (solo para mostrar; en la base nunca se guardan nombres).
    static Dictionary<string, string> Nombres(string aliasJson, IReadOnlyList<Persona> lista)
    {
        var mapa = JsonSerializer.Deserialize<Dictionary<string, int>>(aliasJson, Json) ?? [];
        return mapa.ToDictionary(kv => kv.Key, kv => lista.FirstOrDefault(p => p.Id == kv.Value)?.Completo ?? "Estudiante retirado");
    }
    static string MostrarJson(string json, Dictionary<string, string> nombres) =>
        Seudonimos.Mostrar(json, nombres.ToDictionary(kv => kv.Key, kv => JsonEncodedText.Encode(kv.Value).ToString()));

    public async Task<List<ConversacionView>> Conversaciones(int cursoId, CancellationToken ct)
    {
        await Autorizar(cursoId, ct);
        await Depurar(ct, DocenteId);
        var lista = await Lista(cursoId, ct);
        var filas = await db.Conversaciones.AsNoTracking().Where(c => c.CursoId == cursoId && c.DocenteId == DocenteId).OrderByDescending(c => c.ActualizadoEn)
            .Select(c => new { c.Id, c.Titulo, c.ActualizadoEn, c.AliasJson, Mensajes = c.Mensajes.Count }).ToListAsync(ct);
        return [.. filas.Select(c => new ConversacionView(c.Id, Seudonimos.Mostrar(c.Titulo, Nombres(c.AliasJson, lista)), c.ActualizadoEn, c.ActualizadoEn.AddDays(cfg.RetencionDias), c.Mensajes))];
    }

    public async Task<ConversacionDetalle> Detalle(int cursoId, int id, CancellationToken ct)
    {
        await Autorizar(cursoId, ct);
        await Depurar(ct, DocenteId);
        var c = await db.Conversaciones.AsNoTracking().Include(x => x.Mensajes).SingleOrDefaultAsync(x => x.Id == id && x.CursoId == cursoId && x.DocenteId == DocenteId, ct)
            ?? throw new ApiException(404, "La conversación no existe o ya venció.");
        var lista = await Lista(cursoId, ct);
        var nombres = Nombres(c.AliasJson, lista);
        var mensajes = c.Mensajes.OrderBy(m => m.Id).Select(m => new MensajeView(m.Id, m.Rol, m.Tipo, Seudonimos.Mostrar(m.Texto, nombres),
            JsonSerializer.Deserialize<List<JsonElement>>(MostrarJson(m.TarjetasJson, nombres), Json) ?? [], m.Origen, m.Modelo, m.Alcance, m.Verificada, m.Util, m.CreadoEn)).ToList();
        var alias = nombres.Select(kv => new AliasView(kv.Key, JsonSerializer.Deserialize<Dictionary<string, int>>(c.AliasJson, Json)![kv.Key], kv.Value)).ToList();
        return new ConversacionDetalle(c.Id, Seudonimos.Mostrar(c.Titulo, nombres), c.ActualizadoEn.AddDays(cfg.RetencionDias), mensajes, alias);
    }

    public async Task Borrar(int cursoId, int id, CancellationToken ct)
    {
        await Autorizar(cursoId, ct);
        var n = await db.Conversaciones.Where(c => c.Id == id && c.CursoId == cursoId && c.DocenteId == DocenteId).ExecuteDeleteAsync(ct);
        if (n == 0)
            throw new ApiException(404, "La conversación no existe o ya venció.");
    }

    public async Task Valorar(int cursoId, int mensajeId, bool util, CancellationToken ct)
    {
        await Autorizar(cursoId, ct);
        var m = await db.Mensajes.SingleOrDefaultAsync(x => x.Id == mensajeId && x.Rol == RolMensaje.Asistente, ct) ?? throw new ApiException(404, "El mensaje no existe.");
        if (!await db.Conversaciones.AnyAsync(c => c.Id == m.ConversacionId && c.CursoId == cursoId && c.DocenteId == DocenteId, ct))
            throw new ApiException(404, "El mensaje no existe.");
        m.Util = util;
        await db.SaveChangesAsync(ct);
    }

    // Borra las conversaciones que superaron los días de retención. Se llama al listar (por docente) y a diario desde un servicio de fondo.
    public Task<int> Depurar(CancellationToken ct, int? docenteId = null)
    {
        var limite = DateTime.UtcNow.AddDays(-cfg.RetencionDias);
        var q = db.Conversaciones.Where(c => c.ActualizadoEn < limite);
        if (docenteId is int id)
            q = q.Where(c => c.DocenteId == id);
        return q.ExecuteDeleteAsync(ct);
    }

    public Task Calentar(CancellationToken ct) => agente.Calentar(ct);

    // Validaciones de la petición que no necesitan datos: se hacen antes de abrir el flujo para responder con un 400 normal.
    public void Validar(EntradaChat entrada)
    {
        if (entrada.PreguntaId is not null)
        {
            var pre = Catalogo.Buscar(entrada.PreguntaId) ?? throw new ApiException(400, "La pregunta precargada no existe.");
            if (pre.RequiereEstudiante && (entrada.EstudianteIds?.Count ?? 0) == 0)
                throw new ApiException(400, "Elige primero al estudiante (por nombre o por cédula).");
            return;
        }
        var texto = (entrada.Texto ?? "").Trim();
        if (texto.Length == 0)
            throw new ApiException(400, "Escribe una pregunta.");
        if (texto.Length > cfg.MaxCaracteresPregunta)
            throw new ApiException(400, $"La pregunta es demasiado larga (máximo {cfg.MaxCaracteresPregunta} caracteres).");
    }

    // ---------- Un turno de conversación ----------
    sealed class Turno
    {
        public required DatosCurso D;
        public required Conversacion Conv;
        public required Dictionary<string, int> Mapa;
        public required Func<EventoChat, Task> Emitir;
        public required SesionAgente Sesion;
        public required CancellationToken Ct;
        public required List<Persona> Resueltos;
        public Dictionary<string, string> Nombres() => Mapa.ToDictionary(kv => kv.Key, kv => D.Persona(kv.Value)?.Completo ?? kv.Key);
        public string Alias(Persona p)
        {
            var existente = Mapa.FirstOrDefault(kv => kv.Value == p.Id);
            if (existente.Key is not null)
                return existente.Key;
            if (Mapa.Count >= Seudonimos.MaxAlias)
                throw new ApiException(409, "Esta conversación ya mencionó a demasiados estudiantes. Inicia una conversación nueva.");
            var a = Seudonimos.Alias(Mapa.Count);
            Mapa[a] = p.Id;
            return a;
        }
        public string Mostrar(string s) => Seudonimos.Mostrar(s, Nombres());
        // Convierte un objeto con seudónimos en lo que ve el docente (con nombres reales).
        public JsonElement Cliente(object o) => JsonSerializer.Deserialize<JsonElement>(MostrarJson(JsonSerializer.Serialize(o, Json), Nombres()), Json);
    }

    public async Task Responder(int cursoId, EntradaChat entrada, Func<EventoChat, Task> emitir, CancellationToken ct)
    {
        var docenteId = DocenteId;
        var pre = entrada.PreguntaId is null ? null : Catalogo.Buscar(entrada.PreguntaId) ?? throw new ApiException(400, "La pregunta precargada no existe.");
        var d = await cargador.Cargar(cursoId, ct);
        var elegidos = new List<Persona>();
        foreach (var id in entrada.EstudianteIds ?? [])
            elegidos.Add(d.Persona(id) ?? throw new ApiException(400, "Un estudiante elegido no pertenece a este curso."));

        var texto = (entrada.Texto ?? "").Trim();
        if (pre is not null)
        {
            if (pre.RequiereEstudiante && elegidos.Count == 0)
                throw new ApiException(400, "Elige primero al estudiante (por nombre o por cédula).");
            texto = pre.Texto.Replace(Catalogo.MarcaEstudiante, elegidos.FirstOrDefault()?.Completo ?? "");
        }
        if (texto.Length == 0)
            throw new ApiException(400, "Escribe una pregunta.");
        if (texto.Length > cfg.MaxCaracteresPregunta)
            throw new ApiException(400, $"La pregunta es demasiado larga (máximo {cfg.MaxCaracteresPregunta} caracteres).");

        // ¿A qué estudiante se refiere? Por cédula o por nombre. Si hay duda no se adivina: se le pregunta al docente.
        var busqueda = Seudonimos.Buscar(texto, d.Lista);
        var resueltos = elegidos.Concat(busqueda.Resueltos).DistinctBy(p => p.Id).ToList();
        if (!entrada.IgnorarDudas)
        {
            var dudas = busqueda.Dudas.Where(g => !g.Any(p => resueltos.Any(r => r.Id == p.Id))).ToList();
            if (dudas.Count > 0)
            {
                await emitir(new EventoChat("aclaracion", new
                {
                    pregunta = "Encontré más de una posibilidad. ¿A quién te refieres? Elige uno o varios.",
                    grupos = dudas.Select(g => g.Select(p => new PersonaView(p.Id, p.Completo, p.Correo)))
                }));
                return;
            }
        }

        Conversacion conv;
        if (entrada.ConversacionId is int cid)
            conv = await db.Conversaciones.SingleOrDefaultAsync(c => c.Id == cid && c.CursoId == cursoId && c.DocenteId == docenteId, ct) ?? throw new ApiException(404, "La conversación no existe o ya venció.");
        else
        {
            conv = new Conversacion { CursoId = cursoId, DocenteId = docenteId };
            db.Conversaciones.Add(conv);
        }
        var turno = new Turno
        {
            D = d, Conv = conv, Mapa = JsonSerializer.Deserialize<Dictionary<string, int>>(conv.AliasJson, Json) ?? [], Emitir = emitir, Sesion = new SesionAgente(), Ct = ct, Resueltos = resueltos
        };
        // «Este estudiante», sin nombre ni selección: se entiende que es el de la conversación; si hay varios, se pregunta cuál.
        if (pre is null && resueltos.Count == 0 && ReferenciaAEstudiante().IsMatch(texto))
        {
            var candidatos = turno.Mapa.Values.Select(d.Persona).OfType<Persona>().ToList();
            if (candidatos.Count == 1)
                resueltos.Add(candidatos[0]);
            else
            {
                await emitir(candidatos.Count > 1
                    ? new EventoChat("aclaracion", new { pregunta = "¿De qué estudiante hablas? Elige uno o varios.", grupos = new[] { candidatos.Select(p => new PersonaView(p.Id, p.Completo, p.Correo)) } })
                    : new EventoChat("error", new { mensaje = "No sé de qué estudiante hablas. Usa el botón «Estudiante» o escribe su nombre en la pregunta." }));
                return;
            }
        }
        foreach (var p in resueltos)
            turno.Alias(p);
        var aliasDe = turno.Mapa.ToDictionary(kv => kv.Value, kv => kv.Key);
        var oculto = Seudonimos.Ocultar(texto, aliasDe, d.Lista);
        // El estudiante elegido con el buscador debe quedar visible en la pregunta (y en el historial, como seudónimo).
        var sinMencion = elegidos.Select(turno.Alias).Where(a => !oculto.Contains(a)).ToList();
        if (pre is null && sinMencion.Count > 0)
            oculto += $" ({string.Join(", ", sinMencion)})";
        if (entrada.ConversacionId is null)
            conv.Titulo = oculto.Length <= 60 ? oculto : oculto[..57].TrimEnd() + "…";
        var pregunta = new Mensaje { Rol = RolMensaje.Docente, Tipo = pre is null ? TipoMensaje.Libre : TipoMensaje.Precargada, PreguntaId = pre?.Id, Texto = oculto };
        conv.Mensajes.Add(pregunta);
        conv.AliasJson = JsonSerializer.Serialize(turno.Mapa, Json);
        conv.ActualizadoEn = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await emitir(new EventoChat("inicio", new
        {
            conversacionId = conv.Id,
            titulo = turno.Mostrar(conv.Titulo),
            mensaje = new { id = pregunta.Id, texto = turno.Mostrar(oculto) },
            alias = turno.Mapa.Select(kv => new AliasView(kv.Key, kv.Value, turno.Nombres()[kv.Key]))
        }));

        if (pre?.Id is Catalogo.Ruta or Catalogo.Rutas)
            await Rutas(turno, pre);
        else
            await Chat(turno, pre, oculto, pregunta.Id);
        conv.AliasJson = JsonSerializer.Serialize(turno.Mapa, Json);
        conv.ActualizadoEn = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    // ---------- Pregunta precargada o libre: tarjeta de datos + texto que el modelo escribe en vivo ----------
    async Task Chat(Turno t, PreguntaPre? pre, string oculto, int idPregunta)
    {
        var d = t.D;
        var hechos = pre is null ? null : Datos.De(pre.Id, d, t.Resueltos, t.Alias);
        var pidenNotas = pre is null && t.Resueltos.Count > 0 && PideNotas().IsMatch(oculto);
        var datos = new StringBuilder();
        // Si piden las notas de alguien, el modelo solo ve a esa persona: con todo el curso a la vista mezclaba promedios del grupo con notas del estudiante.
        datos.AppendLine(pidenNotas ? $"Nota mínima para aprobar: {Textos.N(d.NotaMinima)}."
            : hechos is not null && pre!.Id != Catalogo.Resumen ? $"{Datos.ContextoCurso(d, t.Alias)}\n\n{hechos.Datos}" : hechos?.Datos ?? Datos.ContextoCurso(d, t.Alias));
        foreach (var p in t.Resueltos.Where(_ => hechos is null || pre!.Id != Catalogo.Apoyar))
            datos.AppendLine().AppendLine(Datos.DeEstudiante(d, p, t.Alias(p)));
        var contexto = datos.Length > 7000 ? datos.ToString()[..7000] : datos.ToString();

        var tablas = new List<Tabla>();
        if (hechos?.Tarjeta is { } tarjeta)
            tablas.Add(tarjeta);
        if (pidenNotas)
            tablas.AddRange(t.Resueltos.Take(3).Select(p => Datos.NotasDe(d, p, t.Alias(p))));
        foreach (var tabla in tablas)
            await t.Emitir(new EventoChat("tarjeta", new { tarjeta = t.Cliente(tabla) }));

        var previos = await db.Mensajes.AsNoTracking().Where(m => m.ConversacionId == t.Conv.Id && m.Id < idPregunta && m.Texto != "").OrderByDescending(m => m.Id).Take(cfg.MensajesDeContexto).ToListAsync(t.Ct);
        var mensajes = previos.AsEnumerable().Reverse().Select(m => new MensajeChat(m.Rol == RolMensaje.Docente ? "user" : "assistant", m.Texto)).ToList();
        mensajes.Add(new MensajeChat("user", pidenNotas
            ? $"{oculto}\n\nInstrucción: las notas exactas ya se muestran en una tabla. Comenta en 2 o 3 viñetas lo más relevante (notas más bajas, tendencia, qué reforzar) usando solo los datos de esa persona. No repitas la lista completa de notas."
            : hechos is null ? oculto : $"{oculto}\n\nInstrucción: {hechos.Instruccion}"));
        var sistema = $"{PromptsAsistente.Sistema}\n\nDATOS DEL CURSO (verificados por el sistema; los nombres son seudónimos):\n{contexto}";

        var limpio = new StringBuilder();
        string? origen = null, modelo = null, alcance = null;
        var interrumpida = false;
        var etiquetaResuelta = false;
        var cabecera = new StringBuilder();
        var traductor = new Seudonimos.Traductor(t.Nombres());
        try
        {
            await foreach (var f in agente.Conversar(sistema, mensajes, t.Sesion, t.Ct))
            {
                origen ??= f.Origen;
                modelo ??= f.Modelo;
                var trozo = f.Texto;
                if (!etiquetaResuelta)
                {
                    // La respuesta empieza con [CURSO] o [GENERAL]: se lee la etiqueta (puede llegar partida) y no se muestra.
                    cabecera.Append(trozo);
                    var c = cabecera.ToString().TrimStart();
                    if (c.StartsWith('[') && !c.Contains(']') && c.Length < 14)
                        continue;
                    etiquetaResuelta = true;
                    var fin = c.IndexOf(']');
                    if (c.StartsWith('[') && fin >= 0)
                    {
                        alcance = c[1..fin].Contains("GENERAL", StringComparison.OrdinalIgnoreCase) ? "general" : "curso";
                        trozo = c[(fin + 1)..].TrimStart();
                    }
                    else
                        trozo = c;
                }
                limpio.Append(trozo);
                var visible = traductor.Procesar(trozo);
                if (visible.Length > 0)
                    await t.Emitir(new EventoChat("texto", new { delta = visible }));
            }
            var resto = traductor.Terminar();
            if (resto.Length > 0)
                await t.Emitir(new EventoChat("texto", new { delta = resto }));
        }
        catch (RespuestaInterrumpidaException)
        {
            interrumpida = true;
            var resto = traductor.Terminar();
            if (resto.Length > 0)
                await t.Emitir(new EventoChat("texto", new { delta = resto }));
        }
        if (!etiquetaResuelta && cabecera.Length > 0)
        {
            limpio.Append(cabecera.ToString().TrimStart());
            await t.Emitir(new EventoChat("texto", new { delta = t.Mostrar(cabecera.ToString().TrimStart()) }));
        }

        if (origen is null)
        {
            // Sin IA disponible: las preguntas precargadas siguen mostrando sus datos exactos; las libres no se pueden responder.
            origen = OrigenRecomendacion.Plantilla;
            var aviso = hechos is not null
                ? "En este momento no hay un modelo de IA disponible (Ollama apagado y sin conexión a la nube), así que no puedo redactar el análisis. Los datos exactos de la tarjeta de arriba los calculó el sistema y siguen siendo válidos."
                : "En este momento no hay un modelo de IA disponible (Ollama apagado y sin conexión a la nube), así que no puedo responder preguntas libres. Las preguntas precargadas y las rutas de mejora siguen funcionando con los datos del curso.";
            limpio.Append(aviso);
            await t.Emitir(new EventoChat("texto", new { delta = aviso }));
        }
        alcance ??= pre is not null || t.Resueltos.Count > 0 || PalabrasDelCurso().IsMatch(oculto) ? "curso" : "general";

        var avisos = new List<string>();
        if (alcance == "curso")
            avisos.AddRange(Verificador.Revisar(limpio.ToString(), contexto, oculto).Select(c => $"La cifra «{c}» que menciona el asistente no aparece en los datos del curso: confía en la tarjeta."));
        avisos.AddRange(Seudonimos.AliasEn(limpio.ToString()).Where(a => !t.Mapa.ContainsKey(a)).Select(a => $"El asistente nombró a «{a}», que no es parte de esta conversación: ignora esa mención."));
        if (interrumpida)
            avisos.Add("La respuesta se interrumpió porque el modelo dejó de responder. Puedes volver a preguntar.");

        var respuesta = new Mensaje
        {
            Rol = RolMensaje.Asistente, Tipo = pre is null ? TipoMensaje.Libre : TipoMensaje.Precargada, PreguntaId = pre?.Id, Texto = limpio.ToString(),
            TarjetasJson = tablas.Count > 0 ? JsonSerializer.Serialize(tablas, Json) : "[]", Origen = origen, Modelo = modelo, Alcance = alcance, Verificada = avisos.Count == 0
        };
        t.Conv.Mensajes.Add(respuesta);
        t.Conv.AliasJson = JsonSerializer.Serialize(t.Mapa, Json);
        await db.SaveChangesAsync(t.Ct);
        await t.Emitir(new EventoChat("fin", new { mensajeId = respuesta.Id, origen, modelo, alcance, verificada = avisos.Count == 0, avisos }));
    }

    // ---------- Rutas de mejora: números del sistema + acciones de estudio redactadas por la IA ----------
    async Task Rutas(Turno t, PreguntaPre pre)
    {
        var d = t.D;
        var objetivos = pre.Id == Catalogo.Ruta
            ? t.Resueltos.Take(5).ToList()
            : [.. d.Reglas.Estudiantes.Where(e => e.Nivel >= Nivel.Medio).OrderByDescending(e => e.Nivel).ThenBy(e => e.Resultado ?? 11m)
                .Select(e => d.Persona(e.EstudianteId)).OfType<Persona>().Take(cfg.MaxRutasLote)];
        if (objetivos.Count == 0)
        {
            const string vacio = "No hay estudiantes con riesgo medio o alto en este momento, así que no hace falta generar rutas de mejora.";
            await t.Emitir(new EventoChat("texto", new { delta = vacio }));
            var m = new Mensaje { Rol = RolMensaje.Asistente, Tipo = TipoMensaje.Precargada, PreguntaId = pre.Id, Texto = vacio, Origen = OrigenRecomendacion.Plantilla, Alcance = "curso", Verificada = true };
            t.Conv.Mensajes.Add(m);
            await db.SaveChangesAsync(t.Ct);
            await t.Emitir(new EventoChat("fin", new { mensajeId = m.Id, origen = m.Origen, alcance = "curso", verificada = true, avisos = Array.Empty<string>() }));
            return;
        }
        var ids = new List<int>();
        foreach (var p in objetivos)
        {
            var ruta = RutaMejora.Construir(d, p, t.Alias(p), reglas);
            var msg = new Mensaje
            {
                Rol = RolMensaje.Asistente, Tipo = TipoMensaje.Ruta, PreguntaId = pre.Id, Texto = ruta.Resumen, TarjetasJson = JsonSerializer.Serialize(new[] { ruta }, Json),
                Origen = OrigenRecomendacion.Plantilla, Alcance = "curso", Verificada = true
            };
            t.Conv.Mensajes.Add(msg);
            t.Conv.AliasJson = JsonSerializer.Serialize(t.Mapa, Json);
            await db.SaveChangesAsync(t.Ct);
            ids.Add(msg.Id);
            var conIA = ruta.Estado is not ("Aprobado" or "SinNotas") && ruta.Etapas.Count > 0;
            await t.Emitir(new EventoChat("ruta", new { mensajeId = msg.Id, estudianteId = p.Id, ruta = t.Cliente(ruta), redactando = conIA }));
            if (!conIA)
                continue;

            var (red, origen, modelo) = await agente.Redactar<InterpreteRuta.Redaccion>(PromptsAsistente.SistemaRuta, PromptsAsistente.UsuarioRuta(d, ruta), texto =>
            {
                var r = InterpreteRuta.Interpretar(texto, ruta.Etapas.Count, out _);
                if (r is null)
                    return null;
                // La ruta le llega al estudiante: si el modelo inventó una cifra se descarta su redacción y se queda la plantilla.
                var todo = string.Join(" ", new[] { r.Resumen, r.Mensaje }.Concat(r.AccionesDocente).Concat(r.AccionesPorEtapa.SelectMany(x => x)).OfType<string>());
                return Verificador.RevisarRuta(todo, ruta).Count == 0 ? r : null;
            }, t.Sesion, t.Ct);
            if (red is not null)
            {
                ruta = InterpreteRuta.Aplicar(ruta, red, origen!, modelo!);
                msg.TarjetasJson = JsonSerializer.Serialize(new[] { ruta }, Json);
                msg.Texto = ruta.Resumen;
                msg.Origen = origen;
                msg.Modelo = modelo;
                await db.SaveChangesAsync(t.Ct);
            }
            await t.Emitir(new EventoChat("ruta", new { mensajeId = msg.Id, estudianteId = p.Id, ruta = t.Cliente(ruta), redactando = false }));
        }
        await t.Emitir(new EventoChat("fin", new { mensajeIds = ids, alcance = "curso", verificada = true, avisos = Array.Empty<string>() }));
    }

    // ---------- De la ruta a la recomendación del flujo de revisión (HU-13) ----------
    public async Task<GuardadoView> GuardarRuta(int cursoId, int mensajeId, CancellationToken ct)
    {
        await acceso.Curso(cursoId, escritura: true, ct);
        var m = await db.Mensajes.SingleOrDefaultAsync(x => x.Id == mensajeId, ct) ?? throw new ApiException(404, "El mensaje no existe.");
        var conv = await db.Conversaciones.SingleOrDefaultAsync(c => c.Id == m.ConversacionId && c.CursoId == cursoId && c.DocenteId == DocenteId, ct) ?? throw new ApiException(404, "El mensaje no existe.");
        if (m.Tipo != TipoMensaje.Ruta)
            throw new ApiException(400, "Solo se puede guardar una ruta de mejora como recomendación.");
        var ruta = JsonSerializer.Deserialize<List<RutaDatos>>(m.TarjetasJson, Json)?.FirstOrDefault() ?? throw new ApiException(400, "El mensaje no contiene una ruta.");
        var mapa = JsonSerializer.Deserialize<Dictionary<string, int>>(conv.AliasJson, Json) ?? [];
        if (!mapa.TryGetValue(ruta.Estudiante, out var estudianteId))
            throw new ApiException(400, "No se pudo identificar al estudiante de esta ruta.");

        var analisis = await db.Analisis.Where(a => a.CursoId == cursoId).OrderByDescending(a => a.CreadoEn).ThenByDescending(a => a.Id).FirstOrDefaultAsync(ct)
            ?? throw new ApiException(409, "Primero actualiza las alertas del curso (panel «Alertas» del asistente): la recomendación se revisa y se comunica desde allí.");
        var ea = await db.EstudiantesAnalisis.SingleOrDefaultAsync(e => e.AnalisisId == analisis.Id && e.EstudianteId == estudianteId, ct)
            ?? throw new ApiException(409, "Este estudiante no estaba en el último análisis del curso. Genera un análisis nuevo y vuelve a guardar la ruta.");
        var rec = await db.Recomendaciones.SingleOrDefaultAsync(r => r.AnalisisId == analisis.Id && r.EstudianteAnalisisId == ea.Id, ct);
        if (rec is not null && rec.Estado is not (EstadoRecomendacion.Borrador or EstadoRecomendacion.Rechazada))
            throw new ApiException(409, rec.Estado == EstadoRecomendacion.Generando ? "La recomendación de este estudiante se está redactando. Espera a que termine."
                : $"La recomendación de este estudiante ya está «{rec.Estado}»: no se sobrescribe.");

        // En la recomendación se guarda el texto con el nombre real del estudiante (es el flujo que ve el docente y recibe el estudiante).
        var nombres = Nombres(conv.AliasJson, await Lista(cursoId, ct));
        string R(string s) => Seudonimos.Mostrar(s, nombres);
        static string Corto(string s, int max) => s.Length <= max ? s : s[..(max - 1)].TrimEnd() + "…";
        var etapas = ruta.Etapas.Select(e => Corto(R($"Etapa {e.Numero} · {e.Titulo}{(e.Plazo is null ? "" : $" ({e.Plazo})")}: {e.Acciones.FirstOrDefault() ?? "Prepárate con anticipación."}"), 320)).Take(4).ToList();
        var causas = ruta.Temas.Count > 0 ? ruta.Temas.Take(3).Select(t => Corto(R($"Tema con bajo rendimiento: {t.Tema} (promedio {Textos.N(t.Promedio ?? 0)})."), 320)).ToList()
            : ["Sin temas débiles identificados con los datos disponibles."];
        var docente = ruta.AccionesDocente.Take(4).Select(a => Corto(R(a), 320)).ToList();
        if (docente.Count == 0)
            docente.Add("Hacer seguimiento del avance del estudiante.");
        if (etapas.Count == 0)
            etapas.Add(Corto(R(ruta.Situacion), 320));
        var guardada = JsonSerializer.Serialize(ruta with { Estudiante = "" }, Json);
        guardada = MostrarJson(guardada, nombres);
        var nueva = rec is null;
        rec ??= new Recomendacion { AnalisisId = analisis.Id, EstudianteAnalisisId = ea.Id, CursoId = cursoId };
        rec.Estado = EstadoRecomendacion.Borrador;
        rec.Origen = ruta.Origen;
        rec.Modelo = ruta.Modelo;
        rec.Resumen = Corto(R(ruta.Resumen), 600);
        rec.CausasJson = JsonSerializer.Serialize(causas, Json);
        rec.AccionesDocenteJson = JsonSerializer.Serialize(docente, Json);
        rec.AccionesEstudianteJson = JsonSerializer.Serialize(etapas, Json);
        rec.MensajeEstudiante = Corto(R(ruta.Mensaje), 1500);
        rec.RutaJson = guardada;
        rec.Editada = false;
        rec.MotivoRechazo = null;
        rec.ActualizadoEn = DateTime.UtcNow;
        if (nueva)
            db.Recomendaciones.Add(rec);
        else
            rec.Version++;
        await db.SaveChangesAsync(ct);
        db.Eventos.Add(new EventoRecomendacion { RecomendacionId = rec.Id, Evento = "RutaGuardada", UsuarioId = DocenteId, Detalle = "Desde el asistente" });
        await db.SaveChangesAsync(ct);
        return new GuardadoView(rec.Id, rec.Estado, nueva);
    }
}
