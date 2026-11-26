import { useState } from "react";
import { ArrowUpRight } from "lucide-react";
import { type Curso, type ListaEstudiante, type Matriz, type PuntosTipo, type Rendimiento, type EstudianteDetalle } from "./api";
import { useObject, useResource } from "./useResource";
import { Modal, ErrorBox, Empty } from "./components";
import { fmt, EstadoPill, BarraPuntos, LeyendaPuntos, tonoNota } from "./AulaComun";

const nombreTipo: Record<string, string> = { Tarea: "Tareas", APE: "APE", Examen: "Exámenes" };
// "2.40 / 3" : puntos ganados de los que aporta ese tipo al total de 10.
const puntos = (p: PuntosTipo) => (p.actividades === 0 ? "—" : `${p.puntos.toFixed(2)} / ${Number(p.maximo.toFixed(2))}`);

// Rangos de la distribución de notas. Los cortes siguen la nota mínima habitual (7): lo que queda por debajo se ve de un vistazo.
const rangos = [
  { desde: 0, hasta: 2, texto: "0 – 1.9" },
  { desde: 2, hasta: 4, texto: "2 – 3.9" },
  { desde: 4, hasta: 6, texto: "4 – 5.9" },
  { desde: 6, hasta: 7, texto: "6 – 6.9" },
  { desde: 7, hasta: 8.5, texto: "7 – 8.4" },
  { desde: 8.5, hasta: 10.01, texto: "8.5 – 10" },
];
const colorTipo: Record<string, string> = { Tarea: "t-tarea", APE: "t-ape", Examen: "t-examen" };
const categorias = [
  { id: "", texto: "Todas" },
  { id: "Tarea", texto: "Tareas" },
  { id: "APE", texto: "APE" },
  { id: "Examen", texto: "Exámenes" },
];
const todos: Record<string, string> = { Tarea: "todas las tareas", APE: "todos los APE", Examen: "todos los exámenes" };

// Cómo va el curso en notas. Se filtra por categoría (tareas, APE o exámenes), por una actividad y/o por un estudiante: con un estudiante elegido
// la gráfica muestra sus notas y las compara con el promedio del grupo.
function GraficaCurso({ datos, notaMinima }: { datos: Matriz; notaMinima: number }) {
  const [tipo, setTipo] = useState(""),
    [actividad, setActividad] = useState(""),
    [estudiante, setEstudiante] = useState("");
  const delTipo = datos.actividades.filter((a) => !tipo || a.tipo === tipo);
  const elegidas = delTipo.filter((a) => !actividad || String(a.id) === actividad);
  const alumnos = [...datos.estudiantes].sort((x, y) => `${x.apellidos} ${x.nombres}`.localeCompare(`${y.apellidos} ${y.nombres}`, "es"));
  const elegido = estudiante ? datos.estudiantes.find((e) => String(e.estudianteId) === estudiante) : undefined;
  const promedioDe = (notas: number[]) => (notas.length ? notas.reduce((s, n) => s + n, 0) / notas.length : null);
  // Una fila por actividad: las notas de la selección (todo el grupo o solo el estudiante) y, para comparar, el promedio del grupo.
  const filas = elegidas.map((a) => {
    const deTodos = datos.estudiantes.map((e) => e.notas[String(a.id)]).filter((n): n is number => n !== undefined);
    const propia = elegido ? elegido.notas[String(a.id)] : undefined;
    const notas = elegido ? (propia === undefined ? [] : [propia]) : deTodos;
    return { actividad: a, notas, promedio: promedioDe(notas), grupo: promedioDe(deTodos) };
  });
  const todas = filas.flatMap((f) => f.notas);
  const base = elegido ? 1 : datos.estudiantes.length;
  const total = base * elegidas.length;
  const conteo = rangos.map((r) => todas.filter((n) => n >= r.desde && n < r.hasta).length);
  const maximo = Math.max(1, ...conteo);
  const promedio = promedioDe(todas);
  const promedioGrupo = promedioDe(filas.flatMap((f) => datos.estudiantes.map((e) => e.notas[String(f.actividad.id)]).filter((n): n is number => n !== undefined)));
  const bajoMinimo = todas.filter((n) => n < notaMinima).length;
  const alcance = actividad ? elegidas[0]?.nombre : tipo ? todos[tipo] : "todas las actividades";
  const quien = elegido ? `${elegido.apellidos} ${elegido.nombres}` : "el curso";
  const hayFiltros = !!(tipo || actividad || estudiante);
  return (
    <section className="grafica-curso" aria-label="Cómo va el curso en notas">
      <div className="grafica-filtros">
        <div className="filtro-grupo">
          <span className="filtro-etiqueta">Tipo de actividad</span>
          <div className="chips-filtro" role="group" aria-label="Categoría de actividad">
            {categorias.map((c) => (
              <button
                key={c.id}
                className={tipo === c.id ? "activo" : ""}
                aria-pressed={tipo === c.id}
                onClick={() => {
                  setTipo(c.id);
                  setActividad("");
                }}
              >
                {c.texto}
                <span>{datos.actividades.filter((a) => !c.id || a.tipo === c.id).length}</span>
              </button>
            ))}
          </div>
        </div>
        <label className="filtro-grupo">
          <span className="filtro-etiqueta">Actividad</span>
          <select value={actividad} disabled={!delTipo.length} onChange={(e) => setActividad(e.target.value)}>
            <option value="">{tipo ? todos[tipo][0].toUpperCase() + todos[tipo].slice(1) : "Todas las actividades"}</option>
            {delTipo.map((a) => (
              <option key={a.id} value={a.id}>
                {tipo ? a.nombre : `${a.tipo} · ${a.nombre}`}
              </option>
            ))}
          </select>
        </label>
        <label className="filtro-grupo">
          <span className="filtro-etiqueta">Estudiante</span>
          <select value={estudiante} disabled={!alumnos.length} onChange={(e) => setEstudiante(e.target.value)}>
            <option value="">Todo el curso</option>
            {alumnos.map((e) => (
              <option key={e.estudianteId} value={e.estudianteId}>
                {e.apellidos} {e.nombres}
              </option>
            ))}
          </select>
        </label>
        {hayFiltros && (
          <button
            className="text-button filtro-limpiar"
            onClick={() => {
              setTipo("");
              setActividad("");
              setEstudiante("");
            }}
          >
            Quitar filtros
          </button>
        )}
      </div>
      {!datos.actividades.length ? (
        <Empty text="Todavía no hay actividades. Crea actividades y califícalas para ver cómo va el curso." />
      ) : (
        <>
          <p className="grafica-contexto">
            Mostrando <strong>{alcance}</strong> de <strong>{quien}</strong>
          </p>
          <div className="grafica-tiles" aria-label="Resumen de la selección">
            <div>
              <span>Promedio</span>
              <strong>{promedio === null ? "—" : fmt(promedio)}</strong>
              <small>{elegido && promedioGrupo !== null ? `Curso: ${fmt(promedioGrupo)}` : "sobre 10"}</small>
            </div>
            {elegido && (
              <div>
                <span>Resultado final</span>
                <strong>{fmt(elegido.resultado)}</strong>
                <small>ponderado por tipo</small>
              </div>
            )}
            <div>
              <span>Notas registradas</span>
              <strong>
                {todas.length} <em>de {total}</em>
              </strong>
              <small>{total - todas.length > 0 ? `${total - todas.length} pendientes` : "completas"}</small>
            </div>
            <div className={bajoMinimo > 0 ? "alerta" : ""}>
              <span>Bajo la nota mínima</span>
              <strong>{bajoMinimo}</strong>
              <small>menores que {notaMinima}</small>
            </div>
          </div>
          <div className="grafica-grid">
            <div className="grafica-panel">
              <h3>{elegidas.length === 1 && !elegido ? "Estudiantes por rango de nota" : elegido ? "Sus notas por rango" : "Notas por rango"}</h3>
              <div className="histograma" role="img" aria-label={`Distribución de notas de ${alcance}: ${rangos.map((r, i) => `${r.texto}: ${conteo[i]}`).join(", ")}`}>
                {rangos.map((r, i) => (
                  <div className="columna" key={r.texto}>
                    <span className="cantidad">{conteo[i]}</span>
                    <div className="barra-v">
                      <i
                        style={{
                          height: `${(conteo[i] / maximo) * 100}%`,
                          background: `hsl(${tonoNota((r.desde + Math.min(r.hasta, 10)) / 2)} 38% 55%)`,
                        }}
                      />
                    </div>
                    <span className="rango">{r.texto}</span>
                  </div>
                ))}
              </div>
              <small className="muted">
                {todas.length === 0 ? "Aún no hay notas para esta selección." : `El corte en ${notaMinima} marca la nota mínima: ${bajoMinimo} ${bajoMinimo === 1 ? "nota queda" : "notas quedan"} por debajo.`}
              </small>
            </div>
            <div className="grafica-panel">
              <h3>{elegido ? "Sus notas frente al grupo" : "Promedio por actividad"}</h3>
              <ul className="promedios">
                {filas.map((f) => (
                  <li key={f.actividad.id}>
                    <div className="promedio-etiqueta">
                      <span title={f.actividad.nombre}>{f.actividad.nombre}</span>
                      <strong>{f.promedio === null ? (elegido ? "Pendiente" : "—") : fmt(f.promedio)}</strong>
                    </div>
                    <div className="promedio-pista" role="img" aria-label={`${f.actividad.nombre}: ${f.promedio === null ? "sin notas" : fmt(f.promedio)} de 10`}>
                      <span className={`relleno ${colorTipo[f.actividad.tipo]}`} style={{ width: `${f.promedio === null ? 0 : Math.min(100, f.promedio * 10)}%` }} />
                      <span className="minimo" style={{ left: `${notaMinima * 10}%` }} title={`Nota mínima ${notaMinima}`} />
                      {elegido && f.grupo !== null && <span className="grupo-marca" style={{ left: `${Math.min(100, f.grupo * 10)}%` }} title={`Promedio del grupo ${fmt(f.grupo)}`} />}
                    </div>
                    <small className="muted">
                      {f.actividad.tipo}
                      {elegido ? (f.grupo === null ? "" : ` · grupo ${fmt(f.grupo)}`) : ` · ${f.notas.length} de ${datos.estudiantes.length} calificadas`}
                    </small>
                  </li>
                ))}
              </ul>
              <small className="muted">
                La línea roja marca la nota mínima ({notaMinima}).{elegido && " El rombo marca el promedio del grupo."}
              </small>
            </div>
          </div>
        </>
      )}
    </section>
  );
}

// Cómo va cada tema del curso: promedio de las notas de las actividades relacionadas con él. Lo que queda por debajo de la nota mínima se marca.
function TemasRendimiento({ temas, notaMinima }: { temas: Rendimiento["temas"]; notaMinima: number }) {
  return (
    <section className="panel table-wrap rendimiento-temas" aria-label="Rendimiento por tema">
      <h3>Rendimiento por tema</h3>
      <table className="compacta">
        <thead>
          <tr>
            <th>Tema</th>
            <th>Unidad</th>
            <th>Actividades</th>
            <th>Promedio</th>
            <th>Notas registradas</th>
            <th title={`Notas por debajo de ${notaMinima}`}>Notas bajas</th>
          </tr>
        </thead>
        <tbody>
          {temas.map((t) => (
            <tr key={t.temaId} className={t.promedio !== null && t.promedio < notaMinima ? "fila-riesgo" : ""}>
              <td>
                <strong>{t.tema}</strong>
              </td>
              <td>{t.unidad}</td>
              <td>{t.actividades === 0 ? <span className="muted">Sin actividades</span> : t.actividades}</td>
              <td>{fmt(t.promedio)}</td>
              <td>
                {t.calificadas}
                {t.pendientes > 0 && <small className="muted"> · {t.pendientes} pendientes</small>}
              </td>
              <td>{t.notasBajas}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </section>
  );
}

// Pestaña «Rendimiento académico» del curso (HU-08): gráfica de cómo va el curso en notas y, debajo, el seguimiento de cada estudiante.
export function RendimientoTab({ curso, irAAlertas }: { curso: Curso; irAAlertas?: () => void }) {
  const datos = useObject<Rendimiento>(`/rendimiento/curso/${curso.id}`);
  const matriz = useObject<Matriz>(`/calificaciones/curso/${curso.id}`);
  // La lista aporta el correo; el rendimiento, las notas.
  const lista = useResource<ListaEstudiante>(`/estudiantes/curso/${curso.id}`);
  const [detalle, setDetalle] = useState<number | null>(null),
    [soloRiesgo, setSoloRiesgo] = useState(false);
  const r = datos.data;
  const correo = new Map(lista.data.map((e) => [e.id, e.correo]));
  const filas = r?.estudiantes.filter((e) => !soloRiesgo || e.estado === "En riesgo") ?? [];
  const tipos = r?.estudiantes[0]?.desglose ?? [];
  const recargar = () => {
    datos.reload();
    matriz.reload();
    lista.reload();
  };
  return (
    <>
      <div className="toolbar">
        <div>
          <h2 className="seccion">Cómo va el curso</h2>
          <p className="muted small">Las notas de ahora mismo. Filtra por tipo de actividad, por actividad o por estudiante.</p>
        </div>
      </div>
      <ErrorBox message={datos.error || matriz.error} retry={recargar} />
      {matriz.data && <GraficaCurso key={matriz.data.actividades.length} datos={matriz.data} notaMinima={matriz.data.ponderacion.notaMinima} />}
      {r && r.temas.length > 0 && <TemasRendimiento temas={r.temas} notaMinima={r.resumen.notaMinima} />}
      {(datos.loading || matriz.loading) && !r && <p role="status">Cargando…</p>}
      {r && !r.resumen.totalEstudiantes && <Empty text="Este curso todavía no tiene estudiantes. El administrador los carga en la materia y el paralelo." />}
      {r && r.resumen.totalEstudiantes > 0 && (
        <>
          <div className="toolbar seccion-estudiantes">
            <h2 className="seccion">Estudiantes</h2>
            <div className="chips-filtro" role="group" aria-label="Filtrar estudiantes">
              <button className={!soloRiesgo ? "activo" : ""} aria-pressed={!soloRiesgo} onClick={() => setSoloRiesgo(false)}>
                Todos <span>{r.resumen.totalEstudiantes}</span>
              </button>
              <button className={soloRiesgo ? "activo" : ""} aria-pressed={soloRiesgo} onClick={() => setSoloRiesgo(true)}>
                En riesgo <span>{r.resumen.enRiesgo}</span>
              </button>
            </div>
            {irAAlertas && r.resumen.enRiesgo > 0 && (
              <button className="text-button" onClick={irAAlertas} title="Por qué están en riesgo y qué hacer con cada uno">
                Ver por qué y qué hacer en el Asistente <ArrowUpRight size={14} />
              </button>
            )}
          </div>
          {r.resumen.actividades > 0 && !r.resumen.completo && (
            <div className="aviso" role="status">
              Hay {r.resumen.notasPendientes} {r.resumen.notasPendientes === 1 ? "calificación pendiente" : "calificaciones pendientes"}: los puntos son los
              acumulados hasta ahora. Una nota pendiente no cuenta como cero.
            </div>
          )}
          {tipos.length > 0 && <LeyendaPuntos desglose={tipos} />}
          {!filas.length && <Empty text="Ningún estudiante está en riesgo." />}
          {filas.length > 0 && (
            <section className="panel table-wrap">
              <table className="compacta">
                <thead>
                  <tr>
                    <th>N.º</th>
                    <th>Estudiante</th>
                    {tipos.map((t) => (
                      <th key={t.tipo} title={`${nombreTipo[t.tipo]}: aportan ${Number(t.maximo.toFixed(2))} de los 10 puntos`}>
                        {nombreTipo[t.tipo]}
                        <small className="block muted">{t.porcentaje}%</small>
                      </th>
                    ))}
                    <th title="Puntos ganados hasta ahora, de 10">
                      Acumulado
                      <small className="block muted">de 10</small>
                    </th>
                    <th title="Promedio de lo ya calificado">Promedio</th>
                    <th>Estado</th>
                    <th aria-label="Detalle" />
                  </tr>
                </thead>
                <tbody>
                  {filas.map((e, i) => (
                    <tr key={e.estudianteId} className={e.estado === "En riesgo" ? "fila-riesgo" : ""}>
                      <td>{i + 1}</td>
                      <td>
                        <strong>
                          {e.apellidos} {e.nombres}
                        </strong>
                        <small className="block muted">{correo.get(e.estudianteId) ?? ""}</small>
                      </td>
                      {e.desglose.map((p) => (
                        <td key={p.tipo}>{puntos(p)}</td>
                      ))}
                      <td>
                        <BarraPuntos desglose={e.desglose} acumulado={e.acumulado} />
                      </td>
                      <td>{fmt(e.resultado)}</td>
                      <td>
                        <EstadoPill estado={e.estado} definitivo={e.definitivo} />
                      </td>
                      <td>
                        <button className="text-button" onClick={() => setDetalle(e.estudianteId)}>
                          Ver notas <ArrowUpRight size={14} />
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </section>
          )}
          <p className="muted small">
            Cada tipo de actividad aporta al total de 10 según su porcentaje: se toma el promedio de sus notas ya registradas. «Acumulado» son los puntos ganados
            hasta ahora; «Promedio» es el rendimiento sobre lo ya calificado y define el estado.
          </p>
        </>
      )}
      {detalle !== null && <Detalle cursoId={curso.id} estudianteId={detalle} close={() => setDetalle(null)} />}
    </>
  );
}

// Ficha de un estudiante en el curso: sus puntos por tipo de actividad y cada una de sus notas.
function Detalle({ cursoId, estudianteId, close }: { cursoId: number; estudianteId: number; close: () => void }) {
  const d = useObject<EstudianteDetalle>(`/rendimiento/curso/${cursoId}/estudiante/${estudianteId}`);
  const e = d.data;
  return (
    <Modal title={e ? `${e.apellidos} ${e.nombres}` : "Estudiante"} close={close}>
      <ErrorBox message={d.error} retry={d.reload} />
      {d.loading && <p role="status">Cargando…</p>}
      {e && (
        <>
          <p className="muted">{e.correo}</p>
          <div className="desglose">
            {e.desglose.map((p) => (
              <div key={p.tipo}>
                <span>
                  {nombreTipo[p.tipo]} · {p.porcentaje}%
                </span>
                <strong>{puntos(p)}</strong>
                <small className="muted">
                  {p.actividades === 0 ? "Sin actividades" : p.promedio === null ? "Sin notas todavía" : `Promedio ${fmt(p.promedio)} · ${p.calificadas} de ${p.actividades} calificadas`}
                </small>
              </div>
            ))}
            <div className="total">
              <span>Acumulado</span>
              <BarraPuntos desglose={e.desglose} acumulado={e.acumulado} />
              <small>
                <EstadoPill estado={e.estado} definitivo={e.definitivo} />
              </small>
            </div>
          </div>
          <h3>Calificaciones</h3>
          <div className="table-wrap">
            <table>
              <thead>
                <tr>
                  <th>Actividad</th>
                  <th>Tipo</th>
                  <th>Temas</th>
                  <th>Nota</th>
                </tr>
              </thead>
              <tbody>
                {e.notas.map((n) => (
                  <tr key={n.actividadId} className={n.nota !== null && n.nota < e.notaMinima ? "fila-riesgo" : ""}>
                    <td>{n.actividad}</td>
                    <td>{n.tipo}</td>
                    <td>{n.temas.length ? n.temas.join(", ") : <span className="muted">Sin tema</span>}</td>
                    <td>{n.nota === null ? <em className="muted">Pendiente</em> : fmt(n.nota)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          {!e.notas.length && <p className="muted">El curso todavía no tiene actividades.</p>}
          {e.temas.length > 0 && (
            <>
              <h3>Por tema</h3>
              <ul className="temas-estudiante">
                {e.temas.map((t) => (
                  <li key={t.temaId} className={t.promedio !== null && t.promedio < e.notaMinima ? "fila-riesgo" : ""}>
                    <span>{t.tema}</span>
                    <strong>{fmt(t.promedio)}</strong>
                    <small className="muted">
                      {t.calificadas} {t.calificadas === 1 ? "nota" : "notas"}
                      {t.pendientes > 0 && ` · ${t.pendientes} pendientes`}
                    </small>
                  </li>
                ))}
              </ul>
            </>
          )}
        </>
      )}
    </Modal>
  );
}
