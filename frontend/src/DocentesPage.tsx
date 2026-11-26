import { useState, type FormEvent } from "react";
import { Plus, Pencil } from "lucide-react";
import { api, type Docente, type Carrera } from "./api";
import { useResource } from "./useResource";
import { Modal, ErrorBox, Empty } from "./components";

export function DocentesPage() {
  const [estado, setEstado] = useState<"activos" | "bajas" | "todos">("activos");
  const docentes = useResource<Docente>(`/docentes?estado=${estado}`);
  const carreras = useResource<Carrera>("/carreras");
  const [edit, setEdit] = useState<Docente | null | undefined>(undefined),
    [notice, setNotice] = useState(""),
    [error, setError] = useState("");
  // Dar de baja no borra nada: la cuenta queda inactiva y sus cursos del período sin docente. Se puede reactivar.
  async function cambiarEstado(d: Docente, baja: boolean) {
    setNotice("");
    setError("");
    if (baja && !window.confirm(`¿Dar de baja a ${d.name}? Dejará de poder iniciar sesión y sus cursos del período actual quedarán sin docente. Su historial se conserva y puedes reactivarlo cuando quieras.`)) return;
    try {
      await api(`/docentes/${d.id}/${baja ? "baja" : "reactivar"}`, "POST");
      setNotice(baja ? `${d.name} quedó dado de baja. Sus cursos del período están sin docente: asígnalos a otra persona en «Cursos».` : `${d.name} fue reactivado.`);
      docentes.reload();
    } catch (e) {
      setError((e as Error).message);
    }
  }
  const nombreCarrera = (codigo: string) =>
    carreras.data.find((c) => c.codigo === codigo)?.nombre ?? codigo;
  // Una carrera dada de baja deja de mostrarse en la ficha del docente (la lista de carreras trae solo las activas).
  // Si la lista no cargó, se muestra todo para no ocultar datos por un error.
  const visibles = (lista: string[]) =>
    carreras.loading || carreras.error ? lista : lista.filter((c) => carreras.data.some((x) => x.codigo === c));
  return (
    <>
      <div className="page-heading">
        <div>
          <span className="eyebrow">GESTIÓN ACADÉMICA</span>
          <h1>Docentes</h1>
          <p className="muted">
            Registra la ficha de cada docente y las carreras a las que pertenece.
          </p>
        </div>
        <button
          className="primary"
          onClick={() => {
            setNotice("");
            setEdit(null);
          }}
        >
          <Plus size={18} />
          Registrar docente
        </button>
      </div>
      {notice && (
        <div role="status" className="success">
          {notice}
        </div>
      )}
      <ErrorBox message={error || docentes.error} retry={docentes.reload} />
      <div className="toolbar">
        <div className="chips-filtro" role="group" aria-label="Estado del docente">
          {(["activos", "bajas", "todos"] as const).map((e) => (
            <button key={e} className={estado === e ? "activo" : ""} aria-pressed={estado === e} onClick={() => setEstado(e)}>
              {e === "activos" ? "Activos" : e === "bajas" ? "De baja" : "Todos"}
            </button>
          ))}
        </div>
        <span className="muted">
          {docentes.loading
            ? "Cargando…"
            : docentes.error
              ? "Datos no disponibles"
              : `${docentes.data.length} ${docentes.data.length === 1 ? "registro" : "registros"}`}
        </span>
      </div>
      {!docentes.error && !docentes.loading && !docentes.data.length && (
        <Empty text={estado === "bajas" ? "No hay docentes dados de baja." : estado === "activos" ? "Todavía no hay docentes activos registrados." : "Todavía no hay docentes registrados"} />
      )}
      {docentes.data.length > 0 && (
        <section className="panel table-wrap">
          <table>
            <thead>
              <tr>
                <th>Apellidos y nombres</th>
                <th>Cédula</th>
                <th>Correo institucional</th>
                <th>Carreras</th>
                <th>Acciones</th>
              </tr>
            </thead>
            <tbody>
              {docentes.data.map((d) => (
                <tr key={d.id} className={d.activo ? "" : "fila-baja"}>
                  <td>
                    <strong>{d.name}</strong>
                    {!d.activo && <span className="pill spaced">De baja</span>}
                    {d.tituloAcademico && (
                      <small className="block muted">{d.tituloAcademico}</small>
                    )}
                  </td>
                  <td>{d.cedula || "—"}</td>
                  <td>{d.email}</td>
                  <td>
                    {visibles(d.carreras).length
                      ? visibles(d.carreras).map((c) => (
                          <span className="pill spaced" key={c} title={nombreCarrera(c)}>
                            {c}
                          </span>
                        ))
                      : "—"}
                  </td>
                  <td>
                    <button
                      aria-label={`Editar a ${d.name}`}
                      onClick={() => {
                        setNotice("");
                        setEdit(d);
                      }}
                    >
                      <Pencil size={16} />
                      Editar
                    </button>
                    {d.activo ? (
                      <button className="secondary" aria-label={`Dar de baja a ${d.name}`} onClick={() => void cambiarEstado(d, true)}>
                        Dar de baja
                      </button>
                    ) : (
                      <button className="secondary" aria-label={`Reactivar a ${d.name}`} onClick={() => void cambiarEstado(d, false)}>
                        Reactivar
                      </button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </section>
      )}
      {edit !== undefined && (
        <DocenteForm
          item={edit}
          carreras={carreras.data}
          carrerasError={carreras.error}
          retryCarreras={carreras.reload}
          close={() => setEdit(undefined)}
          saved={() => {
            setEdit(undefined);
            setNotice("Los cambios se guardaron correctamente.");
            docentes.reload();
          }}
        />
      )}
    </>
  );
}

function DocenteForm({
  item,
  carreras,
  carrerasError,
  retryCarreras,
  close,
  saved,
}: {
  item: Docente | null;
  carreras: Carrera[];
  carrerasError: string;
  retryCarreras: () => void;
  close: () => void;
  saved: () => void;
}) {
  const [f, setF] = useState({
    cedula: item?.cedula ?? "",
    nombres: item?.nombres ?? "",
    apellidos: item?.apellidos ?? "",
    email: item?.email ?? "",
    telefono: item?.telefono ?? "",
    tituloAcademico: item?.tituloAcademico ?? "",
    password: "",
  });
  const [seleccion, setSeleccion] = useState<string[]>(
    (item?.carreras ?? []).filter((c) => carrerasError !== "" || carreras.length === 0 || carreras.some((x) => x.codigo === c)),
  );
  const [busy, setBusy] = useState(false),
    [error, setError] = useState("");
  const set = (key: keyof typeof f, value: string) =>
    setF((x) => ({ ...x, [key]: value }));
  const alternar = (codigo: string) =>
    setSeleccion((s) =>
      s.includes(codigo) ? s.filter((c) => c !== codigo) : [...s, codigo],
    );
  async function submit(e: FormEvent) {
    e.preventDefault();
    if (!seleccion.length) {
      setError("Selecciona al menos una carrera.");
      return;
    }
    setBusy(true);
    setError("");
    try {
      await api(`/docentes${item ? `/${item.id}` : ""}`, item ? "PUT" : "POST", {
        cedula: f.cedula,
        nombres: f.nombres,
        apellidos: f.apellidos,
        email: f.email,
        telefono: f.telefono || null,
        tituloAcademico: f.tituloAcademico || null,
        carreras: seleccion,
        password: f.password || null,
      });
      saved();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }
  return (
    <Modal title={`${item ? "Editar" : "Registrar"} docente`} close={close} busy={busy}>
      <form onSubmit={submit}>
        <label>
          Cédula
          <input
            required
            inputMode="numeric"
            pattern="[0-9]{10}"
            title="10 dígitos de una cédula ecuatoriana válida"
            maxLength={10}
            value={f.cedula}
            onChange={(e) => set("cedula", e.target.value)}
          />
        </label>
        <div className="two-cols">
          <label>
            Nombres
            <input
              required
              minLength={2}
              maxLength={100}
              value={f.nombres}
              onChange={(e) => set("nombres", e.target.value)}
            />
          </label>
          <label>
            Apellidos
            <input
              required
              minLength={2}
              maxLength={100}
              value={f.apellidos}
              onChange={(e) => set("apellidos", e.target.value)}
            />
          </label>
        </div>
        <label>
          Correo institucional
          <input
            required
            type="email"
            autoComplete="off"
            maxLength={254}
            value={f.email}
            onChange={(e) => set("email", e.target.value)}
          />
        </label>
        <div className="two-cols">
          <label>
            Teléfono <small className="muted">Opcional</small>
            <input
              type="tel"
              pattern="[0-9+\- ]{7,20}"
              title="Solo números, espacios, + y -"
              maxLength={20}
              value={f.telefono}
              onChange={(e) => set("telefono", e.target.value)}
            />
          </label>
          <label>
            Título académico <small className="muted">Opcional</small>
            <input
              maxLength={150}
              placeholder="Ej.: Ingeniero en Sistemas"
              value={f.tituloAcademico}
              onChange={(e) => set("tituloAcademico", e.target.value)}
            />
          </label>
        </div>
        <fieldset className="checks">
          <legend>Carreras a las que pertenece</legend>
          {carrerasError ? (
            <ErrorBox message={carrerasError} retry={retryCarreras} />
          ) : (
            carreras.map((c) => (
              <label key={c.codigo}>
                <input
                  type="checkbox"
                  checked={seleccion.includes(c.codigo)}
                  onChange={() => alternar(c.codigo)}
                />
                {c.nombre}
              </label>
            ))
          )}
        </fieldset>
        <label>
          Contraseña{" "}
          {item && (
            <small className="muted">
              Déjala en blanco para conservar la actual.
            </small>
          )}
          <input
            type="password"
            autoComplete="new-password"
            required={!item}
            minLength={10}
            maxLength={128}
            value={f.password}
            onChange={(e) => set("password", e.target.value)}
          />
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
