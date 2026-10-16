// Saldo de dinero recibido, independiente del presupuesto mensual.
export interface Cobro { id: string; fecha: Date; monto: number; concepto: string; inicial: boolean; }
export interface CajaPC {
  hasta: string;
  egresosPorMes: Record<string, number>;
  gastosIncluidos: { id: string; monto: number }[];
}
export function calcularCaja(cobros: Cobro[], caja: CajaPC, mes: string, hoy = new Date()) {
  const fechaHoy = `${hoy.getFullYear()}-${String(hoy.getMonth() + 1).padStart(2, '0')}-${String(hoy.getDate()).padStart(2, '0')}`;
  const clave = (d: Date) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`;
  const fin = mes < fechaHoy.slice(0, 7) ? new Date(Number(mes.slice(0,4)), Number(mes.slice(5)), 1) : new Date(hoy.getFullYear(), hoy.getMonth(), hoy.getDate() + 1);
  const vigentes = cobros.filter(c => c.fecha < fin && clave(c.fecha) <= mes);
  // Un saldo inicial es el dinero al comenzar ese mes. Los cierres se arrastran automáticamente.
  const inicial = vigentes.filter(c => c.inicial).sort((a,b) => b.fecha.getTime() - a.fecha.getTime())[0];
  const desde = inicial ? clave(inicial.fecha) : vigentes.map(c => clave(c.fecha)).sort()[0] ?? mes;
  const recibidos = vigentes.filter(c => !c.inicial && clave(c.fecha) >= desde).reduce((s,c) => s + c.monto, 0);
  const egresos = Object.entries(caja.egresosPorMes).filter(([m]) => m >= desde && m <= mes).reduce((s,[,v]) => s + v, 0);
  return { saldo: (inicial?.monto ?? 0) + recibidos - egresos, recibidos, egresos, desde, inicial: inicial?.monto ?? 0, confirmado: vigentes.length > 0 };
}

/** Sustituye las salidas diarias del resumen por su estado actual, sin descontar dos veces. */
export function ajusteGastos(caja: CajaPC, gastos: { id: string; monto: number; tarjetaId: number | null }[]) {
  return gastos.filter(g => g.tarjetaId === null).reduce((s,g) => s + g.monto, 0)
    - caja.gastosIncluidos.reduce((s,g) => s + g.monto, 0);
}
