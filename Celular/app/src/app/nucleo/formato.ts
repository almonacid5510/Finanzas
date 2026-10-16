// ============================================================
//  Proyecto: Mis Finanzas
//  Autoria: Juan Almonacid
//  Copyright (c) 2026 Juan Almonacid. Todos los derechos reservados.
// ============================================================
// Formatos de pesos y fechas en español de Colombia.
const PESOS = new Intl.NumberFormat('es-CO', { style: 'currency', currency: 'COP', maximumFractionDigits: 0 });
const MILES = new Intl.NumberFormat('es-CO');

// Sin cortes de línea entre $ y el número (espacio que no se parte)
export const pesos = (n: number) => PESOS.format(n || 0).replace(/\s/g, '\u00a0');
export const miles = (n: number) => MILES.format(n || 0);
export const soloDigitos = (t: string) => Number(String(t).replace(/\D/g, '')) || 0;
/** "2026-10-14" → "14 de octubre" (con el año solo si no es el actual). */
export function fechaLarga(iso: string) {
  const fecha = new Date(`${iso}T00:00:00`);
  if (!Number.isFinite(fecha.getTime())) return iso;
  const conAnio = fecha.getFullYear() !== new Date().getFullYear();
  return new Intl.DateTimeFormat('es-CO', { day: 'numeric', month: 'long', ...(conAnio ? { year: 'numeric' } : {}) }).format(fecha);
}

/** "2026-10-14" en hora local */
export function aISO(d: Date): string {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

/** "2026-10" */
export const claveMes = (d: Date) => aISO(d).slice(0, 7);

export function inicioDia(d = new Date()): Date {
  const x = new Date(d);
  x.setHours(0, 0, 0, 0);
  return x;
}

export function inicioSemana(d = new Date()): Date {
  const x = inicioDia(d);
  x.setDate(x.getDate() - ((x.getDay() + 6) % 7)); // la semana empieza el lunes
  return x;
}

export const inicioMes = (d = new Date()) => new Date(d.getFullYear(), d.getMonth(), 1);

export const hora = (d: Date) => d.toLocaleTimeString('es-CO', { hour: 'numeric', minute: '2-digit' });

/** "HOY", "AYER" o "LUNES 12 DE OCTUBRE" */
export function tituloDia(d: Date): string {
  const hoy = aISO(new Date());
  const ayer = new Date();
  ayer.setDate(ayer.getDate() - 1);
  if (aISO(d) === hoy) return 'HOY';
  if (aISO(d) === aISO(ayer)) return 'AYER';
  return d.toLocaleDateString('es-CO', { weekday: 'long', day: 'numeric', month: 'long' }).toUpperCase();
}

/** "Oct 2026" */
export function nombreMesCorto(clave: string): string {
  const [a, m] = clave.split('-').map(Number);
  const t = new Date(a, m - 1, 1).toLocaleDateString('es-CO', { month: 'short' }).replace('.', '');
  return `${t[0].toUpperCase()}${t.slice(1)} ${a}`;
}

/** "hace un momento", "hace 5 min", "hace 2 h", "el 12 oct" */
export function haceCuanto(d: Date): string {
  const min = Math.round((Date.now() - d.getTime()) / 60000);
  if (min < 1) return 'hace un momento';
  if (min < 60) return `hace ${min} min`;
  if (min < 24 * 60) return `hace ${Math.round(min / 60)} h`;
  return `el ${d.toLocaleDateString('es-CO', { day: 'numeric', month: 'short' }).replace('.', '')}`;
}
