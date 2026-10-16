import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { ActualizacionesService } from '../../nucleo/actualizaciones.service';
@Component({selector:'app-aviso-actualizacion',changeDetection:ChangeDetectionStrategy.OnPush,
 template: `@if (actualizaciones.mostrarAviso()) {
  <aside role="region" aria-label="Actualización disponible">
   <strong>Hay una actualización disponible</strong>
   <span>Versión {{ actualizaciones.disponible()?.version }}</span>
   <p>{{ actualizaciones.disponible()?.novedades }}</p>
   <small role="status">{{ actualizaciones.mensaje() }}</small>
   <div><button (click)="actualizaciones.actualizar()" [disabled]="actualizaciones.aplicando()">{{ actualizaciones.aplicando() ? 'Actualizando…' : 'Actualizar ahora' }}</button>
   <button class="luego" (click)="actualizaciones.masTarde()" [disabled]="actualizaciones.aplicando()">Más tarde</button></div>
  </aside> }`,
 styles: `aside{position:fixed;z-index:45;bottom:calc(var(--alto-nav) + env(safe-area-inset-bottom) + 12px);left:14px;right:14px;max-width:530px;margin:auto;padding:18px;border:1px solid var(--borde);border-radius:16px;background:var(--tarjeta);box-shadow:0 8px 35px #0003}strong,span,small{display:block}span,p,small{color:var(--texto2);font-size:13px}p{margin:8px 0}div{display:flex;gap:10px;margin-top:12px}button{border:0;border-radius:9px;padding:12px;background:var(--verde);color:white;font-weight:700}.luego{background:var(--gris-suave);color:var(--texto)}`,
})
export class AvisoActualizacion { protected readonly actualizaciones=inject(ActualizacionesService); }
