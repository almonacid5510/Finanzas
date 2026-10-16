// ============================================================
//  Proyecto: Mis Finanzas
//  Autoria: Juan Almonacid
//  Copyright (c) 2026 Juan Almonacid. Todos los derechos reservados.
// ============================================================
// Aviso flotante breve ("Guardado: $ 25.000 · Almuerzo").
import { Injectable, signal } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class ToastService {
  readonly mensaje = signal('');
  readonly visible = signal(false);
  private temporizador?: ReturnType<typeof setTimeout>;

  mostrar(texto: string) {
    this.mensaje.set(texto);
    this.visible.set(true);
    clearTimeout(this.temporizador);
    this.temporizador = setTimeout(() => this.visible.set(false), 2600);
  }
}
