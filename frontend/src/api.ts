export type User = {
  id: number;
  name: string;
  email: string;
  role: "Admin" | "Teacher";
  carreras: string[];
};
// Docente con su ficha personal y las carreras a las que pertenece.
export type Docente = {
  id: number;
  name: string;
  email: string;
  cedula: string;
  nombres: string;
  apellidos: string;
  telefono: string | null;
  tituloAcademico: string | null;
  carreras: string[];
};
export type Carrera = {
  id: number;
  codigo: string;
  nombre: string;
  modalidad: string;
  niveles: number;
  horasTotales: number;
  creditosTotales: number;
  materias: number;
  prefijoUta: string;
  // Versión de la malla vigente (0 si la carrera todavía no tiene malla) y cuántas versiones tiene.
  mallaVersion: number;
  versiones: number;
  // Baja lógica: una carrera dada de baja se conserva, pero no admite cambios nuevos.
  activa: boolean;
  bajaEn: string | null;
};
export type MateriaMalla = {
  id: number;
  codigo: string;
  codigoUta: string;
  nombre: string;
  nivel: number;
  unidad: string;
  tipo: string;
  horasContactoDocente: number | null;
  horasPracticoExperimental: number | null;
  horasAutonomas: number | null;
  horasTotales: number;
  creditos: number;
  creditosDerivado: boolean;
  mallaVersion: number;
};
export type Periodo = { id: number; nombre: string; activo: boolean; estado: "Preparación" | "En curso" | "Finalizado" };
export type Paralelo = {
  id: number;
  periodoId: number;
  carrera: string;
  nivel: number;
  letra: string;
  mallaVersion: number;
};
export type Curso = {
  id: number;
  materiaId: number;
  materiaCodigo: string;
  materiaNombre: string;
  creditos: number;
  horasTotales: number;
  carrera: string;
  carreraNombre: string;
  nivel: number;
  paralelo: string;
  periodoId: number;
  periodo: string;
  teacherId: number | null;
  mallaVersion: number;
  // Estado del período del curso: con «Finalizado» el curso es de solo lectura.
  periodoEstado: string;
};
// Un estudiante en un curso (materia en un paralelo). Retirarlo de una materia no lo da de baja de las demás.
export type Inscripcion = {
  id: number;
  cursoId: number;
  nivel: number;
  paralelo: string;
  materiaCodigo: string;
  materiaNombre: string;
  creditos: number;
  activo: boolean;
};
export type Estudiante = {
  id: number;
  cedula: string;
  apellidos: string;
  nombres: string;
  correo: string;
  carreraCodigo: string;
  activo: boolean;
  bajaEn: string | null;
  // Suma de créditos de sus materias activas (aunque el listado esté filtrado) y máximo por período.
  creditos: number;
  maxCreditos: number;
  inscripciones: Inscripcion[];
};
export type ListaEstudiante = {
  id: number;
  apellidos: string;
  nombres: string;
  correo: string;
};
// La sesión sobrevive a un refresco de la página: el token se guarda en sessionStorage (se borra al cerrar la pestaña, a diferencia de
// localStorage, que lo dejaría guardado para siempre). Al cargar, la aplicación lo valida con el servidor antes de usarlo.
const CLAVE_SESION = "edumonitor.sesion";
function leerToken() {
  try {
    return sessionStorage.getItem(CLAVE_SESION) ?? "";
  } catch {
    return ""; // el navegador puede bloquear el almacenamiento (modo privado, políticas): entonces la sesión vive solo en memoria
  }
}
let token = leerToken();
export function setToken(value: string) {
  token = value;
  try {
    if (value) sessionStorage.setItem(CLAVE_SESION, value);
    else sessionStorage.removeItem(CLAVE_SESION);
  } catch {
    /* sin almacenamiento: la sesión sigue en memoria */
  }
}
export const hayToken = () => token !== "";
export async function api<T>(
  path: string,
  method = "GET",
  body?: unknown,
): Promise<T> {
  let response: Response;
  try {
    response = await fetch(`/api${path}`, {
      method,
      headers: {
        // Con un archivo (FormData) el navegador fija el tipo y el límite del formulario.
        ...(body instanceof FormData
          ? {}
          : { "Content-Type": "application/json" }),
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
      },
      body:
        body === undefined
          ? undefined
          : body instanceof FormData
            ? body
            : JSON.stringify(body),
      signal: AbortSignal.timeout(14000),
    });
  } catch {
    throw new Error(
      "El servicio no respondió a tiempo o no está disponible. Puedes reintentar; los otros módulos siguen accesibles.",
    );
  }
  if (!response.ok) {
    if (response.status === 401 && path !== "/auth/login")
      window.dispatchEvent(new Event("session-expired"));
    const data = await response.json().catch(() => ({}));
    const fallback: Record<number, string> = {
      400: "Revisa los campos del formulario.",
      401: "Correo o contraseña incorrectos, o sesión vencida.",
      403: "No tienes permiso para esta operación.",
      404: "El registro no existe o no está autorizado.",
      409: "El registro ya existe.",
      429: "Demasiados intentos. Espera un minuto.",
      503: "El servicio solicitado no está disponible. Inténtalo de nuevo en unos segundos.",
    };
    throw new Error(
      data.message ||
        (data.errors
          ? Object.values(data.errors).flat().join(" ")
          : fallback[response.status]) ||
        "No se pudo completar la operación.",
    );
  }
  return response.status === 204 ? (undefined as T) : response.json();
}

// Descarga un archivo protegido: se pide con el token y se entrega al navegador como descarga.
export async function descargar(path: string, nombre: string) {
  let response: Response;
  try {
    response = await fetch(`/api${path}`, { headers: token ? { Authorization: `Bearer ${token}` } : {}, signal: AbortSignal.timeout(30000) });
  } catch {
    throw new Error("El servicio no respondió a tiempo. Inténtalo de nuevo.");
  }
  if (!response.ok) {
    if (response.status === 401) window.dispatchEvent(new Event("session-expired"));
    const data = await response.json().catch(() => ({}));
    throw new Error(data.message || "No se pudo descargar el archivo.");
  }
  const url = URL.createObjectURL(await response.blob());
  const a = document.createElement("a");
  a.href = url;
  a.download = nombre;
  a.click();
  URL.revokeObjectURL(url);
}

// ---- Sprint 2: contenidos, actividades, calificaciones y rendimiento ----
export type ContenidoMinimo = { id: number; descripcion: string };
// Material de apoyo de un tema. El contenido solo viaja al descargarlo.
export type ArchivoTema = { id: number; temaId: number; nombre: string; tipo: string; tamano: number; subidoEn: string };
export type Tema = {
  id: number;
  unidadId: number;
  nombre: string;
  descripcion: string | null;
  contenidos: ContenidoMinimo[];
  archivos: ArchivoTema[];
};
export type Unidad = {
  id: number;
  cursoId: number;
  nombre: string;
  descripcion: string | null;
  temas: Tema[];
};
export type TipoActividad = "Tarea" | "APE" | "Examen";
export type Actividad = {
  id: number;
  cursoId: number;
  nombre: string;
  tipo: TipoActividad;
  descripcion: string | null;
  fechaEntrega: string | null;
  temas: { id: number; nombre: string; unidadId: number; unidadNombre: string }[];
  calificadas: number;
};
export type Ponderacion = {
  tareas: number;
  ape: number;
  examen: number;
  notaMinima: number;
  personalizada: boolean;
};
export type FilaNotas = {
  estudianteId: number;
  apellidos: string;
  nombres: string;
  notas: Record<string, number>;
  resultado: number | null;
  estado: string;
  definitivo: boolean;
  pendientes: number;
};
export type Matriz = {
  cursoId: number;
  ponderacion: Ponderacion;
  actividades: { id: number; nombre: string; tipo: TipoActividad; fechaEntrega: string | null }[];
  estudiantes: FilaNotas[];
};
export type Rendimiento = {
  cursoId: number;
  resumen: {
    totalEstudiantes: number;
    conResultado: number;
    promedioGrupo: number | null;
    enRiesgo: number;
    porcentajeRiesgo: number | null;
    notaMasBaja: number | null;
    resultadoMasBajo: number | null;
    actividades: number;
    notasPendientes: number;
    completo: boolean;
    notaMinima: number;
  };
  estudiantes: {
    estudianteId: number;
    apellidos: string;
    nombres: string;
    resultado: number | null;
    estado: string;
    definitivo: boolean;
    calificadas: number;
    pendientes: number;
    notaMasBaja: number | null;
    desglose: PuntosTipo[];
    acumulado: number;
  }[];
  temas: {
    temaId: number;
    tema: string;
    unidadId: number;
    unidad: string;
    actividades: number;
    promedio: number | null;
    calificadas: number;
    pendientes: number;
    notasBajas: number;
  }[];
};
// Puntos que un tipo de actividad aporta al total de 10 (su promedio por su porcentaje).
export type PuntosTipo = { tipo: TipoActividad; porcentaje: number; maximo: number; promedio: number | null; puntos: number; calificadas: number; actividades: number };
export type EstudianteDetalle = {
  desglose: PuntosTipo[];
  acumulado: number;
  estudianteId: number;
  apellidos: string;
  nombres: string;
  correo: string;
  resultado: number | null;
  estado: string;
  definitivo: boolean;
  pendientes: number;
  notaMinima: number;
  notas: { actividadId: number; actividad: string; tipo: TipoActividad; nota: number | null; temas: string[] }[];
  temas: { temaId: number; tema: string; promedio: number | null; calificadas: number; pendientes: number }[];
};

// ---- Sprint 3: agente académico (detección de dificultades, recomendaciones, comunicación y reportes) ----
export type NivelAlerta = "Alto" | "Medio" | "Bajo" | "Sin alertas";
export type EstadoRecomendacion = "Generando" | "Borrador" | "Aprobada" | "Comunicada" | "Rechazada" | "Reemplazada";
export type ProveedorIA = { clave: string; origen: "Local" | "Nube"; modelo: string; configurado: boolean; disponible: boolean; detalle: string };
export type EstadoAgente = { proveedores: ProveedorIA[]; plantillas: boolean; nivelMinimo: string; correoReal: boolean };
export type HallazgoSeg = { id: number; codigo: string; nivel: NivelAlerta; titulo: string; detalle: string };
export type ComunicacionSeg = {
  id: number;
  recomendacionId: number;
  canal: string;
  destinatario: string;
  asunto: string;
  cuerpo: string;
  estado: "Enviado" | "Simulado" | "Error";
  error: string | null;
  creadoEn: string;
  estudiante: string;
};
export type RecomendacionSeg = {
  id: number;
  estado: EstadoRecomendacion;
  origen: "Plantilla" | "Local" | "Nube";
  modelo: string | null;
  resumen: string;
  causas: string[];
  accionesDocente: string[];
  accionesEstudiante: string[];
  mensajeEstudiante: string;
  editada: boolean;
  version: number;
  creadoEn: string;
  actualizadoEn: string;
  revisadaEn: string | null;
  motivoRechazo: string | null;
  comunicacion: ComunicacionSeg | null;
  // Ruta de mejora, cuando la recomendación nació del asistente.
  ruta?: Omit<RutaMejora, "estudiante"> & { estudiante?: string } | null;
};
export type EstudianteSeg = {
  estudianteAnalisisId: number;
  estudianteId: number;
  apellidos: string;
  nombres: string;
  correo: string;
  resultado: number | null;
  estado: string;
  pendientes: number;
  nivel: NivelAlerta;
  hallazgos: HallazgoSeg[];
  recomendacion: RecomendacionSeg | null;
};
export type AnalisisSeg = {
  id: number;
  estado: "Generando" | "Completado";
  creadoEn: string;
  terminadoEn: string | null;
  totalEstudiantes: number;
  aRecomendar: number;
  generadas: number;
  notaMinima: number;
  alto: number;
  medio: number;
  bajo: number;
  sinAlertas: number;
  materia: string;
  paralelo: string;
  periodo: string;
};
export type PanelSeguimiento = { cursoId: number; soloLectura: boolean; analisis: AnalisisSeg | null; hallazgosCurso: HallazgoSeg[]; estudiantes: EstudianteSeg[] };
export type ReporteSeguimiento = {
  cursoId: number;
  materia: string;
  paralelo: string;
  periodo: string;
  docente: string;
  generadoEn: string;
  analisis: AnalisisSeg;
  recomendaciones: { borrador: number; aprobada: number; comunicada: number; rechazada: number; generando: number };
  hallazgosCurso: HallazgoSeg[];
  estudiantes: EstudianteSeg[];
};

// ---- Asistente académico (chat del docente, rutas de mejora) ----
export type RutaActividad = { id: number; nombre: string; tipo: string; fecha: string | null; aporte: number; temas: string[] };
export type RutaEtapa = { numero: number; titulo: string; plazo: string | null; actividades: RutaActividad[]; temas: string[]; debiles?: string[] | null; meta: number | null; acciones: string[] };
export type RutaTema = { tema: string; promedio: number | null; calificadas: number; evidencia: string[] };
export type RutaMejora = {
  tipo: "ruta";
  estudiante: string;
  notaMinima: number;
  resultado: number | null;
  estado: "Aprobado" | "Asegurado" | "Alcanzable" | "Exigente" | "MuyExigente" | "Imposible" | "SinPendientes" | "SinNotas";
  situacion: string;
  promedioRequerido: number | null;
  maximoPosible: number | null;
  pendientes: number;
  temas: RutaTema[];
  etapas: RutaEtapa[];
  resumen: string;
  accionesDocente: string[];
  mensaje: string;
  origen: "Plantilla" | "Local" | "Nube";
  modelo: string | null;
};
export type TablaAsistente = { tipo: "tabla"; titulo: string; columnas: string[]; filas: string[][]; notas: string[] };
export type TarjetaAsistente = TablaAsistente | RutaMejora;
export type PreguntaPre = { id: string; grupo: string; texto: string; requiereEstudiante: boolean };
export type PersonaAsist = { id: number; nombre: string; correo: string };
export type MensajeAsist = {
  id: number;
  rol: "docente" | "asistente";
  tipo: string;
  texto: string;
  tarjetas: TarjetaAsistente[];
  origen: string | null;
  modelo: string | null;
  alcance: string | null;
  verificada: boolean | null;
  util: boolean | null;
  creadoEn: string;
};
export type ConversacionResumen = { id: number; titulo: string; actualizadoEn: string; venceEn: string; mensajes: number };
export type ConversacionDetalle = {
  id: number;
  titulo: string;
  venceEn: string;
  mensajes: MensajeAsist[];
  alias: { alias: string; estudianteId: number; nombre: string }[];
};

// Respuesta en flujo (text/event-stream): llama a «alEvento» por cada evento que manda el servidor (inicio, tarjeta, texto, ruta, fin…).
export async function apiStream(path: string, body: unknown, alEvento: (tipo: string, datos: any) => void, signal: AbortSignal): Promise<void> {
  let response: Response;
  try {
    response = await fetch(`/api${path}`, {
      method: "POST",
      headers: { "Content-Type": "application/json", ...(token ? { Authorization: `Bearer ${token}` } : {}) },
      body: JSON.stringify(body),
      signal,
    });
  } catch (e) {
    if ((e as Error).name === "AbortError") throw e;
    throw new Error("No se pudo conectar con el servicio. Puedes reintentar.");
  }
  if (!response.ok) {
    if (response.status === 401) window.dispatchEvent(new Event("session-expired"));
    const data = await response.json().catch(() => ({}));
    throw new Error(data.message || (data.errors ? Object.values(data.errors).flat().join(" ") : "No se pudo completar la operación."));
  }
  const lector = response.body?.getReader();
  if (!lector) throw new Error("El navegador no admite respuestas en vivo.");
  const decodificador = new TextDecoder();
  let pendiente = "";
  for (;;) {
    const { done, value } = await lector.read();
    if (done) break;
    pendiente += decodificador.decode(value, { stream: true });
    let corte: number;
    while ((corte = pendiente.indexOf("\n\n")) >= 0) {
      const bloque = pendiente.slice(0, corte);
      pendiente = pendiente.slice(corte + 2);
      let tipo = "";
      let datos = "";
      for (const linea of bloque.split("\n")) {
        if (linea.startsWith("event: ")) tipo = linea.slice(7);
        else if (linea.startsWith("data: ")) datos += linea.slice(6);
      }
      if (tipo && datos) alEvento(tipo, JSON.parse(datos));
    }
  }
}
