import { useEffect, useRef, useState } from "react";
import { FileText, Mail, RefreshCw, Sparkles } from "lucide-react";
import { api, type ComunicacionSeg, type Curso, type EstudianteSeg, type PanelSeguimiento } from "./api";
import { useResource } from "./useResource";
import { ErrorBox } from "./components";
import { fmt } from "./AulaComun";
import { EstadoRec, NivelPill, hora, nombre } from "./SeguimientoPage";

export type PanelDatos = { data: PanelSeguimiento | null; loading: boolean; error: string; reload: () => void };

// Alertas del curso dentro del asistente: quién está en riesgo (reglas sobre las notas, sin IA) y el estado de su recomendación.
// Desde aquí se le pide al asistente la ruta de mejora de un estudiante o se abre la revisión (aprobar, editar, enviar).
export function AlertasPanel({
  curso,
  panel,
  pedirRuta,
  revisar,
  verReporte,
}: {
  curso: Curso;
  panel: PanelDatos;
  pedirRuta: (e: EstudianteSeg) => void;
  revisar: (estudianteAnalisisId: number) => void;
  verReporte: () => void;
}) {
  const p = panel.data,
    a = p?.analisis ?? null;
  const [filtro, setFiltro] = useState<"alertas" | "todos">("alertas"),
    [busy, setBusy] = useState(false),
    [error, setError] = useState("");
  const intentado = useRef(false);
  const lectura = !p || p.soloLectura;
  const generando = a?.estado === "Generando" || !!p?.estudiantes.some((e) => e.recomendacion?.estado === "Generando");
  const comunicadas = p?.estudiantes.filter((e) => e.recomendacion?.estado === "Comunicada").length ?? 0;
  const comunicaciones = useResource<ComunicacionSeg>(a ? `/seguimiento/cursos/${curso.id}/comunicaciones?n=${comunicadas}` : null);

  async function actualizar() {
    setBusy(true);
    setError("");
    try {
      await api(`/seguimiento/cursos/${curso.id}/analisis?soloAlertas=true`, "POST");
      panel.reload();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }
  // Primera vez: las alertas se calculan solas (son reglas, no usan IA), para que el docente las vea sin pedirlas.
  useEffect(() => {
    if (p && !a && !lectura && !intentado.current) {
      intentado.current = true;
      void actualizar();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [p, a, lectura]);
  // Si quedaron recomendaciones redactándose (un análisis completo anterior), se consulta el avance.
  useEffect(() => {
    if (!generando) return;
    const t = setInterval(panel.reload, 2500);
    return () => clearInterval(t);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [generando]);

  const todos = p?.estudiantes ?? [];
  const conAlerta = todos.filter((e) => e.nivel !== "Sin alertas");
  const lista = (filtro === "alertas" ? conAlerta : todos).slice().sort((x, y) => orden(x) - orden(y) || (x.resultado ?? 11) - (y.resultado ?? 11));
  const porRevisar = todos.filter((e) => e.recomendacion?.estado === "Borrador").length;

  return (
    <div className="alertas-panel">
      <div className="alertas-cab">
        <strong>Estudiantes en alerta</strong>
        <button type="button" className="icon" title="Recalcular las alertas con las notas actuales" aria-label="Recalcular alertas" disabled={busy || generando || lectura} onClick={() => void actualizar()}>
          <RefreshCw size={15} className={busy ? "girando" : ""} />
        </button>
      </div>
      <ErrorBox message={error || panel.error} retry={panel.reload} />
      {panel.loading && !p && <p className="muted small" role="status">Cargando alertas…</p>}
      {p && !a && !busy && (
        <p className="muted small">
          {lectura ? "Todavía no hay alertas calculadas para este curso." : "Calculando las alertas…"}
        </p>
      )}
      {generando && (
        <p className="muted small" role="status">
          <RefreshCw size={13} className="girando" /> Redactando recomendaciones: {a?.generadas ?? 0} de {a?.aRecomendar ?? 0}
        </p>
      )}
      {a && (
        <>
          <div className="alertas-cifras" aria-label="Resumen de alertas">
            <span className={a.alto > 0 ? "riesgo" : ""}><strong>{a.alto}</strong> altas</span>
            <span className={a.medio > 0 ? "atencion" : ""}><strong>{a.medio}</strong> medias</span>
            <span><strong>{a.bajo}</strong> bajas</span>
            <span className={porRevisar > 0 ? "atencion" : ""}><strong>{porRevisar}</strong> por revisar</span>
          </div>
          <p className="muted small alertas-fecha">Notas del {hora(a.creadoEn)} · mínima {fmt(a.notaMinima)}</p>
          <div className="chips-filtro" role="group" aria-label="Qué estudiantes mostrar">
            <button className={filtro === "alertas" ? "activo" : ""} aria-pressed={filtro === "alertas"} onClick={() => setFiltro("alertas")}>
              Con alertas<span>{conAlerta.length}</span>
            </button>
            <button className={filtro === "todos" ? "activo" : ""} aria-pressed={filtro === "todos"} onClick={() => setFiltro("todos")}>
              Todos<span>{todos.length}</span>
            </button>
          </div>
          <ul className="alertas-lista">
            {lista.map((e) => (
              <li key={e.estudianteAnalisisId} className="alerta-tarjeta">
                <div className="alerta-linea">
                  <strong>{nombre(e)}</strong>
                  <span className="alerta-nota">{fmt(e.resultado)}</span>
                </div>
                <div className="alerta-linea">
                  <NivelPill nivel={e.nivel} />
                  {e.recomendacion && <EstadoRec r={e.recomendacion} />}
                </div>
                {e.hallazgos[0] && (
                  <small className="muted" title={e.hallazgos[0].detalle}>
                    {e.hallazgos[0].titulo}
                    {e.hallazgos.length > 1 ? ` · +${e.hallazgos.length - 1}` : ""}
                  </small>
                )}
                <div className="alerta-acciones">
                  <button type="button" className="secondary" onClick={() => pedirRuta(e)} title="Pide al asistente la ruta de mejora de este estudiante">
                    <Sparkles size={14} /> Recomendar
                  </button>
                  {e.recomendacion && (
                    <button type="button" className="text-button" onClick={() => revisar(e.estudianteAnalisisId)}>
                      {e.recomendacion.estado === "Borrador" && !lectura ? "Revisar" : "Ver"}
                    </button>
                  )}
                </div>
              </li>
            ))}
            {lista.length === 0 && <li className="muted small">{filtro === "alertas" ? "Ningún estudiante tiene alertas." : "No hay estudiantes."}</li>}
          </ul>
          <button type="button" className="secondary alertas-reporte" onClick={verReporte}>
            <FileText size={15} /> Ver reporte del curso
          </button>
          <details className="asistente-nota">
            <summary>
              <Mail size={13} aria-hidden /> Comunicaciones enviadas ({comunicaciones.data.length})
            </summary>
            {comunicaciones.data.length === 0 ? (
              <p className="muted small">Todavía no se ha enviado ninguna recomendación a los estudiantes.</p>
            ) : (
              <ul className="comunicaciones">
                {comunicaciones.data.map((c) => (
                  <li key={c.id}>
                    <span>
                      <strong>{c.estudiante}</strong>
                    </span>
                    <span className="muted">{hora(c.creadoEn)}</span>
                    <span className={`rec rec-${c.estado === "Error" ? "rechazada" : c.estado === "Simulado" ? "aprobada" : "comunicada"}`} title={c.estado === "Simulado" ? "El correo no se envió: el sistema no tiene un servidor de correo configurado." : undefined}>
                      {c.estado}
                    </span>
                  </li>
                ))}
              </ul>
            )}
          </details>
        </>
      )}
    </div>
  );
}

// Primero los de mayor riesgo.
const orden = (e: EstudianteSeg) => (e.nivel === "Alto" ? 0 : e.nivel === "Medio" ? 1 : e.nivel === "Bajo" ? 2 : 3);
