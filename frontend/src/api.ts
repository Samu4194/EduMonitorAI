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
let token = "";
export function setToken(value: string) {
  token = value;
}
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
