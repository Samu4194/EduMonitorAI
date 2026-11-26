import { useEffect, useState } from "react";
import { ArrowLeft, Check, Cloud, Cpu, Download, FileText, Printer, RefreshCw, RotateCcw, Send, X } from "lucide-react";
import {
  api,
  descargar,
  type ComunicacionSeg,
  type Curso,
  type EstadoAgente,
  type EstudianteSeg,
  type HallazgoSeg,
  type NivelAlerta,
  type RecomendacionSeg,
  type ReporteSeguimiento,
} from "./api";
import { useObject } from "./useResource";
import { ErrorBox, Modal } from "./components";
import { fmt } from "./AulaComun";
import { RutaCard, type RutaVista } from "./RutaCard";

// El servidor guarda las horas en UTC sin marcar la zona: se la agregamos para que el navegador la muestre en hora local.
export const hora = (iso: string | null | undefined) =>
  iso ? new Date(/[zZ]$|[+-]\d\d:\d\d$/.test(iso) ? iso : `${iso}Z`).toLocaleString("es-EC", { dateStyle: "medium", timeStyle: "short" }) : "—";
export const nombre = (e: { apellidos: string; nombres: string }) => `${e.apellidos} ${e.nombres}`;
const lineas = (texto: string) => texto.split("\n").map((l) => l.trim()).filter(Boolean);
const origenTexto = (r: Pick<RecomendacionSeg, "origen" | "modelo">) =>
  r.origen === "Plantilla" ? "Plantilla pedagógica (sin IA)" : r.origen === "Local" ? `IA local · ${r.modelo}` : `IA en la nube · ${r.modelo}`;

export function NivelPill({ nivel }: { nivel: NivelAlerta }) {
  return <span className={`nivel nivel-${nivel === "Sin alertas" ? "ninguno" : nivel.toLowerCase()}`}>{nivel === "Sin alertas" ? "Sin alertas" : `Riesgo ${nivel.toLowerCase()}`}</span>;
}
export function EstadoRec({ r }: { r: RecomendacionSeg | null }) {
  if (!r) return <span className="muted">—</span>;
  return (
    <span className="rec-estado">
      <span className={`rec rec-${r.estado.toLowerCase()}`}>{r.estado === "Generando" ? "Redactando…" : r.estado}</span>
      {r.estado !== "Generando" && <small>{r.origen === "Plantilla" ? "Plantilla" : r.origen === "Local" ? "IA local" : "IA nube"}</small>}
    </span>
  );
}

function Hallazgos({ lista }: { lista: HallazgoSeg[] }) {
  return (
    <ul className="hallazgos">
      {lista.map((h) => (
        <li key={h.id} className={`h-${h.nivel.toLowerCase()}`}>
          <strong>{h.titulo}</strong>
          <span>{h.detalle}</span>
        </li>
      ))}
    </ul>
  );
}

// Qué motores de redacción hay disponibles ahora. El agente siempre puede responder: si no hay IA, usa plantillas.
export function EstadoDelAgente({ estado }: { estado: EstadoAgente | null }) {
  if (!estado) return null;
  const alguno = estado.proveedores.some((p) => p.disponible);
  return (
    <div className="agente-estado" aria-label="Estado del agente académico">
      {estado.proveedores.map((p) => (
        <span key={p.clave} className={`motor ${p.disponible ? "activo" : "inactivo"}`} title={p.detalle}>
          {p.origen === "Local" ? <Cpu size={14} /> : <Cloud size={14} />}
          {p.origen === "Local" ? "IA local" : "IA en la nube"} · {p.modelo}
          <i>{p.disponible ? "disponible" : "no disponible"}</i>
        </span>
      ))}
      <span className="motor activo" title="Siempre disponible: no usa IA ni internet.">
        <FileText size={14} />
        Plantillas pedagógicas<i>respaldo</i>
      </span>
      {!alguno && (
        <p className="muted agente-nota">
          Ahora no responde ningún modelo de IA, así que las recomendaciones se redactarán con las plantillas. Para usar IA local instala Ollama y descarga el modelo, o configura la clave de Gemini (ver docs/AGENTE.md).
        </p>
      )}
    </div>
  );
}

// Revisión de una recomendación: el docente ve en qué se basó el agente, corrige el texto y decide.
export function Revision({
  est,
  cursoTexto,
  lectura,
  correoReal,
  cerrar,
  recargar,
  avisar,
}: {
  est: EstudianteSeg;
  cursoTexto: string;
  lectura: boolean;
  correoReal: boolean;
  cerrar: () => void;
  recargar: () => void;
  avisar: (texto: string) => void;
}) {
  const r = est.recomendacion;
  const desde = (x: RecomendacionSeg | null) => ({
    resumen: x?.resumen ?? "",
    causas: (x?.causas ?? []).join("\n"),
    docente: (x?.accionesDocente ?? []).join("\n"),
    estudiante: (x?.accionesEstudiante ?? []).join("\n"),
    mensaje: x?.mensajeEstudiante ?? "",
  });
  const [campos, setCampos] = useState(desde(r)),
    [motivo, setMotivo] = useState(""),
    [rechazando, setRechazando] = useState(false),
    [busy, setBusy] = useState(false),
    [error, setError] = useState("");
  // Cuando el agente termina de redactar (o regenera) se cargan los textos nuevos; mientras el docente escribe no se pisan.
  useEffect(() => {
    setCampos(desde(r));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [r?.id, r?.version, r?.estado === "Generando"]);
  const inicial = desde(r);
  const sucio = (Object.keys(inicial) as (keyof typeof inicial)[]).some((k) => inicial[k] !== campos[k]);
  const editable = !!r && r.estado === "Borrador" && !lectura;
  const cambia = (k: keyof typeof campos) => (e: { target: { value: string } }) => setCampos({ ...campos, [k]: e.target.value });

  async function ejecutar(accion: () => Promise<void>) {
    setBusy(true);
    setError("");
    try {
      await accion();
      recargar();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }
  const guardar = () =>
    api(`/seguimiento/recomendaciones/${r!.id}`, "PUT", {
      resumen: campos.resumen.trim(),
      causas: lineas(campos.causas),
      accionesDocente: lineas(campos.docente),
      accionesEstudiante: lineas(campos.estudiante),
      mensajeEstudiante: campos.mensaje.trim(),
    });
  const resultadoCorreo = (c: ComunicacionSeg | null | undefined) => {
    if (!c) return "Recomendación aprobada.";
    if (c.estado === "Error") throw new Error(c.error ?? "No se pudo enviar el correo. Puedes reintentar desde «Enviar al estudiante».");
    return c.estado === "Simulado" ? `Aprobada y registrada para ${c.destinatario} en modo simulado (el correo no salió del sistema).` : `Aprobada y enviada a ${c.destinatario}.`;
  };
  const aprobar = (comunicar: boolean) =>
    ejecutar(async () => {
      if (sucio) await guardar();
      const res = await api<{ comunicacion: ComunicacionSeg | null }>(`/seguimiento/recomendaciones/${r!.id}/aprobar`, "POST", { comunicar });
      avisar(resultadoCorreo(res.comunicacion));
      if (comunicar) cerrar();
    });
  const comunicar = () =>
    ejecutar(async () => {
      const c = await api<ComunicacionSeg>(`/seguimiento/recomendaciones/${r!.id}/comunicar`, "POST");
      avisar(resultadoCorreo(c));
      cerrar();
    });
  const rechazar = () =>
    ejecutar(async () => {
      await api(`/seguimiento/recomendaciones/${r!.id}/rechazar`, "POST", { motivo });
      avisar("Recomendación rechazada.");
      cerrar();
    });
  const regenerar = () => {
    if (r!.editada && !window.confirm("Se descartarán tus cambios y el agente redactará otra versión. ¿Continuar?")) return;
    return ejecutar(async () => {
      await api(`/seguimiento/recomendaciones/${r!.id}/regenerar`, "POST");
      avisar("El agente está redactando otra versión.");
    });
  };

  return (
    <Modal title={`Seguimiento · ${nombre(est)}`} close={cerrar} busy={busy}>
      <div className="revision">
        <div className="revision-cabecera">
          <NivelPill nivel={est.nivel} />
          <span>Resultado actual: <strong>{fmt(est.resultado)}</strong></span>
          <span>Actividades pendientes: <strong>{est.pendientes}</strong></span>
        </div>
        <h3>Qué detectaron las reglas</h3>
        {est.hallazgos.length ? <Hallazgos lista={est.hallazgos} /> : <p className="muted">Sin alertas.</p>}
        {!r && <p className="muted">Este estudiante tiene alertas de nivel bajo: no se generó recomendación. Puedes ajustar el nivel mínimo en la configuración del agente.</p>}
        {r && r.estado === "Generando" && (
          <p role="status" className="seg-progreso">
            <RefreshCw size={16} className="girando" /> El agente está redactando la recomendación…
          </p>
        )}
        {r && r.estado !== "Generando" && (
          <>
            <div className="revision-origen">
              <h3>Recomendación del agente</h3>
              <span className={`motor activo`}>{origenTexto(r)}</span>
              {r.version > 1 && <span className="muted">Versión {r.version}</span>}
              {r.editada && <span className="muted">Editada por ti</span>}
              <EstadoRec r={r} />
            </div>
            {r.estado === "Rechazada" && r.motivoRechazo && <p className="aviso">Motivo del rechazo: {r.motivoRechazo}</p>}
            <label>
              Resumen
              <textarea rows={2} value={campos.resumen} disabled={!editable || busy} onChange={cambia("resumen")} maxLength={600} />
            </label>
            <div className="revision-columnas">
              <label>
                Causas probables <small>(una por línea; son hipótesis a conversar)</small>
                <textarea rows={4} value={campos.causas} disabled={!editable || busy} onChange={cambia("causas")} />
              </label>
              <label>
                Acciones para el docente <small>(una por línea)</small>
                <textarea rows={4} value={campos.docente} disabled={!editable || busy} onChange={cambia("docente")} />
              </label>
              <label>
                Acciones para el estudiante <small>(una por línea)</small>
                <textarea rows={4} value={campos.estudiante} disabled={!editable || busy} onChange={cambia("estudiante")} />
              </label>
            </div>
            <label>
              Mensaje para el estudiante <small>(se le envía por correo si lo apruebas)</small>
              <textarea rows={5} value={campos.mensaje} disabled={!editable || busy} onChange={cambia("mensaje")} maxLength={1500} />
            </label>
            {r.ruta && <RutaCard ruta={r.ruta as RutaVista} nombre={nombre(est)} curso={cursoTexto} />}
            {r.estado === "Comunicada" && r.comunicacion && (
              <div className="aviso" role="status">
                <strong>{r.comunicacion.estado === "Simulado" ? "Registrado en modo simulado" : "Enviado"}</strong> a {r.comunicacion.destinatario} el {hora(r.comunicacion.creadoEn)}.
                {r.comunicacion.estado === "Simulado" && " El correo no salió del sistema porque no hay un servidor de correo configurado."}
                <details>
                  <summary>Ver el mensaje</summary>
                  <pre>{`Asunto: ${r.comunicacion.asunto}\n\n${r.comunicacion.cuerpo}`}</pre>
                </details>
              </div>
            )}
          </>
        )}
        <ErrorBox message={error} />
        {rechazando && (
          <label>
            Motivo del rechazo
            <input value={motivo} onChange={(e) => setMotivo(e.target.value)} maxLength={500} placeholder="Por ejemplo: no refleja la situación real del estudiante" autoFocus />
          </label>
        )}
        {r && !lectura && (
          <div className="modal-actions">
            {r.estado === "Borrador" && !rechazando && (
              <>
                <button className="secondary" disabled={busy} onClick={regenerar}>
                  <RotateCcw size={15} /> Pedir otra versión
                </button>
                <button className="danger" disabled={busy} onClick={() => setRechazando(true)}>
                  <X size={15} /> Rechazar
                </button>
                {sucio && (
                  <button className="secondary" disabled={busy} onClick={() => ejecutar(async () => { await guardar(); avisar("Cambios guardados."); })}>
                    Guardar cambios
                  </button>
                )}
                <button className="secondary" disabled={busy} onClick={() => aprobar(false)}>
                  <Check size={15} /> Aprobar
                </button>
                <button className="primary" disabled={busy} onClick={() => aprobar(true)} title={correoReal ? "Se envía por correo al estudiante" : "Sin servidor de correo: queda registrado en modo simulado"}>
                  <Send size={15} /> Aprobar y enviar
                </button>
              </>
            )}
            {r.estado === "Borrador" && rechazando && (
              <>
                <button className="secondary" disabled={busy} onClick={() => setRechazando(false)}>
                  Cancelar
                </button>
                <button className="danger" disabled={busy || motivo.trim().length < 5} onClick={rechazar}>
                  Confirmar rechazo
                </button>
              </>
            )}
            {r.estado === "Aprobada" && (
              <button className="primary" disabled={busy} onClick={comunicar}>
                <Send size={15} /> Enviar al estudiante
              </button>
            )}
            {r.estado === "Rechazada" && (
              <button className="secondary" disabled={busy} onClick={regenerar}>
                <RotateCcw size={15} /> Volver a generar
              </button>
            )}
            <button className="text-button" disabled={busy} onClick={cerrar}>
              Cerrar
            </button>
          </div>
        )}
      </div>
    </Modal>
  );
}

// HU-14. Reporte del último análisis, listo para imprimir (o guardar como PDF) y para exportar a Excel.
export function ReporteVista({ curso, volver }: { curso: Curso; volver: () => void }) {
  const reporte = useObject<ReporteSeguimiento>(`/seguimiento/cursos/${curso.id}/reporte`);
  const [error, setError] = useState("");
  const r = reporte.data;
  const enAlerta = r?.estudiantes.filter((e) => e.nivel !== "Sin alertas") ?? [];
  return (
    <>
      <div className="reporte-barra">
        <button className="text-button" onClick={volver}>
          <ArrowLeft size={16} />
          Volver al asistente
        </button>
        <div className="seg-acciones">
          <button
            className="secondary"
            disabled={!r}
            onClick={() =>
              descargar(`/seguimiento/cursos/${curso.id}/reporte.csv`, `seguimiento-${curso.materiaCodigo}-${curso.paralelo}.csv`).catch((e) => setError((e as Error).message))
            }
          >
            <Download size={16} />
            Descargar Excel (CSV)
          </button>
          <button className="primary" disabled={!r} onClick={() => window.print()}>
            <Printer size={16} />
            Imprimir / Guardar PDF
          </button>
        </div>
      </div>
      <ErrorBox message={error || reporte.error} retry={reporte.reload} />
      {reporte.loading && !r && <p role="status">Preparando el reporte…</p>}
      {r && (
        <article className="reporte-imprimible">
          <header>
            <span className="eyebrow">REPORTE DE SEGUIMIENTO ACADÉMICO</span>
            <h2>
              {r.materia} · Paralelo {r.paralelo}
            </h2>
            <p>
              {r.periodo} · Docente: {r.docente} · Análisis del {hora(r.analisis.creadoEn)} · Emitido el {hora(r.generadoEn)}
            </p>
          </header>
          <section className="reporte-resumen">
            <div><span>Estudiantes</span><strong>{r.analisis.totalEstudiantes}</strong></div>
            <div><span>Riesgo alto</span><strong>{r.analisis.alto}</strong></div>
            <div><span>Riesgo medio</span><strong>{r.analisis.medio}</strong></div>
            <div><span>Riesgo bajo</span><strong>{r.analisis.bajo}</strong></div>
            <div><span>Sin alertas</span><strong>{r.analisis.sinAlertas}</strong></div>
            <div><span>Nota mínima</span><strong>{fmt(r.analisis.notaMinima)}</strong></div>
          </section>
          <section>
            <h3>Estado de las recomendaciones</h3>
            <p>
              {r.recomendaciones.comunicada} comunicadas · {r.recomendaciones.aprobada} aprobadas sin comunicar · {r.recomendaciones.borrador} por revisar · {r.recomendaciones.rechazada} rechazadas
              {r.recomendaciones.generando > 0 ? ` · ${r.recomendaciones.generando} en redacción` : ""}.
            </p>
          </section>
          {r.hallazgosCurso.length > 0 && (
            <section>
              <h3>Alertas del curso</h3>
              <Hallazgos lista={r.hallazgosCurso} />
            </section>
          )}
          <section>
            <h3>Estudiantes con alertas ({enAlerta.length})</h3>
            {enAlerta.length === 0 && <p className="muted">Ningún estudiante presenta alertas en este análisis.</p>}
            {enAlerta.map((e) => (
              <div key={e.estudianteAnalisisId} className="reporte-estudiante">
                <div className="reporte-estudiante-cab">
                  <strong>{nombre(e)}</strong>
                  <span>Resultado {fmt(e.resultado)}</span>
                  <NivelPill nivel={e.nivel} />
                  <EstadoRec r={e.recomendacion} />
                </div>
                <ul>
                  {e.hallazgos.map((h) => (
                    <li key={h.id}>{h.detalle}</li>
                  ))}
                </ul>
                {e.recomendacion && e.recomendacion.estado !== "Generando" && (
                  <div className="reporte-rec">
                    <p>{e.recomendacion.resumen}</p>
                    <p>
                      <strong>Acciones sugeridas al docente:</strong> {e.recomendacion.accionesDocente.join(" · ")}
                    </p>
                    <small>
                      {origenTexto(e.recomendacion)}
                      {e.recomendacion.comunicacion ? ` · ${e.recomendacion.comunicacion.estado} el ${hora(e.recomendacion.comunicacion.creadoEn)}` : ""}
                    </small>
                  </div>
                )}
              </div>
            ))}
          </section>
          <footer>
            Las recomendaciones son sugerencias redactadas por el agente académico y revisadas por el docente. Las alertas se calculan con reglas sobre las calificaciones registradas al momento del análisis.
          </footer>
        </article>
      )}
    </>
  );
}
