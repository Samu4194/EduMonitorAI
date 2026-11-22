import { useEffect, useState, type FormEvent } from "react";
import { Library, ArrowUpRight, RefreshCw, X, Plus, Upload, Download, Pencil, Play, Flag, Archive, RotateCcw } from "lucide-react";
import { api, type Carrera, type MateriaMalla, type Periodo } from "./api";
import { useResource } from "./useResource";
import { ErrorBox, Empty, Modal, Toast } from "./components";

const romano = ["I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X", "XI", "XII"];
const unidades: Record<string, string> = {
  BASICA: "Unidad básica",
  PROFESIONAL: "Unidad profesional",
  TITULACION: "Unidad de titulación",
};
const modalidades = ["Presencial", "Semipresencial", "En línea", "Híbrida"];
const dash = (n: number | null) => (n === null ? "—" : n);

function descargarPlantilla(prefijo: string) {
  const csv =
    "nivel,nombre,unidad,tipo,codigo_uta,horas_contacto_docente,horas_practico_experimental,horas_autonomas,horas_totales,creditos\n" +
    `1,Física,BASICA,REGULAR,${prefijo}0113,48,32,64,,\n` +
    "1,Álgebra Lineal,BASICA,REGULAR,,48,32,64,,\n" +
    "7,Prácticas de Servicio Comunitario,PROFESIONAL,SERVICIO_COMUNITARIO,,,,,96,\n";
  const url = URL.createObjectURL(new Blob(["﻿" + csv], { type: "text/csv;charset=utf-8" }));
  const a = document.createElement("a");
  a.href = url;
  a.download = "plantilla-malla.csv";
  a.click();
  URL.revokeObjectURL(url);
}

export function CatalogoPage({ admin }: { admin: boolean }) {
  const [estado, setEstado] = useState("activas");
  const carreras = useResource<Carrera>(admin && estado !== "activas" ? `/carreras?estado=${estado}` : "/carreras");
  const periodos = useResource<Periodo>("/periodos");
  const [sel, setSel] = useState<Carrera | null>(null),
    [cargaMalla, setCargaMalla] = useState<Carrera | null>(null),
    [formulario, setFormulario] = useState<Carrera | "nueva" | null>(null),
    [notice, setNotice] = useState(""),
    [error, setError] = useState("");
  const activo = periodos.data.find((p) => p.activo);
  const enCurso = activo?.estado === "En curso";
  // Dar de baja no borra nada: la carrera se oculta y no admite paralelos, mallas, docentes ni estudiantes nuevos; el historial y el código se conservan.
  async function cambiarEstadoCarrera(c: Carrera, baja: boolean) {
    if (
      baja &&
      !window.confirm(
        `¿Dar de baja ${c.nombre}?\nDejará de ofrecerse para nuevos paralelos, mallas, docentes y estudiantes, y los docentes dejarán de verla en su ficha. Su historial y su código se conservan y puede reactivarse.`,
      )
    )
      return;
    setNotice("");
    setError("");
    try {
      await api(`/carreras/${c.codigo}/${baja ? "baja" : "reactivar"}`, "PUT");
      setNotice(baja ? `${c.nombre} fue dada de baja.` : `${c.nombre} fue reactivada.`);
      if (sel?.codigo === c.codigo) setSel(null);
      carreras.reload();
    } catch (e) {
      setError((e as Error).message);
    }
  }
  async function cambiarPeriodo(iniciar: boolean) {
    if (!activo) return;
    const pregunta = iniciar
      ? `¿Iniciar el período ${activo.nombre}?\nLa malla de las carreras quedará bloqueada mientras el período esté en curso.`
      : `¿Finalizar el período ${activo.nombre}?\nSe podrán preparar mallas para el siguiente período.`;
    if (!window.confirm(pregunta)) return;
    setNotice("");
    setError("");
    try {
      await api(`/periodos/${activo.id}/${iniciar ? "iniciar" : "finalizar"}`, "PUT");
      setNotice(iniciar ? `El período ${activo.nombre} está en curso.` : `El período ${activo.nombre} fue finalizado.`);
      periodos.reload();
    } catch (e) {
      setError((e as Error).message);
    }
  }
  return (
    <>
      <div className="page-heading">
        <div>
          <span className="eyebrow">GESTIÓN ACADÉMICA</span>
          <h1>Carreras y mallas</h1>
          <p className="muted">Carreras de la facultad con su malla curricular, créditos y horas.</p>
        </div>
        {admin && (
          <div className="heading-actions">
            <button className="primary" onClick={() => setFormulario("nueva")}>
              <Plus size={16} />
              Nueva carrera
            </button>
          </div>
        )}
      </div>
      {activo && (
        <section className="panel periodo-banner" aria-label="Estado del período académico">
          <div>
            <strong>{activo.nombre}</strong>
            <span className={`estado ${enCurso ? "pend" : "ok"}`}>{activo.estado}</span>
            <p className="small muted">
              {activo.estado === "Preparación"
                ? "La malla de las carreras se puede cargar y actualizar hasta que inicie el período."
                : activo.estado === "En curso"
                  ? "El período está en curso: la malla está bloqueada hasta que finalice."
                  : "El período finalizó: la malla se puede preparar para el siguiente."}
            </p>
          </div>
          {admin && activo.estado === "Preparación" && (
            <button className="secondary" onClick={() => void cambiarPeriodo(true)}>
              <Play size={15} />
              Iniciar período
            </button>
          )}
          {admin && activo.estado === "En curso" && (
            <button className="secondary" onClick={() => void cambiarPeriodo(false)}>
              <Flag size={15} />
              Finalizar período
            </button>
          )}
        </section>
      )}
      <ErrorBox message={carreras.error || error} retry={carreras.error ? carreras.reload : undefined} />
      <Toast message={notice} close={() => setNotice("")} />
      <div className="toolbar">
        <span className="muted">
          {carreras.loading
            ? "Cargando…"
            : carreras.error
              ? "Datos no disponibles"
              : `${carreras.data.length} ${carreras.data.length === 1 ? "carrera" : "carreras"}`}
        </span>
        {admin && (
          <label className="inline-select">
            Estado
            <select value={estado} onChange={(e) => setEstado(e.target.value)}>
              <option value="activas">Activas</option>
              <option value="bajas">Dadas de baja</option>
              <option value="todas">Todas</option>
            </select>
          </label>
        )}
        <button className="secondary" disabled={carreras.loading} onClick={carreras.reload}>
          <RefreshCw size={15} />
          Actualizar
        </button>
      </div>
      {!carreras.error && !carreras.loading && !carreras.data.length && (
        <Empty text={estado === "bajas" ? "No hay carreras dadas de baja" : "No hay carreras disponibles para tu cuenta"} />
      )}
      <div className="course-grid">
        {carreras.data.map((c) => (
          <section className={`course-card${c.activa ? "" : " baja"}`} key={c.codigo}>
            <div className="card-top">
              <span className="course-icon">
                <Library size={22} />
              </span>
              <span className="pill">{c.codigo}</span>
            </div>
            <h2>
              {c.nombre}
              {!c.activa && <span className="estado pend baja">Dada de baja</span>}
            </h2>
            <p className="muted">
              {c.modalidad} · {c.niveles} niveles · UTA {c.prefijoUta}
            </p>
            <p className="small muted">
              {c.versiones === 0
                ? "Sin malla cargada"
                : `Malla ${c.mallaVersion}${c.versiones > 1 ? ` (${c.versiones} versiones)` : ""} · ${c.materias} materias · ${c.horasTotales} horas · ${c.creditosTotales} créditos`}
            </p>
            <div className="card-footer">
              <button className="text-button" disabled={c.versiones === 0} onClick={() => setSel(c)}>
                Ver malla <ArrowUpRight size={16} />
              </button>
              {admin && !c.activa && (
                <button className="text-button" onClick={() => void cambiarEstadoCarrera(c, false)}>
                  <RotateCcw size={15} />
                  Reactivar
                </button>
              )}
              {admin && c.activa && (
                <>
                  <button
                    className="text-button"
                    disabled={enCurso}
                    title={enCurso ? "El período está en curso: la malla está bloqueada" : "Cargar o actualizar la malla con un CSV"}
                    onClick={() => setCargaMalla(c)}
                  >
                    <Upload size={15} />
                    {c.versiones === 0 ? "Cargar malla" : "Actualizar malla"}
                  </button>
                  <button aria-label={`Editar ${c.nombre}`} title="Editar la carrera" onClick={() => setFormulario(c)}>
                    <Pencil size={15} />
                  </button>
                  <button aria-label={`Dar de baja ${c.nombre}`} title="Dar de baja la carrera" onClick={() => void cambiarEstadoCarrera(c, true)}>
                    <Archive size={15} />
                  </button>
                </>
              )}
            </div>
          </section>
        ))}
      </div>
      {sel && <Malla key={sel.codigo} carrera={sel} close={() => setSel(null)} />}
      {formulario && (
        <CarreraForm
          carrera={formulario === "nueva" ? undefined : formulario}
          close={() => setFormulario(null)}
          saved={(texto) => {
            setFormulario(null);
            setNotice(texto);
            carreras.reload();
          }}
        />
      )}
      {cargaMalla && (
        <MallaForm
          carrera={cargaMalla}
          close={() => setCargaMalla(null)}
          saved={(texto) => {
            setCargaMalla(null);
            setSel(null);
            setNotice(texto);
            carreras.reload();
          }}
        />
      )}
    </>
  );
}

// Crear o editar una carrera. El código y el prefijo UTA los asigna el sistema: son únicos, no se repiten y no se pueden cambiar.
function CarreraForm({ carrera, close, saved }: { carrera?: Carrera; close: () => void; saved: (texto: string) => void }) {
  const [nombre, setNombre] = useState(carrera?.nombre ?? ""),
    [modalidad, setModalidad] = useState(carrera?.modalidad ?? modalidades[0]),
    [niveles, setNiveles] = useState(String(carrera?.niveles ?? 9)),
    [codigo, setCodigo] = useState(carrera?.codigo ?? ""),
    [prefijo, setPrefijo] = useState(carrera?.prefijoUta ?? ""),
    [busy, setBusy] = useState(false),
    [error, setError] = useState("");
  // Muestra el código y el prefijo UTA que se asignarán, a medida que se escribe el nombre
  useEffect(() => {
    if (carrera || nombre.trim().length < 3) {
      if (!carrera) {
        setCodigo("");
        setPrefijo("");
      }
      return;
    }
    const espera = setTimeout(() => {
      api<{ codigo: string; prefijoUta: string }>(`/carreras/codigo-sugerido?nombre=${encodeURIComponent(nombre.trim())}`)
        .then((r) => {
          setCodigo(r.codigo);
          setPrefijo(r.prefijoUta);
        })
        .catch(() => {
          setCodigo("");
          setPrefijo("");
        });
    }, 300);
    return () => clearTimeout(espera);
  }, [nombre, carrera]);
  async function submit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError("");
    try {
      const cuerpo = { nombre: nombre.trim(), modalidad, niveles: Number(niveles) };
      const r = carrera
        ? await api<Carrera>(`/carreras/${carrera.codigo}`, "PUT", cuerpo)
        : await api<Carrera>("/carreras", "POST", cuerpo);
      saved(carrera ? `${r.nombre} se actualizó.` : `${r.nombre} se creó con el código ${r.codigo}. Ahora carga su malla.`);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }
  return (
    <Modal title={carrera ? "Editar carrera" : "Nueva carrera"} close={close} busy={busy}>
      <form onSubmit={submit}>
        <div className="campos-fijos">
          <div>
            <span className="etiqueta">Código</span>
            <output className="campo-fijo" aria-label="Código de la carrera">
              {codigo}
            </output>
          </div>
          <div>
            <span className="etiqueta">Prefijo UTA</span>
            <output className="campo-fijo" aria-label="Prefijo UTA de la carrera">
              {prefijo}
            </output>
          </div>
        </div>
        <p className="small muted">Identificadores únicos asignados por el sistema.</p>
        <label>
          Nombre
          <input required minLength={3} maxLength={120} value={nombre} onChange={(e) => setNombre(e.target.value)} />
        </label>
        <label>
          Modalidad
          <select value={modalidad} onChange={(e) => setModalidad(e.target.value)}>
            {[...new Set([...modalidades, modalidad])].map((m) => (
              <option key={m}>{m}</option>
            ))}
          </select>
        </label>
        <label>
          Número de niveles
          <input
            required
            type="number"
            min={carrera?.niveles ?? 1}
            max={12}
            value={niveles}
            onChange={(e) => setNiveles(e.target.value)}
          />
        </label>
        <p className="small muted">Las horas y los créditos totales se calculan con la malla cuando se carga.</p>
        <ErrorBox message={error} />
        <div className="modal-actions">
          <button className="secondary" type="button" disabled={busy} onClick={close}>
            Cancelar
          </button>
          <button className="primary" disabled={busy || (!carrera && (!codigo || !prefijo))}>
            {busy ? "Guardando…" : carrera ? "Guardar" : "Crear carrera"}
          </button>
        </div>
      </form>
    </Modal>
  );
}

// Carga o actualiza la malla. Si la malla vigente ya se usa en algún paralelo se crea una versión nueva: los estudiantes
// registrados siguen con su malla y los nuevos entran con la nueva.
function MallaForm({ carrera, close, saved }: { carrera: Carrera; close: () => void; saved: (texto: string) => void }) {
  const [archivo, setArchivo] = useState<File | null>(null),
    [busy, setBusy] = useState(false),
    [error, setError] = useState("");
  async function submit(e: FormEvent) {
    e.preventDefault();
    if (!archivo) return;
    setBusy(true);
    setError("");
    try {
      const form = new FormData();
      form.append("archivo", archivo);
      const r = await api<{ version: number; nuevaVersion: boolean; materias: number; horasTotales: number; creditosTotales: number }>(
        `/carreras/${carrera.codigo}/malla`,
        "POST",
        form,
      );
      saved(
        `Malla ${r.version} de ${carrera.nombre} guardada: ${r.materias} materias, ${r.horasTotales} horas y ${r.creditosTotales} créditos.` +
          (!r.nuevaVersion
            ? " Reemplazó a la malla vigente, que todavía no se usaba."
            : r.version > 1
              ? " Es una versión nueva: los estudiantes registrados siguen con la anterior."
              : ""),
      );
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }
  const tieneMalla = carrera.versiones > 0;
  return (
    <Modal title={`${tieneMalla ? "Actualizar" : "Cargar"} la malla de ${carrera.nombre}`} close={close} busy={busy}>
      <form onSubmit={submit}>
        <p className="muted">
          El archivo trae la malla completa, con materias en los {carrera.niveles} niveles. Columnas: <strong>nivel, nombre, unidad</strong> y,
          según la materia, <strong>tipo, codigo_uta, horas_contacto_docente, horas_practico_experimental, horas_autonomas, horas_totales, creditos</strong>.
          Si no indicas los créditos se calculan con 48 horas por crédito. Los codigo_uta, si los incluyes, son de 8 dígitos y empiezan con el prefijo UTA de la carrera ({carrera.prefijoUta}). Si hay algún error no se guarda nada.
        </p>
        {tieneMalla && (
          <p className="muted">
            Si la malla {carrera.mallaVersion} todavía no se usa en ningún paralelo, se reemplaza. Si ya se usa, se crea la malla{" "}
            {carrera.mallaVersion + 1}: los estudiantes registrados continúan con la actual y los nuevos entran con la nueva.
          </p>
        )}
        <button className="text-button" type="button" onClick={() => descargarPlantilla(carrera.prefijoUta)}>
          <Download size={15} />
          Descargar plantilla
        </button>
        <label>
          Archivo CSV
          <input
            required
            type="file"
            accept=".csv,text/csv"
            onChange={(e) => {
              setArchivo(e.target.files?.[0] ?? null);
              setError("");
            }}
          />
        </label>
        <ErrorBox message={error} />
        <div className="modal-actions">
          <button className="secondary" type="button" disabled={busy} onClick={close}>
            Cerrar
          </button>
          <button className="primary" disabled={busy || !archivo}>
            {busy ? "Cargando…" : "Cargar"}
          </button>
        </div>
      </form>
    </Modal>
  );
}

function Malla({ carrera, close }: { carrera: Carrera; close: () => void }) {
  const [version, setVersion] = useState(carrera.mallaVersion);
  const materias = useResource<MateriaMalla>(`/carreras/${carrera.codigo}/materias?malla=${version}`);
  const niveles = Array.from({ length: carrera.niveles }, (_, i) => i + 1);
  return (
    <section className="malla">
      <div className="panel-heading">
        <h2>
          Malla de {carrera.nombre}
          {carrera.versiones > 1 ? ` · versión ${version}` : ""}
        </h2>
        {carrera.versiones > 1 && (
          <label className="inline-select">
            Versión
            <select value={version} onChange={(e) => setVersion(Number(e.target.value))}>
              {Array.from({ length: carrera.versiones }, (_, i) => i + 1).map((v) => (
                <option key={v} value={v}>
                  Malla {v}
                  {v === carrera.mallaVersion ? " (vigente)" : ""}
                </option>
              ))}
            </select>
          </label>
        )}
        <button aria-label="Cerrar la malla" onClick={close}>
          <X size={18} />
          Cerrar
        </button>
      </div>
      <ErrorBox message={materias.error} retry={materias.reload} />
      {materias.loading && <p role="status">Cargando la malla…</p>}
      {niveles.map((n) => {
        const filas = materias.data.filter((m) => m.nivel === n);
        if (!filas.length) return null;
        const horas = filas.reduce((s, m) => s + m.horasTotales, 0);
        const creditos = filas.reduce((s, m) => s + m.creditos, 0);
        return (
          <section className="panel table-wrap malla-nivel" key={n}>
            <h3>
              Nivel {romano[n - 1]}
              <small className="muted">
                {" "}
                · {unidades[filas[0].unidad] ?? filas[0].unidad} · {horas} h · {creditos} créditos
              </small>
            </h3>
            <table>
              <thead>
                <tr>
                  <th>Código</th>
                  <th>Materia</th>
                  <th title="Aprendizaje en contacto con el docente">CD</th>
                  <th title="Aprendizaje práctico-experimental">APE</th>
                  <th title="Aprendizaje autónomo">AA</th>
                  <th>Horas</th>
                  <th>Créditos</th>
                </tr>
              </thead>
              <tbody>
                {filas.map((m) => (
                  <tr key={m.id}>
                    <td>{m.codigoUta || m.codigo}</td>
                    <td>
                      <strong>{m.nombre}</strong>
                    </td>
                    <td>{dash(m.horasContactoDocente)}</td>
                    <td>{dash(m.horasPracticoExperimental)}</td>
                    <td>{dash(m.horasAutonomas)}</td>
                    <td>{m.horasTotales}</td>
                    <td>
                      {m.creditos}
                      {m.creditosDerivado ? "*" : ""}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </section>
        );
      })}
      {materias.data.some((m) => m.creditosDerivado) && (
        <p className="small muted">
          * Su crédito se calculó con 48 horas por crédito (prácticas, servicio comunitario, titulación o créditos no indicados en el archivo).
        </p>
      )}
    </section>
  );
}
