// ============================================================
//  Proyecto: Mis Finanzas
//  Autoria: Juan Almonacid
//  Copyright (c) 2026 Juan Almonacid. Todos los derechos reservados.
// ============================================================
// Resumen de cada mes que publica Mis Finanzas del PC (usuarios/{uid}/resumen/{yyyy-MM}):
// los mismos números del Dashboard. "Pagado" deja una acción para que el PC la marque.
import { DestroyRef, Injectable, computed, effect, inject, signal } from '@angular/core';
import { Timestamp, addDoc, collection, deleteDoc, doc, onSnapshot, serverTimestamp, setDoc } from 'firebase/firestore';
import { db, marcarCambio } from './firebase';
import { claveMes } from './formato';
import { mesesVisibles } from './periodos';
import { PlanNomina, validarNomina } from './nomina';
import { CajaPC, Cobro } from './caja';
import { VehiculoBasico } from './interprete';
import { SesionService } from './sesion.service';

export interface Vencida { tipo: 'cuenta' | 'credito'; id: number; mes: string; nombre: string; monto: number; detalle: string; quincena?: number; }
export interface PorPagar {
  dia: string; mesCorto: string; titulo: string; detalle: string; monto: number;
  vencida: boolean; proxima: boolean; credito: boolean;
}
export interface Tarjeta { total: number; detalle?: string; progreso?: number; }
/** Tarjeta de crédito publicada por el PC (cupo, deuda y disponible al momento de sincronizar) */
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
  private cancelar?: () => void;
  private cancelarCobros?: () => void;
  private cancelarNomina?: () => void;
  readonly nomina = signal<PlanNomina | null>(null);
  readonly nominaCargada = signal(false);
  readonly errorNomina = signal('');
  readonly cobros = signal<Cobro[]>([]);
  readonly cobrosCargados = signal(false);
  readonly errorCobros = signal('');

  readonly resumenes = signal<Map<string, Resumen>>(new Map());
  readonly cargado = signal(false);
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
  /** Vencidas que ya se marcaron como pagadas aquí y esperan a que el PC las aplique */
  readonly enviadas = signal<Set<string>>(new Set());

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
      this.cancelar?.();
      this.cancelar = undefined;
      this.cancelarCobros?.();
      this.cancelarNomina?.();
      this.nomina.set(null); this.nominaCargada.set(false); this.errorNomina.set('');
      this.cobros.set([]);
      this.cobrosCargados.set(false);
      this.errorCobros.set('');
      this.resumenes.set(new Map());
      this.cargado.set(false);
      if (u) {
        this.escuchar(u.uid);
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

  private escuchar(uid: string) {
    this.cancelar = onSnapshot(collection(db, 'usuarios', uid, 'resumen'), (snap) => {
      const mapa = new Map<string, Resumen>();
      for (const d of snap.docs) {
        const x = d.data();
        mapa.set(d.id, { ...(x as Resumen), mes: d.id, actualizado: x['actualizado']?.toDate?.() ?? null });
      }
      this.resumenes.set(mapa);
      this.cargado.set(true);
      // Las que el PC ya aplicó dejan de estar "enviadas"
      const vigentes = new Set((mapa.get(claveMes(new Date()))?.vencidas ?? []).map(clave));
      this.enviadas.update((s) => new Set([...s].filter((k) => vigentes.has(k))));
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

  /** Pide al PC marcar la vencida como pagada (cuenta del mes o cuota de crédito) */
  pagar(v: Vencida) {
    const u = this.sesion.usuario();
    if (!u) return;
    addDoc(collection(db, 'usuarios', u.uid, 'acciones'), {
      tipo: v.tipo === 'cuenta' ? 'pagarCuenta' : 'pagarCuota',
      id: v.id, mes: v.mes, monto: v.monto, quincena: v.quincena ?? 0, creado: serverTimestamp(),
    });
    marcarCambio(u.uid);
    this.enviadas.update((s) => new Set(s).add(clave(v)));
  }

  fueEnviada = (v: Vencida) => this.enviadas().has(clave(v));
}

const clave = (v: Vencida) => `${v.tipo}-${v.id}-${v.mes}-${v.quincena ?? 0}`;
