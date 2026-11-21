import { useState, type FormEvent } from "react";
import { Plus, Pencil, Trash2, PenLine } from "lucide-react";
import { api, type Actividad, type Curso, type TipoActividad } from "./api";
import { useResource } from "./useResource";
import { Modal, ErrorBox, Empty, Toast } from "./components";
import { fecha } from "./AulaComun";

const tipos: TipoActividad[] = ["Tarea", "APE", "Examen"];
const nombreTipo: Record<TipoActividad, string> = { Tarea: "Tarea", APE: "APE (práctica de experimentación)", Examen: "Examen" };

// Pestaña «Actividades» del curso: tareas, APE y exámenes (HU-06). Cada tipo pesa su porcentaje en la nota final.
// Desde aquí se pasa a calificar cada actividad.
export function ActividadesTab({
  curso,
  lectura,
  estudiantes,
  calificar,
  cambio,
}: {
  curso: Curso;
  lectura: boolean;
  // Estudiantes del curso, para decir cuántas notas faltan en cada actividad (null mientras carga).
  estudiantes: number | null;
  calificar: (actividadId: number) => void;
  cambio: () => void;
}) {
  const [tipo, setTipo] = useState(""),
    [edicion, setEdicion] = useState<Actividad | "nueva" | null>(null),
    [notice, setNotice] = useState(""),
    [error, setError] = useState("");
  const actividades = useResource<Actividad>(`/actividades?cursoId=${curso.id}`);
  const visibles = actividades.data.filter((a) => !tipo || a.tipo === tipo);

  async function eliminar(a: Actividad) {
    const aviso = a.calificadas ? ` Se perderán las ${a.calificadas} calificaciones registradas.` : "";
    if (!window.confirm(`¿Eliminar «${a.nombre}»?${aviso}`)) return;
    setNotice("");
    setError("");
    try {
      await api(`/actividades/${a.id}`, "DELETE");
      setNotice("Actividad eliminada.");
      actividades.reload();
      cambio();
    } catch (e) {
      setError((e as Error).message);
    }
  }
  return (
    <>
      <Toast message={notice} close={() => setNotice("")} />
      <div className="toolbar">
        <div className="chips-filtro" role="group" aria-label="Filtrar por tipo">
          {["", ...tipos].map((t) => (
            <button key={t} className={tipo === t ? "activo" : ""} aria-pressed={tipo === t} onClick={() => setTipo(t)}>
              {t === "" ? "Todas" : t === "Tarea" ? "Tareas" : t === "Examen" ? "Exámenes" : t}
              <span>{actividades.data.filter((a) => !t || a.tipo === t).length}</span>
            </button>
          ))}
        </div>
        {!lectura && (
          <button className="primary" onClick={() => setEdicion("nueva")}>
            <Plus size={18} />
            Nueva actividad
          </button>
        )}
      </div>
      <ErrorBox message={error} />
      <ErrorBox message={actividades.error} retry={actividades.reload} />
      {!actividades.loading && !actividades.error && !visibles.length && (
        <Empty text={actividades.data.length ? "No hay actividades de este tipo." : "Este curso todavía no tiene actividades."} />
      )}
      {visibles.length > 0 && (
        <section className="panel table-wrap">
          <table className="compacta actividades">
            <thead>
              <tr>
                <th>Actividad</th>
                <th>Tipo</th>
                <th>Entrega</th>
                <th>Calificación</th>
                <th aria-label="Acciones" />
              </tr>
            </thead>
            <tbody>
              {visibles.map((a) => {
                const completa = estudiantes !== null && estudiantes > 0 && a.calificadas >= estudiantes;
                return (
                  <tr key={a.id}>
                    <td>
                      <strong>{a.nombre}</strong>
                      {a.descripcion && <small className="block muted">{a.descripcion}</small>}
                    </td>
                    <td>
                      <span className={`pill tipo-${a.tipo.toLowerCase()}`}>{a.tipo}</span>
                    </td>
                    <td>{fecha(a.fechaEntrega)}</td>
                    <td>
                      <span className={`estado ${completa ? "ok" : "pend"}`}>
                        {estudiantes === null ? `${a.calificadas} calificadas` : `${Math.min(a.calificadas, estudiantes)} de ${estudiantes} calificadas`}
                      </span>
                    </td>
                    <td>
                      <span className="row-actions">
                        <button className="text-button" onClick={() => calificar(a.id)}>
                          <PenLine size={15} />
                          {lectura ? "Ver notas" : "Calificar"}
                        </button>
                        {!lectura && (
                          <>
                            <button className="icon" title="Editar" aria-label={`Editar ${a.nombre}`} onClick={() => setEdicion(a)}>
                              <Pencil size={16} />
                            </button>
                            <button className="icon peligro" title="Eliminar" aria-label={`Eliminar ${a.nombre}`} onClick={() => void eliminar(a)}>
                              <Trash2 size={16} />
                            </button>
                          </>
                        )}
                      </span>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </section>
      )}
      {edicion && (
        <FormularioActividad
          cursoId={curso.id}
          actividad={edicion === "nueva" ? null : edicion}
          close={() => setEdicion(null)}
          saved={(texto) => {
            setEdicion(null);
            setNotice(texto);
            actividades.reload();
            cambio();
          }}
        />
      )}
    </>
  );
}

function FormularioActividad({
  cursoId,
  actividad,
  close,
  saved,
}: {
  cursoId: number;
  actividad: Actividad | null;
  close: () => void;
  saved: (texto: string) => void;
}) {
  const [nombre, setNombre] = useState(actividad?.nombre ?? ""),
    [tipo, setTipo] = useState<TipoActividad>(actividad?.tipo ?? "Tarea"),
    [descripcion, setDescripcion] = useState(actividad?.descripcion ?? ""),
    [entrega, setEntrega] = useState(actividad?.fechaEntrega ?? ""),
    [busy, setBusy] = useState(false),
    [error, setError] = useState("");
  async function submit(e: FormEvent) {
    e.preventDefault();
    setError("");
    setBusy(true);
    const cuerpo = { nombre, tipo, descripcion: descripcion || null, fechaEntrega: entrega || null };
    try {
      if (actividad) await api(`/actividades/${actividad.id}`, "PUT", cuerpo);
      else await api("/actividades", "POST", { cursoId, ...cuerpo });
      saved(actividad ? "Actividad actualizada." : "Actividad creada. Ya puedes calificarla.");
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }
  return (
    <Modal title={actividad ? "Editar actividad" : "Nueva actividad"} close={close} busy={busy}>
      <form onSubmit={submit}>
        <label>
          Nombre
          <input required minLength={3} maxLength={120} value={nombre} onChange={(e) => setNombre(e.target.value)} />
        </label>
        <div className="two-cols">
          <label>
            Tipo
            <select value={tipo} onChange={(e) => setTipo(e.target.value as TipoActividad)}>
              {tipos.map((t) => (
                <option key={t} value={t}>
                  {nombreTipo[t]}
                </option>
              ))}
            </select>
          </label>
          <label>
            Fecha de entrega (opcional)
            <input type="date" value={entrega} onChange={(e) => setEntrega(e.target.value)} />
          </label>
        </div>
        <label>
          Indicaciones (opcional)
          <textarea rows={2} maxLength={500} value={descripcion} onChange={(e) => setDescripcion(e.target.value)} />
        </label>
        <ErrorBox message={error} />
        <div className="modal-actions">
          <button className="secondary" type="button" disabled={busy} onClick={close}>
            Cancelar
          </button>
          <button className="primary" disabled={busy}>
            {busy ? "Guardando…" : "Guardar"}
          </button>
        </div>
      </form>
    </Modal>
  );
}
