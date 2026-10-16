// ============================================================
//  Proyecto: Mis Finanzas
//  Autoria: Juan Almonacid
//  Copyright (c) 2026 Juan Almonacid. Todos los derechos reservados.
// ============================================================
// Entrar o crear la cuenta (la misma que se conecta en Mis Finanzas del PC).
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { Icono } from '../../compartido/icono';
import { SesionService } from '../../nucleo/sesion.service';

@Component({
  selector: 'app-cuenta',
  imports: [Icono],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <header class="cabecera">
      <span class="logo"><app-icono nombre="logo" [tamano]="30" /></span>
      <h1>Mis Finanzas</h1>
      <p>Registra tus gastos del día a día y llévalos a tu PC.</p>
    </header>
    <main class="pagina">
      <form class="tarjeta" (submit)="$event.preventDefault(); enviar(correo.value, clave.value)" novalidate>
        <h2>{{ crear() ? 'Crea tu cuenta' : 'Entra a tu cuenta' }}</h2>
        <p class="nota">Usa la misma cuenta que conectas en Mis Finanzas del computador.</p>
        <label class="etiqueta" for="correo">Correo</label>
        <input #correo id="correo" class="campo" type="email" autocomplete="email" required [value]="correoInicial" />
        <label class="etiqueta" for="clave">Contraseña</label>
        <input #clave id="clave" class="campo" type="password" minlength="6" required
               [attr.autocomplete]="crear() ? 'new-password' : 'current-password'" />
        @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
        <button class="boton" type="submit" [disabled]="enviando()">{{ crear() ? 'Crear cuenta' : 'Entrar' }}</button>
      </form>
      <button class="enlace cambiar" (click)="crear.set(!crear()); error.set('')">
        {{ crear() ? '¿Ya tienes cuenta? Entra aquí' : '¿No tienes cuenta? Créala aquí' }}
      </button>
    </main>
  `,
  styles: `
    .cabecera { background: var(--oscuro); color: #fff; text-align: center; border-radius: 0 0 28px 28px;
      padding: calc(40px + env(safe-area-inset-top)) 24px 34px; }
    .logo { width: 64px; height: 64px; border-radius: 18px; background: var(--verde); display: inline-grid; place-items: center; }
    h1 { font-size: 26px; font-weight: 800; margin: 14px 0 4px; }
    .cabecera p { color: var(--texto-menu); margin: 0; font-size: 15px; }
    form { margin-top: 6px; }
    h2 { font-size: 20px; font-weight: 800; margin: 0 0 4px; }
    .nota { color: var(--texto2); font-size: 14px; margin: 0 0 16px; }
    .etiqueta { margin-top: 14px; }
    .error { color: var(--rojo); font-size: 14px; margin: 12px 0 0; }
    .boton { margin-top: 20px; }
    .cambiar { display: block; margin: 14px auto 0; }
  `,
})
export class Cuenta {
  private readonly sesion = inject(SesionService);
  // El QR de Cuenta en el PC abre esta pantalla con ?cuenta=entrar&correo=… (PC ya conectado) o ?cuenta=crear
  private readonly enlace = new URLSearchParams(location.search);
  protected readonly correoInicial = this.enlace.get('correo') ?? '';
  protected readonly crear = signal(this.enlace.get('cuenta') === 'crear');
  protected readonly error = signal('');
  protected readonly enviando = signal(false);

  protected async enviar(correo: string, clave: string) {
    if (!correo.trim() || clave.length < 6) {
      this.error.set('Escribe tu correo y una contraseña de al menos 6 caracteres.');
      return;
    }
    this.enviando.set(true);
    this.error.set((await this.sesion.entrar(correo.trim(), clave, this.crear())) ?? '');
    this.enviando.set(false);
  }
}
