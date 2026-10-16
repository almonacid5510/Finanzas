import { BorradoresService, CamposFormulario, capturarFormulario, restaurarFormulario } from '../../nucleo/borradores.service';
import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, afterRenderEffect, viewChild, effect, inject, signal } from '@angular/core';
import { ActualizacionesService } from '../../nucleo/actualizaciones.service';
import { ResumenService } from '../../nucleo/resumen.service';
import { ToastService } from '../../nucleo/toast.service';
import { FRECUENCIAS_NOMINA, PlanNomina, montoDelPago } from '../../nucleo/nomina';
import { aISO, pesos, soloDigitos } from '../../nucleo/formato';

@Component({
  selector: 'app-configuracion',
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './configuracion.html',
  styleUrl: './configuracion.css',
})
export class Configuracion {
  private readonly borradores = inject(BorradoresService);
  private readonly destruir = inject(DestroyRef);
  private readonly formulario = viewChild<ElementRef<HTMLFormElement>>('formularioNomina');
  private readonly camposPendientes = signal<CamposFormulario | null>(null);
  protected readonly actualizaciones = inject(ActualizacionesService);
  protected readonly servicio = inject(ResumenService);
  private readonly toast = inject(ToastService);
  protected readonly frecuenciasNomina = FRECUENCIAS_NOMINA;
  protected readonly diasDelMes = Array.from({length: 31}, (_, i) => i + 1);
  protected diaGuardado(indice: number) { return this.servicio.nomina()?.dias.split(',')[indice]?.trim() ?? ''; }
  protected segundoGuardado() { const plan = this.servicio.nomina(); return plan?.frecuencia === 'Quincenal' ? montoDelPago(plan, 1) : ''; }
  /** "Quincenal · $ 3.000.000 el día 15 y $ 3.400.000 el día 30 ($ 6.400.000 al mes)." */
  protected resumenNomina(plan: PlanNomina) {
    const dias = plan.dias.split(',').map(d => d.trim()).filter(Boolean).map(Number).sort((a, b) => a - b);
    if (plan.frecuencia === 'Quincenal' && dias.length === 2 && montoDelPago(plan, 0) !== montoDelPago(plan, 1))
      return `Quincenal · ${this.pesos(montoDelPago(plan, 0))} el día ${dias[0]} y ${this.pesos(montoDelPago(plan, 1))} el día ${dias[1]} (${this.pesos(montoDelPago(plan, 0) + montoDelPago(plan, 1))} al mes).`;
    return `${plan.frecuencia} · ${this.pesos(plan.montoPorPago)} por pago.`;
  }
  protected readonly editorNomina = signal(false);
  protected readonly tipoNomina = signal('Quincenal');
  protected readonly error = signal('');
  protected readonly pesos = pesos;
  protected readonly hoy = aISO(new Date());
  private inicializado = false;
  constructor() {
    this.destruir.onDestroy(this.borradores.registrar('configuracion', () => ({ abierto:this.editorNomina(), tipo:this.tipoNomina(), campos:capturarFormulario(this.formulario()?.nativeElement) }), b => {
      this.inicializado=true; this.editorNomina.set(b.abierto); this.tipoNomina.set(b.tipo); this.camposPendientes.set(b.campos);
    }));
    afterRenderEffect(() => {
      const form=this.formulario()?.nativeElement, campos=this.camposPendientes();
      if (form && campos) { restaurarFormulario(form,campos); this.camposPendientes.set(null); }
    });
    effect(() => {
      if (!this.servicio.nominaCargada() || this.inicializado) return;
      this.inicializado = true;
      if (!this.servicio.nomina()) this.editorNomina.set(true);
    });
  }
  protected editarNomina() {
    this.error.set('');
    const frecuencia = this.servicio.nomina()?.frecuencia;
    this.tipoNomina.set(frecuencia && FRECUENCIAS_NOMINA.includes(frecuencia) ? frecuencia : 'Quincenal');
    this.editorNomina.set(!this.editorNomina());
  }
  protected guardarNomina(primero: string, segundo: string, semana: string, monto: string, montoSegundo: string) {
    try {
      if (!FRECUENCIAS_NOMINA.includes(this.tipoNomina())) throw new Error('Elige la frecuencia de pago.');
      if (this.tipoNomina() !== 'Semanal' && !primero) throw new Error('Selecciona el día de pago.');
      if (this.tipoNomina() === 'Quincenal' && !segundo) throw new Error('Selecciona el segundo día de pago.');
      if (monto.trim() && !/^[0-9.,\s]+$/.test(monto)) throw new Error('Escribe un monto positivo.');
      if (montoSegundo.trim() && !/^[0-9.,\s]+$/.test(montoSegundo)) throw new Error('Escribe un valor positivo para el segundo pago.');
      let dias = this.tipoNomina() === 'Semanal' ? '' : primero, valor = soloDigitos(monto), valorSegundo = 0;
      if (this.tipoNomina() === 'Quincenal') {
        // Vacío = igual al primero. Se guarda en orden de días: montoPorPago es el del día más temprano.
        let d1 = Number(primero), d2 = Number(segundo);
        valorSegundo = montoSegundo.trim() ? soloDigitos(montoSegundo) : valor;
        if (d1 > d2) { [d1, d2] = [d2, d1]; [valor, valorSegundo] = [valorSegundo, valor]; }
        dias = `${d1},${d2}`;
      }
      const envio = this.servicio.guardarNomina({ frecuencia: this.tipoNomina(), dias, diaSemana: Number(semana), fechaBase: this.servicio.nomina()?.fechaBase ?? this.hoy,
        montoPorPago: valor, montoSegundoPago: valorSegundo, confirmada: true });
      this.editorNomina.set(false); this.error.set('');
      envio.catch(() => this.error.set('No se pudo sincronizar la configuración. Revisa la conexión y los permisos.'));
      this.toast.mostrar('Nómina guardada. Se sincroniza al tener conexión.');
    } catch (e) { this.error.set(e instanceof Error ? e.message : 'Revisa los datos de pago.'); }
  }
}
