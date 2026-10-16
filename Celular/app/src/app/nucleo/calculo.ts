// ============================================================
//  Proyecto: Mis Finanzas
//  Autoria: Juan Almonacid
//  Copyright (c) 2026 Juan Almonacid. Todos los derechos reservados.
// ============================================================
// Resumen del mes calculado en el celular con los datos de la nube, sin depender del PC.
// Es la traducción de ResumenCelular.cs (y lo que usa: CuentasMensuales, Amortizacion, Creditos, FlujoCaja,
// DashboardView.FilasPorPagar). tests/calculo.cjs compara el resultado con el que calcula el PC.
// Funciones puras: sin Angular ni Firebase.

// ---------- Datos (documentos de la nube ya leídos) ----------
export interface MovimientoN {
  uid: string; idPc: number; tipo: string; descripcion: string; costo: number; venta: number; fecha: Date;
  diaPago: number; mesFin: string; frecuencia: string; intervaloMeses: number; categoria: string;
}
export interface PagoCuentaN { uid: string; movimientoUid: string; mes: string; monto: number; quincena: number; }
export interface CreditoN {
  uid: string; idPc: number; nombreCustom: string; total: number; cuotaMensual: number; disponible: number;
  tasaEA: number; plazoMeses: number; diaPago: number; modalidad: string;
}
export interface PagoCreditoN { uid: string; creditoUid: string; mes: string; fecha: Date; tipo: string; monto: number; interes: number; capital: number; }
export interface MetaN { uid: string; nombre: string; nombreCustom: string; montoObjetivo: number; montoActual: number; }
export interface AbonoMetaN { uid: string; metaUid: string; fecha: Date; monto: number; }
export interface VehiculoN { uid: string; idPc: number; nombre: string; tipo: string; placa: string; }
export interface GastoVehiculoN { uid: string; vehiculoUid: string; concepto: string; valor: number; fecha: Date; gastoDiarioUid: string; }
export interface GastoN { id: string; fecha: Date; monto: number; categoria: string; descripcion: string; tarjetaId: number | null; cuotas: number; }

export interface DatosNube {
  movimientos: MovimientoN[]; pagosCuenta: PagoCuentaN[]; creditos: CreditoN[]; pagosCredito: PagoCreditoN[];
  metas: MetaN[]; abonosMeta: AbonoMetaN[]; vehiculos: VehiculoN[]; gastosVehiculo: GastoVehiculoN[];
  gastos: GastoN[]; inicioRegistro: Date | null;
}

// ---------- Fechas y textos (como en el PC, cultura es-CO) ----------
export const TIPO_CUOTA = 'Cuota';
export const TARJETA = 'Tarjeta de crédito';
const SIN_CATEGORIA = 'Sin categoría';
const MAXIMO_POR_PAGAR = 5;
const MAXIMO_CUOTAS = 600;

const dia = (d: Date) => new Date(d.getFullYear(), d.getMonth(), d.getDate());
export const inicioMes = (d: Date) => new Date(d.getFullYear(), d.getMonth(), 1);
export const sumarMeses = (d: Date, n: number) => new Date(d.getFullYear(), d.getMonth() + n, 1);
const diasEnMes = (d: Date) => new Date(d.getFullYear(), d.getMonth() + 1, 0).getDate();
export const clave = (d: Date) => `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`;
const dd = (n: number) => String(n).padStart(2, '0');
const ddMM = (d: Date) => `${dd(d.getDate())}/${dd(d.getMonth() + 1)}`;
const mismoDia = (a: Date, b: Date) => dia(a).getTime() === dia(b).getTime();
const diasEntre = (desde: Date, hasta: Date) => Math.round((dia(hasta).getTime() - dia(desde).getTime()) / 86400000);
const MES_CORTO = new Intl.DateTimeFormat('es-CO', { month: 'short' });
const MES_LARGO = new Intl.DateTimeFormat('es-CO', { month: 'long' });
const mesCorto = (d: Date) => MES_CORTO.format(d).replace('.', '').toUpperCase();
const nombreMes = (d: Date) => { const t = new Intl.DateTimeFormat('es-CO', { month: 'long', year: 'numeric' }).format(d).replace(' de ', ' '); return t.charAt(0).toUpperCase() + t.slice(1); };
const PESOS = new Intl.NumberFormat('es-CO', { style: 'currency', currency: 'COP', maximumFractionDigits: 0 });
/** "33 %" como el formato P0 del PC (con espacio, sin depender del navegador). */
const porcentaje = (x: number) => `${redondear(x * 100)} %`;
const mostrarCategoria = (c: string) => c?.trim() ? c : SIN_CATEGORIA;
/** Math.Round(x, 0, MidpointRounding.AwayFromZero) */
export const redondear = (x: number) => Math.sign(x) * Math.round(Math.abs(x));

// ---------- Cuentas del mes (CuentasMensuales.cs) ----------
export const QUINCENAL = 'Quincenal';
const diaDePago = (c: MovimientoN) => c.diaPago > 0 ? c.diaPago : c.fecha.getDate();
const diasQuincenales = (c: MovimientoN) => { const d = diaDePago(c); return d <= 15 ? [d, d + 15] : [d - 15, d]; };
export function vencimiento(c: MovimientoN, mes: Date, quincena = 0): Date {
  const inicio = inicioMes(mes);
  const d = Math.min(quincena > 0 ? diasQuincenales(c)[quincena - 1] : diaDePago(c), diasEnMes(inicio));
  return new Date(inicio.getFullYear(), inicio.getMonth(), d);
}
export function aplicaEn(c: MovimientoN, mes: Date): boolean {
  const k = clave(mes), inicio = clave(c.fecha);
  if (inicio > k) return false;
  if (c.mesFin && k >= c.mesFin) return false;
  if (c.frecuencia === 'Única') return inicio === k;
  if (c.frecuencia === 'Anual') return c.fecha.getMonth() === mes.getMonth();
  const entre = (mes.getFullYear() - c.fecha.getFullYear()) * 12 + mes.getMonth() - c.fecha.getMonth();
  return c.intervaloMeses <= 1 || entre % c.intervaloMeses === 0;
}
export interface CuentaMesN {
  cuenta: MovimientoN; quincena: number; vence: Date; pagada: boolean; montoPagado: number; real: number;
  venta: number; descripcion: string; categoria: string;
}
export function delMes(cuentas: MovimientoN[], pagos: PagoCuentaN[], mes: Date): CuentaMesN[] {
  const k = clave(mes);
  const pagosMes = new Map<string, PagoCuentaN>();
  for (const p of pagos) if (p.mes === k) { const id = `${p.movimientoUid}|${p.quincena === 2 ? 2 : 1}`; if (!pagosMes.has(id)) pagosMes.set(id, p); }
  const fila = (c: MovimientoN, q: number): CuentaMesN => {
    const pago = pagosMes.get(`${c.uid}|${q === 2 ? 2 : 1}`);
    const montoPagado = pago?.monto ?? c.venta;
    return { cuenta: c, quincena: q, vence: vencimiento(c, mes, q), pagada: !!pago, montoPagado, real: pago ? montoPagado : c.venta,
             venta: c.venta, descripcion: c.descripcion, categoria: mostrarCategoria(c.categoria) };
  };
  return cuentas.filter(c => c.tipo === 'Cuenta' && aplicaEn(c, mes))
    .flatMap(c => c.frecuencia === QUINCENAL ? [fila(c, 1), fila(c, 2)] : [fila(c, 0)])
    .sort((a, b) => a.vence.getTime() - b.vence.getTime() || a.descripcion.localeCompare(b.descripcion, 'es'));
}

// ---------- Créditos (Amortizacion.cs y Creditos.cs) ----------
/** (1 + EA)^(1/12) − 1, con los 15 dígitos que conserva decimal en el PC. */
export const tasaMensual = (ea: number) => ea <= 0 ? 0 : Number((Math.pow(1 + ea / 100, 1 / 12) - 1).toPrecision(15));
const interesDelMes = (saldo: number, ea: number) => saldo <= 0 ? 0 : redondear(saldo * tasaMensual(ea));
function repartirCuota(saldo: number, ea: number, cuota: number) {
  const interes = interesDelMes(saldo, ea);
  const capital = Math.min(cuota - interes, saldo);
  return { cuota: interes + capital, interes, capital };
}
/** Cuota del plan (sistema francés) en el mes pedido, empezando en 'desde'; null si el plan no llega. */
function cuotaDelPlan(c: CreditoN, desde: Date, mes: Date): number | null {
  let saldo = c.disponible, m = inicioMes(desde);
  const objetivo = clave(mes);
  for (let n = 1; saldo > 0 && c.cuotaMensual > 0 && n <= MAXIMO_CUOTAS; n++) {
    const r = repartirCuota(saldo, c.tasaEA, c.cuotaMensual);
    if (clave(m) === objetivo) return r.cuota;
    if (r.capital <= 0 || clave(m) > objetivo) return null;
    saldo -= r.capital; m = sumarMeses(m, 1);
  }
  return null;
}
export const esTarjeta = (c: CreditoN) => c.modalidad === TARJETA;
const saldado = (c: CreditoN) => c.disponible <= 0;
const historial = (c: CreditoN, pagos: PagoCreditoN[]) => pagos.filter(p => p.creditoUid === c.uid).sort((a, b) => b.fecha.getTime() - a.fecha.getTime());
const cuotaPagadaEn = (c: CreditoN, pagos: PagoCreditoN[], k: string) => historial(c, pagos).find(p => p.tipo === TIPO_CUOTA && p.mes === k) ?? null;
export function vencimientoCredito(c: CreditoN, mes: Date): Date | null {
  if (c.diaPago <= 0) return null;
  const inicio = inicioMes(mes);
  return new Date(inicio.getFullYear(), inicio.getMonth(), Math.min(c.diaPago, diasEnMes(inicio)));
}
/** Lo que cuesta este mes: lo pagado, o la cuota (o lo que falte si es la última). Las tarjetas van aparte. */
export function cuotaEsteMes(c: CreditoN, pagos: PagoCreditoN[], hoy: Date): number {
  if (esTarjeta(c)) return 0;
  const pagada = cuotaPagadaEn(c, pagos, clave(hoy));
  if (pagada) return pagada.monto;
  return saldado(c) ? 0 : repartirCuota(c.disponible, c.tasaEA, c.cuotaMensual).cuota;
}
export function cuotaVencidaHoy(c: CreditoN, pagos: PagoCreditoN[], hoy: Date): boolean {
  const vence = vencimientoCredito(c, hoy);
  return !saldado(c) && c.cuotaMensual > 0 && !cuotaPagadaEn(c, pagos, clave(hoy)) && vence !== null && dia(vence) <= dia(hoy);
}
/** Cuota de un mes que no es el actual: lo pagado, o la del plan desde la próxima cuota pendiente. */
function cuotaEnMes(c: CreditoN, pagos: PagoCreditoN[], mes: Date, hoy: Date): number {
  if (esTarjeta(c)) return 0;
  const k = clave(mes);
  const pagadas = pagos.filter(p => p.creditoUid === c.uid && p.tipo === TIPO_CUOTA && p.mes === k);
  if (pagadas.length) return pagadas.reduce((s, p) => s + p.monto, 0);
  const pagadoEsteMes = pagos.some(p => p.creditoUid === c.uid && p.tipo === TIPO_CUOTA && p.mes === clave(hoy));
  return cuotaDelPlan(c, sumarMeses(inicioMes(hoy), pagadoEsteMes ? 1 : 0), mes) ?? 0;
}
interface CuotaDelMesN { credito: CreditoN; cuota: number; pagada: boolean; }
function cuotasDelMes(creditos: CreditoN[], pagos: PagoCreditoN[], mes: Date, hoy: Date): CuotaDelMesN[] {
  if (clave(mes) === clave(hoy)) return creditos.map(c => ({ credito: c, cuota: cuotaEsteMes(c, pagos, hoy), pagada: !!cuotaPagadaEn(c, pagos, clave(hoy)) }));
  const k = clave(mes);
  return creditos.map(c => ({ credito: c, cuota: cuotaEnMes(c, pagos, mes, hoy), pagada: pagos.some(p => p.creditoUid === c.uid && p.tipo === TIPO_CUOTA && p.mes === k) }))
    .filter(x => x.cuota > 0);
}

/** Cuota de la tarjeta en el mes: la suma de la cuota de cada compra vigente (Amortizacion.CuotaTarjeta). */
export function cuotaTarjeta(t: CreditoN, compras: GastoN[], mes: Date) {
  const r = tasaMensual(t.tasaEA);
  let capitalTotal = 0, intereses = 0;
  for (const c of compras) {
    if (c.tarjetaId !== t.idPc || c.monto <= 0) continue;
    const n = Math.max(1, c.cuotas || 1);
    const k = (mes.getFullYear() - c.fecha.getFullYear()) * 12 + mes.getMonth() - c.fecha.getMonth();
    if (k < 1 || k > n) continue;
    let cuota: number, interes: number;
    if (n === 1 || r <= 0) { cuota = redondear(c.monto / n); interes = 0; }
    else {
      const fija = c.monto * r / (1 - Math.pow(1 + r, -n));
      const saldo = c.monto * Math.pow(1 + r, k - 1) - fija * (Math.pow(1 + r, k - 1) - 1) / r;
      cuota = redondear(fija); interes = redondear(saldo * r);
    }
    capitalTotal += cuota - interes; intereses += interes;
  }
  const capital = Math.min(capitalTotal, Math.max(0, t.disponible));
  return { cuota: capital + intereses, interes: intereses, capital };
}
const comprasDesde = (gastos: GastoN[], t: CreditoN, desde: Date, hasta?: Date) =>
  gastos.filter(g => g.tarjetaId === t.idPc && g.fecha >= desde && (!hasta || g.fecha < hasta));

/** Cuota de la tarjeta este mes (CreditosView.CargarDatosGlobal): lo pagado o lo de sus compras. */
function cuotaMesTarjeta(t: CreditoN, pagos: PagoCreditoN[], gastos: GastoN[], hoy: Date) {
  const pagada = cuotaPagadaEn(t, pagos, clave(hoy));
  if (pagada) return { cuota: pagada.monto, interes: pagada.interes, pagada: true };
  const r = cuotaTarjeta(t, comprasDesde(gastos, t, sumarMeses(inicioMes(hoy), -37)), hoy);
  return { cuota: r.cuota, interes: r.interes, pagada: false };
}

/** Intereses de las tarjetas del mes: lo pagado, o lo que sale de las cuotas de sus compras (Analisis.InteresesTarjetas). */
function interesesTarjetas(creditos: CreditoN[], pagos: PagoCreditoN[], gastos: GastoN[], mes: Date): number {
  const k = clave(mes), m = inicioMes(mes);
  return creditos.filter(esTarjeta).reduce((s, t) => {
    const pagadas = pagos.filter(p => p.creditoUid === t.uid && p.tipo === TIPO_CUOTA && p.mes === k);
    const interes = pagadas.length ? pagadas.reduce((x, p) => x + p.interes, 0)
      : cuotaTarjeta(t, comprasDesde(gastos, t, sumarMeses(m, -37), m), m).interes;
    return s + (interes > 0 ? interes : 0);
  }, 0);
}

/** Pago de la cuota del mes de un préstamo, como Amortizacion.PagarCuota: interés y capital, y el saldo y plazo que quedan. */
export interface PagoDeCuota { mes: string; monto: number; interes: number; capital: number; saldoAnterior: number; saldoNuevo: number; mesesReducidos: number; plazoMeses: number; }
export function pagoDeCuota(c: CreditoN, pagos: PagoCreditoN[], hoy: Date): PagoDeCuota | null {
  const mes = clave(hoy);
  if (esTarjeta(c) || saldado(c) || c.cuotaMensual <= 0 || cuotaPagadaEn(c, pagos, mes)) return null;
  const r = repartirCuota(c.disponible, c.tasaEA, c.cuotaMensual);
  const reducidos = c.plazoMeses > 0 ? 1 : 0;
  return { mes, monto: r.cuota, interes: r.interes, capital: r.capital, saldoAnterior: c.disponible, saldoNuevo: c.disponible - r.capital,
           mesesReducidos: reducidos, plazoMeses: c.plazoMeses - reducidos };
}

// ---------- Dinero real (FlujoCaja.cs) ----------
export function flujoCaja(d: DatosNube, hasta: Date) {
  const limite = new Date(hasta.getFullYear(), hasta.getMonth(), hasta.getDate() + 1);
  const ultimo = clave(hasta);
  const salidas: Record<string, number> = {};
  const agregar = (mes: string, monto: number) => { if (mes <= ultimo) salidas[mes] = (salidas[mes] ?? 0) + monto; };
  for (const p of d.pagosCuenta) agregar(p.mes, p.monto);
  for (const p of d.pagosCredito) if (p.fecha < limite) agregar(clave(p.fecha), p.monto);
  for (const a of d.abonosMeta) if (a.fecha < limite) agregar(clave(a.fecha), a.monto);
  const diarios = d.gastos.filter(g => g.fecha < limite);
  for (const g of diarios) if (g.tarjetaId === null) agregar(clave(g.fecha), g.monto);
  for (const g of d.gastosVehiculo) if (!g.gastoDiarioUid && g.fecha < limite) agregar(clave(g.fecha), g.valor);
  return {
    hasta: `${hasta.getFullYear()}-${dd(hasta.getMonth() + 1)}-${dd(hasta.getDate())}`,
    egresosPorMes: salidas,
    gastosIncluidos: diarios.filter(g => clave(g.fecha) === ultimo).map(g => ({ id: g.id, monto: g.tarjetaId === null ? g.monto : 0 })),
  };
}

// ---------- Inicio del registro (InicioRegistro.cs) ----------
export function inicioRegistro(d: DatosNube, hoy: Date): Date {
  if (d.inicioRegistro) return inicioMes(d.inicioRegistro);
  const manana = new Date(hoy.getFullYear(), hoy.getMonth(), hoy.getDate() + 1);
  const fechas = [...d.movimientos.map(m => m.fecha), ...d.gastos.map(g => g.fecha), ...d.gastosVehiculo.map(g => g.fecha),
                  ...d.abonosMeta.map(a => a.fecha), ...d.pagosCredito.map(p => p.fecha)]
    .filter(f => f.getFullYear() > 1 && f <= manana);
  return inicioMes(new Date(Math.min(hoy.getTime(), ...fechas.map(f => f.getTime()))));
}

// ---------- Por pagar (DashboardView.FilasPorPagar) ----------
export interface FilaPorPagar { dia: string; mesCorto: string; titulo: string; detalle: string; monto: number; vencida: boolean; proxima: boolean; credito: boolean; }
function filasPorPagar(cuentasMes: CuentaMesN[], cuotas: CuotaDelMesN[], proximas: CuentaMesN[], mes: Date, hoy: Date, d: DatosNube): FilaPorPagar[] {
  const conFecha = (vence: Date, titulo: string, monto: number, credito: boolean): { vence: Date; fila: FilaPorPagar } => {
    const vencida = dia(vence) <= dia(hoy);
    return { vence, fila: { dia: dd(vence.getDate()), mesCorto: mesCorto(vence), titulo, monto, credito, proxima: false, vencida,
      detalle: mismoDia(vence, hoy) ? 'Vence hoy' : vencida ? `Vencida el ${ddMM(vence)}` : `Vence el ${ddMM(vence)}` } };
  };
  const filasFecha = cuentasMes.filter(c => !c.pagada).map(c => conFecha(c.vence, c.descripcion, c.venta, false));
  const sinFecha: FilaPorPagar[] = [];
  const esActual = clave(mes) === clave(hoy);
  for (const cr of cuotas.filter(c => !c.pagada && c.cuota > 0)) {
    const titulo = `Cuota crédito · ${cr.credito.nombreCustom}`;
    const vence = vencimientoCredito(cr.credito, mes);
    if (vence) filasFecha.push(conFecha(vence, titulo, cr.cuota, true));
    else sinFecha.push({ dia: '—', mesCorto: mesCorto(mes), titulo, monto: cr.cuota, credito: true, proxima: false, vencida: false,
      detalle: esActual ? 'Pendiente este mes' : `Cuota de ${MES_LARGO.format(mes)}` });
  }
  if (esActual)
    for (const cr of cuotas.filter(c => esTarjeta(c.credito))) {
      const t = cuotaMesTarjeta(cr.credito, d.pagosCredito, d.gastos, hoy);
      if (t.pagada || t.cuota <= 0) continue;
      const titulo = `Cuota tarjeta · ${cr.credito.nombreCustom}`;
      const vence = vencimientoCredito(cr.credito, mes);
      if (vence) filasFecha.push(conFecha(vence, titulo, t.cuota, true));
      else sinFecha.push({ dia: '—', mesCorto: mesCorto(mes), titulo, monto: t.cuota, credito: true, proxima: false, vencida: false,
        detalle: t.interes > 0 ? `Incluye ${PESOS.format(t.interes)} de intereses` : 'Pendiente este mes' });
    }

  const ordenadas = [...filasFecha].sort((a, b) => a.vence.getTime() - b.vence.getTime());
  const todas = [...ordenadas.filter(x => dia(x.vence) <= dia(hoy)).map(x => x.fila), ...sinFecha,
                 ...ordenadas.filter(x => dia(x.vence) > dia(hoy)).map(x => x.fila)];
  const filas: FilaPorPagar[] = [];
  let cupo = Math.max(0, MAXIMO_POR_PAGAR - todas.filter(f => f.credito).length);
  for (const f of todas) if (f.credito || cupo-- > 0) filas.push(f);

  for (const p of [...proximas].sort((a, b) => a.vence.getTime() - b.vence.getTime()).slice(0, Math.max(0, Math.min(2, MAXIMO_POR_PAGAR - filas.length)))) {
    const dias = diasEntre(hoy, p.vence);
    filas.push({ dia: dd(p.vence.getDate()), mesCorto: mesCorto(p.vence), titulo: p.descripcion, monto: p.venta, credito: false, proxima: true, vencida: false,
      detalle: dias >= 0 ? `${p.categoria} · en ${dias} ${dias === 1 ? 'día' : 'días'}` : `${p.categoria} · ${ddMM(p.vence)}/${p.vence.getFullYear()}` });
  }
  return filas;
}

// ---------- Resumen del mes (ResumenCelular.Construir) ----------
export function construirResumen(d: DatosNube, mesPedido: Date, hoy: Date) {
  const mes = inicioMes(mesPedido);
  const esActual = clave(mes) === clave(hoy);
  const inicio = inicioRegistro(d, hoy);
  const siguienteMes = sumarMeses(mes, 1);
  const hastaCaja = mes > inicioMes(hoy) || esActual ? hoy : new Date(mes.getFullYear(), mes.getMonth() + 1, 0);

  // Ingresos: salario + extras que aplican (antes del inicio del registro, nada)
  const salario = [...d.movimientos].filter(m => m.tipo === 'Ingreso').sort((a, b) => a.idPc - b.idPc)[0]?.venta ?? 0;
  const ingresos = mes < inicio ? 0 : salario + d.movimientos.filter(m => m.tipo === 'IngresoExtra' && aplicaEn(m, mes)).reduce((s, m) => s + m.venta, 0);

  const cuentasMes = delMes(d.movimientos, d.pagosCuenta, mes);
  const cuotas = cuotasDelMes(d.creditos, d.pagosCredito, mes, hoy);
  const diarios = d.gastos.filter(g => g.fecha >= mes && g.fecha < siguienteMes).reduce((s, g) => s + g.monto, 0)
    + d.gastosVehiculo.filter(g => !g.gastoDiarioUid && g.fecha >= mes && g.fecha < siguienteMes).reduce((s, g) => s + g.valor, 0)
    + interesesTarjetas(d.creditos, d.pagosCredito, d.gastos, mes);
  const gastos = cuentasMes.reduce((s, c) => s + c.real, 0) + cuotas.reduce((s, c) => s + c.cuota, 0) + diarios;
  const balance = ingresos - gastos;
  const metas = new Set(d.metas.map(m => m.uid));
  const ahorroMes = d.abonosMeta.filter(a => clave(a.fecha) === clave(mes) && metas.has(a.metaUid)).reduce((s, a) => s + a.monto, 0);

  // uid: el registro en la nube, para que el botón "Pagado" del celular guarde el pago directo allá
  const vencidas: { tipo: 'cuenta' | 'credito'; id: number; uid: string; mes: string; quincena?: number; nombre: string; monto: number; detalle: string }[] = [];
  if (esActual) {
    for (const c of cuentasMes.filter(c => !c.pagada && dia(c.vence) <= dia(hoy)))
      vencidas.push({ tipo: 'cuenta', id: c.cuenta.idPc, uid: c.cuenta.uid, mes: clave(mes), quincena: c.quincena, nombre: c.descripcion, monto: c.venta,
                      detalle: mismoDia(c.vence, hoy) ? 'Vence hoy' : `Vencida el ${ddMM(c.vence)}` });
    for (const c of d.creditos.filter(c => cuotaVencidaHoy(c, d.pagosCredito, hoy))) {
      const vence = vencimientoCredito(c, hoy)!;
      vencidas.push({ tipo: 'credito', id: c.idPc, uid: c.uid, mes: clave(mes), nombre: `Cuota crédito · ${c.nombreCustom}`, monto: cuotaEsteMes(c, d.pagosCredito, hoy),
                      detalle: mismoDia(vence, hoy) ? 'Vence hoy' : `Vencida el ${ddMM(vence)}` });
    }
  }

  const proximas = delMes(d.movimientos, d.pagosCuenta, siguienteMes).filter(c => !c.pagada);
  const porPagar = filasPorPagar(cuentasMes, cuotas, proximas, mes, hoy, d);

  const ahorro = d.metas.reduce((s, m) => s + m.montoActual, 0);
  const progresoMeta = (m: MetaN) => m.montoObjetivo > 0 ? m.montoActual / m.montoObjetivo : 0;
  const ahorroDetalle = d.metas.length === 0 ? 'Sin metas de ahorro' : d.metas.length === 1 ? `Meta al ${porcentaje(progresoMeta(d.metas[0]))}` : `Repartido en ${d.metas.length} metas`;
  const deuda = d.creditos.reduce((s, c) => s + Math.max(0, c.disponible), 0);
  const prestamos = d.creditos.filter(c => !esTarjeta(c));
  const prestado = prestamos.reduce((s, c) => s + c.total, 0);
  const inversiones = d.movimientos.filter(m => m.tipo === 'Inversion').sort((a, b) => a.idPc - b.idPc);
  const vehiculos = [...d.vehiculos].sort((a, b) => a.idPc - b.idPc);
  const invertidoEn = (v: VehiculoN) => d.gastosVehiculo.filter(g => g.vehiculoUid === v.uid).reduce((s, g) => s + g.valor, 0);

  return {
    mes: clave(mes),
    inicioRegistro: clave(inicio),
    nombre: nombreMes(mes),
    ingresos, gastos, ahorroMes,
    disponible: balance - ahorroMes,
    caja: flujoCaja(d, hastaCaja),
    saludable: balance >= 0,
    vencidas,
    porPagar,
    ahorro: { total: ahorro, detalle: ahorroDetalle },
    inversiones: { total: inversiones.reduce((s, i) => s + i.costo, 0), detalle: inversiones.length === 0 ? 'Sin inversiones' : inversiones.slice(0, 2).map(i => i.descripcion).join(' · ') },
    deuda: { total: deuda, progreso: prestado > 0 ? prestamos.reduce((s, c) => s + Math.max(0, c.total - c.disponible), 0) / prestado : 0 },
    vehiculos: { total: vehiculos.reduce((s, v) => s + invertidoEn(v), 0), detalle: vehiculos.length === 0 ? 'Sin vehículos' : vehiculos.slice(0, 2).map(v => v.nombre).join(' · ') },
    listaTarjetas: d.creditos.filter(esTarjeta).map(c => ({ id: c.idPc, nombre: c.nombreCustom, cupo: c.total, deuda: c.disponible, disponible: Math.max(0, c.total - c.disponible), tasaEA: c.tasaEA })),
    listaVehiculos: vehiculos.map(v => ({ id: v.idPc, nombre: v.nombre, tipo: v.tipo, placa: v.placa })),
  };
}

/** Meses que se muestran: del inicio del registro al mes siguiente al actual. */
export function mesesDelRegistro(d: DatosNube, hoy: Date): string[] {
  const lista: string[] = [];
  for (let m = inicioRegistro(d, hoy); m <= sumarMeses(inicioMes(hoy), 1); m = sumarMeses(m, 1)) lista.push(clave(m));
  return lista;
}

// ---------- Documentos de la nube → datos (los valores ya vienen como en el SDK de Firestore) ----------
type Doc = Record<string, unknown>;
const num = (v: unknown) => typeof v === 'number' ? v : Number(v) || 0;
const txt = (v: unknown) => typeof v === 'string' ? v : '';
const fecha = (v: unknown): Date => v instanceof Date ? v : (v as { toDate?: () => Date })?.toDate?.() ?? new Date(0);
const vivos = (docs: { id: string; data: Doc }[]) => docs.filter(x => x.data['borrado'] !== true);

export function desdeDocumentos(col: Record<string, { id: string; data: Doc }[]>, gastos: GastoN[]): DatosNube {
  const v = (n: string) => vivos(col[n] ?? []);
  // Como el PC (InicioRegistro.Obtener): el primer registro guardado
  const inicio = [...v('inicioRegistro')].sort((a, b) => num(a.data['idPc']) - num(b.data['idPc'])).map(x => fecha(x.data['fecha']))[0] ?? null;
  return {
    movimientos: v('movimientos').map(({ id, data: x }) => ({ uid: id, idPc: num(x['idPc']), tipo: txt(x['tipo']), descripcion: txt(x['descripcion']),
      costo: num(x['costo']), venta: num(x['venta']), fecha: fecha(x['fecha']), diaPago: num(x['diaPago']), mesFin: txt(x['mesFin']),
      frecuencia: txt(x['frecuencia']) || 'Mensual', intervaloMeses: num(x['intervaloMeses']), categoria: txt(x['categoria']) })),
    pagosCuenta: v('pagosCuenta').map(({ id, data: x }) => ({ uid: id, movimientoUid: txt(x['movimientoUid']), mes: txt(x['mes']), monto: num(x['monto']), quincena: num(x['quincena']) })),
    creditos: v('creditos').map(({ id, data: x }) => ({ uid: id, idPc: num(x['idPc']), nombreCustom: txt(x['nombreCustom']), total: num(x['total']),
      cuotaMensual: num(x['cuotaMensual']), disponible: num(x['disponible']), tasaEA: num(x['tasaEA']), plazoMeses: num(x['plazoMeses']),
      diaPago: num(x['diaPago']), modalidad: txt(x['modalidad']) })),
    pagosCredito: v('pagosCredito').map(({ id, data: x }) => ({ uid: id, creditoUid: txt(x['creditoUid']), mes: txt(x['mes']), fecha: fecha(x['fecha']),
      tipo: txt(x['tipo']) || TIPO_CUOTA, monto: num(x['monto']), interes: num(x['interes']), capital: num(x['capital']) })),
    metas: v('metas').map(({ id, data: x }) => ({ uid: id, nombre: txt(x['nombre']), nombreCustom: txt(x['nombreCustom']), montoObjetivo: num(x['montoObjetivo']), montoActual: num(x['montoActual']) })),
    abonosMeta: v('abonosMeta').map(({ id, data: x }) => ({ uid: id, metaUid: txt(x['metaUid']), fecha: fecha(x['fecha']), monto: num(x['monto']) })),
    vehiculos: v('vehiculos').map(({ id, data: x }) => ({ uid: id, idPc: num(x['idPc']), nombre: txt(x['nombre']), tipo: txt(x['tipo']), placa: txt(x['placa']) })),
    gastosVehiculo: v('gastosVehiculo').map(({ id, data: x }) => ({ uid: id, vehiculoUid: txt(x['vehiculoUid']), concepto: txt(x['concepto']), valor: num(x['valor']),
      fecha: fecha(x['fecha']), gastoDiarioUid: txt(x['gastoDiarioUid']) })),
    gastos,
    inicioRegistro: inicio,
  };
}
