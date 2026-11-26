import { useEffect, useState, type FormEvent } from "react";
import { Save, SlidersHorizontal, Pencil } from "lucide-react";
import { api, type Curso, type FilaNotas, type Matriz, type Ponderacion } from "./api";
import { useObject } from "./useResource";
import { Modal, ErrorBox, Empty, Toast } from "./components";
import { fmt, estiloNota, EstadoPill } from "./AulaComun";

// Pestaña «Calificaciones» del curso (HU-07). Sin actividad elegida es la vista general de las notas (solo lectura).
// Para calificar se elige una actividad: solo esa columna queda a la vista y se puede escribir en ella. Las notas ya guardadas
// se bloquean y se modifican una por una en una ventana. Se llega con la actividad elegida desde «Calificar» en Actividades.
export function CalificacionesTab({
  curso,
  lectura,
  actividadInicial,
  sucio,
  cambio,
  irAActividades,
}: {
  curso: Curso;
  lectura: boolean;
  actividadInicial: string;
  // Avisa al curso si hay notas escritas sin guardar, para no perderlas al cambiar de pestaña.
  sucio: (hay: boolean) => void;
  cambio: () => void;
  irAActividades: () => void;
}) {
  const matriz = useObject<Matriz>(`/calificaciones/curso/${curso.id}`);
  const [actividad, setActividad] = useState(actividadInicial),
    // Primero se elige la categoría (tareas, APE o exámenes) y después una actividad de ella o todas.
    [tipoElegido, setTipoElegido] = useState(""),
    // "estudiante:actividad" -> texto escrito en la casilla. Solo se guardan las casillas modificadas.
    [cambios, setCambios] = useState<Record<string, string>>({}),
    // Nota ya guardada que se está modificando en la ventana
    [modal, setModal] = useState<{ fila: FilaNotas; actividad: Matriz["actividades"][number] } | null>(null),
    [notice, setNotice] = useState(""),
    [error, setError] = useState(""),
    [busy, setBusy] = useState(false),
    [pesos, setPesos] = useState(false);
  const datos = matriz.data;
  // Si se llega con «Calificar» desde una actividad, su categoría queda elegida
  const tipo = tipoElegido || datos?.actividades.find((a) => String(a.id) === actividad)?.tipo || "";
  // Las columnas van agrupadas por tipo (tareas, APE, exámenes), cada grupo en el orden en que se registró.
  const ordenTipo: Record<string, number> = { Tarea: 0, APE: 1, Examen: 2 };
  const delTipo = (datos?.actividades.filter((a) => !tipo || a.tipo === tipo) ?? [])
    .map((a, i) => ({ a, i }))
    .sort((x, y) => (ordenTipo[x.a.tipo] ?? 9) - (ordenTipo[y.a.tipo] ?? 9) || x.i - y.i)
    .map((x) => x.a);
  const columnas = delTipo.filter((a) => !actividad || String(a.id) === actividad);
  const nombreGrupo: Record<string, string> = { Tarea: "Tareas", APE: "APE", Examen: "Exámenes" };
  const grupos = columnas.reduce<{ tipo: string; cantidad: number }[]>((g, a) => {
    if (g.length && g[g.length - 1].tipo === a.tipo) g[g.length - 1].cantidad++;
    else g.push({ tipo: a.tipo, cantidad: 1 });
    return g;
  }, []);
  const inicioGrupo = new Set<number>();
  grupos.reduce((pos, g) => (inicioGrupo.add(pos), pos + g.cantidad), 0);
  const elegida = actividad ? datos?.actividades.find((a) => String(a.id) === actividad) : undefined;
  // Con una actividad elegida se califica; sin ella es solo la vista general
  const calificando = !!elegida && !lectura;
  const nombreTipo: Record<string, string> = { Tarea: "las tareas", APE: "los APE", Examen: "los exámenes" };
  const categorias: { id: string; texto: string }[] = [
    { id: "", texto: "Todas" },
    { id: "Tarea", texto: "Tareas" },
    { id: "APE", texto: "APE" },
    { id: "Examen", texto: "Exámenes" },
  ];
  const pendientesDeGuardar = Object.keys(cambios).length;
  useEffect(() => {
    sucio(pendientesDeGuardar > 0);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [pendientesDeGuardar]);
  // Cambiar de categoría o de actividad con notas sin guardar las descartaría: se pregunta antes
  function cambiarFiltro(aplicar: () => void) {
    if (pendientesDeGuardar > 0 && !window.confirm("Tienes calificaciones sin guardar. ¿Descartarlas?")) return;
    setCambios({});
    aplicar();
  }
  const calificadas = elegida && datos ? datos.estudiantes.filter((e) => e.notas[String(elegida.id)] !== undefined).length : 0;

  function escribir(est: number, act: number, valor: string) {
    const clave = `${est}:${act}`;
    const original = datos?.estudiantes.find((e) => e.estudianteId === est)?.notas[String(act)];
    const igual = (original === undefined && valor === "") || (original !== undefined && Number(valor.replace(",", ".")) === original && valor !== "");
    setCambios((c) => {
      const copia = { ...c };
      if (igual) delete copia[clave];
      else copia[clave] = valor;
      return copia;
    });
  }
  async function guardar() {
    setNotice("");
    setError("");
    const porActividad = new Map<number, { estudianteId: number; nota: number | null }[]>();
    for (const [clave, texto] of Object.entries(cambios)) {
      const [est, act] = clave.split(":").map(Number);
      const nota = texto.trim() === "" ? null : Number(texto.replace(",", "."));
      if (nota !== null && (Number.isNaN(nota) || nota < 0 || nota > 10)) {
        setError("Cada calificación debe ser un número entre 0 y 10. Revisa las casillas marcadas en rojo.");
        return;
      }
      porActividad.set(act, [...(porActividad.get(act) ?? []), { estudianteId: est, nota }]);
    }
    setBusy(true);
    try {
      for (const [act, notas] of porActividad) await api(`/calificaciones/actividad/${act}`, "PUT", { notas });
      setCambios({});
      setNotice(pendientesDeGuardar === 1 ? "Calificación guardada." : `${pendientesDeGuardar} calificaciones guardadas.`);
      matriz.reload();
      cambio();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }
  const invalida = (texto: string) => {
    const n = Number(texto.replace(",", "."));
    return texto.trim() !== "" && (Number.isNaN(n) || n < 0 || n > 10);
  };

  return (
    <>
      <Toast message={notice} close={() => setNotice("")} />
      {datos && (
        <div className="toolbar libro-barra">
          <div className="filtros-libro">
            <div className="chips-filtro" role="group" aria-label="Categoría de actividad">
              {categorias.map((c) => (
                <button
                  key={c.id}
                  className={tipo === c.id ? "activo" : ""}
                  aria-pressed={tipo === c.id}
                  onClick={() =>
                    cambiarFiltro(() => {
                      setTipoElegido(c.id);
                      setActividad("");
                    })
                  }
                >
                  {c.texto}
                  <span>{datos.actividades.filter((a) => !c.id || a.tipo === c.id).length}</span>
                </button>
              ))}
            </div>
            <label className="inline-select">
              Actividad
              <select value={actividad} disabled={!delTipo.length} onChange={(e) => cambiarFiltro(() => setActividad(e.target.value))}>
                <option value="">{tipo ? `Ver todas ${nombreTipo[tipo]}` : "Ver todas las actividades"}</option>
                {delTipo.map((a) => (
                  <option key={a.id} value={a.id}>
                    {tipo ? a.nombre : `${a.tipo} · ${a.nombre}`}
                  </option>
                ))}
              </select>
            </label>
          </div>
          <span className="row-actions">
            <button className="secondary" disabled={busy} onClick={() => setPesos(true)}>
              <SlidersHorizontal size={16} />
              Ponderaciones
            </button>
            {calificando && (
              <button className="primary" disabled={busy || !pendientesDeGuardar} onClick={() => void guardar()}>
                <Save size={16} />
                {busy ? "Guardando…" : pendientesDeGuardar ? `Guardar ${pendientesDeGuardar} ${pendientesDeGuardar === 1 ? "cambio" : "cambios"}` : "Guardar"}
              </button>
            )}
          </span>
        </div>
      )}
      {datos && datos.actividades.length > 0 && (
        <div className={`modo-banner ${elegida ? "edicion" : "vista"}`} role="status">
          {elegida ? (
            <>
              <span>
                {lectura ? "Consultando" : "Calificando"} <strong>{elegida.nombre}</strong> · {elegida.tipo} ·{" "}
                <strong>
                  {calificadas} de {datos.estudiantes.length}
                </strong>{" "}
                calificadas.{calificando && " Las notas ya guardadas están bloqueadas: pulsa una para modificarla."}
              </span>
              <button className="text-button" onClick={() => cambiarFiltro(() => setActividad(""))}>
                Volver a la vista general
              </button>
            </>
          ) : (
            <span>
              Vista general de las calificaciones.{!lectura && " Para calificar o modificar notas, elige una actividad en el selector o pulsa «Calificar» en su columna."}
            </span>
          )}
        </div>
      )}
      {datos && (
        <p className="muted peso-resumen">
          Promedio sobre 10 · Tareas {datos.ponderacion.tareas}% · APE {datos.ponderacion.ape}% · Exámenes {datos.ponderacion.examen}% · Nota mínima{" "}
          {datos.ponderacion.notaMinima}
          {!datos.ponderacion.personalizada && " (valores predeterminados)"}
          <span className="leyenda-calor" style={{ marginLeft: 16 }}>
            0 <i /> 10
          </span>
        </p>
      )}
      <ErrorBox message={error} />
      <ErrorBox message={matriz.error} retry={matriz.reload} />
      {matriz.loading && !datos && <p role="status">Cargando calificaciones…</p>}
      {datos && !datos.actividades.length && (
        <div className="aviso" role="status">
          Este curso todavía no tiene actividades que calificar.{" "}
          <button className="text-button" onClick={irAActividades}>
            Ir a Actividades
          </button>
        </div>
      )}
      {datos && datos.actividades.length > 0 && !datos.estudiantes.length && (
        <Empty text="Este curso todavía no tiene estudiantes. El administrador los carga en la materia y el paralelo." />
      )}
      {datos && datos.actividades.length > 0 && datos.estudiantes.length > 0 && (
        <>
          <section className="panel table-wrap">
            <table className="notas compacta">
              <thead>
                <tr className="grupos-fila">
                  <th rowSpan={2}>Estudiante</th>
                  {grupos.map((g, i) => (
                    <th key={i} colSpan={g.cantidad} className={`grupo-cab tipo-${g.tipo.toLowerCase()}`}>
                      {nombreGrupo[g.tipo] ?? g.tipo} <small>({g.cantidad})</small>
                    </th>
                  ))}
                  <th rowSpan={2}>Promedio</th>
                </tr>
                <tr>
                  {columnas.map((a, k) => (
                    <th key={a.id} className={inicioGrupo.has(k) ? "grupo-inicio" : undefined}>
                      {a.nombre}
                      {!elegida && !lectura && (
                        <button
                          className="text-button calificar-columna"
                          onClick={() =>
                            cambiarFiltro(() => {
                              setTipoElegido(a.tipo);
                              setActividad(String(a.id));
                            })
                          }
                        >
                          Calificar
                        </button>
                      )}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {datos.estudiantes.map((e) => (
                  <tr key={e.estudianteId}>
                    <td>
                      {e.apellidos} {e.nombres}
                    </td>
                    {columnas.map((a, k) => {
                      const inicio = inicioGrupo.has(k) ? "grupo-inicio" : undefined;
                      const clave = `${e.estudianteId}:${a.id}`;
                      const guardada = e.notas[String(a.id)];
                      const valor = clave in cambios ? cambios[clave] : guardada === undefined ? "" : String(guardada);
                      const nombre = `${e.apellidos} ${e.nombres} en ${a.nombre}`;
                      // Vista general o período finalizado: solo se muestra la nota
                      if (!calificando)
                        return (
                          <td key={a.id} className={inicio}>
                            <span className={`nota-celda${valor === "" ? " pendiente" : ""}`} style={estiloNota(valor)} aria-label={`Nota de ${nombre}: ${valor === "" ? "pendiente" : valor}`}>
                              {valor === "" ? "—" : valor}
                            </span>
                          </td>
                        );
                      // Nota ya guardada: queda bloqueada hasta que se pida modificarla
                      if (guardada !== undefined)
                        return (
                          <td key={a.id} className={inicio}>
                            <button
                              className="nota-celda bloqueada"
                              style={estiloNota(valor)}
                              title="Modificar esta calificación"
                              aria-label={`Modificar la nota de ${nombre} (actual: ${valor})`}
                              onClick={() => setModal({ fila: e, actividad: a })}
                            >
                              {valor}
                              <Pencil size={11} />
                            </button>
                          </td>
                        );
                      return (
                        <td key={a.id} className={inicio}>
                          <input
                            className={`nota-input${invalida(valor) ? " invalida" : ""}${clave in cambios ? " editada" : ""}`}
                            inputMode="decimal"
                            placeholder="—"
                            style={estiloNota(valor)}
                            aria-label={`Nota de ${nombre}`}
                            value={valor}
                            onChange={(ev) => escribir(e.estudianteId, a.id, ev.target.value)}
                          />
                        </td>
                      );
                    })}
                    <td>
                      <strong>{fmt(e.resultado)}</strong>
                      <small className="block">
                        <EstadoPill estado={e.estado} definitivo={e.definitivo} />
                      </small>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </section>
          <p className="muted small">
            Una casilla vacía es «pendiente» y no cuenta como cero. Dejar vacía una nota que ya estaba guardada la devuelve a pendiente. El promedio combina el
            promedio de cada tipo de actividad según sus porcentajes y se marca «parcial» mientras queden notas pendientes.
          </p>
        </>
      )}
      {modal && datos && (
        <ModalNota
          fila={modal.fila}
          actividad={modal.actividad}
          notaMinima={datos.ponderacion.notaMinima}
          close={() => setModal(null)}
          saved={(texto) => {
            setModal(null);
            setNotice(texto);
            matriz.reload();
            cambio();
          }}
        />
      )}
      {pesos && datos && (
        <PonderacionesForm
          cursoId={curso.id}
          actual={datos.ponderacion}
          lectura={lectura}
          close={() => setPesos(false)}
          saved={() => {
            setPesos(false);
            setNotice("Ponderaciones guardadas. Los resultados se recalcularon.");
            matriz.reload();
            cambio();
          }}
        />
      )}
    </>
  );
}

function PonderacionesForm({ cursoId, actual, lectura, close, saved }: { cursoId: number; actual: Ponderacion; lectura: boolean; close: () => void; saved: () => void }) {
  const [tareas, setTareas] = useState(String(actual.tareas)),
    [ape, setApe] = useState(String(actual.ape)),
    [examen, setExamen] = useState(String(actual.examen)),
    [minima, setMinima] = useState(String(actual.notaMinima)),
    [busy, setBusy] = useState(false),
    [error, setError] = useState("");
  const n = (x: string) => Number(x.replace(",", ".")) || 0;
  const total = n(tareas) + n(ape) + n(examen);
  async function submit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError("");
    try {
      await api(`/calificaciones/curso/${cursoId}/ponderaciones`, "PUT", { tareas: n(tareas), ape: n(ape), examen: n(examen), notaMinima: n(minima) });
      saved();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }
  return (
    <Modal title="Ponderaciones de evaluación" close={close} busy={busy}>
      <form onSubmit={submit}>
        <p className="muted">Porcentaje de cada tipo de actividad en el resultado del curso. Deben sumar 100.</p>
        <div className="two-cols">
          <label>
            Tareas (%)
            <input inputMode="decimal" required disabled={lectura} value={tareas} onChange={(e) => setTareas(e.target.value)} />
          </label>
          <label>
            APE (%)
            <input inputMode="decimal" required disabled={lectura} value={ape} onChange={(e) => setApe(e.target.value)} />
          </label>
          <label>
            Exámenes (%)
            <input inputMode="decimal" required disabled={lectura} value={examen} onChange={(e) => setExamen(e.target.value)} />
          </label>
          <label>
            Nota mínima (sobre 10)
            <input inputMode="decimal" required disabled={lectura} value={minima} onChange={(e) => setMinima(e.target.value)} />
          </label>
        </div>
        <p className={total === 100 ? "muted" : "error-text"} role="status">
          Suma actual: {total}%
        </p>
        <ErrorBox message={error} />
        <div className="modal-actions">
          <button className="secondary" type="button" disabled={busy} onClick={close}>
            {lectura ? "Cerrar" : "Cancelar"}
          </button>
          {!lectura && (
            <button className="primary" disabled={busy || total !== 100}>
              {busy ? "Guardando…" : "Guardar"}
            </button>
          )}
        </div>
      </form>
    </Modal>
  );
}

// Ventana para modificar una calificación ya guardada: número grande con el color de su nota, deslizador y notas rápidas.
function ModalNota({
  fila,
  actividad,
  notaMinima,
  close,
  saved,
}: {
  fila: FilaNotas;
  actividad: Matriz["actividades"][number];
  notaMinima: number;
  close: () => void;
  saved: (texto: string) => void;
}) {
  const anterior = fila.notas[String(actividad.id)];
  const [texto, setTexto] = useState(String(anterior)),
    [busy, setBusy] = useState(false),
    [error, setError] = useState("");
  const nombre = `${fila.apellidos} ${fila.nombres}`;
  const numero = Number(texto.replace(",", "."));
  const valida = texto.trim() !== "" && !Number.isNaN(numero) && numero >= 0 && numero <= 10;
  const igual = valida && numero === anterior;
  async function guardar(nota: number | null) {
    setBusy(true);
    setError("");
    try {
      await api(`/calificaciones/actividad/${actividad.id}`, "PUT", { notas: [{ estudianteId: fila.estudianteId, nota }] });
      saved(nota === null ? `La nota de ${nombre} en ${actividad.nombre} volvió a pendiente.` : `Nota de ${nombre} en ${actividad.nombre} actualizada: ${anterior} → ${nota}.`);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }
  function submit(e: FormEvent) {
    e.preventDefault();
    if (valida && !igual) void guardar(Math.round(numero * 100) / 100);
  }
  return (
    <Modal title="Modificar calificación" close={close} busy={busy}>
      <form onSubmit={submit} className="modal-nota">
        <div className="nota-contexto">
          <strong>{nombre}</strong>
          <span>
            <span className={`pill tipo-${actividad.tipo.toLowerCase()}`}>{actividad.tipo}</span> {actividad.nombre}
          </span>
        </div>
        <div className="nota-escenario">
          <div className="nota-grande" style={valida ? estiloNota(texto) : undefined} aria-live="polite">
            {valida ? Number(numero.toFixed(2)) : "—"}
            <small>sobre 10</small>
          </div>
          <div className="nota-controles">
            <label>
              Nueva calificación
              <input
                autoFocus
                inputMode="decimal"
                value={texto}
                onChange={(e) => setTexto(e.target.value)}
                onFocus={(e) => e.target.select()}
                aria-invalid={!valida}
              />
            </label>
            <input
              type="range"
              min={0}
              max={10}
              step={0.5}
              value={valida ? numero : 0}
              aria-label="Ajustar la calificación"
              onChange={(e) => setTexto(e.target.value)}
            />
            <div className="notas-rapidas" role="group" aria-label="Notas rápidas">
              {[0, 5, 6, 7, 8, 9, 10].map((n) => (
                <button type="button" key={n} className={valida && numero === n ? "activo" : ""} onClick={() => setTexto(String(n))}>
                  {n}
                </button>
              ))}
            </div>
          </div>
        </div>
        <p className={`nota-aviso${valida && numero < notaMinima ? " bajo" : ""}`} role="status">
          {!valida
            ? "Escribe una nota entre 0 y 10."
            : igual
              ? `Es la misma nota que ya estaba guardada (${anterior}).`
              : numero < notaMinima
                ? `Queda por debajo de la nota mínima (${notaMinima}). Nota anterior: ${anterior}.`
                : `Nota anterior: ${anterior}. Se recalculará el resultado del estudiante.`}
        </p>
        <ErrorBox message={error} />
        <div className="modal-actions">
          <button
            type="button"
            className="secondary"
            disabled={busy}
            onClick={() => {
              if (window.confirm(`¿Dejar pendiente la nota de ${nombre}? Dejará de contar en su resultado.`)) void guardar(null);
            }}
          >
            Dejar pendiente
          </button>
          <button type="button" className="secondary" disabled={busy} onClick={close}>
            Cancelar
          </button>
          <button className="primary" disabled={busy || !valida || igual}>
            {busy ? "Guardando…" : "Guardar calificación"}
          </button>
        </div>
      </form>
    </Modal>
  );
}
