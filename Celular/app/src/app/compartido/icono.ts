// ============================================================
//  Proyecto: Mis Finanzas
//  Autoria: Juan Almonacid
//  Copyright (c) 2026 Juan Almonacid. Todos los derechos reservados.
// ============================================================
// Íconos de línea (estilo Lucide) en una cuadrícula de 24×24, como los de la app de escritorio.
import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

const ICONOS = {
  configuracion: ['M4 6h16', 'M4 12h16', 'M4 18h16', 'M8 3v6', 'M16 9v6', 'M10 15v6'],
  logo: ['M19 7V4a1 1 0 0 0-1-1H5a2 2 0 0 0 0 4h15a1 1 0 0 1 1 1v4h-3a2 2 0 0 0 0 4h3a1 1 0 0 0 1-1v-2a1 1 0 0 0-1-1', 'M3 5v14a2 2 0 0 0 2 2h15a1 1 0 0 0 1-1v-4'],
  microfono: ['M12 2a3 3 0 0 0-3 3v7a3 3 0 0 0 6 0V5a3 3 0 0 0-3-3Z', 'M19 10v2a7 7 0 0 1-14 0v-2', 'M12 19v3'],
  tarjeta: ['M4 5h16a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V7a2 2 0 0 1 2-2Z', 'M2 10h20', 'M6 15h4'],
  efectivo: ['M4 6h16a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2Z', 'M12 9.5a2.5 2.5 0 1 0 0 5a2.5 2.5 0 1 0 0-5Z', 'M6 12h.01', 'M18 12h.01'],
  camara: ['M14.5 4h-5L7 7H4a2 2 0 0 0-2 2v9a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2V9a2 2 0 0 0-2-2h-3l-2.5-3Z', 'M9 13a3 3 0 1 0 6 0a3 3 0 1 0-6 0'],
  pausa: ['M7 4h2a1 1 0 0 1 1 1v14a1 1 0 0 1-1 1H7a1 1 0 0 1-1-1V5a1 1 0 0 1 1-1Z', 'M15 4h2a1 1 0 0 1 1 1v14a1 1 0 0 1-1 1h-2a1 1 0 0 1-1-1V5a1 1 0 0 1 1-1Z'],
  menu: ['M4 6h16', 'M4 12h16', 'M4 18h16'],
  buscar: ['M11 3a8 8 0 1 0 0 16a8 8 0 1 0 0-16Z', 'm21 21-4.3-4.3'],
  registrar: ['M2 12a10 10 0 1 0 20 0a10 10 0 1 0-20 0', 'M8 12h8', 'M12 8v8'],
  gastos: ['M4 2v20l2-1 2 1 2-1 2 1 2-1 2 1 2-1 2 1V2l-2 1-2-1-2 1-2-1-2 1-2-1-2 1Z', 'M16 8H8', 'M16 12H8', 'M13 16H8'],
  resumen: ['M4 3h5a1 1 0 0 1 1 1v7a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1Z', 'M15 3h5a1 1 0 0 1 1 1v3a1 1 0 0 1-1 1h-5a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1Z',
            'M15 12h5a1 1 0 0 1 1 1v7a1 1 0 0 1-1 1h-5a1 1 0 0 1-1-1v-7a1 1 0 0 1 1-1Z', 'M4 16h5a1 1 0 0 1 1 1v3a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1v-3a1 1 0 0 1 1-1Z'],
  check: ['M20 6 9 17l-5-5'],
  recargar: ['M3 12a9 9 0 0 1 9-9 9.75 9.75 0 0 1 6.74 2.74L21 8', 'M21 3v5h-5', 'M21 12a9 9 0 0 1-9 9 9.75 9.75 0 0 1-6.74-2.74L3 16', 'M8 16H3v5'],
  borrar: ['M3 6h18', 'M19 6v14c0 1-1 2-2 2H7c-1 0-2-1-2-2V6', 'M8 6V4c0-1 1-2 2-2h4c1 0 2 1 2 2v2'],
  alerta: ['M21.73 18 13.73 4a2 2 0 0 0-3.46 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3Z', 'M12 9v4', 'M12 17h.01'],
  izquierda: ['m15 18-6-6 6-6'],
  derecha: ['m9 18 6-6-6-6'],
  lapiz: ['M21.17 6.81a2.82 2.82 0 0 0-3.98-3.98L3.84 16.17a2 2 0 0 0-.5.84l-1.32 4.35a.5.5 0 0 0 .62.62l4.35-1.32a2 2 0 0 0 .84-.5Z', 'm15 5 4 4'],
  cerrar: ['M18 6 6 18', 'm6 6 12 12'],
  salir: ['M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4', 'm16 17 5-5-5-5', 'M21 12H9'],
  persona: ['M19 21v-2a4 4 0 0 0-4-4H9a4 4 0 0 0-4 4v2', 'M8 7a4 4 0 1 0 8 0a4 4 0 1 0-8 0'],
  comida: ['M3 2v7c0 1.1.9 2 2 2h4a2 2 0 0 0 2-2V2', 'M7 2v20', 'M21 15V2a5 5 0 0 0-5 5v6c0 1.1.9 2 2 2h3Zm0 0v7'],
  carro: ['M19 17h2c.6 0 1-.4 1-1v-3c0-.9-.7-1.7-1.5-1.9C18.7 10.6 16 10 16 10s-1.3-1.4-2.2-2.3c-.5-.4-1.1-.7-1.8-.7H5c-.6 0-1.1.4-1.4.9l-1.4 2.9A3.7 3.7 0 0 0 2 12v4c0 .6.4 1 1 1h2', 'M5 17a2 2 0 1 0 4 0a2 2 0 1 0-4 0', 'M15 17a2 2 0 1 0 4 0a2 2 0 1 0-4 0', 'M9 17h6'],
  moto: ['M2 17.5a3.5 3.5 0 1 0 7 0a3.5 3.5 0 1 0-7 0', 'M15 17.5a3.5 3.5 0 1 0 7 0a3.5 3.5 0 1 0-7 0', 'M15 6a1 1 0 1 0 0-2a1 1 0 0 0 0 2Z', 'M12 17.5V14l-3-3 4-3 2 3h2'],
  bus: ['M6 4h12a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2Z', 'M4 11h16', 'M8 15h.01', 'M16 15h.01', 'M6 20v2', 'M18 20v2'],
  play: ['M2 12a10 10 0 1 0 20 0a10 10 0 1 0-20 0', 'm10 8 6 4-6 4Z'],
  salud: ['M22 12h-2.48a2 2 0 0 0-1.93 1.46l-2.35 8.36a.25.25 0 0 1-.48 0L9.24 2.18a.25.25 0 0 0-.48 0l-2.35 8.36A2 2 0 0 1 4.49 12H2'],
  educacion: ['M22 10 12 5 2 10l10 5 10-5Z', 'M6 12v5c3 3 9 3 12 0v-5'],
  rayo: ['M4 14a1 1 0 0 1-.78-1.63l9.9-10.2a.5.5 0 0 1 .86.46l-1.92 6.02A1 1 0 0 0 13 10h7a1 1 0 0 1 .78 1.63l-9.9 10.2a.5.5 0 0 1-.86-.46l1.92-6.02A1 1 0 0 0 11 14z'],
  casa: ['M3 10 12 3l9 7v10a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1Z', 'M9 21v-7h6v7'],
  etiqueta: ['M12.59 2.59A2 2 0 0 0 11.17 2H4a2 2 0 0 0-2 2v7.17a2 2 0 0 0 .59 1.42l8.7 8.7a2.43 2.43 0 0 0 3.42 0l6.58-6.58a2.43 2.43 0 0 0 0-3.42Z', 'M7.5 7.5h.01'],
} as const;

export type NombreIcono = keyof typeof ICONOS;

@Component({
  selector: 'app-icono',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'icono', 'aria-hidden': 'true' },
  template: `
    <svg viewBox="0 0 24 24" [attr.width]="tamano()" [attr.height]="tamano()" fill="none" stroke="currentColor"
         [attr.stroke-width]="grosor()" stroke-linecap="round" stroke-linejoin="round">
      @for (d of trazos(); track $index) { <path [attr.d]="d" /> }
    </svg>
  `,
  styles: `:host { display: inline-flex; flex: none; } svg { display: block; }`,
})
export class Icono {
  readonly nombre = input.required<NombreIcono>();
  readonly tamano = input(20);
  readonly grosor = input(2);
  protected readonly trazos = computed(() => ICONOS[this.nombre()]);
}
