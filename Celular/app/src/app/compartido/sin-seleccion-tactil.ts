import { DestroyRef, Directive, ElementRef, inject } from '@angular/core';

/** Safari puede mostrar la lupa aun con user-select:none; cancela el gesto nativo solo aquí. */
@Directive({ selector: '[sinSeleccionTactil]' })
export class SinSeleccionTactil {
  constructor() {
    const elemento = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
    const impedir = (evento: Event) => { if (evento.cancelable) evento.preventDefault(); };
    // Debe ser un listener activo para que Safari respete preventDefault.
    elemento.addEventListener('touchstart', impedir, { passive: false });
    elemento.addEventListener('selectstart', impedir);
    inject(DestroyRef).onDestroy(() => {
      elemento.removeEventListener('touchstart', impedir);
      elemento.removeEventListener('selectstart', impedir);
    });
  }
}
