import { DestroyRef, Injectable, inject, signal } from '@angular/core';
import { SwUpdate } from '@angular/service-worker';
import { BorradoresService } from './borradores.service';
import { VERSION_APP } from './version';

export interface VersionNueva { hash: string; version: string; novedades: string; }
@Injectable({ providedIn: 'root' })
export class ActualizacionesService {
  private readonly sw = inject(SwUpdate);
  private readonly borradores = inject(BorradoresService);
  private readonly destruir = inject(DestroyRef);
  readonly versionInstalada = VERSION_APP;
  readonly disponible = signal<VersionNueva | null>(null);
  readonly mostrarAviso = signal(false);
  readonly buscando = signal(false);
  readonly descargando = signal(false);
  readonly aplicando = signal(false);
  readonly mensaje = signal('');
  private pospuesta = '';
  private ultimaBusqueda = 0;

  constructor() {
    if (!this.sw.isEnabled) { this.mensaje.set('Las actualizaciones se habilitan en la versión publicada.'); return; }
    const suscripcion = this.sw.versionUpdates.subscribe(evento => {
      if (evento.type === 'VERSION_DETECTED') { this.descargando.set(true); this.mensaje.set('Descargando la nueva versión…'); }
      if (evento.type === 'VERSION_READY') {
        this.descargando.set(false);
        const datos = evento.latestVersion.appData as { version?: string; novedades?: string } | undefined;
        this.disponible.set({ hash: evento.latestVersion.hash, version: datos?.version ?? 'Nueva versión', novedades: datos?.novedades ?? 'Mejoras y correcciones de la aplicación.' });
        this.mensaje.set('Actualización lista para instalar.');
        if (this.pospuesta !== evento.latestVersion.hash) this.mostrarAviso.set(true);
      }
      if (evento.type === 'VERSION_INSTALLATION_FAILED') {
        this.descargando.set(false); this.mensaje.set('No se pudo descargar la actualización. Puedes volver a intentarlo.');
      }
    });
    const buscarAlVolver = () => { if (document.visibilityState === 'visible' && Date.now()-this.ultimaBusqueda > 60000) void this.buscar(); };
    document.addEventListener('visibilitychange', buscarAlVolver);
    window.addEventListener('online', buscarAlVolver);
    this.destruir.onDestroy(() => { suscripcion.unsubscribe(); document.removeEventListener('visibilitychange', buscarAlVolver); window.removeEventListener('online', buscarAlVolver); });
    void this.buscar();
  }

  async buscar() {
    if (!this.sw.isEnabled || this.buscando() || this.aplicando()) return;
    if (this.disponible()) { this.mostrarAviso.set(true); return; }
    if (!navigator.onLine) { this.mensaje.set('Sin internet. Puedes seguir usando la versión instalada.'); return; }
    this.buscando.set(true); this.mensaje.set('Buscando actualizaciones…'); this.ultimaBusqueda = Date.now();
    try {
      const hayNueva = await this.sw.checkForUpdate();
      if (!hayNueva && !this.disponible()) this.mensaje.set('Tienes la versión más reciente.');
    } catch { this.mensaje.set('No se pudo consultar la actualización. Revisa tu conexión e inténtalo de nuevo.'); }
    finally { this.buscando.set(false); }
  }

  masTarde() { this.pospuesta = this.disponible()?.hash ?? ''; this.mostrarAviso.set(false); }

  actualizar() {
    if (!this.disponible() || this.aplicando()) return;
    this.aplicando.set(true);
    try {
      this.borradores.guardar();
      // El worker ya descargó y comprobó la versión; recargar evita mezclar archivos de versiones distintas.
      location.reload();
    } catch (error) {
      this.aplicando.set(false);
      this.mensaje.set(error instanceof Error && error.message ? error.message : 'No se pudo guardar el formulario. Termínalo antes de actualizar.');
    }
  }
}
