import { useState } from "react";
import { api, type Curso } from "./api";
import { ErrorBox, Modal } from "./components";
import { confirmarSalida } from "./AulaComun";
import { useObject } from "./useResource";

export function useVinculoMoodle(cursoId: number) {
  return useObject<{ vinculado: boolean; urlCurso: string | null }>(`/moodle/cursos/${cursoId}/vinculo`);
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
  conflictos: string[];
}
interface Ficha {
  codigo: string;
  nombres: string;
  apellidos: string;
  cedula: string | null;
  vincularExistente?: boolean;
}
function Resultado({ valor }: { valor: Resumen | null }) {
  return valor && <div role="status" className="aviso">
    Creados: {valor.creados} · Actualizados: {valor.actualizados} · Omitidos: {valor.omitidos}
    {valor.conflictos.length > 0 && <><h4>Conflictos</h4><ul>
      {valor.conflictos.map((texto) => <li key={texto}>{texto}</li>)}
    </ul></>}
  </div>;
}

export function VinculoMoodle({ curso, close }: { curso: Curso; close: () => void }) {
  const [codigo, setCodigo] = useState("");
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
    {vinculo.data?.urlCurso && <AvisoMoodle url={vinculo.data.urlCurso} />}
    <ErrorBox message={vinculo.error} retry={vinculo.reload} />
    <form onSubmit={e => { e.preventDefault(); void ejecutar(async () => {
      setFichas([]);
      await api(`/moodle/cursos/${curso.id}/vinculo`, "PUT", { codigo: codigo.trim() });
      vinculo.reload();
      setFichas(await api<Ficha[]>(ruta));
    }); }}>
      <label>Identificador del curso en Moodle
        <input required maxLength={100} placeholder="SOF-101-2026AD-A" value={codigo}
          onChange={e => { setCodigo(e.target.value); setFichas([]); setResultado(null); }} />
      </label>
      <div className="modal-actions">
        <button className="secondary" type="button" disabled={busy} onClick={() => void ejecutar(async () => {
          setFichas([]); setFichas(await api<Ficha[]>(ruta));
        })}>Consultar matrícula vinculada</button>
        <button className="primary" disabled={busy}>Vincular curso</button>
      </div>
    </form>
    {fichas.length > 0 && <form onSubmit={e => { e.preventDefault(); void ejecutar(async () => {
      setResultado(await api<Resumen>(ruta, "POST", { personas: fichas.map(({codigo, cedula, vincularExistente}) =>
        ({codigo, cedula, vincularExistente: !!vincularExistente})) }));
    }); }}>
      <h3>Correspondencias de estudiantes</h3>
      <p>Confirma la cédula de cada participante. Marca la casilla si autorizas asociarlo a una ficha local que ya existe.</p>
      {fichas.map((f, i) => <fieldset key={f.codigo}>
        <legend>{f.nombres} {f.apellidos} · {f.codigo}</legend>
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

export function SincronizarMoodle({ cursoId, actualizado }: { cursoId: number; actualizado: () => void }) {
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
  return <div><button className="secondary" disabled={busy} onClick={sincronizar}>
    {busy ? "Sincronizando…" : "Sincronizar con Moodle"}
  </button><ErrorBox message={error} /><Resultado valor={resultado} /></div>;
}
