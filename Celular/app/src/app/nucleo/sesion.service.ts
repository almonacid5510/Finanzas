// ============================================================
//  Proyecto: Mis Finanzas
//  Autoria: Juan Almonacid
//  Copyright (c) 2026 Juan Almonacid. Todos los derechos reservados.
// ============================================================
// Cuenta del usuario (la misma que se conecta en Mis Finanzas del PC → Cuenta → Conectar tu celular).
import { Injectable, signal } from '@angular/core';
import { User, createUserWithEmailAndPassword, onAuthStateChanged, signInWithEmailAndPassword, signOut } from 'firebase/auth';
import { auth } from './firebase';

@Injectable({ providedIn: 'root' })
export class SesionService {
  /** undefined = aún revisando si hay sesión guardada; null = sin sesión */
  readonly usuario = signal<User | null | undefined>(undefined);

  constructor() {
    onAuthStateChanged(auth, (u) => this.usuario.set(u));
  }

  /** Devuelve un mensaje de error o null si entró */
  async entrar(correo: string, clave: string, crear: boolean): Promise<string | null> {
    try {
      if (crear) await createUserWithEmailAndPassword(auth, correo, clave);
      else await signInWithEmailAndPassword(auth, correo, clave);
      return null;
    } catch (e: unknown) {
      return mensajeError((e as { code?: string }).code);
    }
  }

  salir() {
    return signOut(auth);
  }
}

function mensajeError(codigo?: string): string {
  switch (codigo) {
    case 'auth/invalid-credential':
    case 'auth/wrong-password':
    case 'auth/user-not-found': return 'Correo o contraseña incorrectos.';
    case 'auth/email-already-in-use': return 'Ese correo ya tiene cuenta. Entra con él.';
    case 'auth/invalid-email': return 'El correo no es válido.';
    case 'auth/weak-password': return 'La contraseña debe tener al menos 6 caracteres.';
    case 'auth/network-request-failed': return 'Sin conexión. Intenta de nuevo cuando tengas señal.';
    case 'auth/too-many-requests': return 'Demasiados intentos. Espera un momento.';
    default: return 'No se pudo continuar. Intenta de nuevo.';
  }
}
