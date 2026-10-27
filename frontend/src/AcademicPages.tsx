import { useState, type FormEvent } from "react";
import { Plus, Pencil, BookOpen, ArrowUpRight, RefreshCw } from "lucide-react";
import { api, type Teacher, type Student, type Materia } from "./api";
import { useResource } from "./useResource";
import { Modal, ErrorBox, Empty } from "./components";

type Entity = "docentes" | "materias" | "estudiantes";
type Row = Teacher | Student | Materia;
type Fields = {
  name: string;
  email: string;
  password: string;
  code: string;
  academicPeriod: string;
  teacherId: string;
  studentCode: string;
  firstNames: string;
  lastNames: string;
};
const blank: Fields = {
  name: "",
  email: "",
  password: "",
  code: "",
  academicPeriod: "",
  teacherId: "",
  studentCode: "",
  firstNames: "",
  lastNames: "",
};
const title = {
  docentes: "Docentes",
  materias: "Materias",
  estudiantes: "Estudiantes",
};
const singular = {
  docentes: "docente",
  materias: "materia",
  estudiantes: "estudiante",
};

export function AcademicPage({
  kind,
  admin,
}: {
  kind: Entity;
  admin: boolean;
}) {
  const resource = useResource<Row>(`/${kind}`);
  const teachers = useResource<Teacher>(
    admin && kind === "materias" ? "/docentes" : null,
  );
  const [edit, setEdit] = useState<Row | null | undefined>(undefined),
    [selected, setSelected] = useState<Materia | null>(null),
    [notice, setNotice] = useState("");
  return (
    <>
      <div className="page-heading">
        <div>
          <span className="eyebrow">GESTIÓN ACADÉMICA</span>
          <h1>
            {kind === "materias" && !admin ? "Mis materias" : title[kind]}
          </h1>
          <p className="muted">
            {kind === "materias"
              ? "Consulta las materias, sus períodos y sus estudiantes."
              : kind === "docentes"
                ? "Registra y actualiza las cuentas de tus docentes."
                : "Organiza los datos de los estudiantes de tu institución."}
          </p>
        </div>
        {admin && (
          <button
            className="primary"
            onClick={() => {
              setNotice("");
              setEdit(null);
            }}
          >
            <Plus size={18} />
            Registrar {singular[kind]}
          </button>
        )}
      </div>
      {notice && (
        <div role="status" className="success">
          {notice}
        </div>
      )}
      <ErrorBox message={resource.error} retry={resource.reload} />
      <div className="toolbar">
        <span className="muted">
          {resource.loading
            ? "Cargando…"
            : resource.error
              ? "Datos no disponibles"
              : `${resource.data.length} ${resource.data.length === 1 ? "registro" : "registros"}`}
        </span>
        <button
          className="secondary"
          disabled={resource.loading}
          onClick={resource.reload}
        >
          <RefreshCw size={15} />
          Actualizar
        </button>
      </div>
      {!resource.error && !resource.loading && !resource.data.length && (
        <Empty text="Todavía no hay registros" />
      )}
      {kind === "materias" ? (
        <div className="course-grid">
          {(resource.data as Materia[]).map((m) => (
            <section className="course-card" key={m.id}>
              <div className="card-top">
                <span className="course-icon">
                  <BookOpen size={22} />
                </span>
                <span className="pill">{m.code}</span>
              </div>
              <h2>{m.name}</h2>
              <p className="muted">{m.academicPeriod}</p>
              <p className="small muted">
                {m.teacherId
                  ? teachers.data.find((t) => t.id === m.teacherId)?.name ||
                    "Docente asignado"
                  : "Pendiente de asignación"}
              </p>
              <div className="card-footer">
                <button className="text-button" onClick={() => setSelected(m)}>
                  Ver estudiantes <ArrowUpRight size={16} />
                </button>
                {admin && (
                  <button
                    aria-label={`Editar ${m.name}`}
                    onClick={() => setEdit(m)}
                  >
                    <Pencil size={16} />
                    Editar / asignar
                  </button>
                )}
              </div>
            </section>
          ))}
        </div>
      ) : (
        resource.data.length > 0 && (
          <section className="panel table-wrap">
            <table>
              <thead>
                <tr>
                  <th>
                    {kind === "docentes" ? "Nombre" : "Nombres y apellidos"}
                  </th>
                  <th>
                    {kind === "docentes" ? "Correo electrónico" : "Código"}
                  </th>
                  <th>Acciones</th>
                </tr>
              </thead>
              <tbody>
                {resource.data.map((row) => (
                  <tr key={row.id}>
                    <td>
                      <strong>
                        {"firstNames" in row
                          ? `${row.firstNames} ${row.lastNames}`
                          : "name" in row
                            ? row.name
                            : ""}
                      </strong>
                    </td>
                    <td>
                      {"email" in row
                        ? row.email
                        : "studentCode" in row
                          ? row.studentCode
                          : ""}
                    </td>
                    <td>
                      <button
                        aria-label={`Editar registro ${row.id}`}
                        onClick={() => setEdit(row)}
                      >
                        <Pencil size={16} />
                        Editar
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </section>
        )
      )}
      {edit !== undefined && (
        <EditForm
          kind={kind}
          item={edit}
          close={() => setEdit(undefined)}
          saved={() => {
            setEdit(undefined);
            setNotice("Los cambios se guardaron correctamente.");
            resource.reload();
          }}
        />
      )}
      {selected && (
        <Roster
          materia={selected}
          admin={admin}
          close={() => setSelected(null)}
        />
      )}
    </>
  );
}

function EditForm({
  kind,
  item,
  close,
  saved,
}: {
  kind: Entity;
  item: Row | null;
  close: () => void;
  saved: () => void;
}) {
  const [fields, setFields] = useState<Fields>(() => ({
    ...blank,
    ...item,
    teacherId: item && "teacherId" in item ? String(item.teacherId ?? "") : "",
  }));
  const [busy, setBusy] = useState(false),
    [error, setError] = useState("");
  const set = (key: keyof Fields, value: string) =>
    setFields((f) => ({ ...f, [key]: value }));
  async function submit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError("");
    try {
      const body =
        kind === "docentes"
          ? {
              name: fields.name,
              email: fields.email,
              password: fields.password || null,
            }
          : kind === "materias"
            ? {
                code: fields.code,
                name: fields.name,
                academicPeriod: fields.academicPeriod,
                teacherId: fields.teacherId ? Number(fields.teacherId) : null,
              }
            : {
                studentCode: fields.studentCode,
                firstNames: fields.firstNames,
                lastNames: fields.lastNames,
              };
      await api(
        `/${kind}${item ? `/${item.id}` : ""}`,
        item ? "PUT" : "POST",
        body,
      );
      saved();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }
  return (
    <Modal
      title={`${item ? "Editar" : "Registrar"} ${singular[kind]}`}
      close={close}
      busy={busy}
    >
      <form onSubmit={submit}>
        {kind === "estudiantes" ? (
          <>
            <label>
              Código de estudiante
              <input
                required
                pattern="[A-Za-z0-9-]{2,30}"
                title="De 2 a 30 letras, números o guiones"
                value={fields.studentCode}
                onChange={(e) => set("studentCode", e.target.value)}
              />
            </label>
            <label>
              Nombres
              <input
                required
                minLength={2}
                maxLength={100}
                value={fields.firstNames}
                onChange={(e) => set("firstNames", e.target.value)}
              />
            </label>
            <label>
              Apellidos
              <input
                required
                minLength={2}
                maxLength={100}
                value={fields.lastNames}
                onChange={(e) => set("lastNames", e.target.value)}
              />
            </label>
          </>
        ) : (
          <label>
            Nombre
            <input
              required
              minLength={2}
              maxLength={120}
              value={fields.name}
              onChange={(e) => set("name", e.target.value)}
            />
          </label>
        )}
        {kind === "docentes" && (
          <>
            <label>
              Correo electrónico
              <input
                required
                type="email"
                maxLength={254}
                value={fields.email}
                onChange={(e) => set("email", e.target.value)}
              />
            </label>
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
                value={fields.password}
                onChange={(e) => set("password", e.target.value)}
              />
            </label>
          </>
        )}
        {kind === "materias" && (
          <>
            <label>
              Código de materia
              <input
                required
                pattern="[A-Za-z0-9-]{2,30}"
                title="De 2 a 30 letras, números o guiones"
                value={fields.code}
                onChange={(e) => set("code", e.target.value)}
              />
            </label>
            <label>
              Período académico
              <input
                required
                minLength={2}
                maxLength={80}
                placeholder="Ej.: octubre 2026 – febrero 2027"
                value={fields.academicPeriod}
                onChange={(e) => set("academicPeriod", e.target.value)}
              />
            </label>
            <TeacherSelect
              value={fields.teacherId}
              change={(v) => set("teacherId", v)}
            />
          </>
        )}
        <ErrorBox message={error} />
        <div className="modal-actions">
          <button
            className="secondary"
            type="button"
            disabled={busy}
            onClick={close}
          >
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

function TeacherSelect({
  value,
  change,
}: {
  value: string;
  change: (v: string) => void;
}) {
  const resource = useResource<Teacher>("/docentes");
  return (
    <>
      <label>
        Docente responsable
        <select
          value={value}
          disabled={resource.loading || !!resource.error}
          onChange={(e) => change(e.target.value)}
        >
          <option value="">Asignar después</option>
          {resource.data.map((t) => (
            <option key={t.id} value={t.id}>
              {t.name} · {t.email}
            </option>
          ))}
        </select>
      </label>
      <ErrorBox message={resource.error} retry={resource.reload} />
    </>
  );
}

function Roster({
  materia,
  admin,
  close,
}: {
  materia: Materia;
  admin: boolean;
  close: () => void;
}) {
  const resource = useResource<Student>(`/matriculas/materia/${materia.id}`);
  return (
    <Modal title={materia.name} close={close}>
      <p className="muted">
        {materia.code} · {materia.academicPeriod}
      </p>
      <ErrorBox message={resource.error} retry={resource.reload} />
      {resource.loading ? (
        <p role="status">Cargando estudiantes…</p>
      ) : (
        !resource.error && (
          <>
            <h3>{resource.data.length} estudiantes matriculados</h3>
            {resource.data.map((s) => (
              <div className="roster-row" key={s.id}>
                <span className="avatar light">{s.firstNames[0]}</span>
                <div>
                  <strong>
                    {s.firstNames} {s.lastNames}
                  </strong>
                  <small>{s.studentCode}</small>
                </div>
              </div>
            ))}
            {!resource.data.length && (
              <Empty text="Esta materia todavía no tiene estudiantes" />
            )}
            {admin && (
              <EnrollForm
                materiaId={materia.id}
                enrolled={resource.data}
                saved={resource.reload}
              />
            )}
          </>
        )
      )}
    </Modal>
  );
}

function EnrollForm({
  materiaId,
  enrolled,
  saved,
}: {
  materiaId: number;
  enrolled: Student[];
  saved: () => void;
}) {
  const students = useResource<Student>("/estudiantes");
  const [id, setId] = useState(""),
    [busy, setBusy] = useState(false),
    [error, setError] = useState("");
  async function submit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError("");
    try {
      await api("/matriculas", "POST", { materiaId, studentId: Number(id) });
      setId("");
      saved();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }
  return (
    <>
      <ErrorBox message={students.error} retry={students.reload} />
      <form className="enroll" onSubmit={submit}>
        <label>
          Matricular estudiante
          <select
            required
            disabled={students.loading || !!students.error}
            value={id}
            onChange={(e) => setId(e.target.value)}
          >
            <option value="">Selecciona un estudiante</option>
            {students.data
              .filter((s) => !enrolled.some((r) => r.id === s.id))
              .map((s) => (
                <option key={s.id} value={s.id}>
                  {s.firstNames} {s.lastNames} · {s.studentCode}
                </option>
              ))}
          </select>
        </label>
        <button className="primary" disabled={busy || !id}>
          {busy ? "Guardando…" : "Matricular"}
        </button>
      </form>
      <ErrorBox message={error} />
    </>
  );
}
