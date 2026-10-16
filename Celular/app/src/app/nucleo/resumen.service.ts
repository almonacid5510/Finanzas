// ============================================================
//  Proyecto: Mis Finanzas
//  Autoria: Juan Almonacid
//  Copyright (c) 2026 Juan Almonacid. Todos los derechos reservados.
// ============================================================
// Resumen de cada mes que publica Mis Finanzas del PC (usuarios/{uid}/resumen/{yyyy-MM}):
// los mismos números del Dashboard, calculados aquí con los datos de la nube. "Pagado" guarda el pago allá.
import { DestroyRef, Injectable, computed, effect, inject, signal } from '@angular/core';
import { Timestamp, collection, deleteDoc, doc, onSnapshot, serverTimestamp, setDoc, writeBatch } from 'firebase/firestore';
import { db, marcarCambio } from './firebase';
import { claveMes } from './formato';
import { mesesVisibles } from './periodos';
import { PlanNomina, validarNomina } from './nomina';
import { DatosNubeService } from './datos-nube.service';
import { TIPO_CUOTA, construirResumen, mesesDelRegistro, pagoDeCuota } from './calculo';
import { CajaPC, Cobro } from './caja';
import { VehiculoBasico } from './interprete';
import { SesionService } from './sesion.service';

export interface Vencida { tipo: 'cuenta' | 'credito'; id: number; mes: string; nombre: string; monto: number; detalle: string; quincena?: number; uid?: string; }
export interface PorPagar {
  dia: string; mesCorto: string; titulo: string; detalle: string; monto: number;
  vencida: boolean; proxima: boolean; credito: boolean;
}
export interface Tarjeta { total: number; detalle?: string; progreso?: number; }
/** Tarjeta de crédito (cupo, deuda y disponible), calculada con los datos de la nube */
export interface TarjetaCredito { id: number; nombre: string; cupo: number; deuda: number; disponible: number; tasaEA?: number; }

export interface Resumen {
  mes: string;
  inicioRegistro?: string;
  nombre: string;
  ingresos: number;
  gastos: number;
  ahorroMes: number;
  disponible: number;
  saludable: boolean;
  vencidas: Vencida[];
  porPagar: PorPagar[];
  ahorro: Tarjeta;
  inversiones: Tarjeta;
  deuda: Tarjeta;
  vehiculos: Tarjeta;
  listaVehiculos?: VehiculoBasico[];
  listaTarjetas?: TarjetaCredito[];
  caja?: CajaPC;
  actualizado: Date | null;
}

@Injectable({ providedIn: 'root' })
export class ResumenService {
  private readonly sesion = inject(SesionService);
  private cancelarCobros?: () => void;
  private cancelarNomina?: () => void;
  readonly nomina = signal<PlanNomina | null>(null);
  readonly nominaCargada = signal(false);
  readonly errorNomina = signal('');
  readonly cobros = signal<Cobro[]>([]);
  readonly cobrosCargados = signal(false);
  readonly errorCobros = signal('');

  private readonly nube = inject(DatosNubeService);
  /**
   * Resumen calculado aquí con los datos de la nube (la base principal): está al día aunque el PC esté
   * apagado. Mismo cálculo que el PC (calculo.ts ↔ ResumenCelular.cs).
   */
  private readonly calculados = computed(() => {
    const datos = this.nube.datos();
    if (!datos || !this.nube.hayDatos()) return null;
    const hoy = this.hoy();
    const mapa = new Map<string, Resumen>();
    for (const m of mesesDelRegistro(datos, hoy)) {
      const [y, mm] = m.split('-').map(Number);
      mapa.set(m, { ...construirResumen(datos, new Date(y, mm - 1, 1), hoy), actualizado: this.nube.actualizado() } as Resumen);
    }
    return mapa;
  });
  readonly resumenes = computed(() => this.calculados() ?? new Map<string, Resumen>());
  /** Ya llegaron los datos de la nube (si el PC aún no los subió, el Resumen queda vacío). */
  readonly cargado = computed(() => this.nube.listo());
  readonly mes = signal(claveMes(new Date()));
  private readonly hoy = signal(new Date());
  readonly inicioRegistro = computed(() => {
    const ordenados = [...this.resumenes().values()].sort((a,b) => (b.actualizado?.getTime() ?? 0) - (a.actualizado?.getTime() ?? 0));
    return ordenados.map(r => r.inicioRegistro).find(m => typeof m === 'string' && /^\d{4}-\d{2}$/.test(m)) ?? null;
  });
  readonly meses = computed(() => mesesVisibles([...this.resumenes().keys()], this.inicioRegistro(), this.hoy()));
  readonly actual = computed(() => this.meses().includes(this.mes()) ? this.resumenes().get(this.mes()) ?? null : null);
  readonly mesesRegistrados = computed(() => {
    const inicio = this.inicioRegistro();
    return inicio ? [...this.resumenes().keys()].filter(m => m >= inicio && m <= claveMes(this.hoy())).sort() : [claveMes(this.hoy())];
  });
  /** Vehículos del PC (para elegir o detectar el vehículo de un gasto) */
  readonly vehiculos = computed<VehiculoBasico[]>(() =>
    (this.resumenes().get(claveMes(new Date())) ?? [...this.resumenes().values()].pop())?.listaVehiculos ?? []);
  /** Tarjetas de crédito del PC (para pagar con ellas y ver su cupo) */
  readonly tarjetas = computed<TarjetaCredito[]>(() =>
    (this.resumenes().get(claveMes(new Date())) ?? [...this.resumenes().values()].pop())?.listaTarjetas ?? []);

  constructor() {
    const actualizar = () => this.hoy.set(new Date());
    const intervalo = window.setInterval(actualizar, 60000);
    document.addEventListener('visibilitychange', actualizar);
    inject(DestroyRef).onDestroy(() => { clearInterval(intervalo); document.removeEventListener('visibilitychange', actualizar); });
    effect(() => {
      const lista = this.meses();
      if (!lista.includes(this.mes())) this.mes.set(lista.includes(claveMes(this.hoy())) ? claveMes(this.hoy()) : lista[0] ?? claveMes(this.hoy()));
    });
    effect(() => {
      const u = this.sesion.usuario();
      this.cancelarCobros?.();
      this.cancelarNomina?.();
      this.nomina.set(null); this.nominaCargada.set(false); this.errorNomina.set('');
      this.cobros.set([]);
      this.cobrosCargados.set(false);
      this.errorCobros.set('');
      if (u) {
        this.cancelarNomina = onSnapshot(doc(db, 'usuarios', u.uid, 'configuracion', 'nomina'), snap => {
          this.nomina.set(snap.exists() ? snap.data() as PlanNomina : null);
          this.nominaCargada.set(true);
        }, () => this.errorNomina.set('No se pudo cargar la configuración de pagos. Revisa la conexión y los permisos.'));
        this.cancelarCobros = onSnapshot(collection(db, 'usuarios', u.uid, 'ingresosRecibidos'), { includeMetadataChanges: true }, snap => {
          this.cobros.set(snap.docs.filter(d => d.data()['borrado'] !== true).map(d => { const x = d.data(); return {
            id: d.id, fecha: x['fecha']?.toDate?.() ?? new Date(), monto: Number(x['monto']) || 0,
            concepto: x['concepto'] || 'Ingreso recibido', inicial: x['inicial'] === true,
          }; }));
          this.cobrosCargados.set(true);
        }, () => { this.errorCobros.set('No se pudieron cargar los cobros. Comprueba la conexión y las reglas de acceso.'); });
      }
    });
  }

  registrarCobro(monto: number, concepto: string, fecha: Date, inicial: boolean, idEsperado?: string) {
    const u = this.sesion.usuario();
    if (!u) throw new Error('Sin sesión');
    if (!Number.isSafeInteger(monto) || monto > 1e12 || monto < 0 || (!inicial && monto === 0) || !Number.isFinite(fecha.getTime())) throw new Error('Revisa el monto y la fecha');
    // Un saldo por mes evita sumar dos saldos iniciales al tocar el botón otra vez.
    const ref = inicial ? doc(db, 'usuarios', u.uid, 'ingresosRecibidos', `saldo-${claveMes(fecha)}`)
      : idEsperado ? doc(db, 'usuarios', u.uid, 'ingresosRecibidos', idEsperado) : doc(collection(db, 'usuarios', u.uid, 'ingresosRecibidos'));
    const envio = setDoc(ref, { monto, concepto: concepto.trim().slice(0, 120), fecha: Timestamp.fromDate(fecha), inicial, borrado: false, actualizada: serverTimestamp() });
    marcarCambio(u.uid);
    return envio;
  }

  borrarCobro(cobro: Cobro) {
    const u = this.sesion.usuario();
    if (!u) throw new Error('Sin sesión');
    const envio = setDoc(doc(db, 'usuarios', u.uid, 'ingresosRecibidos', cobro.id), { borrado: true, actualizada: serverTimestamp() }, { merge: true });
    marcarCambio(u.uid);
    return envio;
  }

  guardarNomina(plan: PlanNomina) {
    const u = this.sesion.usuario();
    if (!u) throw new Error('Sin sesión');
    validarNomina(plan);
    const envio = setDoc(doc(db, 'usuarios', u.uid, 'configuracion', 'nomina'), { ...plan, actualizada: serverTimestamp() });
    marcarCambio(u.uid);
    return envio;
  }

  irA(paso: number) {
    const lista = this.meses();
    if (paso !== -1 && paso !== 1) return;
    const actual = lista.indexOf(this.mes());
    if (actual < 0) return;
    const i = actual + paso;
    if (i >= 0 && i < lista.length) this.mes.set(lista[i]);
  }

  /** Marca la vencida como pagada guardando el pago en la nube (el PC lo recibe por la réplica). Devuelve true si se guardó. */
  pagar(v: Vencida): boolean {
    const u = this.sesion.usuario();
    if (!u || !this.pagarEnNube(u.uid, v)) return false;
    marcarCambio(u.uid);
    return true;
  }

  /**
   * Guarda en la nube el pago de una vencida, como lo haría el PC: la cuenta queda pagada en su mes
   * (o quincena) y la cuota del crédito baja su saldo y su plazo. El id del pago es fijo por mes:
   * tocar dos veces no lo duplica. Sin señal queda en la caché y se sube al volver la conexión.
   */
  private pagarEnNube(uid: string, v: Vencida): boolean {
    const datos = this.nube.datos();
    if (!datos || !v.uid) return false;
    const base = { actualizada: serverTimestamp(), borrado: false };
    if (v.tipo === 'cuenta') {
      const quincena = v.quincena === 2 ? 2 : 0;
      setDoc(doc(db, 'usuarios', uid, 'pagosCuenta', `${v.uid}-${v.mes}-${quincena}`),
        { movimientoUid: v.uid, mes: v.mes, monto: v.monto, quincena, ...base }).catch(() => { /* reintenta la caché */ });
      return true;
    }
    const credito = datos.creditos.find(c => c.uid === v.uid);
    const pago = credito ? pagoDeCuota(credito, datos.pagosCredito, new Date()) : null;
    if (!credito || !pago) return false;
    const lote = writeBatch(db);
    lote.set(doc(db, 'usuarios', uid, 'pagosCredito', `${credito.uid}-${pago.mes}-cuota`), {
      creditoUid: credito.uid, mes: pago.mes, fecha: Timestamp.now(), tipo: TIPO_CUOTA, monto: pago.monto, interes: pago.interes, capital: pago.capital,
      saldoAnterior: pago.saldoAnterior, saldoNuevo: pago.saldoNuevo, mesesReducidos: pago.mesesReducidos, ...base,
    });
    lote.set(doc(db, 'usuarios', uid, 'creditos', credito.uid),
      { disponible: pago.saldoNuevo, plazoMeses: pago.plazoMeses, pagado: true, mesPagado: pago.mes, actualizada: serverTimestamp() }, { merge: true });
    lote.commit().catch(() => { /* reintenta la caché */ });
    return true;
  }
}

