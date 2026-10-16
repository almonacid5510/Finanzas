import { BorradoresService, CamposFormulario, capturarFormulario, restaurarFormulario } from '../../nucleo/borradores.service';
// ============================================================
//  Proyecto: Mis Finanzas
//  Autoria: Juan Almonacid
//  Copyright (c) 2026 Juan Almonacid. Todos los derechos reservados.
// ============================================================
// Pestaña "Resumen": los números del Dashboard del PC (los publica Mis Finanzas al sincronizar).
import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, afterRenderEffect, viewChild, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Icono } from '../../compartido/icono';
import { MarcaTarjetaLogo, tituloTarjeta } from '../../compartido/marca-tarjeta';
import { aISO, claveMes, fechaLarga, haceCuanto, miles, nombreMesCorto, pesos, soloDigitos } from '../../nucleo/formato';
import { GastosService } from '../../nucleo/gastos.service';
import { ResumenService, Vencida } from '../../nucleo/resumen.service';
import { pagosNomina, EsperadoNomina } from '../../nucleo/nomina';
import { calcularCaja, ajusteGastos } from '../../nucleo/caja';
import { ToastService } from '../../nucleo/toast.service';

@Component({
  selector: 'app-resumen',
  imports: [Icono, RouterLink, MarcaTarjetaLogo],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './resumen.html',
  styleUrl: './resumen.css',
})
export class Resumen {
  private readonly borradores = inject(BorradoresService);
  private readonly destruir = inject(DestroyRef);
  private readonly formulario = viewChild<ElementRef<HTMLFormElement>>('formularioCobro');
  private readonly camposPendientes = signal<CamposFormulario | null>(null);
  protected readonly servicio = inject(ResumenService);
  private readonly toast = inject(ToastService);
  private readonly gastos = inject(GastosService);

  /** Tarjetas con el cupo al día (lo del PC menos las compras de aquí que aún no descuenta) */
  protected readonly tarjetas = computed(() => this.servicio.tarjetas().map((t) => {
    const disponible = Math.max(0, t.disponible - this.gastos.pendienteEnTarjeta(t.id));
    return { ...t, disponible, uso: t.cupo > 0 ? Math.min(1, (t.cupo - disponible) / t.cupo) : 0 };
  }));

  protected readonly pesos = pesos;
  protected readonly fechaLarga = fechaLarga;
  /** Valor con puntos de miles para los campos ("3.200.000"); vacío si no hay valor. */
  protected miles(n: number | null | undefined) { return n ? miles(n) : ''; }
  protected readonly soloDigitos = soloDigitos;
  protected readonly verCalculo = signal(false);
  protected readonly totalPorPagar = computed(() => (this.r()?.porPagar ?? []).reduce((suma, p) => suma + (p.monto || 0), 0));
  /** 0.0041 → "0,4 %" */
  protected porcentaje(uso: number) { return new Intl.NumberFormat('es-CO', { style: 'percent', maximumFractionDigits: 1 }).format(uso); }
  protected readonly tituloTarjeta = tituloTarjeta;
  protected readonly r = this.servicio.actual;
  protected readonly nombreMes = computed(() => nombreMesCorto(this.servicio.mes()));
  protected readonly hayAnterior = computed(() => this.servicio.meses().indexOf(this.servicio.mes()) > 0);
  protected readonly haySiguiente = computed(() => {
    const lista = this.servicio.meses();
    const i = lista.indexOf(this.servicio.mes());
    return i >= 0 && i < lista.length - 1;
  });

  protected readonly cobroElegido = signal<EsperadoNomina | null>(null);
  protected readonly esperados = computed(() => {
    try { return pagosNomina(this.servicio.nomina(), this.servicio.mes()).filter(p => !this.servicio.cobros().some(c => c.id === p.id)); }
    catch { return []; }
  });
  protected recibirEsperado(p: EsperadoNomina) {
    this.cobroElegido.set(p); this.formularioCobro.set(true);
  }
  protected nuevoCobro() { this.cobroElegido.set(null); this.formularioCobro.set(!this.formularioCobro()); }

  protected readonly hoy = aISO(new Date());
  protected readonly formularioCobro = signal(false);
  protected readonly errorCobro = signal('');
  protected readonly cobrosMes = computed(() => this.servicio.cobros().filter(c => claveMes(c.fecha) === this.servicio.mes()).sort((a,b) => b.fecha.getTime() - a.fecha.getTime()));
  protected readonly fecha = (d: Date) => d.toLocaleDateString('es-CO');
  /** Gastos del celular que el PC aún no incluyó en su resumen (solo el mes en curso). */
  private readonly diferenciaGastos = computed(() => {
    const r = this.r(), mes = this.servicio.mes();
    if (!r?.caja || mes !== claveMes(new Date()) || !this.gastos.cargado()) return 0;
    // Sustituye solo los gastos del mes que el PC incluyó por su versión actual del celular.
    // No depende de descargado: ese indicador puede llegar antes que el nuevo resumen.
    const visibles = this.gastos.gastos().filter(g => claveMes(g.fecha) === mes && aISO(g.fecha) <= this.hoy);
    return ajusteGastos(r.caja, visibles);
  });

  protected readonly caja = computed(() => {
    const r = this.r();
    if (!r?.caja || !this.servicio.cobrosCargados() || this.servicio.errorCobros()) return null;
    const mes = this.servicio.mes();
    if (mes > claveMes(new Date())) return null;
    const caja = calcularCaja(this.servicio.cobros(), r.caja, mes);
    const diferencia = this.diferenciaGastos();
    return { ...caja, saldo: caja.saldo - diferencia, egresos: caja.egresos + diferencia };
  });

  /**
   * Disponible = dinero real, igual que el Dashboard del PC: lo confirmado como recibido (y el saldo inicial)
   * menos lo efectivamente pagado. Lo pendiente no se descuenta y no se supone nada: solo se informa.
   */
  protected readonly balanceMes = computed(() => {
    const r = this.r(), efectivo = this.caja();
    if (!r || !efectivo) return null;
    const porPagar = r.porPagar.filter(p => !p.proxima).reduce((s, p) => s + (p.monto || 0), 0);
    const porRecibir = this.esperados().reduce((s, p) => s + p.monto, 0);
    return { ...efectivo, recibidoTotal: efectivo.inicial + efectivo.recibidos, porPagar, porRecibir, ingresos: r.ingresos };
  });

  constructor() {
    this.destruir.onDestroy(this.borradores.registrar('resumen', () => ({ abierto:this.formularioCobro(), elegido:this.cobroElegido(), mes:this.servicio.mes(), campos:capturarFormulario(this.formulario()?.nativeElement) }), b => {
      this.formularioCobro.set(b.abierto); this.cobroElegido.set(b.elegido); this.servicio.mes.set(b.mes); this.camposPendientes.set(b.campos);
    }));
    afterRenderEffect(() => {
      const form=this.formulario()?.nativeElement, campos=this.camposPendientes();
      if (form && campos) { restaurarFormulario(form,campos); this.camposPendientes.set(null); }
    });
  }

  protected guardarCobro(valor: string, concepto: string, fecha: string, inicial: boolean) {
    this.errorCobro.set('');
    const monto = soloDigitos(valor);
    const dia = new Date(`${fecha}T00:00:00`);
    if (!/^[0-9.,\s]+$/.test(valor) || !valor.trim() || (!inicial && monto <= 0) || !Number.isFinite(dia.getTime()) || fecha > this.hoy || !concepto.trim()) {
      this.errorCobro.set('Escribe el concepto, un monto válido y una fecha que no sea futura.'); return;
    }
    if (inicial) dia.setDate(1);
    try {
      const envio = this.servicio.registrarCobro(monto, concepto, dia, inicial, this.cobroElegido()?.id);
      this.formularioCobro.set(false); this.cobroElegido.set(null);
      this.toast.mostrar('Registro guardado en este dispositivo. Se sincroniza al tener conexión.');
      envio.catch(() => this.errorCobro.set('No se pudo sincronizar el ingreso. Revisa la conexión y los permisos antes de volver a registrarlo.'));
    } catch { this.errorCobro.set('No se pudo registrar. Revisa el valor.'); }
  }

  protected borrarCobro(c: import('../../nucleo/caja').Cobro) {
    if (!confirm(`¿Eliminar “${c.concepto}” por ${pesos(c.monto)}?`)) return;
    this.servicio.borrarCobro(c).catch(() => this.errorCobro.set('No se pudo eliminar el registro.'));
  }

  protected readonly actualizado = computed(() => {
    const d = this.r()?.actualizado;
    return d ? `Sincronizado ${haceCuanto(d)}` : 'Datos sincronizados con el PC';
  });

  protected pagar(v: Vencida) {
    const guardado = this.servicio.pagar(v);
    this.toast.mostrar(guardado ? `"${v.nombre}" quedó pagado` : `"${v.nombre}" se marcará como pagado en el PC`);
  }
}
