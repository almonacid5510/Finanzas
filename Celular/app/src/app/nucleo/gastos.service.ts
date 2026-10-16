// ============================================================
//  Proyecto: Mis Finanzas
//  Autoria: Juan Almonacid
//  Copyright (c) 2026 Juan Almonacid. Todos los derechos reservados.
// ============================================================
// Gastos del día a día en la nube (usuarios/{uid}/gastos), compartidos con el PC:
// - Los del celular llegan al PC (descargado = true cuando el PC ya los tiene).
// - Los del PC también se ven aquí (el PC los sube con origen "PC").
// - Borrar uno que ya está en el PC deja una acción "borrarGasto" para que el PC también lo borre.
import { Injectable, computed, effect, inject, signal } from '@angular/core';
import {
  Timestamp, addDoc, collection, deleteDoc, disableNetwork, doc, enableNetwork, onSnapshot, orderBy, query,
  serverTimestamp, setDoc, where,
} from 'firebase/firestore';
import { db } from './firebase';
import { aISO, inicioMes, inicioSemana } from './formato';
import { SesionService } from './sesion.service';

export interface Gasto {
  id: string;
  fecha: Date;
  monto: number;
  descripcion: string;
  categoria: string;
  textoOriginal: string;
  origen: string;     // "Celular" o "PC"
  descargado: boolean; // ya está en el PC
  sinEnviar: boolean;  // aún solo en este celular (sin señal)
  vehiculoId: number | null; // gasto directo de un vehículo (también en su historial del PC)
  vehiculo: string;
  tarjetaId: number | null;  // pagado con tarjeta de crédito (sube su deuda en el PC)
  tarjeta: string;
  cuotas: number;     // cuotas de la compra con tarjeta (1 si no)
}

export interface NuevoGasto {
  fecha: Date;
  monto: number;
  descripcion: string;
  categoria: string;
  textoOriginal: string;
  vehiculoId: number | null;
  vehiculo: string;
  tarjetaId: number | null;
  tarjeta: string;
  cuotas: number;
}

@Injectable({ providedIn: 'root' })
export class GastosService {
  private readonly sesion = inject(SesionService);
  private cancelar?: () => void;

  /** Gastos desde el inicio del mes (o de la semana, si empezó el mes anterior), del más reciente al más antiguo */
  readonly gastos = signal<Gasto[]>([]);
  readonly cargado = signal(false);

  readonly sinEnviar = computed(() => this.gastos().filter((g) => g.sinEnviar).length);
  readonly deHoy = computed(() => {
    const hoy = aISO(new Date());
    return this.gastos().filter((g) => aISO(g.fecha) === hoy);
  });
  readonly totalHoy = computed(() => this.deHoy().reduce((s, g) => s + g.monto, 0));

  constructor() {
    effect(() => {
      const u = this.sesion.usuario();
      this.cancelar?.();
      this.cancelar = undefined;
      this.gastos.set([]);
      this.cargado.set(false);
      if (u) this.escuchar(u.uid);
    });
  }

  private escuchar(uid: string) {
    const desde = new Date(Math.min(inicioMes().getTime(), inicioSemana().getTime()));
    const q = query(
      collection(db, 'usuarios', uid, 'gastos'),
      where('fecha', '>=', Timestamp.fromDate(desde)),
      orderBy('fecha', 'desc'),
    );
    this.cancelar = onSnapshot(q, { includeMetadataChanges: true }, (snap) => {
      this.gastos.set(snap.docs.map((d) => {
        const x = d.data();
        return {
          id: d.id,
          fecha: x['fecha']?.toDate?.() ?? new Date(),
          monto: Number(x['monto']) || 0,
          descripcion: x['descripcion'] || 'Gasto',
          categoria: x['categoria'] || '',
          textoOriginal: x['textoOriginal'] || '',
          origen: x['origen'] || 'Celular',
          descargado: !!x['descargado'],
          sinEnviar: d.metadata.hasPendingWrites,
          vehiculoId: typeof x['vehiculoId'] === 'number' ? x['vehiculoId'] : null,
          vehiculo: x['vehiculo'] || '',
          tarjetaId: typeof x['tarjetaId'] === 'number' ? x['tarjetaId'] : null,
          tarjeta: x['tarjeta'] || '',
          cuotas: Math.max(1, Number(x['cuotas']) || 1),
        };
      }));
      this.cargado.set(true);
    });
  }

  private get uid(): string {
    const u = this.sesion.usuario();
    if (!u) throw new Error('Sin sesión');
    return u.uid;
  }

  /** Guarda sin esperar a la red: con o sin señal queda guardado (la caché local lo sube después) */
  guardar(g: NuevoGasto): Promise<void> {
    const ref = doc(collection(db, 'usuarios', this.uid, 'gastos')); // el id del documento es el Uid del gasto en el PC
    return setDoc(ref, {
      fecha: Timestamp.fromDate(g.fecha),
      monto: g.monto,
      descripcion: g.descripcion,
      categoria: g.categoria,
      textoOriginal: g.textoOriginal,
      vehiculoId: g.vehiculoId,
      vehiculo: g.vehiculo,
      tarjetaId: g.tarjetaId,
      tarjeta: g.tarjeta,
      cuotas: g.tarjetaId !== null ? Math.max(1, g.cuotas) : 1,
      origen: 'Celular',
      descargado: false,
      creado: serverTimestamp(),
    });
  }

  borrar(g: Gasto): Promise<unknown> {
    const uid = this.uid;
    // Si el PC ya lo tiene, se le pide que también lo borre
    if (g.descargado)
      addDoc(collection(db, 'usuarios', uid, 'acciones'), { tipo: 'borrarGasto', gasto: g.id, creado: serverTimestamp() });
    return deleteDoc(doc(db, 'usuarios', uid, 'gastos', g.id));
  }

  /** Compras con la tarjeta hechas aquí que el PC aún no ha descontado (para mostrar el cupo al día) */
  pendienteEnTarjeta(tarjetaId: number): number {
    return this.gastos().filter((g) => g.tarjetaId === tarjetaId && !g.descargado).reduce((s, g) => s + g.monto, 0);
  }

  /** Reintenta enviar lo que quedó en el celular (reconecta con la nube) */
  async enviarAhora() {
    await disableNetwork(db);
    await enableNetwork(db);
  }
}
