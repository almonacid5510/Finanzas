// ============================================================
//  Proyecto: Mis Finanzas
//  Autoria: Juan Almonacid
//  Copyright (c) 2026 Juan Almonacid. Todos los derechos reservados.
// ============================================================
// Todos los datos del usuario en la nube (la base principal), escuchados en vivo.
// Con ellos el celular calcula su propio Resumen (calculo.ts) aunque el PC esté apagado.
import { Injectable, computed, effect, inject, signal } from '@angular/core';
import { collection, onSnapshot } from 'firebase/firestore';
import { DatosNube, GastoN, desdeDocumentos } from './calculo';
import { db } from './firebase';
import { SesionService } from './sesion.service';

/** Colecciones que replica el PC (SincronizacionNube.cs); los gastos del día a día se leen aparte. */
export const COLECCIONES_NUBE = ['movimientos', 'pagosCuenta', 'creditos', 'pagosCredito', 'metas', 'abonosMeta',
  'vehiculos', 'gastosVehiculo', 'inicioRegistro'] as const;

type DocsPorColeccion = Record<string, { id: string; data: Record<string, unknown> }[]>;

@Injectable({ providedIn: 'root' })
export class DatosNubeService {
  private readonly sesion = inject(SesionService);
  private cancelar: (() => void)[] = [];
  private readonly docs = signal<DocsPorColeccion>({});
  private readonly gastos = signal<GastoN[]>([]);
  private readonly recibidas = signal<Set<string>>(new Set());
  /** Momento del último cambio recibido (para "Sincronizado hace…"). */
  readonly actualizado = signal<Date | null>(null);
  readonly error = signal('');

  /** Ya llegó al menos una vez cada colección (de la caché local o de la red). */
  readonly listo = computed(() => this.recibidas().size >= COLECCIONES_NUBE.length + 1);
  readonly datos = computed<DatosNube | null>(() => this.listo() ? desdeDocumentos(this.docs(), this.gastos()) : null);
  /** El PC ya subió sus datos: si no, se sigue usando el resumen que publica el PC. */
  readonly hayDatos = computed(() => {
    const d = this.datos();
    return !!d && (d.movimientos.length > 0 || d.creditos.length > 0 || d.inicioRegistro !== null);
  });

  constructor() {
    effect(() => {
      const u = this.sesion.usuario();
      this.cancelar.forEach(c => c());
      this.cancelar = [];
      this.docs.set({}); this.gastos.set([]); this.recibidas.set(new Set()); this.actualizado.set(null); this.error.set('');
      if (u) this.escuchar(u.uid);
    });
  }

  private recibida(nombre: string) {
    this.actualizado.set(new Date());
    if (!this.recibidas().has(nombre)) this.recibidas.update(s => new Set(s).add(nombre));
  }

  private escuchar(uid: string) {
    const fallo = () => this.error.set('No se pudieron leer tus datos de la nube. Revisa la conexión.');
    for (const nombre of COLECCIONES_NUBE)
      this.cancelar.push(onSnapshot(collection(db, 'usuarios', uid, nombre), snap => {
        this.docs.update(d => ({ ...d, [nombre]: snap.docs.map(x => ({ id: x.id, data: x.data() })) }));
        this.recibida(nombre);
      }, fallo));

    // Todos los gastos (las cuotas de las tarjetas usan compras de hasta 3 años)
    this.cancelar.push(onSnapshot(collection(db, 'usuarios', uid, 'gastos'), snap => {
      this.gastos.set(snap.docs.map(d => {
        const x = d.data();
        return {
          id: d.id, fecha: x['fecha']?.toDate?.() ?? new Date(), monto: Number(x['monto']) || 0,
          categoria: x['categoria'] ?? '', descripcion: x['descripcion'] ?? '',
          tarjetaId: typeof x['tarjetaId'] === 'number' ? x['tarjetaId'] : null, cuotas: Number(x['cuotas']) || 1,
        };
      }));
      this.recibida('gastos');
    }, fallo));
  }
}
