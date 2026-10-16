// ============================================================
//  Proyecto: Mis Finanzas
//  Autoria: Juan Almonacid
//  Copyright (c) 2026 Juan Almonacid. Todos los derechos reservados.
// ============================================================
// Pestaña "Gastos": total del periodo por categoría, lista por día y deslizar para borrar.
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Icono } from '../../compartido/icono';
import { colorCategoria, iconoCategoria, mostrarCategoria } from '../../nucleo/categorias';
import { aISO, hora, inicioDia, inicioMes, inicioSemana, pesos, tituloDia } from '../../nucleo/formato';
import { Gasto, GastosService } from '../../nucleo/gastos.service';
import { sinTildes } from '../../nucleo/interprete';
import { ToastService } from '../../nucleo/toast.service';

type Periodo = 'hoy' | 'semana' | 'mes';
const ANCHO_BORRAR = 96;

@Component({
  selector: 'app-gastos',
  imports: [Icono],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './gastos.html',
  styleUrl: './gastos.css',
})
export class Gastos {
  protected readonly servicio = inject(GastosService);
  private readonly toast = inject(ToastService);

  protected readonly pesos = pesos;
  protected readonly hora = hora;
  protected readonly mostrarCategoria = mostrarCategoria;
  protected readonly colorCategoria = colorCategoria;
  protected readonly iconoCategoria = iconoCategoria;

  protected readonly periodo = signal<Periodo>('hoy');
  protected readonly buscando = signal(false);
  protected readonly busqueda = signal('');

  protected readonly nombrePeriodo = computed(() => ({ hoy: 'de hoy', semana: 'de la semana', mes: 'del mes' })[this.periodo()]);

  /** Gastos del periodo elegido que coinciden con la búsqueda */
  protected readonly filtrados = computed(() => {
    const desde = { hoy: inicioDia(), semana: inicioSemana(), mes: inicioMes() }[this.periodo()];
    const texto = sinTildes(this.busqueda().trim().toLowerCase());
    return this.servicio.gastos().filter((g) =>
      g.fecha >= desde &&
      (!texto || sinTildes(`${g.descripcion} ${g.categoria}`.toLowerCase()).includes(texto)));
  });

  protected readonly total = computed(() => this.filtrados().reduce((s, g) => s + g.monto, 0));

  /** Reparto por categoría: barra apilada y leyenda (las más grandes) */
  protected readonly porCategoria = computed(() => {
    const total = this.total();
    const mapa = new Map<string, number>();
    for (const g of this.filtrados()) mapa.set(mostrarCategoria(g.categoria), (mapa.get(mostrarCategoria(g.categoria)) ?? 0) + g.monto);
    return [...mapa.entries()]
      .map(([nombre, monto]) => ({ nombre, monto, proporcion: total ? monto / total : 0, color: colorCategoria(nombre).barra }))
      .sort((a, b) => b.monto - a.monto);
  });

  protected readonly grupos = computed(() => {
    const grupos: { titulo: string; gastos: Gasto[] }[] = [];
    for (const g of this.filtrados()) {
      const titulo = tituloDia(g.fecha);
      const ultimo = grupos[grupos.length - 1];
      if (ultimo?.titulo === titulo) ultimo.gastos.push(g);
      else grupos.push({ titulo, gastos: [g] });
    }
    return grupos;
  });

  protected alternarBusqueda() {
    this.buscando.update((b) => !b);
    if (!this.buscando()) this.busqueda.set('');
  }

  protected async enviarAhora() {
    await this.servicio.enviarAhora();
    this.toast.mostrar(navigator.onLine ? 'Enviando tus gastos…' : 'Sin conexión: se enviarán cuando haya señal');
  }

  // ---------- Deslizar para borrar ----------
  protected readonly abierto = signal<string | null>(null);
  protected readonly arrastre = signal<{ id: string; dx: number } | null>(null);
  private inicio: { id: string; x: number; y: number; moviendo: boolean } | null = null;

  protected desplazamiento(id: string): number {
    const a = this.arrastre();
    if (a?.id === id) return a.dx;
    return this.abierto() === id ? -ANCHO_BORRAR : 0;
  }

  protected alTocar(e: PointerEvent, id: string) {
    this.inicio = { id, x: e.clientX, y: e.clientY, moviendo: false };
    if (this.abierto() && this.abierto() !== id) this.abierto.set(null);
  }

  protected alMover(e: PointerEvent, id: string) {
    const i = this.inicio;
    if (!i || i.id !== id) return;
    const dx = e.clientX - i.x;
    const dy = e.clientY - i.y;
    if (!i.moviendo) {
      if (Math.abs(dx) < 8 || Math.abs(dx) < Math.abs(dy)) return; // es scroll vertical
      i.moviendo = true;
      (e.currentTarget as HTMLElement).setPointerCapture(e.pointerId);
    }
    const base = this.abierto() === id ? -ANCHO_BORRAR : 0;
    this.arrastre.set({ id, dx: Math.max(-ANCHO_BORRAR - 20, Math.min(0, base + dx)) });
  }

  protected alSoltar(id: string) {
    const a = this.arrastre();
    if (a?.id === id) this.abierto.set(a.dx < -ANCHO_BORRAR / 2 ? id : null);
    this.arrastre.set(null);
    this.inicio = null;
  }

  protected borrar(g: Gasto) {
    if (!confirm(`¿Borrar "${g.descripcion}" de ${pesos(g.monto)}?${g.descargado ? ' También se borrará en el PC.' : ''}`)) {
      this.abierto.set(null);
      return;
    }
    this.servicio.borrar(g).catch(() => this.toast.mostrar('No se pudo borrar. Revisa tu sesión.'));
    this.abierto.set(null);
    this.toast.mostrar(`"${g.descripcion}" borrado`);
  }

  protected estado(g: Gasto): { clase: string; texto: string } {
    if (g.sinEnviar) return { clase: 'sin-enviar', texto: '● Sin enviar' };
    if (g.descargado) return { clase: 'en-pc', texto: '✓ En el PC' };
    return { clase: 'pendiente', texto: '● Pendiente' };
  }

  protected readonly esHoy = (d: Date) => aISO(d) === aISO(new Date());
}
