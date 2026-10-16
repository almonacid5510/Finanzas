// ============================================================
//  Proyecto: Mis Finanzas
//  Autoria: Juan Almonacid
//  Copyright (c) 2026 Juan Almonacid. Todos los derechos reservados.
// ============================================================
// Tema de colores de la app (Claro, Oscuro o Rosado). Se guarda en este celular y se aplica con
// <html data-tema="…"> (las paletas están en styles.css). index.html lo aplica antes de cargar, sin parpadeo.
import { Injectable, effect, signal } from '@angular/core';

export type Tema = 'claro' | 'oscuro' | 'rosado';
export const TEMAS: { id: Tema; nombre: string; muestra: [string, string, string] }[] = [
  { id: 'claro', nombre: 'Claro', muestra: ['#F2F4F7', '#FFFFFF', '#1D5BD8'] },
  { id: 'oscuro', nombre: 'Oscuro', muestra: ['#0B1220', '#141C2B', '#4C84FF'] },
  { id: 'rosado', nombre: 'Rosado', muestra: ['#FFF4F8', '#FFFFFF', '#D6338A'] },
];
const CLAVE = 'mis-finanzas-tema';

function leer(): Tema {
  try {
    const t = localStorage.getItem(CLAVE);
    return t === 'oscuro' || t === 'rosado' ? t : 'claro';
  } catch { return 'claro'; }
}

@Injectable({ providedIn: 'root' })
export class TemaService {
  readonly tema = signal<Tema>(leer());

  constructor() {
    effect(() => {
      const t = this.tema();
      if (t === 'claro') delete document.documentElement.dataset['tema'];
      else document.documentElement.dataset['tema'] = t;
    });
  }

  elegir(t: Tema) {
    this.tema.set(t);
    try { localStorage.setItem(CLAVE, t); } catch { /* sin almacenamiento: dura mientras la app esté abierta */ }
  }
}
