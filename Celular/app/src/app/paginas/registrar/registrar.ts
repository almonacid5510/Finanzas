// ============================================================
//  Proyecto: Mis Finanzas
//  Autoria: Juan Almonacid
//  Copyright (c) 2026 Juan Almonacid. Todos los derechos reservados.
// ============================================================
// Pestaña "Registrar": se escribe o dicta la frase y los campos se llenan solos; también se puede llenar a mano.
import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, computed, effect, inject, signal, viewChild } from '@angular/core';
import { Router } from '@angular/router';
import { SinSeleccionTactil } from '../../compartido/sin-seleccion-tactil';
import { MarcaTarjetaLogo, tituloTarjeta } from '../../compartido/marca-tarjeta';
import { Icono } from '../../compartido/icono';
import { CATEGORIAS, colorCategoria, iconoCategoria } from '../../nucleo/categorias';
import { aISO, miles, pesos, soloDigitos } from '../../nucleo/formato';
import { BorradoresService } from '../../nucleo/borradores.service';
import { GastosService } from '../../nucleo/gastos.service';
import { detectarCuotas, detectarPago, detectarVehiculo, estimarCuotas, interpretar, quitarPalabras } from '../../nucleo/interprete';
import { ResumenService } from '../../nucleo/resumen.service';
import { SesionService } from '../../nucleo/sesion.service';
import { ToastService } from '../../nucleo/toast.service';
import { VERSION_APP } from '../../nucleo/version';

const NUEVA = '__nueva';

@Component({
  selector: 'app-registrar',
  imports: [Icono, SinSeleccionTactil, MarcaTarjetaLogo],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './registrar.html',
  styleUrl: './registrar.css',
})
export class Registrar {
  protected readonly tituloTarjeta = tituloTarjeta;
  protected readonly gastos = inject(GastosService);
  protected readonly sesion = inject(SesionService);
  protected readonly resumen = inject(ResumenService);
  private readonly toast = inject(ToastService);
  private readonly router = inject(Router);
  private readonly borradores = inject(BorradoresService);
  private montoDeFrase = false;
  private readonly campoFrase = viewChild<ElementRef<HTMLInputElement>>('campoFrase');
  private readonly campoNueva = viewChild<ElementRef<HTMLInputElement>>('campoNueva');
  private readonly filaCategorias = viewChild<ElementRef<HTMLElement>>('filaCategorias');

  protected readonly pesos = pesos;
  protected readonly version = VERSION_APP;
  protected readonly NUEVA = NUEVA;
  protected readonly montosRapidos = [5_000, 10_000, 20_000, 50_000];
  protected readonly categorias = CATEGORIAS.map((nombre) => ({ nombre, icono: iconoCategoria(nombre), color: colorCategoria(nombre) }));

  protected readonly frase = signal('');
  protected readonly monto = signal(0);
  protected readonly descripcion = signal('');
  protected readonly categoria = signal('');
  protected readonly categoriaNueva = signal('');
  protected readonly diasAtras = signal<number | null>(0); // 0 hoy, 1 ayer, null otra fecha
  protected readonly fechaOtra = signal('');
  protected readonly menuAbierto = signal(false);
  // Gasto directo de un vehículo (SOAT, llantas, repuestos…): también va a su historial en el PC
  protected readonly vehiculo = signal<number | null>(null);
  protected readonly faltaVehiculo = signal(false); // es de un vehículo pero no se sabe cuál
  private vehiculoDeFrase = false;
  // Pagado con tarjeta de crédito (null = efectivo o débito): la compra baja el cupo de la tarjeta
  protected readonly tarjeta = signal<number | null>(null);
  private tarjetaDeFrase = false; // la eligió la frase (si la frase ya no la dice, se quita)
  /** Cupo disponible al día: lo que publicó el PC menos las compras de aquí que aún no descuenta */
  protected readonly tarjetasConCupo = computed(() => this.resumen.tarjetas().map((t) => ({
    ...t, disponible: Math.max(0, t.disponible - this.gastos.pendienteEnTarjeta(t.id)),
  })));
  protected readonly superaCupo = computed(() => {
    const t = this.tarjetasConCupo().find((x) => x.id === this.tarjeta());
    return !!t && this.monto() > t.disponible;
  });
  // Cuotas de la compra con tarjeta: siempre 1 por defecto (también se dicen: "a 6 cuotas", "en 12 cuotas")
  protected readonly cuotas = signal(1);
  private cuotasDeFrase = false;
  private readonly CUOTAS_RAPIDAS = [1, 2, 3, 6, 12, 24, 36];
  protected readonly opcionesCuotas = computed(() =>
    this.CUOTAS_RAPIDAS.includes(this.cuotas()) ? this.CUOTAS_RAPIDAS : [...this.CUOTAS_RAPIDAS, this.cuotas()].sort((a, b) => a - b));
  /** "6 cuotas de $X · intereses aprox. $Y" con la tasa de la tarjeta */
  protected readonly detalleCuotas = computed(() => {
    const t = this.resumen.tarjetas().find((x) => x.id === this.tarjeta());
    const n = this.cuotas(), monto = this.monto();
    if (!t) return '';
    if (n === 1) return 'A 1 cuota se paga completa el mes siguiente, sin intereses.';
    if (!monto) return `A ${n} cuotas, con la tasa de la tarjeta.`;
    const e = estimarCuotas(monto, n, t.tasaEA ?? 0);
    return e.intereses > 0 ? `${n} cuotas de ${pesos(e.cuota)} · intereses aprox. ${pesos(e.intereses)}`
                           : `${n} cuotas de ${pesos(e.cuota)}`;
  });
  protected readonly escuchando = signal(false);
  protected readonly sosteniendo = signal(false);
  private readonly destruir = inject(DestroyRef);

  protected readonly montoTexto = computed(() => (this.monto() ? miles(this.monto()) : ''));

  constructor() {
    const alOcultar = () => { if (document.hidden) this.detenerTodo(); };
    const alSalir = () => this.detenerTodo();
    document.addEventListener('visibilitychange', alOcultar);
    window.addEventListener('blur', alSalir);
    window.addEventListener('pagehide', alSalir);
    this.destruir.onDestroy(() => {
      document.removeEventListener('visibilitychange', alOcultar);
      window.removeEventListener('blur', alSalir);
      window.removeEventListener('pagehide', alSalir);
      this.punteroVoz = null;
      this.sosteniendo.set(false);
      const r = this.reconocimiento;
      this.reconocimiento = null;
      if (r) { r.onresult = r.onerror = r.onend = r.onstart = null; try { r.abort(); } catch {} }
    });
    this.destruir.onDestroy(this.borradores.registrar('registrar', () => {
      if (this.escuchando()) throw new Error('Termina el dictado antes de actualizar.');
      if (this.leyendo()) throw new Error('Espera a que termine de leer el recibo antes de actualizar.');
      return { frase:this.frase(), monto:this.monto(), descripcion:this.descripcion(), categoria:this.categoria(), categoriaNueva:this.categoriaNueva(), diasAtras:this.diasAtras(), fechaOtra:this.fechaOtra(), vehiculo:this.vehiculo(), tarjeta:this.tarjeta(), cuotas:this.cuotas(), faltaVehiculo:this.faltaVehiculo(), montoDeFrase:this.montoDeFrase, vehiculoDeFrase:this.vehiculoDeFrase, tarjetaDeFrase:this.tarjetaDeFrase, cuotasDeFrase:this.cuotasDeFrase };
    }, b => {
      this.frase.set(b.frase); this.monto.set(b.monto); this.descripcion.set(b.descripcion); this.categoria.set(b.categoria); this.categoriaNueva.set(b.categoriaNueva);
      this.diasAtras.set(b.diasAtras); this.fechaOtra.set(b.fechaOtra); this.vehiculo.set(b.vehiculo); this.tarjeta.set(b.tarjeta); this.cuotas.set(b.cuotas); this.faltaVehiculo.set(b.faltaVehiculo);
      this.montoDeFrase=b.montoDeFrase; this.vehiculoDeFrase=b.vehiculoDeFrase; this.tarjetaDeFrase=b.tarjetaDeFrase; this.cuotasDeFrase=b.cuotasDeFrase;
    }));
    // La categoría elegida (también la que pone la frase o la foto) se ve en su fila, sin mover el resto de la página
    effect(() => {
      this.categoria();
      setTimeout(() => {
        const fila = this.filaCategorias()?.nativeElement;
        const chip = fila?.querySelector<HTMLElement>('.chip[aria-checked="true"]');
        if (!fila || !chip) return;
        const izq = chip.offsetLeft - fila.offsetLeft, der = izq + chip.offsetWidth;
        if (izq < fila.scrollLeft || der > fila.scrollLeft + fila.clientWidth)
          fila.scrollTo({ left: Math.max(0, izq - 16), behavior: 'smooth' });
      });
    });
  }

  // ---------- Frase ----------
  // El valor lo puso la frase (si no, vino de la foto o se escribió a mano y la frase no lo borra)

  protected alEscribirFrase(valor: string) {
    this.frase.set(valor);
    // Cuotas ("a 6 cuotas"): se quitan antes de leer el valor, para no confundir ese número con el monto
    const { cuotas, resto } = detectarCuotas(valor);
    const r = interpretar(resto);
    // ¿Cómo pagaste? ("con la visa", "en efectivo"): elige el medio y esas palabras no van en la descripción
    const tarjetas = this.resumen.tarjetas();
    const pago = detectarPago(resto, tarjetas);
    if (pago.dicho) { this.tarjeta.set(pago.id); this.tarjetaDeFrase = true; }
    else if (cuotas !== null && cuotas > 1 && this.tarjeta() === null && tarjetas.length === 1) {
      this.tarjeta.set(tarjetas[0].id); this.tarjetaDeFrase = true; // "a 6 cuotas" con una sola tarjeta: es esa
    }
    else if (this.tarjetaDeFrase && !(cuotas !== null && cuotas > 1)) { this.tarjeta.set(null); this.tarjetaDeFrase = false; }
    if (cuotas !== null) { this.cuotas.set(cuotas); this.cuotasDeFrase = true; }
    else if (this.cuotasDeFrase) { this.cuotas.set(1); this.cuotasDeFrase = false; }
    if (this.tarjeta() === null) this.cuotas.set(1);
    this.descripcion.set(quitarPalabras(r.descripcion, pago.palabras));
    if (r.monto !== null) { this.monto.set(r.monto); this.montoDeFrase = true; }
    else if (this.montoDeFrase) { this.monto.set(0); this.montoDeFrase = false; } // se borró el número de la frase
    if (r.categoria) this.categoria.set(r.categoria);
    this.detectarVehiculo(valor);
    const dias = Math.round((new Date().setHours(0, 0, 0, 0) - r.fecha.getTime()) / 864e5);
    if (dias === 0 || dias === 1) this.diasAtras.set(dias);
    else { this.diasAtras.set(null); this.fechaOtra.set(aISO(r.fecha)); }
  }

  private reconocimiento: any = null;
  private detenerSolicitado = false;
  private punteroVoz: number | null = null;
  private teclaVoz: string | null = null;
  /** Una pulsación más corta que esto es un toque: sigue escuchando hasta tocar la pausa. */
  private static readonly UMBRAL_SOSTENER_MS = 400;
  private inicioPulsacion = 0;
  /** La pulsación en curso es la que termina un dictado iniciado con un toque. */
  private pulsacionParaDetener = false;

  /** El botón original conserva inicio/fin con un toque. */
  protected dictar() {
    if (this.reconocimiento) { this.detenerDictado(); return; }
    const Reconocimiento = (window as any).SpeechRecognition || (window as any).webkitSpeechRecognition;
    if (!Reconocimiento) {
      this.campoFrase()?.nativeElement.focus();
      this.toast.mostrar('Toca el micrófono 🎤 del teclado para dictar');
      return;
    }
    const r = new Reconocimiento();
    r.lang = 'es-CO';
    r.continuous = true;
    r.interimResults = true;
    this.detenerSolicitado = false;
    r.onstart = () => {
      // Si soltó mientras autorizaba el micrófono, no dejarlo escuchando después.
      if (this.reconocimiento === r && this.detenerSolicitado) { try { r.stop(); } catch {} }
    };
    r.onresult = (e: any) => {
      if (this.reconocimiento !== r || this.destruir.destroyed) return;
      let texto = '';
      for (let i = 0; i < e.results.length; i++) texto += e.results[i][0].transcript;
      this.alEscribirFrase(texto.trim());
    };
    r.onerror = (e: any) => {
      if (this.reconocimiento !== r) return;
      if (e.error === 'not-allowed') this.toast.mostrar('Permite el uso del micrófono para dictar');
      else if (e.error !== 'aborted' && e.error !== 'no-speech') this.toast.mostrar('No te escuché bien. Intenta de nuevo.');
    };
    r.onend = () => {
      if (this.reconocimiento !== r) return;
      this.escuchando.set(false); this.sosteniendo.set(false);
      this.punteroVoz = null; this.teclaVoz = null; this.pulsacionParaDetener = false; this.reconocimiento = null;
    };
    this.reconocimiento = r;
    this.escuchando.set(true);
    try { r.start(); }
    catch {
      this.reconocimiento = null; this.escuchando.set(false); this.sosteniendo.set(false);
      this.toast.mostrar('No se pudo iniciar el micrófono. Intenta de nuevo.');
    }
  }

  private detenerDictado() {
    this.detenerSolicitado = true;
    // stop conserva el último resultado, a diferencia de abort.
    try { this.reconocimiento?.stop(); } catch { /* onstart reintenta si estaba esperando permiso. */ }
  }

  /** Al presionar: si no escucha, empieza (toque o sostener se decide al soltar); si ya escucha, al soltar termina. */
  protected sostenerVoz(evento: PointerEvent) {
    if (!evento.isPrimary || evento.button !== 0 || this.punteroVoz !== null || this.sosteniendo()) return;
    if (evento.cancelable) evento.preventDefault();
    this.punteroVoz = evento.pointerId;
    try { (evento.currentTarget as HTMLElement).setPointerCapture(evento.pointerId); } catch {}
    if (this.reconocimiento) { this.pulsacionParaDetener = true; return; }
    this.sosteniendo.set(true);
    this.inicioPulsacion = Date.now();
    this.dictar();
    if (!this.reconocimiento) { this.punteroVoz = null; this.sosteniendo.set(false); }
  }

  protected soltarTacto(evento: TouchEvent) {
    // Respaldo para Safari si cancela la secuencia de eventos de puntero.
    if (evento.touches.length === 0 && (this.sosteniendo() || this.pulsacionParaDetener)) this.terminarPulsacion();
  }

  protected soltarVoz(evento: PointerEvent) {
    if (evento.pointerId !== this.punteroVoz) return;
    this.terminarPulsacion();
  }

  protected sostenerTecla(evento: KeyboardEvent) {
    if (evento.key !== ' ' && evento.key !== 'Enter') return;
    evento.preventDefault();
    if (evento.repeat || this.teclaVoz !== null || this.sosteniendo()) return;
    this.teclaVoz = evento.key;
    if (this.reconocimiento) { this.pulsacionParaDetener = true; return; }
    this.inicioPulsacion = Date.now();
    this.sosteniendo.set(true); this.dictar();
    if (!this.reconocimiento) { this.teclaVoz = null; this.sosteniendo.set(false); }
  }

  protected soltarTecla(evento: KeyboardEvent) {
    if (evento.key !== this.teclaVoz) return;
    evento.preventDefault(); this.terminarPulsacion();
  }

  /** Salir u ocultar la app: termina el dictado sin importar cómo empezó. */
  private detenerTodo() {
    this.punteroVoz = null; this.teclaVoz = null; this.pulsacionParaDetener = false;
    this.sosteniendo.set(false);
    if (this.reconocimiento) this.detenerDictado();
  }

  protected terminarPulsacion() {
    if (this.punteroVoz === null && this.teclaVoz === null && !this.sosteniendo() && !this.pulsacionParaDetener) return;
    this.punteroVoz = null; this.teclaVoz = null;
    // Segundo toque (o pulsación) sobre la pausa: termina el dictado.
    if (this.pulsacionParaDetener) { this.pulsacionParaDetener = false; this.detenerDictado(); return; }
    const fueToque = Date.now() - this.inicioPulsacion < Registrar.UMBRAL_SOSTENER_MS;
    this.sosteniendo.set(false);
    if (!fueToque && this.reconocimiento) this.detenerDictado(); // sostenido: soltar termina
  }

  // ---------- Foto del recibo ----------
  protected readonly foto = signal<string | null>(null);      // vista previa
  protected readonly leyendo = signal(false);
  protected readonly avance = signal(0);                       // 0 a 1
  protected readonly lecturaFoto = signal<{ total: number | null; comercio: string } | null>(null);

  /** Lee el total de la foto en el celular (la foto no se envía ni se guarda) */
  protected async alElegirFoto(campo: HTMLInputElement) {
    const archivo = campo.files?.[0];
    campo.value = ''; // permite volver a elegir la misma foto
    if (!archivo) return;

    this.quitarFoto();
    this.foto.set(URL.createObjectURL(archivo));
    this.leyendo.set(true);
    this.avance.set(0);
    try {
      const { leerRecibo } = await import('../../nucleo/recibo');
      const datos = await leerRecibo(archivo, (p) => this.avance.set(p));
      this.lecturaFoto.set({ total: datos.total, comercio: datos.comercio });
      // Se anota lo que llenó la foto, para quitarlo si se quita la foto
      const lleno: { frase?: string; monto?: number; descripcion?: string; categoria?: string } = {};
      if (datos.total) { this.monto.set(datos.total); this.montoDeFrase = false; lleno.monto = datos.total; }
      if (datos.comercio && !this.descripcion().trim()) { this.descripcion.set(datos.comercio); lleno.descripcion = datos.comercio; }
      // En "¿En qué gastaste?" va solo la descripción (el comercio)
      if (datos.comercio && !this.frase().trim()) { this.frase.set(datos.comercio); lleno.frase = datos.comercio; }
      const categoria = interpretar(`${datos.comercio} ${datos.texto}`).categoria;
      if (categoria && !this.categoria()) { this.categoria.set(categoria); lleno.categoria = categoria; }
      this.llenoPorFoto = lleno;
      this.toast.mostrar(datos.total ? `Total detectado: ${pesos(datos.total)}` : 'No encontré el total: escríbelo en Valor');
    } catch (e) {
      console.error('No se pudo leer el recibo', e);
      this.toast.mostrar(navigator.onLine ? 'No pude leer la foto. Intenta con otra más nítida.'
                                          : 'Para leer la foto la primera vez se necesita internet');
    } finally {
      this.leyendo.set(false);
    }
  }

  // Valores que puso la última foto (si el usuario los cambió a mano, no se tocan al quitarla)
  private llenoPorFoto: { frase?: string; monto?: number; descripcion?: string; categoria?: string } = {};

  /** Quita la foto y lo que ella llenó (frase, valor, descripción y categoría) */
  protected quitarFoto() {
    const url = this.foto();
    if (url) URL.revokeObjectURL(url);
    this.foto.set(null);
    this.lecturaFoto.set(null);
    const lleno = this.llenoPorFoto;
    if (lleno.frase !== undefined && this.frase() === lleno.frase) this.frase.set('');
    if (lleno.monto !== undefined && this.monto() === lleno.monto) this.monto.set(0);
    if (lleno.descripcion !== undefined && this.descripcion() === lleno.descripcion) this.descripcion.set('');
    if (lleno.categoria !== undefined && this.categoria() === lleno.categoria) this.categoria.set('');
    this.llenoPorFoto = {};
  }

  // ---------- Vehículo ----------
  private detectarVehiculo(frase: string) {
    const { esDeVehiculo, id } = detectarVehiculo(frase, this.resumen.vehiculos());
    if (esDeVehiculo) {
      this.categoria.set('Vehículos');
      if (id !== null) { this.vehiculo.set(id); this.vehiculoDeFrase = true; }
      this.faltaVehiculo.set(id === null && this.vehiculo() === null);
    } else if (this.vehiculoDeFrase) {
      this.vehiculo.set(null);
      this.vehiculoDeFrase = false;
      this.faltaVehiculo.set(false);
      if (this.categoria() === 'Vehículos') this.categoria.set('');
    }
  }

  /** Tocar un vehículo lo elige (o lo quita si ya estaba); la categoría pasa a "Vehículos" */
  protected elegirVehiculo(id: number) {
    this.vehiculoDeFrase = false;
    this.faltaVehiculo.set(false);
    this.vehiculo.set(this.vehiculo() === id ? null : id);
    if (this.vehiculo() !== null && (!this.categoria() || this.categoria() === 'Transporte')) this.categoria.set('Vehículos');
  }

  /** Elegida a mano: deja de seguir lo que diga la frase */
  protected elegirTarjeta(id: number | null) {
    this.tarjeta.set(id); this.tarjetaDeFrase = false;
    if (id === null) { this.cuotas.set(1); this.cuotasDeFrase = false; } // en efectivo no hay cuotas
  }

  protected elegirCuotas(n: number) { this.cuotas.set(n); this.cuotasDeFrase = false; }

  // ---------- A mano ----------
  protected alEscribirMonto(valor: string) { this.monto.set(soloDigitos(valor)); this.montoDeFrase = false; }
  protected sumar(n: number) { this.monto.update((m) => m + n); this.montoDeFrase = false; }

  protected elegirCategoria(nombre: string) {
    this.categoria.set(this.categoria() === nombre ? '' : nombre); // tocar la elegida la quita
    if (this.categoria() === NUEVA) setTimeout(() => this.campoNueva()?.nativeElement.focus());
  }

  protected elegirFecha(dias: number | null) {
    this.diasAtras.set(dias);
    if (dias === null && !this.fechaOtra()) this.fechaOtra.set(aISO(new Date()));
  }

  // ---------- Guardar ----------
  protected guardar() {
    const monto = this.monto();
    if (!monto) {
      this.toast.mostrar('Escribe cuánto gastaste (por ejemplo: "almuerzo 25 mil")');
      return;
    }
    // Hoy con la hora actual; otro día, a mediodía (no se sabe la hora)
    let fecha = new Date();
    const dias = this.diasAtras();
    if (dias === 1) { fecha.setDate(fecha.getDate() - 1); fecha.setHours(12, 0, 0, 0); }
    else if (dias === null && this.fechaOtra()) {
      fecha = new Date(this.fechaOtra() + 'T12:00:00');
      if (aISO(fecha) === aISO(new Date())) fecha = new Date();
    }
    const descripcion = this.descripcion().trim() || 'Gasto';
    const categoria = this.categoria() === NUEVA ? this.categoriaNueva().trim() : this.categoria();

    const idTarjeta = this.tarjeta();
    const nombreTarjeta = this.resumen.tarjetas().find((t) => t.id === idTarjeta)?.nombre ?? '';
    const idVehiculo = this.vehiculo();
    const nombreVehiculo = this.resumen.vehiculos().find((v) => v.id === idVehiculo)?.nombre ?? '';
    this.gastos.guardar({ fecha, monto, descripcion, categoria, textoOriginal: this.frase().trim(),
                          vehiculoId: nombreVehiculo ? idVehiculo : null, vehiculo: nombreVehiculo,
                          tarjetaId: nombreTarjeta ? idTarjeta : null, tarjeta: nombreTarjeta,
                          cuotas: nombreTarjeta ? this.cuotas() : 1 })
      .catch(() => this.toast.mostrar('No se pudo guardar el gasto. Revisa tu sesión.'));
    const aCuotas = nombreTarjeta && this.cuotas() > 1 ? ` a ${this.cuotas()} cuotas` : '';
    this.toast.mostrar(nombreTarjeta ? `Guardado con ${nombreTarjeta}${aCuotas}: ${pesos(monto)} · ${descripcion}` : `Guardado: ${pesos(monto)} · ${descripcion}`);
    this.limpiar();
  }

  private limpiar() {
    this.quitarFoto();
    this.frase.set(''); this.monto.set(0); this.montoDeFrase = false; this.descripcion.set('');
    this.categoria.set(''); this.categoriaNueva.set('');
    this.vehiculo.set(null); this.vehiculoDeFrase = false; this.faltaVehiculo.set(false);
    this.tarjeta.set(null); this.tarjetaDeFrase = false; this.cuotas.set(1); this.cuotasDeFrase = false;
    this.diasAtras.set(0); this.fechaOtra.set('');
  }

  protected verGastos() { this.router.navigate(['/gastos']); }

  protected async salir() {
    this.menuAbierto.set(false);
    await this.sesion.salir();
  }
}
