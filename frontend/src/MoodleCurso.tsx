import { useState } from "react";
import { RefreshCw } from "lucide-react";
import { api, type Curso } from "./api";
import { ErrorBox, Modal } from "./components";
import { confirmarSalida } from "./AulaComun";
import { useObject } from "./useResource";

export function useVinculoMoodle(cursoId: number) {
  return useObject<{ vinculado: boolean; urlCurso: string | null }>(`/moodle/cursos/${cursoId}/vinculo`);
}

export type FuenteAcademica = { fuente: "Moodle" | "Manual"; moodleConfigurado: boolean };
// De dónde salen los datos académicos: con «Moodle» nadie los edita a mano; con «Manual» el docente los registra en EduMonitor.
export function useFuenteAcademica() {
  return useObject<FuenteAcademica>("/moodle/fuente");
}

export function AvisoSinVincular() {
  return <p className="aviso" role="status">Este curso todavía no tiene datos académicos: se cargan desde Moodle. Pide al administrador que lo vincule
    con su curso de Moodle (Cursos → «Vincular Moodle»); después podrás sincronizarlo desde aquí.</p>;
}

// Selector del administrador. Moodle es la fuente normal; «Manual» queda para desarrollo, pruebas o cursos sin Moodle.
export function FuenteAcademicaAdmin() {
  const f = useFuenteAcademica();
  const [busy, setBusy] = useState(false), [error, setError] = useState("");
  async function cambiar(fuente: "Moodle" | "Manual") {
    if (fuente === f.data?.fuente) return;
    if (fuente === "Manual" && !window.confirm("En modo Manual los docentes registran y editan unidades, actividades y notas dentro de EduMonitor. Úsalo solo para desarrollo, pruebas o cursos sin Moodle. ¿Continuar?")) return;
    setBusy(true);
    setError("");
    try { await api("/moodle/fuente", "PUT", { fuente }); f.reload(); }
    catch (e) { setError((e as Error).message); }
    finally { setBusy(false); }
  }
  if (!f.data) return <ErrorBox message={f.error} retry={f.reload} />;
  return <section className="panel acolchado fuente-academica" aria-label="Fuente de los datos académicos">
    <div>
      <h3>Fuente de los datos académicos</h3>
      <p className="muted small">
        {f.data.fuente === "Moodle"
          ? "Moodle es la fuente: las unidades, actividades y notas se leen de Moodle y nadie las edita a mano. EduMonitor calcula el rendimiento, las alertas y las recomendaciones."
          : "Modo manual: los docentes registran y editan unidades, actividades y notas en EduMonitor. Úsalo solo para desarrollo, pruebas o cursos sin Moodle."}
        {!f.data.moodleConfigurado && f.data.fuente === "Moodle" && " Atención: todavía no hay un token de Moodle configurado."}
      </p>
    </div>
    <div className="chips-filtro" role="group" aria-label="Fuente">
      {(["Moodle", "Manual"] as const).map((x) => (
        <button key={x} className={f.data!.fuente === x ? "activo" : ""} aria-pressed={f.data!.fuente === x} disabled={busy} onClick={() => void cambiar(x)}>{x}</button>
      ))}
    </div>
    <ErrorBox message={error} />
  </section>;
}

export function AvisoMoodle({ url }: { url: string }) {
  return <p className="aviso">Este curso es de solo lectura para los datos académicos. El contenido, las actividades,
    las calificaciones y las ponderaciones se administran desde Moodle.{" "}
    <a href={url} target="_blank" rel="noreferrer">Abrir curso en Moodle</a>
  </p>;
}

interface Resumen {
  creados: number;
  actualizados: number;
  omitidos: number;
  eliminados?: number;
  conflictos: string[];
  avisos?: string[];
}
interface Ficha {
  codigo: string;
  nombres: string;
  apellidos: string;
  cedula: string | null;
  vincularExistente?: boolean;
  // Ya existe un estudiante con el correo de Moodle: la cédula viene sugerida y solo hay que confirmarla.
  sugerida?: boolean;
  // La cédula viene del «Número de ID» del usuario en Moodle.
  deMoodle?: boolean;
}
const conSugerencias = (lista: Ficha[]) => lista.map(f => f.sugerida ? { ...f, vincularExistente: true } : f);
function Resultado({ valor }: { valor: Resumen | null }) {
  return valor && <div role="status" className="aviso">
    Creados: {valor.creados} · Actualizados: {valor.actualizados} · Omitidos: {valor.omitidos}
    {valor.conflictos.length > 0 && <><h4>Conflictos</h4><ul>
      {valor.conflictos.map((texto) => <li key={texto}>{texto}</li>)}
    </ul></>}
    {(valor.avisos?.length ?? 0) > 0 && <><h4>Avisos de la sincronización</h4><ul>
      {valor.avisos!.map((texto) => <li key={texto}>{texto}</li>)}
    </ul></>}
  </div>;
}

// Identificador estándar del curso en Moodle (su «Número de ID»): materia + año y meses del período + paralelo.
// Ejemplo: Física A de «Agosto - Diciembre 2026» → SOF-101-2026AD-A. Es único porque cada curso es una combinación única de esas tres cosas.
export function codigoMoodle(curso: Curso): string {
  const periodo = curso.periodo ?? "";
  const anio = periodo.match(/\d{4}/)?.[0] ?? "";
  const meses = (periodo.match(/[A-Za-zÁÉÍÓÚÑáéíóúñ]{3,}/g) ?? []).map((m) => m[0].toUpperCase()).join("").slice(0, 4);
  return [curso.materiaCodigo, `${anio}${meses}`, curso.paralelo].filter(Boolean).join("-");
}

export function VinculoMoodle({ curso, close }: { curso: Curso; close: () => void }) {
  const sugerido = codigoMoodle(curso);
  const [codigo, setCodigo] = useState(sugerido);
  const [otro, setOtro] = useState(false);
  const [copiado, setCopiado] = useState(false);
  const [fichas, setFichas] = useState<Ficha[]>([]);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [resultado, setResultado] = useState<Resumen | null>(null);
  const ruta = `/estudiantes/moodle/${curso.id}`;
  const vinculo = useVinculoMoodle(curso.id);
  async function ejecutar(accion: () => Promise<void>) {
    setBusy(true);
    setError("");
    setResultado(null);
    try { await accion(); } catch (e) { setError((e as Error).message); }
    finally { setBusy(false); }
  }
  function cambiar(indice: number, edicion: Partial<Ficha>) {
    setFichas(fichas.map((f, i) => i === indice ? { ...f, ...edicion } : f));
  }
  return <Modal title={`Moodle · ${curso.materiaNombre} · ${curso.paralelo}`} close={close} busy={busy}>
    <p>{curso.carreraNombre} · {curso.materiaCodigo} · {curso.periodo}</p>
    {vinculo.data?.urlCurso && <p className="aviso" role="status">Este curso ya está vinculado con Moodle. {" "}
      <a href={vinculo.data.urlCurso} target="_blank" rel="noreferrer">Abrir curso en Moodle</a></p>}
    <ErrorBox message={vinculo.error} retry={vinculo.reload} />
    <form onSubmit={e => { e.preventDefault(); void ejecutar(async () => {
      setFichas([]);
      await api(`/moodle/cursos/${curso.id}/vinculo`, "PUT", { codigo: codigo.trim() });
      vinculo.reload();
      setFichas(conSugerencias(await api<Ficha[]>(ruta)));
    }); }}>
      <label>Identificador del curso en Moodle
        <input required maxLength={100} value={codigo} readOnly={!otro} aria-readonly={!otro}
          onChange={e => { setCodigo(e.target.value); setFichas([]); setResultado(null); }} />
      </label>
      <p className="muted small">
        Lo genera el sistema y no se repite. Al crear el curso en Moodle, escríbelo en el campo
        «Número de ID del curso» (ID number); así EduMonitor lo encuentra.
      </p>
      <div className="enlaces-fila">
        <button type="button" className="text-button" onClick={() => {
          void navigator.clipboard?.writeText(codigo).then(() => { setCopiado(true); setTimeout(() => setCopiado(false), 2500); }, () => undefined);
        }}>{copiado ? "Copiado" : "Copiar código"}</button>
        <button type="button" className="text-button" onClick={() => {
          if (otro) setCodigo(sugerido);
          setOtro(!otro); setFichas([]); setResultado(null);
        }}>{otro ? "Usar el código generado" : "Usar otro código (el curso ya existe en Moodle con otro ID)"}</button>
      </div>
      <div className="modal-actions">
        <button className="secondary" type="button" disabled={busy || !vinculo.data?.vinculado}
          title={vinculo.data?.vinculado ? "Vuelve a leer los estudiantes inscritos en Moodle" : "Primero vincula el curso"}
          onClick={() => void ejecutar(async () => {
            setFichas([]); setFichas(conSugerencias(await api<Ficha[]>(ruta)));
          })}>Revisar matrícula de Moodle</button>
        <button className="primary" disabled={busy}>Vincular curso</button>
      </div>
      <p className="muted small">«Revisar matrícula» sirve cuando ya está vinculado y se inscribieron estudiantes nuevos en Moodle: los lista para confirmar su cédula y crearlos en EduMonitor.</p>
    </form>
    {fichas.length > 0 && <form onSubmit={e => { e.preventDefault(); void ejecutar(async () => {
      setResultado(await api<Resumen>(ruta, "POST", { personas: fichas.map(({codigo, cedula, vincularExistente}) =>
        ({codigo, cedula, vincularExistente: !!vincularExistente})) }));
    }); }}>
      <h3>Correspondencias de estudiantes</h3>
      <p>Confirma la cédula de cada participante. Viene ya puesta cuando el «Número de ID» del usuario en Moodle es una cédula válida o cuando ya existe un estudiante con su correo: solo revísala.
        Marca la casilla si autorizas asociarlo a una ficha local que ya existe.</p>
      {fichas.map((f, i) => <fieldset key={f.codigo}>
        <legend>{f.nombres} {f.apellidos} · {f.codigo}{f.deMoodle ? <span className="muted"> · cédula tomada de Moodle{f.sugerida ? " (ya existe una ficha con ella)" : ""}</span>
          : f.sugerida && <span className="muted"> · sugerida por correo</span>}</legend>
        <label>Cédula<input required pattern="[0-9]{10}" maxLength={10} inputMode="numeric"
          value={f.cedula ?? ""} onChange={e => cambiar(i, { cedula: e.target.value })} /></label>
        <label><input type="checkbox" checked={!!f.vincularExistente}
          onChange={e => cambiar(i, { vincularExistente: e.target.checked })} /> Asociar a ficha existente</label>
      </fieldset>)}
      <div className="modal-actions"><button className="primary" disabled={busy}>Confirmar matrícula</button></div>
    </form>}
    {busy && <p role="status">Consultando el curso…</p>}
    <ErrorBox message={error} /><Resultado valor={resultado} />
  </Modal>;
}

// Barra única del curso vinculado: dice de dónde salen los datos y ofrece el botón principal para traerlos.
// El resultado es una línea breve; los avisos y conflictos van plegados para no saturar la pantalla.
export function SincronizarMoodle({ cursoId, url, actualizado }: { cursoId: number; url?: string | null; actualizado: () => void }) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [resultado, setResultado] = useState<Resumen | null>(null);
  async function sincronizar() {
    if (!confirmarSalida()) return;
    setBusy(true); setError(""); setResultado(null);
    try {
      setResultado(await api<Resumen>(`/moodle/cursos/${cursoId}/sincronizar`, "POST"));
      actualizado();
    } catch (e) { setError((e as Error).message); }
    finally { setBusy(false); }
  }
  const detalles = resultado ? [...resultado.conflictos.map((t) => ({ t, tipo: "Conflicto" })), ...(resultado.avisos ?? []).map((t) => ({ t, tipo: "Aviso" }))] : [];
  return <div className="moodle-barra-contenedor">
    <section className="moodle-barra" aria-label="Datos desde Moodle">
      <div>
        <strong>Datos desde Moodle</strong>
        <p className="muted small">
          Las unidades, actividades y notas se editan en Moodle; aquí solo se leen.
          {url && <> <a href={url} target="_blank" rel="noreferrer">Abrir curso en Moodle</a></>}
        </p>
      </div>
      <button className="primary moodle-sync" disabled={busy} onClick={sincronizar}>
        <RefreshCw size={18} className={busy ? "girando" : ""} aria-hidden="true" />
        {busy ? "Sincronizando…" : "Sincronizar con Moodle"}
      </button>
    </section>
    <ErrorBox message={error} />
    {resultado && <div className="moodle-resultado" role="status">
      <p>✓ Sincronizado: <strong>{resultado.creados}</strong> nuevos · <strong>{resultado.actualizados}</strong> actualizados · <strong>{resultado.omitidos}</strong> sin cambios o sin nota todavía
        {(resultado.eliminados ?? 0) > 0 && <> · <strong>{resultado.eliminados}</strong> retirados (ya no existen en Moodle)</>}</p>
      {detalles.length > 0 && <details>
        <summary>{detalles.length === 1 ? "Ver 1 aviso" : `Ver ${detalles.length} avisos`}</summary>
        <ul>{detalles.map((d) => <li key={d.t}>{d.t}</li>)}</ul>
      </details>}
    </div>}
  </div>;
}
