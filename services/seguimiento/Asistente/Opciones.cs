namespace EduMonitor.Seguimiento.Asistente;

// Sección "Asistente" de appsettings.
public class OpcionesAsistente
{
    // Las conversaciones se borran solas pasados estos días desde el último mensaje.
    public int RetencionDias { get; set; } = 30;
    // Cuántos mensajes anteriores de la conversación se le entregan al modelo (ventana de contexto limitada).
    public int MensajesDeContexto { get; set; } = 6;
    public int MaxCaracteresPregunta { get; set; } = 800;
    // Máximo de rutas que se generan de una vez con «todos los estudiantes en riesgo».
    public int MaxRutasLote { get; set; } = 10;
}

// Un docente conversa con una respuesta a la vez: la IA local usa la tarjeta gráfica de la máquina y otra pregunta encima solo la haría esperar.
public sealed class LimiteDocente
{
    readonly HashSet<int> ocupados = [];
    public IDisposable? Entrar(int docenteId)
    {
        lock (ocupados)
            return ocupados.Add(docenteId) ? new Salida(this, docenteId) : null;
    }
    sealed class Salida(LimiteDocente dueño, int id) : IDisposable
    {
        public void Dispose()
        {
            lock (dueño.ocupados)
                dueño.ocupados.Remove(id);
        }
    }
}
