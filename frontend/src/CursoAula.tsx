import { useEffect, useState } from "react";
import { BookOpen, ArrowLeft, ArrowUpRight, ListTree, ClipboardList, PenLine, TrendingUp, Bot } from "lucide-react";
import { type Curso, type Rendimiento } from "./api";
import { useObject, useResource } from "./useResource";
import { ErrorBox, Empty } from "./components";
import { romano, fmt, soloLectura, sinGuardar, confirmarSalida } from "./AulaComun";
import { ContenidoTab } from "./ContenidosPage";
import { AvisoMoodle, AvisoSinVincular, SincronizarMoodle, useFuenteAcademica, useVinculoMoodle } from "./MoodleCurso";
import { ActividadesTab } from "./ActividadesPage";
import { CalificacionesTab } from "./CalificacionesPage";
import { RendimientoTab } from "./RendimientoPage";
import { AsistenteTab } from "./AsistentePage";

// Portal del docente: sus cursos del período y, dentro de cada curso, todo lo que gestiona (contenido, actividades,
// calificaciones, rendimiento y estudiantes). No hay módulos sueltos: siempre se trabaja dentro de un curso.
export function MisCursos({ detalle }: { detalle: (texto: string | null) => void }) {
  const cursos = useResource<Curso>("/cursos");
  const [abierto, setAbierto] = useState<number | null>(null);
  const curso = cursos.data.find((c) => c.id === abierto) ?? null;
  useEffect(() => {
    detalle(curso ? `${curso.materiaNombre} · ${romano[curso.nivel - 1]} «${curso.paralelo}»` : null);
    return () => detalle(null);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [curso?.id]);
  if (curso) return <Aula key={curso.id} curso={curso} volver={() => setAbierto(null)} />;
  const periodo = cursos.data[0]?.periodo;
  return (
    <>
      <div className="page-heading">
        <div>
          <span className="eyebrow">PORTAL DOCENTE</span>
          <h1>Mis cursos</h1>
          <p className="muted">
            Entra a un curso para gestionar su contenido, sus actividades y sus calificaciones.
            {periodo ? ` Período académico: ${periodo}.` : ""}
          </p>
        </div>
      </div>
      <ErrorBox message={cursos.error} retry={cursos.reload} />
      {cursos.loading && <p role="status">Cargando tus cursos…</p>}
      {!cursos.error && !cursos.loading && !cursos.data.length && (
        <Empty text="Todavía no tienes cursos asignados en este período. El administrador asigna los cursos a cada docente." />
      )}
      <div className="course-grid">
        {cursos.data.map((c) => (
          <CursoCard key={c.id} curso={c} abrir={() => setAbierto(c.id)} />
        ))}
      </div>
    </>
  );
}

function CursoCard({ curso, abrir }: { curso: Curso; abrir: () => void }) {
  const r = useObject<Rendimiento>(`/rendimiento/curso/${curso.id}`);
  const s = r.data?.resumen;
  return (
    <section className="course-card curso-docente">
      <div className="card-top">
        <span className="course-icon">
          <BookOpen size={22} />
        </span>
        <span className="pill">{curso.materiaCodigo}</span>
      </div>
      <h2>{curso.materiaNombre}</h2>
      <p className="muted">
        {curso.carreraNombre} · Nivel {romano[curso.nivel - 1]} · Paralelo {curso.paralelo}
      </p>
      <dl className="curso-datos">
        <div>
          <dt>Estudiantes</dt>
          <dd>{s ? s.totalEstudiantes : "…"}</dd>
        </div>
        <div>
          <dt>Actividades</dt>
          <dd>{s ? s.actividades : "…"}</dd>
        </div>
        <div>
          <dt>Por calificar</dt>
          <dd className={s && s.notasPendientes > 0 ? "atencion" : ""}>{s ? s.notasPendientes : "…"}</dd>
        </div>
      </dl>
      <div className="card-footer">
        <button className="primary" onClick={abrir} aria-label={`Entrar a ${curso.materiaNombre}, paralelo ${curso.paralelo}`}>
          Entrar al curso <ArrowUpRight size={16} />
        </button>
      </div>
    </section>
  );
}

type Pestana = "contenido" | "actividades" | "calificaciones" | "estudiantes" | "asistente";
const pestanas: { id: Pestana; texto: string; icono: typeof ListTree }[] = [
  { id: "contenido", texto: "Contenido", icono: ListTree },
  { id: "actividades", texto: "Actividades", icono: ClipboardList },
  { id: "calificaciones", texto: "Calificaciones", icono: PenLine },
  { id: "estudiantes", texto: "Rendimiento académico", icono: TrendingUp },
  { id: "asistente", texto: "Asistente", icono: Bot },
];

function Aula({ curso, volver }: { curso: Curso; volver: () => void }) {
  const [pestana, setPestana] = useState<Pestana>("contenido"),
    // Actividad con la que se abre el libro de calificaciones al pulsar «Calificar».
    [actividad, setActividad] = useState("");
  const resumen = useObject<Rendimiento>(`/rendimiento/curso/${curso.id}`);
  const finalizado = soloLectura(curso);
  const vinculo = useVinculoMoodle(curso.id);
  const fuente = useFuenteAcademica();
  // Solo se edita a mano si el curso no está vinculado Y la fuente es «Manual». Mientras carga, se asume solo lectura.
  const lectura = finalizado || vinculo.data?.vinculado !== false || fuente.data?.fuente !== "Manual";
  const [revision, setRevision] = useState(0);
  const s = resumen.data?.resumen;
  // En el chat se compacta la cabecera del curso (sin indicadores ni avisos) para que la conversación use casi toda la pantalla.
  const enChat = pestana === "asistente";

  // Antes de salir de Calificaciones con notas escritas sin guardar, se pregunta.
  function ir(destino: Pestana, actividadId = "") {
    if (destino === pestana && !actividadId) return;
    if (!confirmarSalida()) return;
    setActividad(actividadId);
    setPestana(destino);
  }
  return (
    <>
      <button
        className={`text-button volver${enChat ? " volver-compacto" : ""}`}
        onClick={() => {
          if (confirmarSalida()) volver();
        }}
      >
        <ArrowLeft size={16} />
        Mis cursos
      </button>
      {enChat ? (
        <h1 className="aula-titulo-compacto">
          {curso.materiaNombre} <small>· Paralelo {curso.paralelo}</small>
        </h1>
      ) : (
        <>
        <div className="page-heading aula-heading">
          <div>
            <span className="eyebrow">
              {curso.carreraNombre.toUpperCase()} · NIVEL {romano[curso.nivel - 1]} · PARALELO {curso.paralelo}
            </span>
            <h1>{curso.materiaNombre}</h1>
            <p className="muted">
              {curso.periodo} · {curso.creditos} créditos · {curso.horasTotales} horas · {curso.materiaCodigo}
            </p>
          </div>
        </div>
        <ErrorBox message={vinculo.error} retry={vinculo.reload} />
        {finalizado && vinculo.data?.urlCurso && <AvisoMoodle url={vinculo.data.urlCurso} />}
        {!finalizado && vinculo.data?.vinculado === false && fuente.data?.fuente === "Moodle" && <AvisoSinVincular />}
        {!finalizado && vinculo.data?.vinculado && <SincronizarMoodle cursoId={curso.id} url={vinculo.data.urlCurso} actualizado={() => { setRevision(v => v + 1); resumen.reload(); }} />}
        {finalizado && (
          <div className="aviso" role="status">
            El período {curso.periodo} finalizó: el curso está en modo de consulta y ya no se pueden cambiar su contenido, sus actividades ni sus calificaciones.
          </div>
        )}
        <div className="indicadores" aria-label="Indicadores del curso">
          <div>
            <span>Estudiantes</span>
            <strong>{s ? s.totalEstudiantes : "…"}</strong>
          </div>
          <div>
            <span>Actividades</span>
            <strong>{s ? s.actividades : "…"}</strong>
          </div>
          <div>
            <span>Notas por registrar</span>
            <strong className={s && s.notasPendientes > 0 ? "atencion" : ""}>{s ? s.notasPendientes : "…"}</strong>
          </div>
          <div>
            <span>Promedio del curso</span>
            <strong>{s ? fmt(s.promedioGrupo) : "…"}</strong>
          </div>
          <div>
            <span>En riesgo</span>
            <strong className={s && s.enRiesgo > 0 ? "riesgo" : ""}>{s ? s.enRiesgo : "…"}</strong>
          </div>
        </div>
        </>
      )}
      <div className="tabs" role="tablist" aria-label="Secciones del curso">
        {pestanas.map(({ id, texto, icono: Icono }) => (
          <button key={id} role="tab" aria-selected={pestana === id} className={pestana === id ? "activa" : ""} onClick={() => ir(id)}>
            <Icono size={16} />
            {texto}
          </button>
        ))}
      </div>
      <div key={revision} role="tabpanel" className="tab-panel">
        {pestana === "contenido" ? (
          <ContenidoTab curso={curso} lectura={lectura} archivosEditables={!lectura} />
        ) : pestana === "actividades" ? (
          <ActividadesTab
            curso={curso}
            lectura={lectura}
            estudiantes={s ? s.totalEstudiantes : null}
            calificar={(id) => ir("calificaciones", String(id))}
            cambio={resumen.reload}
          />
        ) : pestana === "calificaciones" ? (
          <CalificacionesTab
            key={actividad}
            curso={curso}
            lectura={lectura}
            actividadInicial={actividad}
            sucio={(hay) => (sinGuardar.hay = hay)}
            cambio={resumen.reload}
            irAActividades={() => ir("actividades")}
          />
        ) : pestana === "asistente" ? (
          <AsistenteTab curso={curso} />
        ) : (
          <RendimientoTab curso={curso} irAAlertas={() => ir("asistente")} />
        )}
      </div>
    </>
  );
}
