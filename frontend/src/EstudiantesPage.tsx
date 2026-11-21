import { useState, type FormEvent } from "react";
import { UserMinus, UserCheck, Pencil, RefreshCw, Upload, Download, X, RotateCcw } from "lucide-react";
import { api, type Carrera, type Estudiante, type Inscripcion, type MateriaMalla, type Paralelo } from "./api";
import { useResource } from "./useResource";
import { Modal, ErrorBox, Empty } from "./components";

// Con más de una malla en la carrera, se indica a cuál pertenece cada paralelo y cada materia.
const mallaDe = (c: Carrera | undefined, version: number) => (c && c.versiones > 1 ? ` · malla ${version}` : "");

const romano = ["I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX"];

function descargarPlantilla() {
  // El destino (carrera, nivel, paralelo y materia) se elige al cargar; el archivo solo trae a los estudiantes.
  const csv =
    "cedula,apellidos,nombres,correo\n" +
    "1711234565,Pérez Torres,Ana María,ana.perez@prueba.edu.ec\n" +
    "1802345676,Mora Cruz,Luis Fernando,luis.mora@prueba.edu.ec\n";
  const url = URL.createObjectURL(
    new Blob(["﻿" + csv], { type: "text/csv;charset=utf-8" }),
  );
  const a = document.createElement("a");
  a.href = url;
  a.download = "plantilla-estudiantes.csv";
  a.click();
  URL.revokeObjectURL(url);
}

export function EstudiantesPage() {
  const [carrera, setCarrera] = useState(""),
    [nivel, setNivel] = useState(""),
    [materia, setMateria] = useState(""),
    [paralelo, setParalelo] = useState(""),
    [estado, setEstado] = useState("activos"),
    [error, setError] = useState(""),
    [edit, setEdit] = useState<Estudiante | undefined>(undefined),
    [precarga, setPrecarga] = useState(false),
    [notice, setNotice] = useState("");
  const carreras = useResource<Carrera>("/carreras");
  const paralelos = useResource<Paralelo>(
    carrera && nivel ? `/paralelos?carrera=${carrera}&nivel=${nivel}` : null,
  );
  // Las materias del filtro salen de la malla de la carrera y el nivel elegidos.
  const materias = useResource<MateriaMalla>(
    carrera && nivel ? `/carreras/${carrera}/materias?nivel=${nivel}&malla=todas` : null,
  );
  const params = new URLSearchParams();
  if (carrera) params.set("carrera", carrera);
  if (nivel) params.set("nivel", nivel);
  if (materia) params.set("materia", materia);
  if (paralelo) params.set("paralelo", paralelo);
  if (estado !== "activos") params.set("estado", estado);
  const estudiantes = useResource<Estudiante>(
    `/estudiantes${params.size ? `?${params}` : ""}`,
  );
  const niveles = carreras.data.find((c) => c.codigo === carrera)?.niveles ?? 9;
  // Dar de baja no borra nada: el estudiante deja de aparecer en listas y calificaciones, y puede reactivarse.
  async function cambiarEstado(s: Estudiante, baja: boolean) {
    const nombre = `${s.apellidos} ${s.nombres}`;
    if (
      baja &&
      !window.confirm(
        `¿Dar de baja a ${nombre}?\nDejará de aparecer en las listas de su paralelo, en las calificaciones y en el rendimiento. Sus datos y notas se conservan y puede reactivarse.`,
      )
    )
      return;
    setNotice("");
    setError("");
    try {
      await api(`/estudiantes/${s.id}/${baja ? "baja" : "reactivar"}`, "POST");
      setNotice(baja ? `${nombre} fue dado de baja.` : `${nombre} fue reactivado.`);
      estudiantes.reload();
    } catch (e) {
      setError((e as Error).message);
    }
  }
  // Retirar de una materia no da de baja al estudiante: sigue activo en sus demás materias y conserva sus notas.
  async function cambiarInscripcion(s: Estudiante, i: Inscripcion) {
    const retirar = i.activo;
    if (
      retirar &&
      !window.confirm(
        `¿Retirar a ${s.apellidos} ${s.nombres} de ${i.materiaNombre} (nivel ${romano[i.nivel - 1]}, paralelo ${i.paralelo})?\nDejará de aparecer en ese curso; sus demás materias y sus notas se conservan.`,
      )
    )
      return;
    setNotice("");
    setError("");
    try {
      await api(`/estudiantes/inscripciones/${i.id}/${retirar ? "baja" : "reactivar"}`, "POST");
      setNotice(retirar ? `${s.apellidos} ${s.nombres} fue retirado de ${i.materiaNombre}.` : `${s.apellidos} ${s.nombres} fue reincorporado a ${i.materiaNombre}.`);
      estudiantes.reload();
    } catch (e) {
      setError((e as Error).message);
    }
  }
  // Agrupa las materias del estudiante por nivel y paralelo para mostrarlas ordenadas.
  const grupos = (s: Estudiante) => {
    const mapa = new Map<string, { nivel: number; paralelo: string; items: Inscripcion[] }>();
    for (const i of s.inscripciones) {
      const clave = `${i.nivel}|${i.paralelo}`;
      if (!mapa.has(clave)) mapa.set(clave, { nivel: i.nivel, paralelo: i.paralelo, items: [] });
      mapa.get(clave)!.items.push(i);
    }
    return [...mapa.entries()];
  };
  return (
    <>
      <div className="page-heading">
        <div>
          <span className="eyebrow">GESTIÓN ACADÉMICA</span>
          <h1>Estudiantes</h1>
          <p className="muted">
            Estudiantes por materia y paralelo. Se incorporan mediante la carga de un archivo CSV.
          </p>
        </div>
        <div className="heading-actions">
          <button
            className="primary"
            onClick={() => {
              setNotice("");
              setPrecarga(true);
            }}
          >
            <Upload size={18} />
            Cargar CSV
          </button>
        </div>
      </div>
      <div className="filters">
        <label>
          Carrera
          <select
            value={carrera}
            onChange={(e) => {
              setCarrera(e.target.value);
              setNivel("");
              setMateria("");
              setParalelo("");
            }}
          >
            <option value="">Todas</option>
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
              setMateria("");
              setParalelo("");
            }}
          >
            <option value="">Todos</option>
            {Array.from({ length: niveles }, (_, i) => i + 1).map((n) => (
              <option key={n} value={n}>
                Nivel {romano[n - 1]}
              </option>
            ))}
          </select>
        </label>
        <label>
          Materia
          <select
            value={materia}
            disabled={!carrera || !nivel || !materias.data.length}
            onChange={(e) => setMateria(e.target.value)}
          >
            <option value="">Todas</option>
            {materias.data.map((m) => (
              <option key={m.codigo} value={m.codigo}>
                {m.nombre}
                {mallaDe(carreras.data.find((c) => c.codigo === carrera), m.mallaVersion)}
              </option>
            ))}
          </select>
        </label>
        <label>
          Paralelo
          <select
            value={paralelo}
            disabled={!carrera || !nivel}
            onChange={(e) => setParalelo(e.target.value)}
          >
            <option value="">Todos</option>
            {paralelos.data.map((p) => (
              <option key={p.id} value={p.letra}>
                Paralelo {p.letra}
                {mallaDe(carreras.data.find((c) => c.codigo === carrera), p.mallaVersion)}
              </option>
            ))}
          </select>
        </label>
        <label>
          Estado
          <select value={estado} onChange={(e) => setEstado(e.target.value)}>
            <option value="activos">Activos</option>
            <option value="bajas">Dados de baja</option>
            <option value="todos">Todos</option>
          </select>
        </label>
      </div>
      {notice && (
        <div role="status" className="success">
          {notice}
        </div>
      )}
      <ErrorBox message={error} />
      <ErrorBox message={estudiantes.error} retry={estudiantes.reload} />
      <div className="toolbar">
        <span className="muted">
          {estudiantes.loading
            ? "Cargando…"
            : estudiantes.error
              ? "Datos no disponibles"
              : `${estudiantes.data.length} ${estudiantes.data.length === 1 ? "registro" : "registros"}`}
        </span>
        <button className="secondary" disabled={estudiantes.loading} onClick={estudiantes.reload}>
          <RefreshCw size={15} />
          Actualizar
        </button>
      </div>
      {!estudiantes.error && !estudiantes.loading && !estudiantes.data.length && (
        <Empty text={estado === "bajas" ? "No hay estudiantes dados de baja con estos filtros." : "No hay estudiantes con estos filtros. Carga un CSV con los estudiantes."} />
      )}
      {estudiantes.data.length > 0 && (
        <section className="panel table-wrap">
          <table>
            <thead>
              <tr>
                <th>Apellidos y nombres</th>
                <th>Cédula</th>
                <th>Correo</th>
                <th>Materias</th>
                <th title="Créditos de sus materias activas frente al máximo por período">Créditos</th>
                <th>Acciones</th>
              </tr>
            </thead>
            <tbody>
              {estudiantes.data.map((s) => (
                <tr key={s.id}>
                  <td>
                    <strong>
                      {s.apellidos} {s.nombres}
                    </strong>
                    {!s.activo && <span className="estado pend baja">Baja</span>}
                  </td>
                  <td>{s.cedula}</td>
                  <td>{s.correo}</td>
                  <td>
                    {grupos(s).map(([clave, g]) => (
                      <div className="grupo" key={clave}>
                        <span className="grupo-etiqueta">
                          {s.carreraCodigo} · {romano[g.nivel - 1]} «{g.paralelo}»
                        </span>
                        {g.items.map((i) => (
                          <span key={i.id} className={`chip${i.activo ? "" : " retirada"}`} title={`${i.materiaCodigo} · ${i.creditos} créditos${i.activo ? "" : " (retirado)"}`}>
                            {i.materiaNombre}
                            <button
                              type="button"
                              className="chip-x"
                              title={i.activo ? "Retirar de esta materia" : "Reincorporar a esta materia"}
                              aria-label={`${i.activo ? "Retirar a" : "Reincorporar a"} ${s.apellidos} ${s.nombres} ${i.activo ? "de" : "a"} ${i.materiaNombre}, paralelo ${i.paralelo}`}
                              onClick={() => void cambiarInscripcion(s, i)}
                            >
                              {i.activo ? <X size={12} /> : <RotateCcw size={12} />}
                            </button>
                          </span>
                        ))}
                      </div>
                    ))}
                    {!s.inscripciones.length && <span className="muted">Sin materias</span>}
                  </td>
                  <td>
                    <strong>{s.creditos}</strong>
                    <span className="muted"> / {s.maxCreditos}</span>
                  </td>
                  <td>
                    <button
                      aria-label={`Editar a ${s.apellidos} ${s.nombres}`}
                      onClick={() => {
                        setNotice("");
                        setEdit(s);
                      }}
                    >
                      <Pencil size={16} />
                      Editar
                    </button>
                    <button
                      className={s.activo ? "danger-link" : ""}
                      aria-label={`${s.activo ? "Dar de baja a" : "Reactivar a"} ${s.apellidos} ${s.nombres}`}
                      onClick={() => void cambiarEstado(s, s.activo)}
                    >
                      {s.activo ? <UserMinus size={16} /> : <UserCheck size={16} />}
                      {s.activo ? "Dar de baja" : "Reactivar"}
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </section>
      )}
      {edit !== undefined && (
        <EstudianteForm
          item={edit}
          close={() => setEdit(undefined)}
          saved={() => {
            setEdit(undefined);
            setNotice("Los cambios se guardaron correctamente.");
            estudiantes.reload();
          }}
        />
      )}
      {precarga && (
        <PrecargaForm
          close={() => setPrecarga(false)}
          cargado={estudiantes.reload}
        />
      )}
    </>
  );
}

// Solo se editan los datos personales. Las materias llegan por la carga CSV y se retiran desde la tabla.
function EstudianteForm({
  item,
  close,
  saved,
}: {
  item: Estudiante;
  close: () => void;
  saved: () => void;
}) {
  const [f, setF] = useState({
    cedula: item.cedula,
    apellidos: item.apellidos,
    nombres: item.nombres,
    correo: item.correo,
  });
  const [busy, setBusy] = useState(false),
    [error, setError] = useState("");
  const set = (key: keyof typeof f, value: string) =>
    setF((x) => ({ ...x, [key]: value }));
  async function submit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError("");
    try {
      await api(`/estudiantes/${item.id}`, "PUT", f);
      saved();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }
  return (
    <Modal title="Editar estudiante" close={close} busy={busy}>
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
            Apellidos
            <input required minLength={2} maxLength={100} value={f.apellidos} onChange={(e) => set("apellidos", e.target.value)} />
          </label>
          <label>
            Nombres
            <input required minLength={2} maxLength={100} value={f.nombres} onChange={(e) => set("nombres", e.target.value)} />
          </label>
        </div>
        <label>
          Correo
          <input required type="email" maxLength={254} value={f.correo} onChange={(e) => set("correo", e.target.value)} />
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

function PrecargaForm({
  close,
  cargado,
}: {
  close: () => void;
  cargado: () => void;
}) {
  const [archivo, setArchivo] = useState<File | null>(null),
    [carrera, setCarrera] = useState(""),
    [nivel, setNivel] = useState(""),
    [paralelo, setParalelo] = useState(""),
    [materia, setMateria] = useState(""),
    [busy, setBusy] = useState(false),
    [error, setError] = useState(""),
    [resultado, setResultado] = useState("");
  // Destino opcional: si el archivo no trae carrera, nivel o paralelo, se toman de aquí.
  const carreras = useResource<Carrera>("/carreras");
  const paralelos = useResource<Paralelo>(carrera && nivel ? `/paralelos?carrera=${carrera}&nivel=${nivel}` : null);
  const mallaDelParalelo = paralelos.data.find((p) => p.letra === paralelo)?.mallaVersion;
  const materias = useResource<MateriaMalla>(carrera && nivel && mallaDelParalelo ? `/carreras/${carrera}/materias?nivel=${nivel}&malla=${mallaDelParalelo}` : null);
  const niveles = carreras.data.find((c) => c.codigo === carrera)?.niveles ?? 9;
  async function submit(e: FormEvent) {
    e.preventDefault();
    if (!archivo) return;
    setBusy(true);
    setError("");
    setResultado("");
    try {
      const form = new FormData();
      form.append("archivo", archivo);
      if (carrera) form.append("carrera", carrera);
      if (nivel) form.append("nivel", nivel);
      if (paralelo) form.append("paralelo", paralelo);
      if (materia) form.append("materia", materia);
      const r = await api<{ creados: number; actualizados: number; total: number; reactivados: number; inscripciones: number }>(
        "/estudiantes/precarga",
        "POST",
        form,
      );
      setResultado(
        `Listo: ${r.creados} estudiante(s) nuevos y ${r.actualizados} actualizados (${r.total} en total). ${r.inscripciones} inscripciones en materias agregadas.${r.reactivados ? ` ${r.reactivados} estaban dados de baja y fueron reactivados.` : ""}`,
      );
      cargado();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }
  return (
    <Modal title="Cargar estudiantes desde un CSV" close={close} busy={busy}>
      <form onSubmit={submit}>
        <p className="muted">
          Elige la carrera, el nivel, el paralelo y la materia donde se cargarán los estudiantes. El archivo solo trae{" "}
          <strong>cedula, apellidos, nombres, correo</strong>. Cada carga es de una materia: para inscribirlos en otra
          materia, paralelo o nivel, vuelve a cargar el archivo con ese destino. Si hay algún error no se guarda nada; si la
          cédula ya existe, se actualizan sus datos y se agrega la materia. Cada estudiante puede sumar hasta 15 créditos
          por período, cursa una materia en un solo paralelo y pertenece a una sola carrera; si la carga incumple alguna
          de estas reglas, el sistema indica qué estudiantes y por qué.
        </p>
        <button className="text-button" type="button" onClick={descargarPlantilla}>
          <Download size={15} />
          Descargar plantilla
        </button>
        <div className="filters">
          <label>
            Carrera
            <select
              required
              value={carrera}
              onChange={(e) => {
                setCarrera(e.target.value);
                setNivel("");
                setParalelo("");
                setMateria("");
              }}
            >
              <option value="">Elige…</option>
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
              required
              value={nivel}
              disabled={!carrera}
              onChange={(e) => {
                setNivel(e.target.value);
                setParalelo("");
                setMateria("");
              }}
            >
              <option value="">Elige…</option>
              {Array.from({ length: niveles }, (_, i) => i + 1).map((n) => (
                <option key={n} value={n}>
                  Nivel {romano[n - 1]}
                </option>
              ))}
            </select>
          </label>
          <label>
            Paralelo
            <select
              required
              value={paralelo}
              disabled={!carrera || !nivel}
              onChange={(e) => {
                setParalelo(e.target.value);
                setMateria("");
              }}
            >
              <option value="">Elige…</option>
              {paralelos.data.map((p) => (
                <option key={p.id} value={p.letra}>
                  Paralelo {p.letra}
                {mallaDe(carreras.data.find((c) => c.codigo === carrera), p.mallaVersion)}
                </option>
              ))}
            </select>
          </label>
          <label>
            Materia
            <select
              required
              value={materia}
              disabled={!carrera || !nivel || !paralelo || !materias.data.length}
              onChange={(e) => setMateria(e.target.value)}
            >
              <option value="">Elige…</option>
              {materias.data.map((m) => (
                <option key={m.codigo} value={m.codigo}>
                  {m.nombre}
                {mallaDe(carreras.data.find((c) => c.codigo === carrera), m.mallaVersion)}
                </option>
              ))}
            </select>
          </label>
        </div>
        <label>
          Archivo CSV
          <input
            required
            type="file"
            accept=".csv,text/csv"
            onChange={(e) => {
              setArchivo(e.target.files?.[0] ?? null);
              setResultado("");
              setError("");
            }}
          />
        </label>
        <ErrorBox message={error} />
        {resultado && (
          <div role="status" className="success">
            {resultado}
          </div>
        )}
        <div className="modal-actions">
          <button className="secondary" type="button" disabled={busy} onClick={close}>
            Cerrar
          </button>
          <button className="primary" disabled={busy || !archivo || !carrera || !nivel || !paralelo || !materia}>
            {busy ? "Cargando…" : "Cargar"}
          </button>
        </div>
      </form>
    </Modal>
  );
}
