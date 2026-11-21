import { useState, type FormEvent } from "react";
import { Plus, Pencil, Trash2, ChevronUp, ChevronDown, Paperclip, Download, Upload } from "lucide-react";
import { api, descargar, type ArchivoTema, type Curso, type Unidad, type Tema, type ContenidoMinimo } from "./api";
import { useResource } from "./useResource";
import { Modal, ErrorBox, Empty, Toast } from "./components";

// Lo que se está creando o editando en el formulario emergente.
type Edicion =
  | { tipo: "unidad"; unidad?: Unidad }
  | { tipo: "tema"; unidad: Unidad; tema?: Tema }
  | { tipo: "contenido"; tema: Tema; contenido?: ContenidoMinimo };

// Pestaña «Contenido» del curso: unidades, temas, contenidos mínimos y material de apoyo (HU-05).
export function ContenidoTab({ curso, lectura }: { curso: Curso; lectura: boolean }) {
  const unidades = useResource<Unidad>(`/unidades?cursoId=${curso.id}`);
  const [edicion, setEdicion] = useState<Edicion | null>(null),
    [notice, setNotice] = useState(""),
    [error, setError] = useState("");

  async function accion(texto: string, run: () => Promise<unknown>) {
    setNotice("");
    setError("");
    try {
      await run();
      if (texto) setNotice(texto);
      unidades.reload();
    } catch (e) {
      setError((e as Error).message);
    }
  }
  const borrar = (pregunta: string, ruta: string, hecho: string) => {
    if (window.confirm(pregunta)) void accion(hecho, () => api(ruta, "DELETE"));
  };
  // Material de apoyo: archivos ligeros (hasta 5 MB) que acompañan al tema.
  const subir = (tema: Tema, archivo: File | undefined) => {
    if (!archivo) return;
    if (archivo.size > 5 * 1024 * 1024) {
      setNotice("");
      setError(`«${archivo.name}» pesa más de 5 MB. Sube material ligero.`);
      return;
    }
    const form = new FormData();
    form.append("archivo", archivo);
    void accion(`«${archivo.name}» se subió a ${tema.nombre}.`, () => api(`/temas/${tema.id}/archivos`, "POST", form));
  };
  const bajar = (a: ArchivoTema) => {
    setError("");
    descargar(`/temas/archivos/${a.id}`, a.nombre).catch((e) => setError((e as Error).message));
  };
  const peso = (bytes: number) => (bytes >= 1024 * 1024 ? `${(bytes / 1024 / 1024).toFixed(1)} MB` : `${Math.max(1, Math.round(bytes / 1024))} KB`);
  const mover = (ruta: string, direccion: number) => void accion("", () => api(`${ruta}/mover`, "POST", { direccion }));
  const temas = unidades.data.reduce((s, u) => s + u.temas.length, 0);

  return (
    <>
      <Toast message={notice} close={() => setNotice("")} />
      <div className="toolbar">
        <span className="muted">
          {unidades.loading
            ? "Cargando…"
            : `${unidades.data.length} ${unidades.data.length === 1 ? "unidad" : "unidades"} · ${temas} ${temas === 1 ? "tema" : "temas"}`}
        </span>
        {!lectura && (
          <button className="primary" onClick={() => setEdicion({ tipo: "unidad" })}>
            <Plus size={18} />
            Nueva unidad
          </button>
        )}
      </div>
      <ErrorBox message={error} />
      <ErrorBox message={unidades.error} retry={unidades.reload} />
      {!unidades.loading && !unidades.error && !unidades.data.length && (
        <Empty
          text={
            lectura
              ? "Este curso no registró unidades."
              : "Este curso todavía no tiene contenido. Crea la primera unidad y agrega sus temas."
          }
        />
      )}
      {unidades.data.map((u, i) => (
        <section className="panel unidad" key={u.id}>
          <div className="unidad-head">
            <div>
              <span className="pill">Unidad {i + 1}</span>
              <h2>{u.nombre}</h2>
              {u.descripcion && <p className="muted">{u.descripcion}</p>}
            </div>
            {!lectura && (
              <div className="row-actions">
                <button className="icon" title="Subir unidad" aria-label={`Subir ${u.nombre}`} disabled={i === 0} onClick={() => mover(`/unidades/${u.id}`, -1)}>
                  <ChevronUp size={17} />
                </button>
                <button className="icon" title="Bajar unidad" aria-label={`Bajar ${u.nombre}`} disabled={i === unidades.data.length - 1} onClick={() => mover(`/unidades/${u.id}`, 1)}>
                  <ChevronDown size={17} />
                </button>
                <button className="icon" title="Editar unidad" aria-label={`Editar ${u.nombre}`} onClick={() => setEdicion({ tipo: "unidad", unidad: u })}>
                  <Pencil size={16} />
                </button>
                <button
                  className="icon peligro"
                  title="Eliminar unidad"
                  aria-label={`Eliminar ${u.nombre}`}
                  onClick={() => borrar(`¿Eliminar la unidad «${u.nombre}» con sus temas y contenidos?`, `/unidades/${u.id}`, "Unidad eliminada.")}
                >
                  <Trash2 size={16} />
                </button>
              </div>
            )}
          </div>
          {u.temas.map((t, j) => {
            return (
              <div className="tema" key={t.id}>
                <div className="tema-head">
                  <div>
                    <strong>
                      {i + 1}.{j + 1} {t.nombre}
                    </strong>
                    {t.descripcion && <small className="block muted">{t.descripcion}</small>}
                  </div>
                  {!lectura && (
                    <div className="row-actions">
                      <button className="icon" title="Subir tema" aria-label={`Subir ${t.nombre}`} disabled={j === 0} onClick={() => mover(`/temas/${t.id}`, -1)}>
                        <ChevronUp size={16} />
                      </button>
                      <button className="icon" title="Bajar tema" aria-label={`Bajar ${t.nombre}`} disabled={j === u.temas.length - 1} onClick={() => mover(`/temas/${t.id}`, 1)}>
                        <ChevronDown size={16} />
                      </button>
                      <button className="icon" title="Editar tema" aria-label={`Editar ${t.nombre}`} onClick={() => setEdicion({ tipo: "tema", unidad: u, tema: t })}>
                        <Pencil size={15} />
                      </button>
                      <button
                        className="icon peligro"
                        title="Eliminar tema"
                        aria-label={`Eliminar ${t.nombre}`}
                        onClick={() => borrar(`¿Eliminar el tema «${t.nombre}» y sus contenidos?`, `/temas/${t.id}`, "Tema eliminado.")}
                      >
                        <Trash2 size={15} />
                      </button>
                    </div>
                  )}
                </div>
                <ul className="contenidos">
                  {t.contenidos.map((c) => (
                    <li key={c.id}>
                      <span>{c.descripcion}</span>
                      {!lectura && (
                        <span className="row-actions">
                          <button className="icon" title="Editar contenido" aria-label={`Editar contenido ${c.descripcion}`} onClick={() => setEdicion({ tipo: "contenido", tema: t, contenido: c })}>
                            <Pencil size={14} />
                          </button>
                          <button
                            className="icon peligro"
                            title="Eliminar contenido"
                            aria-label={`Eliminar contenido ${c.descripcion}`}
                            onClick={() => borrar("¿Eliminar este contenido mínimo?", `/contenidos/${c.id}`, "Contenido eliminado.")}
                          >
                            <Trash2 size={14} />
                          </button>
                        </span>
                      )}
                    </li>
                  ))}
                  {!t.contenidos.length && <li className="muted">Sin contenidos mínimos todavía.</li>}
                </ul>
                {(t.archivos.length > 0 || !lectura) && (
                  <div className="material">
                    <small className="muted">Material del tema</small>
                    {t.archivos.map((a) => (
                      <span className="archivo" key={a.id}>
                        <Paperclip size={13} />
                        <button className="text-button" title="Descargar" onClick={() => bajar(a)}>
                          {a.nombre}
                        </button>
                        <small className="muted">{peso(a.tamano)}</small>
                        <button className="icon" title="Descargar" aria-label={`Descargar ${a.nombre}`} onClick={() => bajar(a)}>
                          <Download size={14} />
                        </button>
                        {!lectura && (
                          <button
                            className="icon peligro"
                            title="Eliminar archivo"
                            aria-label={`Eliminar el archivo ${a.nombre}`}
                            onClick={() => borrar(`¿Eliminar el archivo «${a.nombre}»?`, `/temas/archivos/${a.id}`, "Archivo eliminado.")}
                          >
                            <Trash2 size={14} />
                          </button>
                        )}
                      </span>
                    ))}
                    {!t.archivos.length && <small className="muted">Sin archivos todavía.</small>}
                    {!lectura && (
                      <label className="subir">
                        <Upload size={14} /> Subir archivo
                        <input
                          type="file"
                          accept=".pdf,.doc,.docx,.ppt,.pptx,.xls,.xlsx,.txt,.csv,.png,.jpg,.jpeg,.zip"
                          aria-label={`Subir un archivo al tema ${t.nombre}`}
                          onChange={(e) => {
                            subir(t, e.target.files?.[0]);
                            e.target.value = "";
                          }}
                        />
                      </label>
                    )}
                  </div>
                )}
                <div className="tema-pie">
                  {!lectura && (
                    <button className="text-button" onClick={() => setEdicion({ tipo: "contenido", tema: t })}>
                      <Plus size={14} /> Agregar contenido mínimo
                    </button>
                  )}
                </div>
              </div>
            );
          })}
          {!u.temas.length && <p className="muted">Esta unidad todavía no tiene temas.</p>}
          {!lectura && (
            <button className="secondary" onClick={() => setEdicion({ tipo: "tema", unidad: u })}>
              <Plus size={16} /> Agregar tema
            </button>
          )}
        </section>
      ))}
      {edicion && (
        <Formulario
          edicion={edicion}
          cursoId={curso.id}
          close={() => setEdicion(null)}
          saved={(texto) => {
            setEdicion(null);
            setNotice(texto);
            unidades.reload();
          }}
        />
      )}
    </>
  );
}

function Formulario({
  edicion,
  cursoId,
  close,
  saved,
}: {
  edicion: Edicion;
  cursoId: number;
  close: () => void;
  saved: (texto: string) => void;
}) {
  const inicial =
    edicion.tipo === "unidad"
      ? { nombre: edicion.unidad?.nombre ?? "", descripcion: edicion.unidad?.descripcion ?? "" }
      : edicion.tipo === "tema"
        ? { nombre: edicion.tema?.nombre ?? "", descripcion: edicion.tema?.descripcion ?? "" }
        : { nombre: edicion.contenido?.descripcion ?? "", descripcion: "" };
  const [nombre, setNombre] = useState(inicial.nombre),
    [descripcion, setDescripcion] = useState(inicial.descripcion),
    [contenidos, setContenidos] = useState(""),
    [busy, setBusy] = useState(false),
    [error, setError] = useState("");
  const editando = edicion.tipo === "unidad" ? !!edicion.unidad : edicion.tipo === "tema" ? !!edicion.tema : !!edicion.contenido;
  const titulo =
    edicion.tipo === "unidad"
      ? editando ? "Editar unidad" : "Nueva unidad"
      : edicion.tipo === "tema"
        ? editando ? "Editar tema" : "Nuevo tema"
        : editando ? "Editar contenido mínimo" : "Nuevo contenido mínimo";

  async function submit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError("");
    try {
      if (edicion.tipo === "unidad") {
        const cuerpo = { nombre, descripcion: descripcion || null };
        if (edicion.unidad) await api(`/unidades/${edicion.unidad.id}`, "PUT", cuerpo);
        else await api("/unidades", "POST", { cursoId, ...cuerpo });
        saved(editando ? "Unidad actualizada." : "Unidad creada. Agrega sus temas.");
      } else if (edicion.tipo === "tema") {
        const cuerpo = { nombre, descripcion: descripcion || null };
        if (edicion.tema) await api(`/temas/${edicion.tema.id}`, "PUT", cuerpo);
        else
          await api(`/unidades/${edicion.unidad.id}/temas`, "POST", {
            ...cuerpo,
            contenidos: contenidos
              .split("\n")
              .map((x) => x.trim())
              .filter(Boolean),
          });
        saved(editando ? "Tema actualizado." : "Tema creado.");
      } else {
        if (edicion.contenido) await api(`/contenidos/${edicion.contenido.id}`, "PUT", { descripcion: nombre });
        else await api(`/temas/${edicion.tema.id}/contenidos`, "POST", { descripcion: nombre });
        saved(editando ? "Contenido actualizado." : "Contenido agregado.");
      }
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }
  return (
    <Modal title={titulo} close={close} busy={busy}>
      <form onSubmit={submit}>
        {edicion.tipo === "tema" && !edicion.tema && <p className="muted">Unidad: {edicion.unidad.nombre}</p>}
        {edicion.tipo === "contenido" && <p className="muted">Tema: {edicion.tema.nombre}</p>}
        <label>
          {edicion.tipo === "contenido" ? "Contenido mínimo" : "Nombre"}
          <input required minLength={3} maxLength={edicion.tipo === "contenido" ? 300 : edicion.tipo === "tema" ? 150 : 120} value={nombre} onChange={(e) => setNombre(e.target.value)} />
        </label>
        {edicion.tipo !== "contenido" && (
          <label>
            Descripción (opcional)
            <textarea rows={3} maxLength={500} value={descripcion} onChange={(e) => setDescripcion(e.target.value)} />
          </label>
        )}
        {edicion.tipo === "tema" && !edicion.tema && (
          <label>
            Contenidos mínimos (uno por línea, opcional)
            <textarea rows={4} value={contenidos} onChange={(e) => setContenidos(e.target.value)} />
          </label>
        )}
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
