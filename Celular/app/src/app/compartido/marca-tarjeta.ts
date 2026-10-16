// ============================================================
//  Proyecto: Mis Finanzas
//  Autoria: Juan Almonacid
//  Copyright (c) 2026 Juan Almonacid. Todos los derechos reservados.
// ============================================================
// Logo de la marca de una tarjeta de crédito según su nombre ("Visa oro", "Mastercard Black"…).
import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { Icono } from './icono';

export type MarcaTarjeta = 'visa' | 'mastercard' | 'amex' | 'otra';

export function marcaTarjeta(nombre: string): MarcaTarjeta {
  const n = nombre.toLowerCase();
  if (n.includes('master')) return 'mastercard';
  if (n.includes('visa')) return 'visa';
  if (n.includes('amex') || n.includes('american')) return 'amex';
  return 'otra';
}

/** "visa" → "Tarjeta visa"; si el nombre ya dice "tarjeta", se deja igual. */
export function tituloTarjeta(nombre: string) { return /^tarjeta/i.test(nombre.trim()) ? nombre : `Tarjeta ${nombre}`; }

const NOMBRES: Record<MarcaTarjeta, string> = { visa: 'Visa', mastercard: 'Mastercard', amex: 'American Express', otra: 'Tarjeta de crédito' };

@Component({
  selector: 'app-marca-tarjeta',
  imports: [Icono],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { role: 'img', '[attr.aria-label]': 'etiqueta()', '[class]': '"marca " + marca()', '[class.pequena]': 'pequena()' },
  template: `
    @switch (marca()) {
      @case ('visa') { <b>VISA</b> }
      @case ('mastercard') { <i class="circulo rojo"></i><i class="circulo amarillo"></i> }
      @case ('amex') { <b>AMEX</b> }
      @default { <app-icono nombre="tarjeta" [tamano]="pequena() ? 16 : 18" /> }
    }
  `,
  styles: `
    :host { width: 44px; height: 30px; border-radius: 6px; flex: none; display: flex; align-items: center; justify-content: center; overflow: hidden;
      background: var(--gris-suave); color: var(--texto2); border: 1px solid var(--borde); box-sizing: border-box; }
    :host(.pequena) { width: 38px; height: 26px; border-radius: 5px; }
    :host(.visa) { background: linear-gradient(135deg, #1A1F71, #2A3BA8); border: 0; color: #fff; }
    :host(.visa) b { font-size: 12px; font-style: italic; font-weight: 900; letter-spacing: .5px; }
    :host(.pequena.visa) b { font-size: 10.5px; }
    :host(.mastercard) { background: #1C1C1C; border: 0; }
    .circulo { width: 15px; height: 15px; border-radius: 50%; display: block; }
    :host(.pequena) .circulo { width: 13px; height: 13px; }
    .rojo { background: #EB001B; }
    .amarillo { background: #F79E1B; margin-left: -6px; opacity: .9; }
    :host(.amex) { background: #2E77BC; border: 0; color: #fff; }
    :host(.amex) b { font-size: 10.5px; font-weight: 900; letter-spacing: .3px; }
    :host(.pequena.amex) b { font-size: 9px; }
  `,
})
export class MarcaTarjetaLogo {
  readonly nombre = input.required<string>();
  readonly pequena = input(false);
  protected readonly marca = computed(() => marcaTarjeta(this.nombre()));
  protected readonly etiqueta = computed(() => NOMBRES[this.marca()]);
}
