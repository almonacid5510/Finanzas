import { Injectable, inject } from '@angular/core';
import { SesionService } from './sesion.service';

export type CamposFormulario = Record<string, string | boolean>;
export function capturarFormulario(formulario?: HTMLFormElement): CamposFormulario {
  const valores: CamposFormulario = {};
  if (formulario) for (const campo of Array.from(formulario.elements)) {
    if (campo instanceof HTMLInputElement || campo instanceof HTMLSelectElement || campo instanceof HTMLTextAreaElement) {
      if (campo.name) valores[campo.name] = campo instanceof HTMLInputElement && campo.type === 'checkbox' ? campo.checked : campo.value;
    }
  }
  return valores;
}
export function restaurarFormulario(formulario: HTMLFormElement, valores: CamposFormulario) {
  for (const [nombre, valor] of Object.entries(valores)) {
    const campo = formulario.elements.namedItem(nombre);
    if (campo instanceof HTMLInputElement && campo.type === 'checkbox') campo.checked = valor === true;
    else if (campo instanceof HTMLInputElement || campo instanceof HTMLSelectElement || campo instanceof HTMLTextAreaElement) campo.value = String(valor);
  }
}
@Injectable({ providedIn: 'root' })
export class BorradoresService {
  private readonly sesion = inject(SesionService);
  private readonly capturas = new Map<string, () => unknown>();
  private clave(nombre: string) { return `actualizacion-borrador:${this.sesion.usuario()?.uid ?? 'sin-sesion'}:${nombre}`; }
  registrar<T>(nombre: string, capturar: () => T, restaurar: (valor: T) => void): () => void {
    this.capturas.set(nombre, capturar);
    try {
      const clave = this.clave(nombre), texto = sessionStorage.getItem(clave);
      if (texto) { restaurar(JSON.parse(texto) as T); sessionStorage.removeItem(clave); }
    } catch { /* Un borrador corrupto no bloquea la aplicación. */ }
    return () => this.capturas.delete(nombre);
  }
  guardar() {
    // Si no hay espacio o un formulario aún trabaja, no se recarga la página.
    const valores = [...this.capturas].map(([nombre, capturar]) => [this.clave(nombre), JSON.stringify(capturar())] as const);
    for (const [clave, valor] of valores) sessionStorage.setItem(clave, valor);
  }
}
