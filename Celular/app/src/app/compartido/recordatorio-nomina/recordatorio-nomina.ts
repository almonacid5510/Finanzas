import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, afterRenderEffect, computed, effect, inject, signal, viewChild } from '@angular/core';
import { ResumenService } from '../../nucleo/resumen.service';
import { SesionService } from '../../nucleo/sesion.service';
import { ToastService } from '../../nucleo/toast.service';
import { aISO, fechaLarga, miles, pesos, soloDigitos } from '../../nucleo/formato';
import { recordatoriosNomina } from '../../nucleo/nomina';

@Component({
  selector: 'app-recordatorio-nomina',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './recordatorio-nomina.html',
  styleUrl: './recordatorio-nomina.css',
})
export class RecordatorioNomina {
  private readonly resumen = inject(ResumenService);
  private readonly sesion = inject(SesionService);
  private readonly toast = inject(ToastService);
  private readonly destruir = inject(DestroyRef);
  private readonly dialogo = viewChild.required<ElementRef<HTMLDialogElement>>('dialogo');
  protected readonly hoy = signal(aISO(new Date()));
  protected readonly error = signal('');
  protected readonly pesos = pesos;
  protected readonly soloDigitos = soloDigitos;
  /** 6400000 → "6.400.000" (el campo se lee con soloDigitos, así que los puntos no afectan). */
  protected miles(n: number) { return n > 0 ? miles(n) : ''; }
  protected readonly fechaLarga = fechaLarga;
  private readonly pospuestos = signal<Record<string, string>>({});
  private readonly confirmando = signal<string[]>([]);
  private clave = '';
  protected readonly pendiente = computed(() => {
    if (!this.sesion.usuario() || !this.resumen.cargado() || !this.resumen.nominaCargada() || !this.resumen.cobrosCargados()
      || this.resumen.errorNomina() || this.resumen.errorCobros()) return null;
    try {
      return recordatoriosNomina(this.resumen.nomina(), this.resumen.mesesRegistrados(),
        [...this.resumen.cobros().map(c => c.id), ...this.confirmando()], this.pospuestos(), this.hoy())[0] ?? null;
    } catch { return null; }
  });

  constructor() {
    effect(() => {
      const usuario = this.sesion.usuario();
      this.clave = usuario ? `nomina-pospuesta:${usuario.uid}` : '';
      let guardados: Record<string, string> = {};
      try {
        const datos: unknown = this.clave ? JSON.parse(localStorage.getItem(this.clave) ?? '{}') : {};
        if (datos && typeof datos === 'object' && !Array.isArray(datos)) {
          guardados = Object.fromEntries(Object.entries(datos).filter(([k, v]) => k.startsWith('nomina-') && typeof v === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(v)));
        }
      } catch { /* El aviso sigue disponible aunque no se pueda leer el almacenamiento. */ }
      this.pospuestos.set(guardados); this.confirmando.set([]); this.error.set('');
    });
    afterRenderEffect(() => {
      const modal = this.dialogo().nativeElement;
      if (this.pendiente()) { if (!modal.open) modal.showModal(); }
      else if (modal.open) modal.close();
    });
    const actualizar = () => this.hoy.set(aISO(new Date()));
    const intervalo = window.setInterval(actualizar, 60000);
    document.addEventListener('visibilitychange', actualizar);
    window.addEventListener('focus', actualizar);
    this.destruir.onDestroy(() => {
      clearInterval(intervalo);
      document.removeEventListener('visibilitychange', actualizar);
      window.removeEventListener('focus', actualizar);
    });
  }

  protected posponer() {
    const pago = this.pendiente();
    if (!pago) return;
    const manana = new Date(); manana.setDate(manana.getDate() + 1);
    const valores = { ...this.pospuestos(), [pago.id]: aISO(manana) };
    try {
      localStorage.setItem(this.clave, JSON.stringify(valores));
      this.pospuestos.set(valores); this.error.set('');
      this.toast.mostrar('Te lo recordaremos mañana al usar la app en este celular.');
    } catch { this.error.set('No se pudo guardar la fecha del recordatorio. Habilita el almacenamiento del navegador y vuelve a intentar.'); }
  }

  protected cancelar(evento: Event) {
    evento.preventDefault();
    this.posponer();
  }

  protected confirmar(valor: string, fecha: string) {
    const pago = this.pendiente();
    if (!pago) return;
    const monto = soloDigitos(valor);
    const dia = new Date(`${fecha}T00:00:00`);
    if (!valor.trim() || !/^[0-9.,\s]+$/.test(valor) || monto <= 0 || monto > 1e12
      || !/^\d{4}-\d{2}-\d{2}$/.test(fecha) || !Number.isFinite(dia.getTime()) || aISO(dia) !== fecha || fecha > aISO(new Date())) {
      this.error.set('Escribe el valor recibido y una fecha válida que no sea futura.'); return;
    }
    // El mismo identificador usado por PC y Resumen impide duplicar este pago.
    try {
      const uid = this.sesion.usuario()?.uid;
      const envio = this.resumen.registrarCobro(monto, `Nómina prevista ${pago.fecha}`, dia, false, pago.id);
      this.confirmando.update(ids => [...ids, pago.id]); this.error.set('');
      this.toast.mostrar('Nómina registrada. Se sincroniza al tener conexión.');
      envio.catch(() => {
        if (this.sesion.usuario()?.uid !== uid || this.destruir.destroyed) return;
        this.confirmando.update(ids => ids.filter(id => id !== pago.id));
        this.error.set('No se pudo guardar la confirmación. Revisa la conexión y los permisos.');
        this.toast.mostrar('No se pudo confirmar la nómina. Vuelve a intentarlo.');
      });
    } catch { this.error.set('No se pudo confirmar la nómina. Revisa los datos y vuelve a intentar.'); }
  }
}
