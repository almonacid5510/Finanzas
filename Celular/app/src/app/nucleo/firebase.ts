// ============================================================
//  Proyecto: Mis Finanzas
//  Autoria: Juan Almonacid
//  Copyright (c) 2026 Juan Almonacid. Todos los derechos reservados.
// ============================================================
// Conexión con el proyecto de Firebase. Estos valores identifican el proyecto y son públicos por diseño:
// la seguridad la dan las reglas de Celular/firestore.rules (cada usuario solo lee y escribe lo suyo).
// Los mismos valores están en MisFinanzas/Modulos/SincronizacionCelular.cs (ApiKey y ProjectId).
import { initializeApp } from 'firebase/app';
import { getAuth } from 'firebase/auth';
import { doc, initializeFirestore, persistentLocalCache, persistentMultipleTabManager, serverTimestamp, setDoc } from 'firebase/firestore';

export const firebaseConfig = {
  apiKey: 'AIzaSyD5nujQQBtcMwn-V34b1kyll9taIoq_v0k',
  authDomain: 'mis-finanzas-4ea0b.firebaseapp.com',
  projectId: 'mis-finanzas-4ea0b',
  appId: '1:597832759386:web:a6498c7dcfc3dff1f14063',
};

export const app = initializeApp(firebaseConfig);
export const auth = getAuth(app);

// Caché local: sin señal los gastos se guardan en el celular y se suben solos al volver la conexión
export const db = initializeFirestore(app, {
  localCache: persistentLocalCache({ tabManager: persistentMultipleTabManager() }),
});

/**
 * Avisa a los demás dispositivos que hubo un cambio: el PC revisa esta marca cada pocos segundos y,
 * si cambió, sincroniza. Sin señal queda en la caché y se envía junto con el cambio.
 */
export function marcarCambio(uid: string) {
  setDoc(doc(db, 'usuarios', uid, 'estado', 'cambios'), { actualizada: serverTimestamp() }).catch(() => { /* se reintenta con el próximo cambio */ });
}
