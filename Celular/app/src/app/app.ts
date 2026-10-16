// ============================================================
//  Proyecto: Mis Finanzas
//  Autoria: Juan Almonacid
//  Copyright (c) 2026 Juan Almonacid. Todos los derechos reservados.
// ============================================================
// Estructura de la app: sin sesión, la pantalla de cuenta; con sesión, las pestañas y la barra inferior.
import { ChangeDetectionStrategy, Component, effect, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter, map } from 'rxjs';
import { Icono } from './compartido/icono';
import { SesionService } from './nucleo/sesion.service';
import { ToastService } from './nucleo/toast.service';
import { AvisoActualizacion } from './compartido/aviso-actualizacion/aviso-actualizacion';
import { RecordatorioNomina } from './compartido/recordatorio-nomina/recordatorio-nomina';
import { TemaService } from './nucleo/tema.service';
import { Cuenta } from './paginas/cuenta/cuenta';


@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, Icono, Cuenta, RecordatorioNomina, AvisoActualizacion],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (sesion.usuario() === undefined) {
      <p class="cargando">Cargando…</p>
    } @else if (!sesion.usuario()) {
      <app-cuenta />
    } @else {
      <router-outlet />
      <app-recordatorio-nomina />
      <nav class="barra-inferior" aria-label="Secciones">
        <a routerLink="/" routerLinkActive="activo" [routerLinkActiveOptions]="{ exact: true }">
          <app-icono nombre="registrar" [tamano]="24" [grosor]="1.8" /> Registrar
        </a>
        <a routerLink="/gastos" routerLinkActive="activo"><app-icono nombre="gastos" [tamano]="24" [grosor]="1.8" /> Gastos</a>
        <a routerLink="/resumen" routerLinkActive="activo"><app-icono nombre="resumen" [tamano]="24" [grosor]="1.8" /> Resumen</a>
        <a routerLink="/configuracion" routerLinkActive="activo"><app-icono nombre="persona" [tamano]="24" [grosor]="1.8" /> Cuenta</a>
      </nav>
    }
    <app-aviso-actualizacion />
    <div class="toast" [class.visible]="toast.visible()" role="status" aria-live="polite">{{ toast.mensaje() }}</div>
  `,
})
export class App {
  protected readonly sesion = inject(SesionService);
  protected readonly toast = inject(ToastService);
  private readonly router = inject(Router);
  private readonly tema = inject(TemaService);

  private readonly url = toSignal(
    this.router.events.pipe(filter((e) => e instanceof NavigationEnd), map((e) => e.urlAfterRedirects)),
    { initialValue: this.router.url },
  );

  constructor() {
    // La zona del reloj y la isla del iPhone toma el color de arriba de cada pantalla:
    // oscuro donde hay encabezado oscuro (Registrar y la cuenta), claro en Gastos y Resumen.
    effect(() => {
      const conEncabezadoOscuro = !this.sesion.usuario() || this.url().split('?')[0] === '/';
      this.tema.tema(); // cambia también al elegir otro tema
      const estilo = getComputedStyle(document.documentElement);
      const color = estilo.getPropertyValue(conEncabezadoOscuro ? '--oscuro' : '--fondo').trim();
      document.documentElement.style.backgroundColor = color;
      document.querySelector('meta[name="theme-color"]')?.setAttribute('content', color);
    });
  }
}
