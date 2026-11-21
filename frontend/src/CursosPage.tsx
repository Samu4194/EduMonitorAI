import { useState, type FormEvent } from "react";
import { Plus, ArrowUpRight, RefreshCw } from "lucide-react";
import {
  api,
  type Carrera,
  type Curso,
  type Docente,
  type ListaEstudiante,
  type Paralelo,
  type Periodo,
} from "./api";
import { useResource } from "./useResource";
import { Modal, ErrorBox, Empty } from "./components";

const romano = ["I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX"];

// Cursos del período para el administrador: paralelos y asignación de docentes. El docente usa «Mis cursos» (CursoAula).
export function CursosPage() {
  const [carrera, setCarrera] = useState(""),
    [nivel, setNivel] = useState(""),
    [paralelo, setParalelo] = useState(""),
    [materia, setMateria] = useState(""),
    [notice, setNotice] = useState(""),
    [error, setError] = useState(""),
    [lista, setLista] = useState<Curso | null>(null),
    [nuevo, setNuevo] = useState(false);
  const carreras = useResource<Carrera>("/carreras");
  const periodos = useResource<Periodo>("/periodos");
  const elegido = carrera && nivel;
  const paralelos = useResource<Paralelo>(
    elegido ? `/paralelos?carrera=${carrera}&nivel=${nivel}` : null,
  );
  const cursos = useResource<Curso>(
    elegido
      ? `/cursos?carrera=${carrera}&nivel=${nivel}${paralelo ? `&paralelo=${paralelo}` : ""}`
      : null,
  );
  // Solo se ofrecen los docentes de la carrera elegida: así no se cruzan docentes entre carreras.
  const docentes = useResource<Docente>(carrera ? `/docentes?carrera=${carrera}` : null);
  // El filtro por materia se aplica sobre los cursos ya cargados (carrera + nivel): no hace otra consulta.
  const materias = [...new Map(cursos.data.map((c) => [c.materiaId, c.materiaNombre])).entries()].sort((a, b) => a[1].localeCompare(b[1], "es"));
  const visibles = materia ? cursos.data.filter((c) => String(c.materiaId) === materia) : cursos.data;
  const activo = periodos.data.find((p) => p.activo);
  const niveles = carreras.data.find((c) => c.codigo === carrera)?.niveles ?? 9;

  async function asignar(curso: Curso, valor: string) {
    setNotice("");
    setError("");
    try {
      await api(`/cursos/${curso.id}/docente`, "PUT", {
        teacherId: valor ? Number(valor) : null,
      });
      setNotice(
        valor
          ? `Docente asignado a ${curso.materiaNombre}, paralelo ${curso.paralelo}.`
          : `${curso.materiaNombre}, paralelo ${curso.paralelo}: sin docente.`,
      );
      cursos.reload();
    } catch (e) {
      setError((e as Error).message);
      cursos.reload();
    }
  }
  return (
    <>
      <div className="page-heading">
        <div>
          <span className="eyebrow">GESTIÓN ACADÉMICA</span>
          <h1>Cursos</h1>
          <p className="muted">
            Gestión de cursos y asignación de docentes.
            {activo ? ` Período académico: ${activo.nombre}.` : " No hay un período académico activo."}
          </p>
        </div>
        <button
          className="primary"
          disabled={!elegido}
          title={elegido ? "" : "Elige una carrera y un nivel"}
          onClick={() => {
            setNotice("");
            setNuevo(true);
          }}
        >
          <Plus size={18} />
          Agregar paralelo
        </button>
      </div>
      <div className="filters">
        <label>
          Carrera
          <select
            value={carrera}
            onChange={(e) => {
              setCarrera(e.target.value);
              setNivel("");
              setParalelo("");
              setMateria("");
            }}
          >
            <option value="">Elige una carrera</option>
            {carreras.data.map((c) => (
              <option key={c.codigo} value={c.codigo}>
                {c.nombre}
              </option>
            ))}
          </select>
        </label>
        <label>
          Nivel
          <select
            value={nivel}
            disabled={!carrera}
            onChange={(e) => {
              setNivel(e.target.value);
              setParalelo("");
              setMateria("");
            }}
          >
            <option value="">Elige un nivel</option>
            {Array.from({ length: niveles }, (_, i) => i + 1).map((n) => (
              <option key={n} value={n}>
                Nivel {romano[n - 1]}
              </option>
            ))}
          </select>
        </label>
        <label>
          Materia
          <select value={materia} disabled={!elegido || !materias.length} onChange={(e) => setMateria(e.target.value)}>
            <option value="">Todas</option>
            {materias.map(([id, nombre]) => (
              <option key={id} value={id}>
                {nombre}
              </option>
            ))}
          </select>
        </label>
        <label>
          Paralelo
          <select value={paralelo} disabled={!elegido} onChange={(e) => setParalelo(e.target.value)}>
            <option value="">Todos</option>
            {paralelos.data.map((p) => (
              <option key={p.id} value={p.letra}>
                Paralelo {p.letra}
              </option>
            ))}
          </select>
        </label>
      </div>
      {notice && (
        <div role="status" className="success">
          {notice}
        </div>
      )}
      <ErrorBox message={error} />
      <ErrorBox message={carreras.error || cursos.error || docentes.error} retry={() => { carreras.reload(); cursos.reload(); docentes.reload(); }} />
      {!elegido ? (
        <Empty text="Elige una carrera y un nivel para ver sus cursos" />
      ) : (
        <>
          <div className="toolbar">
            <span className="muted">
              {cursos.loading
                ? "Cargando…"
                : `${visibles.length} ${visibles.length === 1 ? "curso" : "cursos"}`}
            </span>
            <button className="secondary" disabled={cursos.loading} onClick={cursos.reload}>
              <RefreshCw size={15} />
              Actualizar
            </button>
          </div>
          {!cursos.loading && !cursos.error && !cursos.data.length && (
            <Empty text="Este nivel todavía no tiene paralelos. Agrega uno." />
          )}
          {cursos.data.length > 0 && (
            <section className="panel table-wrap">
              <table>
                <thead>
                  <tr>
                    <th>Materia</th>
                    <th>Paralelo</th>
                    <th>Créditos</th>
                    <th>Docente</th>
                    <th>Estudiantes</th>
                  </tr>
                </thead>
                <tbody>
                  {visibles.map((c) => (
                    <tr key={c.id}>
                      <td>
                        <strong>{c.materiaNombre}</strong>
                        <small className="block muted">{c.materiaCodigo}</small>
                      </td>
                      <td>{c.paralelo}</td>
                      <td>
                        {c.creditos} · {c.horasTotales} h
                      </td>
                      <td>
                        <select
                          aria-label={`Docente de ${c.materiaNombre}, paralelo ${c.paralelo}`}
                          value={c.teacherId ?? ""}
                          disabled={docentes.loading}
                          onChange={(e) => void asignar(c, e.target.value)}
                        >
                          <option value="">Sin docente</option>
                          {docentes.data.map((d) => (
                            <option key={d.id} value={d.id}>
                              {d.name}
                            </option>
                          ))}
                        </select>
                      </td>
                      <td>
                        <button className="text-button" onClick={() => setLista(c)}>
                          Ver lista <ArrowUpRight size={15} />
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </section>
          )}
        </>
      )}
      {nuevo && (
        <NuevoParalelo
          carrera={carrera}
          nivel={Number(nivel)}
          close={() => setNuevo(false)}
          saved={(letra) => {
            setNuevo(false);
            setNotice(`Paralelo ${letra} creado con sus cursos.`);
            paralelos.reload();
            cursos.reload();
          }}
        />
      )}
      {lista && <Lista curso={lista} close={() => setLista(null)} />}
    </>
  );
}

function NuevoParalelo({
  carrera,
  nivel,
  close,
  saved,
}: {
  carrera: string;
  nivel: number;
  close: () => void;
  saved: (letra: string) => void;
}) {
  const [letra, setLetra] = useState(""),
    [malla, setMalla] = useState(""),
    [busy, setBusy] = useState(false),
    [error, setError] = useState("");
  // Con más de una malla, el paralelo se dicta con una de ellas: la vigente (para estudiantes nuevos) u otra anterior (para quienes siguen con ella).
  const carreras = useResource<Carrera>("/carreras");
  const datos = carreras.data.find((c) => c.codigo === carrera);
  async function submit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError("");
    try {
      await api("/paralelos", "POST", { carrera, nivel, letra, malla: malla ? Number(malla) : null });
      saved(letra.toUpperCase());
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }
  return (
    <Modal title="Agregar paralelo" close={close} busy={busy}>
      <form onSubmit={submit}>
        <p className="muted">
          Se crea en el período activo, con un curso por cada materia del nivel {romano[nivel - 1]}.
        </p>
        {datos && datos.versiones > 1 && (
          <label>
            Malla del paralelo
            <select value={malla} onChange={(e) => setMalla(e.target.value)}>
              <option value="">Malla {datos.mallaVersion} (vigente, para estudiantes nuevos)</option>
              {Array.from({ length: datos.versiones }, (_, i) => i + 1)
                .filter((v) => v !== datos.mallaVersion)
                .map((v) => (
                  <option key={v} value={v}>
                    Malla {v} (para quienes continúan con ella)
                  </option>
                ))}
            </select>
          </label>
        )}
        <label>
          Letra del paralelo
          <input
            required
            pattern="[A-Za-z]{1,2}"
            title="Una o dos letras"
            maxLength={2}
            value={letra}
            onChange={(e) => setLetra(e.target.value)}
          />
        </label>
        <ErrorBox message={error} />
        <div className="modal-actions">
          <button className="secondary" type="button" disabled={busy} onClick={close}>
            Cancelar
          </button>
          <button className="primary" disabled={busy}>
            {busy ? "Creando…" : "Crear paralelo"}
          </button>
        </div>
      </form>
    </Modal>
  );
}

function Lista({ curso, close }: { curso: Curso; close: () => void }) {
  const resource = useResource<ListaEstudiante>(`/estudiantes/curso/${curso.id}`);
  return (
    <Modal title={curso.materiaNombre} close={close}>
      <p className="muted">
        {curso.carreraNombre} · Nivel {romano[curso.nivel - 1]} · Paralelo {curso.paralelo}
      </p>
      <ErrorBox message={resource.error} retry={resource.reload} />
      {resource.loading ? (
        <p role="status">Cargando estudiantes…</p>
      ) : (
        !resource.error && (
          <>
            <h3>
              {resource.data.length} {resource.data.length === 1 ? "estudiante" : "estudiantes"}
            </h3>
            {resource.data.map((s) => (
              <div className="roster-row" key={s.id}>
                <span className="avatar light">{s.apellidos[0]}</span>
                <div>
                  <strong>
                    {s.apellidos} {s.nombres}
                  </strong>
                  <small>{s.correo}</small>
                </div>
              </div>
            ))}
            {!resource.data.length && <Empty text="Este paralelo todavía no tiene estudiantes" />}
          </>
        )
      )}
    </Modal>
  );
}
