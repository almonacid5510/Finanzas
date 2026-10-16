// Retira el service worker de la versión anterior de la app (antes de Angular).
// Los celulares que la tenían instalada buscan este archivo; al encontrarlo borra su caché y se quita,
// y la app nueva (con el service worker de Angular, ngsw-worker.js) toma su lugar.
self.addEventListener('install', () => self.skipWaiting());
self.addEventListener('activate', (e) => {
  e.waitUntil((async () => {
    for (const clave of await caches.keys()) {
      if (!clave.startsWith('ngsw:')) await caches.delete(clave);
    }
    await self.registration.unregister();
    for (const cliente of await self.clients.matchAll({ type: 'window' })) cliente.navigate(cliente.url);
  })());
});
