export interface PlanNomina {
  frecuencia: string; dias: string; diaSemana: number; fechaBase: string;
  montoPorPago: number; confirmada: boolean;
  /** Quincenal: valor del pago del día más tardío (0 o ausente = igual a montoPorPago). */
  montoSegundoPago?: number;
}
export interface EsperadoNomina { id: string; fecha: string; monto: number; }
export const FRECUENCIAS_NOMINA = ['Mensual', 'Quincenal', 'Semanal'];
// Compatibilidad de lectura con calendarios ya guardados; no se ofrecen al configurar.
const FRECUENCIAS_GUARDADAS = [...FRECUENCIAS_NOMINA, 'Cada 14 días', 'Variable'];
export function validarNomina(c: PlanNomina) {
  if (!FRECUENCIAS_GUARDADAS.includes(c.frecuencia) || !Number.isSafeInteger(c.montoPorPago) || (c.montoPorPago < 0 || (c.frecuencia !== 'Variable' && c.montoPorPago === 0)) || c.montoPorPago > 1e12) throw new Error('Revisa la frecuencia y el monto neto esperado por pago.');
  if (c.frecuencia === 'Mensual' || c.frecuencia === 'Quincenal') {
    const dias = [...new Set(c.dias.split(',').filter(x => x.trim()).map(x => /^\d+$/.test(x.trim()) ? Number(x.trim()) : 0))];
    if (dias.length !== (c.frecuencia === 'Mensual' ? 1 : 2) || dias.some(d => !Number.isInteger(d) || d < 1 || d > 31)) throw new Error('Escribe un día o dos días diferentes (del 1 al 31).');
  }
  const segundo = c.montoSegundoPago ?? 0;
  if (!Number.isSafeInteger(segundo) || segundo < 0 || segundo > 1e12) throw new Error('Revisa el valor del segundo pago.');
  if (!Number.isInteger(c.diaSemana) || c.diaSemana < 0 || c.diaSemana > 6 || !/^\d{4}-\d{2}-\d{2}$/.test(c.fechaBase) || !Number.isFinite(new Date(c.fechaBase + 'T00:00:00').getTime())) throw new Error('Revisa el día semanal y la fecha de referencia.');
}
/** Valor de cada pago del mes: en quincenal el segundo (día más tardío) puede ser distinto. */
export function montoDelPago(c: PlanNomina, indice: number): number {
  return c.frecuencia === 'Quincenal' && indice === 1 && (c.montoSegundoPago ?? 0) > 0 ? c.montoSegundoPago! : c.montoPorPago;
}

export function pagosNomina(c: PlanNomina | null, mes: string): EsperadoNomina[] {
  if (!c?.confirmada) return [];
  validarNomina(c);
  const [y,m] = mes.split('-').map(Number);
  const ultimo = new Date(y,m,0).getDate();
  const dias = new Set<number>();
  if (c.frecuencia === 'Mensual' || c.frecuencia === 'Quincenal') return [...new Set(c.dias.split(',').filter(x => x.trim()).map(Number))].sort((a,b)=>a-b).map((d, i) => {
    const fecha = `${mes}-${String(Math.min(d,ultimo)).padStart(2,'0')}`;
    return { id:`nomina-${fecha}-dia${d}`, fecha, monto:montoDelPago(c, i) };
  });
  else {
    const [by,bm,bd] = c.fechaBase.split('-').map(Number);
    const base = Date.UTC(by,bm-1,bd);
    for (let d=1;d<=ultimo;d++) {
      if (c.frecuencia === 'Semanal' && new Date(y,m-1,d).getDay() === c.diaSemana) dias.add(d);
      if (c.frecuencia === 'Cada 14 días' && ((Date.UTC(y,m-1,d)-base)/86400000)%14 === 0) dias.add(d);
    }
  }
  return [...dias].sort((a,b)=>a-b).map(d => { const fecha = `${mes}-${String(d).padStart(2,'0')}`; return { id:`nomina-${fecha}`, fecha, monto:c.montoPorPago }; });
}

/** Pagos pendientes cuyo día siguiente ya llegó. Las claves ISO se comparan en hora local. */
export function recordatoriosNomina(plan: PlanNomina | null, meses: string[], recibidos: string[],
  pospuestos: Record<string, string>, hoy: string): EsperadoNomina[] {
  const confirmados = new Set(recibidos);
  return [...new Set([...meses, hoy.slice(0, 7)])].filter(m => m <= hoy.slice(0, 7))
    .flatMap(m => pagosNomina(plan, m))
    .filter(p => p.fecha < hoy && !confirmados.has(p.id) && (!pospuestos[p.id] || pospuestos[p.id] <= hoy))
    .sort((a, b) => a.fecha.localeCompare(b.fecha) || a.id.localeCompare(b.id));
}
