import { Download } from "lucide-react";
import { type RutaMejora } from "./api";
import { fmt } from "./AulaComun";

export type RutaVista = Omit<RutaMejora, "estudiante"> & { estudiante?: string };

const estados: Record<string, { texto: string; clase: string }> = {
  Aprobado: { texto: "Ya aprobó", clase: "nivel-ninguno" },
  Asegurado: { texto: "Aprobación asegurada", clase: "nivel-ninguno" },
  Alcanzable: { texto: "Alcanzable", clase: "nivel-ninguno" },
  Exigente: { texto: "Exigente", clase: "nivel-medio" },
  MuyExigente: { texto: "Muy exigente", clase: "nivel-alto" },
  Imposible: { texto: "Fuera de alcance: recuperación", clase: "nivel-alto" },
  SinPendientes: { texto: "Sin actividades pendientes", clase: "nivel-alto" },
  SinNotas: { texto: "Sin notas todavía", clase: "nivel-bajo" },
};

export const origenTexto = (origen: string | null | undefined, modelo?: string | null) =>
  origen === "Local" ? `IA local${modelo ? ` · ${modelo}` : ""}` : origen === "Nube" ? `IA en la nube${modelo ? ` · ${modelo}` : ""}` : "Plantillas pedagógicas";

const esc = (s: string) => s.replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" })[c]!);
const fecha = (iso: string | null) => {
  if (!iso) return "";
  const [a, m, d] = iso.split("-").map(Number);
  return `${d}/${m}/${a}`;
};

// «Descargar PDF»: abre la ruta como documento aparte y el navegador la guarda como PDF (Imprimir -> Guardar como PDF).
export function imprimirRuta(ruta: RutaVista, estudiante: string, curso: string) {
  const w = window.open("", "_blank");
  if (!w) {
    window.alert("El navegador bloqueó la ventana del documento. Permite las ventanas emergentes para este sitio y vuelve a intentarlo.");
    return;
  }
  const etapas = ruta.etapas
    .map(
      (e) => `<section class="etapa"><h3>Etapa ${e.numero} · ${esc(e.titulo)}</h3>
        <p class="meta">${e.plazo ? esc(e.plazo) : ""}${e.plazo && e.meta !== null ? " · " : ""}${e.meta !== null ? `nota objetivo: <strong>${fmt(e.meta)}</strong>` : ""}</p>
        ${e.actividades.length ? `<p class="menor">Actividades: ${e.actividades.map((a) => esc(`${a.nombre} (${a.tipo}${a.fecha ? `, ${fecha(a.fecha)}` : ""})`)).join("; ")}</p>` : ""}
        ${e.temas.length ? `<p class="menor">Temas que evalúa: ${e.temas.map((t) => esc(e.debiles?.includes(t) ? `${t} (te cuesta)` : t)).join(", ")}</p>` : ""}
        <ul>${e.acciones.map((a) => `<li>${esc(a)}</li>`).join("")}</ul></section>`,
    )
    .join("");
  const temas = ruta.temas.length
    ? `<h2>Temas a reforzar</h2><ul>${ruta.temas.map((t) => `<li><strong>${esc(t.tema)}</strong>${t.promedio !== null ? ` · promedio ${fmt(t.promedio)}` : ""}${t.evidencia.length ? ` (${esc(t.evidencia.join(", "))})` : ""}</li>`).join("")}</ul>`
    : "";
  w.document.write(`<!doctype html><html lang="es"><head><meta charset="utf-8"><title>Ruta de mejora · ${esc(estudiante)}</title>
    <style>
      body{font-family:Georgia,'Times New Roman',serif;color:#1c2430;margin:36px auto;max-width:760px;line-height:1.5;padding:0 16px}
      h1{font-size:24px;margin:0 0 4px;border-bottom:3px solid #b8892b;padding-bottom:8px} h2{font-size:17px;margin:22px 0 6px} h3{font-size:15px;margin:0 0 2px}
      .sub{color:#5a6472;margin:6px 0 16px;font-size:13px} .etapa{border:1px solid #cfd6e0;border-radius:6px;padding:10px 14px;margin:10px 0;break-inside:avoid}
      .meta{margin:2px 0 6px;color:#5a6472;font-size:13px} .menor{margin:2px 0;font-size:13px;color:#3b4552} ul{margin:6px 0 0;padding-left:20px}
      .cifras{display:flex;gap:16px;flex-wrap:wrap;font-size:13px;margin:8px 0} .cifras div{border:1px solid #cfd6e0;border-radius:4px;padding:6px 10px}
      .mensaje{background:#f4f1ea;border-left:4px solid #b8892b;padding:10px 14px;margin:14px 0;border-radius:0 6px 6px 0}
      footer{margin-top:28px;font-size:11px;color:#6b7684;border-top:1px solid #cfd6e0;padding-top:8px}
    </style></head><body>
    <h1>Ruta de mejora académica</h1>
    <p class="sub">${esc(estudiante)} · ${esc(curso)} · ${new Date().toLocaleDateString("es-EC", { day: "numeric", month: "long", year: "numeric" })}</p>
    <p>${esc(ruta.situacion)}</p>
    <div class="cifras"><div>Resultado actual: <strong>${fmt(ruta.resultado)}</strong> / 10</div><div>Nota mínima: <strong>${fmt(ruta.notaMinima)}</strong></div>
    ${ruta.promedioRequerido !== null && ruta.estado !== "Imposible" ? `<div>Promedio necesario en lo que falta: <strong>${fmt(ruta.promedioRequerido)}</strong></div>` : ""}</div>
    ${temas}<h2>Tu ruta</h2>${etapas || "<p>No hay actividades pendientes: conversa con tu docente sobre las opciones disponibles.</p>"}
    <div class="mensaje">${esc(ruta.mensaje)}</div>
    <footer>Documento generado por EduMonitor AI. Las notas objetivo son una referencia calculada con tus notas actuales; el docente revisa y valida esta ruta.</footer>
    </body></html>`);
  w.document.close();
  w.focus();
  setTimeout(() => w.print(), 350);
}

// Ruta de mejora de un estudiante: lo numérico lo calcula el sistema y las acciones de estudio las redacta la IA (o las plantillas).
export function RutaCard({ ruta, nombre, curso, redactando, acciones }: { ruta: RutaVista; nombre: string; curso: string; redactando?: boolean; acciones?: React.ReactNode }) {
  const estado = estados[ruta.estado] ?? { texto: ruta.estado, clase: "nivel-bajo" };
  return (
    <article className="ruta" aria-label={`Ruta de mejora de ${nombre}`}>
      <div className="ruta-cab">
        <div>
          <span className="eyebrow">RUTA DE MEJORA</span>
          <h3>{nombre}</h3>
        </div>
        <span className={`nivel ${estado.clase}`}>{estado.texto}</span>
      </div>
      <p className="ruta-situacion">{ruta.situacion}</p>
      {ruta.origen !== "Plantilla" && ruta.resumen && <p className="ruta-resumen">{ruta.resumen}</p>}
      <dl className="ruta-cifras">
        <div>
          <dt>Resultado actual</dt>
          <dd>{fmt(ruta.resultado)}</dd>
        </div>
        <div>
          <dt>Nota mínima</dt>
          <dd>{fmt(ruta.notaMinima)}</dd>
        </div>
        {ruta.promedioRequerido !== null && ruta.estado !== "Imposible" && ruta.pendientes > 0 && (
          <div>
            <dt>Promedio necesario</dt>
            <dd>{fmt(ruta.promedioRequerido)}</dd>
          </div>
        )}
        {ruta.maximoPosible !== null && (
          <div>
            <dt>Máximo posible</dt>
            <dd>{fmt(ruta.maximoPosible)}</dd>
          </div>
        )}
        <div>
          <dt>Actividades pendientes</dt>
          <dd>{ruta.pendientes}</dd>
        </div>
      </dl>
      {ruta.temas.length > 0 && (
        <section>
          <h4>Temas a reforzar</h4>
          <ul className="ruta-temas">
            {ruta.temas.map((t) => (
              <li key={t.tema}>
                <strong>{t.tema}</strong>
                {t.promedio !== null && <span> · promedio {fmt(t.promedio)}</span>}
                {t.evidencia.length > 0 && <small className="muted"> (notas bajas en {t.evidencia.join(", ")})</small>}
              </li>
            ))}
          </ul>
        </section>
      )}
      {ruta.etapas.length > 0 && (
        <ol className="ruta-etapas">
          {ruta.etapas.map((e) => (
            <li key={e.numero}>
              <div className="ruta-etapa-cab">
                <strong>
                  Etapa {e.numero} · {e.titulo}
                </strong>
                <span className="muted">
                  {e.plazo}
                  {e.plazo && e.meta !== null ? " · " : ""}
                  {e.meta !== null && <b className="meta-nota">nota objetivo {fmt(e.meta)}</b>}
                </span>
              </div>
              {e.actividades.length > 0 && (
                <small className="muted block">
                  {e.actividades.map((a) => `${a.nombre} (${a.tipo}, ${a.aporte}% de la nota final)`).join(" · ")}
                </small>
              )}
              {e.temas.length > 0 && <small className="block">Temas que evalúa: {e.temas.map((t) => (e.debiles?.includes(t) ? `${t} (te cuesta)` : t)).join(", ")}</small>}
              <ul>
                {e.acciones.map((a) => (
                  <li key={a}>{a}</li>
                ))}
              </ul>
            </li>
          ))}
        </ol>
      )}
      {redactando && (
        <p className="ruta-redactando" role="status">
          Redactando las acciones de estudio con IA…
        </p>
      )}
      {ruta.accionesDocente.length > 0 && (
        <details>
          <summary>Para el docente</summary>
          <ul>
            {ruta.accionesDocente.map((a) => (
              <li key={a}>{a}</li>
            ))}
          </ul>
        </details>
      )}
      <details>
        <summary>Mensaje para el estudiante</summary>
        <p>{ruta.mensaje}</p>
      </details>
      <div className="ruta-pie">
        <small className="muted">Redacción: {origenTexto(ruta.origen, ruta.modelo)}</small>
        <span className="ruta-acciones">
          <button className="secondary" type="button" onClick={() => imprimirRuta(ruta, nombre, curso)}>
            <Download size={15} />
            Descargar PDF
          </button>
          {acciones}
        </span>
      </div>
    </article>
  );
}
