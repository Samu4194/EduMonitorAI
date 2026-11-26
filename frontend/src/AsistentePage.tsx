import { useEffect, useRef, useState, type FormEvent } from "react";
import { Bot, PanelLeftClose, PanelLeftOpen, Plus, Trash2, Send, Square, ThumbsUp, ThumbsDown, UserSearch, X, Save, Info, TriangleAlert } from "lucide-react";
import {
  api,
  apiStream,
  type ConversacionDetalle,
  type ConversacionResumen,
  type EstadoAgente,
  type PersonaAsist,
  type PreguntaPre,
  type RutaMejora,
  type TarjetaAsistente,
} from "./api";
import { useObject, useResource } from "./useResource";
import { ErrorBox, Toast } from "./components";
import { RutaCard, origenTexto } from "./RutaCard";
import { EstadoDelAgente, ReporteVista, Revision, nombre } from "./SeguimientoPage";
import { AlertasPanel } from "./AlertasPanel";
import { type Curso, type EstudianteSeg, type PanelSeguimiento } from "./api";

// Mensaje tal como se muestra en pantalla (los de la base y los que llegan en vivo se convierten a esta forma).
type Msg = {
  clave: string;
  id?: number;
  rol: "docente" | "asistente";
  texto: string;
  tarjetas: TarjetaAsistente[];
  origen?: string | null;
  modelo?: string | null;
  alcance?: string | null;
  avisos?: string[];
  util?: boolean | null;
  escribiendo?: boolean;
  redactando?: boolean;
  estudianteId?: number;
  error?: boolean;
  guardada?: boolean;
};
type Pedido = { conversacionId?: number; texto?: string; preguntaId?: string; estudianteIds?: number[]; ignorarDudas?: boolean };
type Duda = { pedido: Pedido; pregunta: string; grupos: PersonaAsist[][]; elegidos: number[] };

const base = (cursoId: number) => `/seguimiento/cursos/${cursoId}/asistente`;
let contador = 0;
const nueva = () => `m${++contador}`;

// Texto de la respuesta: párrafos, listas con guion y **negritas**. Se arma con elementos de React (nunca como HTML) para no abrir inyecciones.
function Negritas({ t }: { t: string }) {
  return (
    <>
      {t.split(/(\*\*[^*]+\*\*)/g).map((p, i) => (p.startsWith("**") && p.endsWith("**") && p.length > 4 ? <strong key={i}>{p.slice(2, -2)}</strong> : <span key={i}>{p}</span>))}
    </>
  );
}
function Texto({ texto }: { texto: string }) {
  const bloques: { tipo: "p" | "ul" | "ol" | "h"; lineas: string[] }[] = [];
  for (const linea of texto.split("\n")) {
    if (!linea.trim()) continue;
    // Título de sección: «## Panorama», «**Panorama**» o «**Panorama:**» solo en su línea.
    const titulo = /^\s*#{1,4}\s+(.*)$/.exec(linea) ?? /^\s*\*\*([^*]+?)\*\*:?\s*$/.exec(linea);
    if (titulo) {
      bloques.push({ tipo: "h", lineas: [titulo[1].replace(/:$/, "")] });
      continue;
    }
    const vineta = /^\s*[-*•]\s+(.*)$/.exec(linea);
    const numero = /^\s*\d+[.)]\s+(.*)$/.exec(linea);
    const tipo = vineta ? "ul" : numero ? "ol" : "p";
    const contenido = vineta?.[1] ?? numero?.[1] ?? linea;
    const ultimo = bloques[bloques.length - 1];
    // Respaldo: un párrafo de 3 o más frases se parte en viñetas, una por frase, para que no se lea todo junto.
    const frases = tipo === "p" ? contenido.split(/(?<=[.!?])\s+(?=[A-ZÁÉÍÓÚÑ¿¡*])/) : [];
    if (frases.length >= 3) bloques.push({ tipo: "ul", lineas: frases });
    else if (tipo !== "p" && ultimo?.tipo === tipo) ultimo.lineas.push(contenido);
    else bloques.push({ tipo, lineas: [contenido] });
  }
  return (
    <>
      {bloques.map((b, i) =>
        b.tipo === "h" ? (
          <h5 key={i}>
            <Negritas t={b.lineas[0]} />
          </h5>
        ) : b.tipo === "p" ? (
          <p key={i}>
            <Negritas t={b.lineas[0]} />
          </p>
        ) : b.tipo === "ul" ? (
          <ul key={i}>
            {b.lineas.map((l, j) => (
              <li key={j}>
                <Negritas t={l} />
              </li>
            ))}
          </ul>
        ) : (
          <ol key={i}>
            {b.lineas.map((l, j) => (
              <li key={j}>
                <Negritas t={l} />
              </li>
            ))}
          </ol>
        ),
      )}
    </>
  );
}

function Tabla({ t }: { t: Extract<TarjetaAsistente, { tipo: "tabla" }> }) {
  return (
    <section className="asistente-tabla table-wrap" aria-label={t.titulo}>
      <h4>{t.titulo}</h4>
      <table className="compacta">
        <thead>
          <tr>
            {t.columnas.map((c) => (
              <th key={c}>{c}</th>
            ))}
          </tr>
        </thead>
        <tbody>
          {t.filas.map((f, i) => (
            <tr key={i}>
              {f.map((c, j) => (
                <td key={j}>{c}</td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
      {t.notas.map((n) => (
        <small className="muted block" key={n}>
          {n}
        </small>
      ))}
    </section>
  );
}

// Buscador de estudiante por nombre (sin importar tildes ni mayúsculas). El correo ayuda a distinguir a dos homónimos.
function BuscadorEstudiante({ cursoId, elegir, cancelar }: { cursoId: number; elegir: (p: PersonaAsist) => void; cancelar?: () => void }) {
  const [q, setQ] = useState(""),
    [lista, setLista] = useState<PersonaAsist[]>([]),
    [buscando, setBuscando] = useState(false);
  useEffect(() => {
    if (q.trim().length < 2) {
      setLista([]);
      return;
    }
    setBuscando(true);
    const t = setTimeout(() => {
      api<PersonaAsist[]>(`${base(cursoId)}/estudiantes?q=${encodeURIComponent(q.trim())}`)
        .then(setLista)
        .catch(() => setLista([]))
        .finally(() => setBuscando(false));
    }, 250);
    return () => clearTimeout(t);
  }, [q, cursoId]);
  return (
    <div className="buscador">
      <div className="buscador-campo">
        <UserSearch size={16} aria-hidden />
        <input autoFocus placeholder="Nombre o apellido del estudiante" value={q} onChange={(e) => setQ(e.target.value)} aria-label="Buscar estudiante" />
        {cancelar && (
          <button type="button" className="icon" aria-label="Cerrar búsqueda" onClick={cancelar}>
            <X size={16} />
          </button>
        )}
      </div>
      {q.trim().length >= 2 && !buscando && lista.length === 0 && <p className="muted small">Ningún estudiante coincide. Prueba con el apellido o con el nombre completo.</p>}
      {lista.length > 0 && (
        <ul className="buscador-lista" role="listbox">
          {lista.map((p) => (
            <li key={p.id}>
              <button type="button" role="option" aria-selected={false} onClick={() => elegir(p)}>
                <strong>{p.nombre}</strong>
                <small className="muted">{p.correo}</small>
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

export function AsistenteTab({ curso }: { curso: Curso }) {
  const cursoId = curso.id;
  const conversaciones = useResource<ConversacionResumen>(`${base(cursoId)}/conversaciones`);
  const sugerencias = useResource<PreguntaPre>(`${base(cursoId)}/sugerencias`);
  // Alertas y recomendaciones del curso (antes una pestaña aparte): viven en el asistente.
  const panel = useObject<PanelSeguimiento>(`/seguimiento/cursos/${cursoId}`);
  const [lateral, setLateral] = useState<"conversaciones" | "alertas">("conversaciones");
  const [revisando, setRevisando] = useState<number | null>(null);
  const [verReporte, setVerReporte] = useState(false);
  // El panel lateral se puede ocultar para darle todo el ancho a la conversación.
  const [panelVisible, setPanelVisible] = useState(true);
  const [estado, setEstado] = useState<EstadoAgente | null>(null);
  const [convId, setConvId] = useState<number | null>(null);
  const [titulo, setTitulo] = useState("");
  const [venceEn, setVenceEn] = useState<string | null>(null);
  const [mensajes, setMensajes] = useState<Msg[]>([]);
  const [texto, setTexto] = useState("");
  const [elegidos, setElegidos] = useState<PersonaAsist[]>([]);
  const [eligiendo, setEligiendo] = useState(false);
  const [pendiente, setPendiente] = useState<PreguntaPre | null>(null);
  const [duda, setDuda] = useState<Duda | null>(null);
  const [enviando, setEnviando] = useState(false);
  const [error, aviso] = useState("");
  const [notice, setNotice] = useState("");
  const abortar = useRef<AbortController | null>(null);
  const caja = useRef<HTMLDivElement>(null);
  // Mientras el usuario esté cerca del final de la conversación, esta sigue a la respuesta; si sube a leer, no se le arrastra de vuelta.
  const alFinal = useRef(true);
  const lectura = curso.periodoEstado === "Finalizado";

  useEffect(() => {
    api<EstadoAgente>("/seguimiento/agente/estado").then(setEstado).catch(() => setEstado(null));
    // Carga el modelo local en segundo plano: así la primera pregunta no espera el arranque en frío.
    api(`${base(cursoId)}/calentar`, "POST").catch(() => undefined);
    return () => abortar.current?.abort();
  }, [cursoId]);
  useEffect(() => {
    const c = caja.current;
    if (c && alFinal.current) c.scrollTop = c.scrollHeight;
  }, [mensajes, duda]);
  const alDesplazar = () => {
    const c = caja.current;
    if (c) alFinal.current = c.scrollHeight - c.scrollTop - c.clientHeight < 80;
  };

  function nuevaConversacion() {
    abortar.current?.abort();
    setConvId(null);
    setTitulo("");
    setVenceEn(null);
    setMensajes([]);
    setDuda(null);
    setPendiente(null);
    setElegidos([]);
    aviso("");
  }
  async function abrir(id: number) {
    abortar.current?.abort();
    setDuda(null);
    setPendiente(null);
    aviso("");
    try {
      const d = await api<ConversacionDetalle>(`${base(cursoId)}/conversaciones/${id}`);
      alFinal.current = true;
      setConvId(d.id);
      setTitulo(d.titulo);
      setVenceEn(d.venceEn);
      setMensajes(
        d.mensajes.map((m) => ({
          clave: nueva(),
          id: m.id,
          rol: m.rol,
          texto: m.texto,
          tarjetas: m.tarjetas,
          origen: m.origen,
          modelo: m.modelo,
          alcance: m.alcance,
          avisos: m.verificada === false ? ["Alguna cifra de esta respuesta no coincidía con los datos del curso."] : [],
          util: m.util,
          estudianteId: m.tarjetas.find((t) => t.tipo === "ruta") ? d.alias.find((a) => a.nombre === (m.tarjetas.find((t) => t.tipo === "ruta") as RutaMejora).estudiante)?.estudianteId : undefined,
        })),
      );
    } catch (e) {
      aviso((e as Error).message);
      conversaciones.reload();
    }
  }
  async function borrar(c: ConversacionResumen) {
    if (!window.confirm(`¿Borrar la conversación «${c.titulo}»? No se puede recuperar.`)) return;
    try {
      await api(`${base(cursoId)}/conversaciones/${c.id}`, "DELETE");
      if (c.id === convId) nuevaConversacion();
      conversaciones.reload();
    } catch (e) {
      aviso((e as Error).message);
    }
  }

  async function enviar(pedido: Pedido, visible: string) {
    if (enviando) return;
    aviso("");
    setDuda(null);
    alFinal.current = true;
    const completo: Pedido = { ...pedido, conversacionId: pedido.conversacionId ?? convId ?? undefined };
    const docente: Msg = { clave: nueva(), rol: "docente", texto: visible, tarjetas: [] };
    let actual: string | null = null; // clave del mensaje del asistente que se está armando
    setMensajes((m) => [...m, docente]);
    setEnviando(true);
    const control = new AbortController();
    abortar.current = control;
    const cambiar = (clave: string, f: (m: Msg) => Msg) => setMensajes((lista) => lista.map((m) => (m.clave === clave ? f(m) : m)));
    const asegurar = () => {
      if (actual) return actual;
      const m: Msg = { clave: nueva(), rol: "asistente", texto: "", tarjetas: [], escribiendo: true };
      actual = m.clave;
      setMensajes((lista) => [...lista, m]);
      return m.clave;
    };
    try {
      await apiStream(
        `${base(cursoId)}/mensajes`,
        completo,
        (tipo, d) => {
          if (tipo === "inicio") {
            setConvId(d.conversacionId);
            setTitulo(d.titulo);
            cambiar(docente.clave, (m) => ({ ...m, id: d.mensaje.id, texto: d.mensaje.texto }));
          } else if (tipo === "aclaracion") {
            setMensajes((lista) => lista.filter((m) => m.clave !== docente.clave));
            setDuda({ pedido: completo, pregunta: d.pregunta, grupos: d.grupos, elegidos: [] });
          } else if (tipo === "tarjeta") {
            const c = asegurar();
            cambiar(c, (m) => ({ ...m, tarjetas: [...m.tarjetas, d.tarjeta] }));
          } else if (tipo === "texto") {
            const c = asegurar();
            cambiar(c, (m) => ({ ...m, texto: m.texto + d.delta }));
          } else if (tipo === "ruta") {
            setMensajes((lista) => {
              const existe = lista.some((m) => m.id === d.mensajeId);
              const nuevo: Msg = { clave: nueva(), id: d.mensajeId, rol: "asistente", texto: "", tarjetas: [d.ruta], origen: d.ruta.origen, modelo: d.ruta.modelo, redactando: d.redactando, estudianteId: d.estudianteId };
              return existe ? lista.map((m) => (m.id === d.mensajeId ? { ...m, tarjetas: [d.ruta], origen: d.ruta.origen, modelo: d.ruta.modelo, redactando: d.redactando } : m)) : [...lista, nuevo];
            });
          } else if (tipo === "fin") {
            if (actual) cambiar(actual, (m) => ({ ...m, id: d.mensajeId, origen: d.origen, modelo: d.modelo, alcance: d.alcance, avisos: d.avisos ?? [], escribiendo: false }));
          } else if (tipo === "error") {
            setMensajes((lista) => [...lista, { clave: nueva(), rol: "asistente", texto: d.mensaje, tarjetas: [], error: true }]);
          }
        },
        control.signal,
      );
    } catch (e) {
      if ((e as Error).name !== "AbortError") aviso((e as Error).message);
    } finally {
      setMensajes((lista) => lista.map((m) => (m.escribiendo || m.redactando ? { ...m, escribiendo: false, redactando: false } : m)));
      setEnviando(false);
      abortar.current = null;
      conversaciones.reload();
    }
  }

  const sinEstudiante = (p: PreguntaPre) => p.requiereEstudiante;
  function usarSugerencia(p: PreguntaPre) {
    if (sinEstudiante(p)) {
      setPendiente(p);
      return;
    }
    void enviar({ preguntaId: p.id }, p.texto);
  }
  function enviarConEstudiante(p: PreguntaPre, e: PersonaAsist) {
    setPendiente(null);
    void enviar({ preguntaId: p.id, estudianteIds: [e.id] }, p.texto.replace("[estudiante]", e.nombre));
  }
  function enviarLibre(ev?: FormEvent) {
    ev?.preventDefault();
    const t = texto.trim();
    if (!t || enviando) return;
    setTexto("");
    const ids = elegidos.map((e) => e.id);
    setElegidos([]);
    void enviar({ texto: t, estudianteIds: ids.length ? ids : undefined }, t);
  }
  function resolverDuda(ignorar: boolean) {
    if (!duda) return;
    const { pedido, elegidos: ids } = duda;
    setDuda(null);
    void enviar(
      { ...pedido, estudianteIds: [...(pedido.estudianteIds ?? []), ...ids], ignorarDudas: ignorar || undefined },
      pedido.texto ?? sugerencias.data.find((s) => s.id === pedido.preguntaId)?.texto ?? "",
    );
  }
  async function valorar(m: Msg, util: boolean) {
    if (!m.id) return;
    try {
      await api(`${base(cursoId)}/mensajes/${m.id}/valoracion`, "POST", { util });
      setMensajes((l) => l.map((x) => (x.clave === m.clave ? { ...x, util } : x)));
    } catch (e) {
      aviso((e as Error).message);
    }
  }
  // Guarda de una vez todas las rutas generadas en el último lote («todos los estudiantes en riesgo»).
  async function guardarTodas(lista: Msg[]) {
    aviso("");
    let ok = 0;
    const fallas: string[] = [];
    for (const m of lista) {
      if (!m.id) continue;
      try {
        await api(`${base(cursoId)}/mensajes/${m.id}/guardar-ruta`, "POST");
        ok++;
        setMensajes((l) => l.map((x) => (x.clave === m.clave ? { ...x, guardada: true } : x)));
      } catch (e) {
        fallas.push((e as Error).message);
      }
    }
    if (ok > 0) setNotice(`${ok} ${ok === 1 ? "ruta guardada" : "rutas guardadas"} como recomendaciones en borrador.`);
    if (fallas.length > 0) aviso([...new Set(fallas)].join(" "));
  }
  // Guarda la ruta como recomendación (si aún no lo está) y abre su revisión: ahí el docente edita, aprueba y la envía al estudiante.
  async function revisarRuta(m: Msg) {
    if (!m.id) return;
    aviso("");
    try {
      const guardar = () => api<{ nueva: boolean }>(`${base(cursoId)}/mensajes/${m.id}/guardar-ruta`, "POST");
      if (!m.guardada) {
        try {
          await guardar();
        } catch (e) {
          // Las alertas aún no existen o el estudiante no estaba en ellas: se calculan (son reglas, sin IA) y se reintenta una vez.
          if (!/alertas|análisis/i.test((e as Error).message)) throw e;
          await api(`/seguimiento/cursos/${cursoId}/analisis?soloAlertas=true`, "POST");
          await guardar();
        }
        setMensajes((l) => l.map((x) => (x.clave === m.clave ? { ...x, guardada: true } : x)));
      }
      const actual = await api<PanelSeguimiento>(`/seguimiento/cursos/${cursoId}`);
      panel.reload();
      const est = actual.estudiantes.find((x) => x.estudianteId === m.estudianteId);
      if (est) setRevisando(est.estudianteAnalisisId);
      else setNotice("Ruta guardada como recomendación. Búscala en el panel «Alertas».");
    } catch (e) {
      aviso((e as Error).message);
    }
  }
  // Desde el panel de alertas: pide al asistente la ruta de mejora de ese estudiante.
  function pedirRuta(e: EstudianteSeg) {
    const pre = sugerencias.data.find((s) => s.id === "ruta");
    void enviar({ preguntaId: "ruta", estudianteIds: [e.estudianteId] }, (pre?.texto ?? "Ruta de mejora de [estudiante]").replace("[estudiante]", nombre(e)));
  }

  if (verReporte) return <ReporteVista curso={curso} volver={() => setVerReporte(false)} />;
  const abierto = panel.data?.estudiantes.find((e) => e.estudianteAnalisisId === revisando) ?? null;
  const grupos = [...new Set(sugerencias.data.map((s) => s.grupo))];
  const vacio = mensajes.length === 0 && !duda;
  const listaSugerencias = grupos.map((g) => (
    <div key={g} className="sugerencia-grupo">
      <strong>{g}</strong>
      <div className="sugerencia-chips">
        {sugerencias.data
          .filter((s) => s.grupo === g)
          .map((s) => (
            <button key={s.id} className="chip-sugerencia" disabled={enviando} onClick={() => usarSugerencia(s)}>
              {s.texto.replace("[estudiante]", "un estudiante…")}
            </button>
          ))}
      </div>
    </div>
  ));
  return (
    <div className={`asistente${panelVisible ? "" : " sin-lateral"}`}>
      <Toast message={notice} close={() => setNotice("")} />
      {abierto && (
        <Revision
          key={abierto.estudianteAnalisisId}
          est={abierto}
          cursoTexto={`${curso.materiaNombre} · ${curso.paralelo}`}
          lectura={!panel.data || panel.data.soloLectura}
          correoReal={!!estado?.correoReal}
          cerrar={() => setRevisando(null)}
          recargar={panel.reload}
          avisar={setNotice}
        />
      )}
      <div className="asistente-lateral" role="complementary" aria-label="Conversaciones">
        <button className="primary" onClick={nuevaConversacion} disabled={enviando && false}>
          <Plus size={16} />
          Nueva conversación
        </button>
        <div className="lateral-vistas" role="group" aria-label="Qué mostrar">
          <button className={lateral === "conversaciones" ? "activo" : ""} aria-pressed={lateral === "conversaciones"} onClick={() => setLateral("conversaciones")}>
            Conversaciones
          </button>
          <button className={lateral === "alertas" ? "activo" : ""} aria-pressed={lateral === "alertas"} onClick={() => setLateral("alertas")}>
            Alertas
            {(panel.data?.analisis?.alto ?? 0) + (panel.data?.analisis?.medio ?? 0) > 0 && <span>{(panel.data?.analisis?.alto ?? 0) + (panel.data?.analisis?.medio ?? 0)}</span>}
          </button>
        </div>
        {lateral === "alertas" ? (
          <AlertasPanel curso={curso} panel={panel} pedirRuta={pedirRuta} revisar={setRevisando} verReporte={() => setVerReporte(true)} />
        ) : (
          <>
        <ul className="conversaciones-lista">
          {conversaciones.data.map((c) => (
            <li key={c.id} className={c.id === convId ? "activa" : ""}>
              <button className="conversacion-abrir" onClick={() => void abrir(c.id)} title={c.titulo}>
                <span>{c.titulo}</span>
                <small className="muted">
                  {new Date(c.actualizadoEn).toLocaleDateString("es-EC", { day: "numeric", month: "short" })} · se borra el{" "}
                  {new Date(c.venceEn).toLocaleDateString("es-EC", { day: "numeric", month: "short" })}
                </small>
              </button>
              <button className="icon peligro" aria-label={`Borrar conversación ${c.titulo}`} onClick={() => void borrar(c)}>
                <Trash2 size={15} />
              </button>
            </li>
          ))}
        </ul>
        {!conversaciones.data.length && !conversaciones.loading && <p className="muted small">Aún no hay conversaciones.</p>}
          </>
        )}
        <details className="asistente-nota">
          <summary>
            <Info size={13} aria-hidden /> Privacidad del historial
          </summary>
          <p className="muted small">
            El historial es temporal: cada conversación se borra sola a los 30 días de su último mensaje. En ella los estudiantes se guardan como «Estudiante A, B…», nunca con
            su nombre.
          </p>
        </details>
        {estado && (
          <details className="asistente-nota">
            <summary>
              <Bot size={13} aria-hidden /> Estado de la IA
            </summary>
            <EstadoDelAgente estado={estado} />
          </details>
        )}
      </div>

      <section className="asistente-chat" aria-label="Chat con el asistente académico">
        <div className="asistente-cabecera">
          <button
            type="button"
            className="icon alternar-lateral"
            title={panelVisible ? "Ocultar el panel lateral" : "Mostrar el panel lateral (conversaciones y alertas)"}
            aria-label={panelVisible ? "Ocultar el panel lateral" : "Mostrar el panel lateral"}
            aria-pressed={!panelVisible}
            onClick={() => setPanelVisible((v) => !v)}
          >
            {panelVisible ? <PanelLeftClose size={18} /> : <PanelLeftOpen size={18} />}
          </button>
          <div>
            <span className="eyebrow">ASISTENTE ACADÉMICO</span>
            <h2>{titulo || "Pregunta sobre tu curso"}</h2>
            {venceEn && <small className="muted">Esta conversación se borrará el {new Date(venceEn).toLocaleDateString("es-EC", { day: "numeric", month: "long" })}.</small>}
          </div>
        </div>
        <ErrorBox message={error || sugerencias.error} />

        <div className="asistente-mensajes" aria-live="polite" ref={caja} onScroll={alDesplazar}>
          {vacio && (
            <div className="asistente-bienvenida">
              <Bot size={28} aria-hidden />
              <p>
                Pregúntame cómo va tu curso, qué temas reforzar o cómo apoyar a un estudiante. Los números salen de tus notas actuales; yo solo los explico. Puedes
                escribir lo que quieras o elegir una pregunta sugerida.
              </p>
              <div className="bienvenida-sugerencias">{listaSugerencias}</div>
            </div>
          )}
          {mensajes.map((m) => (
            <div key={m.clave} className={`burbuja rol-${m.rol}${m.error ? " error" : ""}`}>
              {m.rol === "asistente" && m.alcance === "general" && (
                <span className="etiqueta-general">
                  <Info size={13} aria-hidden /> Respuesta general, sin datos del curso
                </span>
              )}
              {m.tarjetas.map((t, i) =>
                t.tipo === "tabla" ? (
                  <Tabla key={i} t={t} />
                ) : (
                  <RutaCard
                    key={i}
                    ruta={t}
                    nombre={t.estudiante}
                    curso={`${curso.materiaNombre} · ${curso.paralelo}`}
                    redactando={m.redactando}
                    acciones={
                      !lectura && (
                        <button className="primary" type="button" disabled={m.redactando || !m.id} onClick={() => void revisarRuta(m)}>
                          <Send size={15} />
                          {m.guardada ? "Revisar y enviar" : "Guardar y revisar para enviar"}
                        </button>
                      )
                    }
                  />
                ),
              )}
              {m.texto && !m.tarjetas.some((t) => t.tipo === "ruta") && (
                <div className="burbuja-texto">
                  <Texto texto={m.texto} />
                </div>
              )}
              {m.escribiendo && !m.texto && (
                <p className="escribiendo" role="status">
                  Pensando… la primera respuesta puede tardar unos segundos.
                </p>
              )}
              {m.escribiendo && m.texto && <span className="cursor" aria-hidden />}
              {m.avisos?.map((a) => (
                <p className="aviso-cifras" key={a}>
                  <TriangleAlert size={14} aria-hidden /> {a}
                </p>
              ))}
              {m.rol === "asistente" && !m.escribiendo && !m.error && m.id && (
                <div className="burbuja-pie">
                  <small className="muted">{origenTexto(m.origen, m.modelo)}</small>
                  <span className="valorar">
                    <button className={`icon${m.util === true ? " activo" : ""}`} aria-label="Respuesta útil" aria-pressed={m.util === true} onClick={() => void valorar(m, true)}>
                      <ThumbsUp size={15} />
                    </button>
                    <button className={`icon${m.util === false ? " activo" : ""}`} aria-label="Respuesta poco útil" aria-pressed={m.util === false} onClick={() => void valorar(m, false)}>
                      <ThumbsDown size={15} />
                    </button>
                  </span>
                </div>
              )}
            </div>
          ))}
          {!enviando && !lectura && mensajes.filter((m) => m.tarjetas.some((t) => t.tipo === "ruta") && !m.guardada && !m.redactando).length >= 2 && (
            <div className="lote-guardar">
              <p>
                Hay {mensajes.filter((m) => m.tarjetas.some((t) => t.tipo === "ruta") && !m.guardada).length} rutas sin guardar. Guárdalas como recomendaciones en borrador para
                revisarlas, aprobarlas y enviarlas desde el panel «Alertas».
              </p>
              <button className="primary" onClick={() => void guardarTodas(mensajes.filter((m) => m.tarjetas.some((t) => t.tipo === "ruta") && !m.guardada))}>
                <Save size={15} />
                Guardar todas como recomendaciones
              </button>
            </div>
          )}
          {duda && (
            <div className="burbuja rol-asistente aclaracion" role="group" aria-label="Aclaración">
              <p>
                <strong>{duda.pregunta}</strong>
              </p>
              {duda.grupos.map((g, gi) => (
                <fieldset key={gi}>
                  {g.map((p) => (
                    <label key={p.id} className="check">
                      <input
                        type="checkbox"
                        checked={duda.elegidos.includes(p.id)}
                        onChange={() => setDuda({ ...duda, elegidos: duda.elegidos.includes(p.id) ? duda.elegidos.filter((x) => x !== p.id) : [...duda.elegidos, p.id] })}
                      />
                      <span>
                        {p.nombre} <small className="muted">· {p.correo}</small>
                      </span>
                    </label>
                  ))}
                </fieldset>
              ))}
              <div className="modal-actions">
                <button className="secondary" onClick={() => resolverDuda(true)}>
                  Ninguno de ellos, continuar
                </button>
                <button className="primary" disabled={duda.elegidos.length === 0} onClick={() => resolverDuda(false)}>
                  Continuar con los elegidos
                </button>
              </div>
            </div>
          )}
        </div>

        <div className="asistente-sugerencias">
          {!vacio && (
            <details>
              <summary>Preguntas sugeridas</summary>
              {listaSugerencias}
            </details>
          )}
          {pendiente && (
            <div className="sugerencia-estudiante">
              <p>
                <strong>{pendiente.texto.replace("[estudiante]", "…")}</strong> · ¿de qué estudiante?
              </p>
              <BuscadorEstudiante cursoId={cursoId} elegir={(e) => enviarConEstudiante(pendiente, e)} cancelar={() => setPendiente(null)} />
            </div>
          )}
        </div>

        <form className="asistente-compositor" onSubmit={enviarLibre}>
          {elegidos.length > 0 && (
            <div className="elegidos">
              <small className="muted">Pregunta sobre:</small>
              {elegidos.map((e) => (
                <span className="chip" key={e.id}>
                  {e.nombre}
                  <button type="button" className="chip-x" aria-label={`Quitar a ${e.nombre}`} onClick={() => setElegidos(elegidos.filter((x) => x.id !== e.id))}>
                    <X size={12} />
                  </button>
                </span>
              ))}
            </div>
          )}
          {eligiendo && (
            <BuscadorEstudiante
              cursoId={cursoId}
              elegir={(p) => {
                setElegidos((l) => (l.some((x) => x.id === p.id) ? l : [...l, p]));
                setEligiendo(false);
              }}
              cancelar={() => setEligiendo(false)}
            />
          )}
          <textarea
            rows={2}
            maxLength={800}
            value={texto}
            placeholder="Escribe tu pregunta. Puedes nombrar a un estudiante por su nombre."
            aria-label="Tu pregunta"
            onChange={(e) => setTexto(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === "Enter" && !e.shiftKey) {
                e.preventDefault();
                enviarLibre();
              }
            }}
          />
          <div className="compositor-barra">
            <button type="button" className="secondary" title="Añadir un estudiante a la pregunta" onClick={() => setEligiendo((v) => !v)}>
              <UserSearch size={16} />
              Estudiante
            </button>
            <small className="muted compositor-ayuda">
              {texto.length}/800 · Enter envía, Shift+Enter agrega una línea
              {lectura && " · Período finalizado: puedes consultar, pero no guardar recomendaciones."}
            </small>
            {enviando ? (
              <button type="button" className="danger" onClick={() => abortar.current?.abort()}>
                <Square size={15} />
                Detener
              </button>
            ) : (
              <button className="primary" disabled={!texto.trim()}>
                <Send size={15} />
                Enviar
              </button>
            )}
          </div>
        </form>
      </section>
    </div>
  );
}
