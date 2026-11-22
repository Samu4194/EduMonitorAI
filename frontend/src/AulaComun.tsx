// Utilidades compartidas por las pestañas del curso del docente.
import type { CSSProperties } from "react";
import { type Curso, type PuntosTipo } from "./api";

export const romano = ["I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X", "XI", "XII"];
const meses = ["ene", "feb", "mar", "abr", "may", "jun", "jul", "ago", "sep", "oct", "nov", "dic"];

export function fmt(n: number | null | undefined) {
  return n === null || n === undefined ? "—" : n.toFixed(2);
}

// "2026-09-12" -> "12 sep 2026". Se arma a mano para que la zona horaria no mueva el día.
export function fecha(iso: string | null | undefined) {
  if (!iso) return "Sin fecha";
  const [anio, mes, dia] = iso.split("-").map(Number);
  return `${dia} ${meses[mes - 1]} ${anio}`;
}

// Notas escritas en el libro de calificaciones que todavía no se guardaron. Lo consultan el menú y las pestañas antes de salir.
export const sinGuardar = { hay: false };
export function confirmarSalida() {
  if (sinGuardar.hay && !window.confirm("Tienes calificaciones sin guardar. ¿Salir sin guardarlas?")) return false;
  sinGuardar.hay = false;
  return true;
}

// Un curso de un período finalizado solo se consulta.
export const soloLectura = (curso: Curso) => curso.periodoEstado === "Finalizado";

export function EstadoPill({ estado, definitivo }: { estado: string; definitivo?: boolean }) {
  const clase = estado === "Óptimo" ? "ok" : estado === "En riesgo" ? "riesgo" : "pend";
  return (
    <span className={`estado ${clase}`}>
      {estado}
      {definitivo === false && estado !== "Sin calificaciones" ? " (parcial)" : ""}
    </span>
  );
}

// Mapa de calor del libro de notas: del rojo (0) al verde (10) pasando por ámbar. Cada casilla toma el color de su nota.
const paradas: [number, number][] = [[0, 4], [5, 20], [7, 42], [8.5, 100], [10, 150]];
export function tonoNota(n: number) {
  let hue = 150;
  for (let i = 1; i < paradas.length; i++)
    if (n <= paradas[i][0]) {
      const [x0, h0] = paradas[i - 1];
      const [x1, h1] = paradas[i];
      hue = h0 + ((n - x0) / (x1 - x0)) * (h1 - h0);
      break;
    }
  return Math.round(hue);
}
export function estiloNota(texto: string): CSSProperties | undefined {
  const n = Number(texto.replace(",", "."));
  if (texto.trim() === "" || Number.isNaN(n) || n < 0 || n > 10) return undefined;
  let hue = 150;
  for (let i = 1; i < paradas.length; i++)
    if (n <= paradas[i][0]) {
      const [x0, h0] = paradas[i - 1];
      const [x1, h1] = paradas[i];
      hue = h0 + ((n - x0) / (x1 - x0)) * (h1 - h0);
      break;
    }
  return { background: `hsl(${hue.toFixed(0)} 55% 90%)`, color: `hsl(${hue.toFixed(0)} 65% 24%)`, borderColor: `hsl(${hue.toFixed(0)} 40% 72%)` };
}

const claseTipo: Record<string, string> = { Tarea: "t-tarea", APE: "t-ape", Examen: "t-examen" };
// Barra de los 10 puntos: un tramo por tipo (30 % → 3 puntos) que se llena con lo ganado hasta ahora.
export function BarraPuntos({ desglose, acumulado }: { desglose: PuntosTipo[]; acumulado: number }) {
  return (
    <div className="puntos-celda">
      <strong>
        {acumulado.toFixed(2)}
        <small className="muted" style={{ display: "inline", marginLeft: 4, fontWeight: 400 }}>
          / 10
        </small>
      </strong>
      <div className="puntos" role="img" aria-label={`${acumulado.toFixed(2)} puntos de 10`}>
        {desglose.map((p) => (
          <span key={p.tipo} className={claseTipo[p.tipo]} style={{ flex: Math.max(p.maximo, 0.01) }} title={`${p.tipo}: ${p.puntos.toFixed(2)} de ${Number(p.maximo.toFixed(2))}`}>
            <i style={{ width: `${p.maximo > 0 ? Math.min(100, (p.puntos / p.maximo) * 100) : 0}%` }} />
          </span>
        ))}
      </div>
    </div>
  );
}
export function LeyendaPuntos({ desglose }: { desglose: PuntosTipo[] }) {
  return (
    <div className="leyenda-puntos" aria-label="Cómo se reparten los 10 puntos">
      <span>Los 10 puntos se reparten así:</span>
      {desglose.map((p) => (
        <span key={p.tipo} className={claseTipo[p.tipo]}>
          {p.tipo === "Tarea" ? "Tareas" : p.tipo === "Examen" ? "Exámenes" : "APE"} {p.porcentaje}% = {Number(p.maximo.toFixed(2))} pts
        </span>
      ))}
    </div>
  );
}
