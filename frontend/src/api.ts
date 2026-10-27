export type User = {
  id: number;
  name: string;
  email: string;
  role: "Admin" | "Teacher";
};
export type Teacher = User;
export type Student = {
  id: number;
  studentCode: string;
  firstNames: string;
  lastNames: string;
};
export type Materia = {
  id: number;
  code: string;
  name: string;
  academicPeriod: string;
  teacherId: number | null;
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
        "Content-Type": "application/json",
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
      },
      body: body === undefined ? undefined : JSON.stringify(body),
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
