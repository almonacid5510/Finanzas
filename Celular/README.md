# Mis Finanzas — app del celular

App web instalable (PWA, hecha en **Angular**) para Android y iPhone. Se sincroniza en las dos direcciones con
Mis Finanzas del PC a través de Firebase. No necesita tiendas de aplicaciones y funciona sin señal.

**Dirección:** https://mis-finanzas-4ea0b.web.app

## Pestañas

| Pestaña | Qué hace |
|---|---|
| **Registrar** | Escribes o dictas "gasté 25 mil en almuerzo" y se llenan valor, descripción y categoría. Con **Foto del recibo** se lee el total en el celular (OCR con Tesseract, `nucleo/recibo.ts`; la foto no se envía ni se guarda). También se llena a mano (montos rápidos +5 mil…, categorías con color, fecha). Arriba: cuánto llevas hoy y cuántos gastos faltan por enviar. |
| **Gastos** | Hoy / Semana / Mes, total por categoría, gastos por día con su estado (✓ En el PC · ● Pendiente · ● Sin enviar), búsqueda y deslizar ← para borrar. |
| **Resumen** | Lo mismo del Dashboard del PC: disponible, vencidas (botón **Pagado**), por pagar, ahorro, inversiones, deuda y vehículos. |

## Cómo se sincroniza

Todo vive en Firestore bajo `usuarios/{uid}/`; cada persona solo ve lo suyo (`firestore.rules`).

| Colección | Quién escribe | Para qué |
|---|---|---|
| `gastos` | Celular y PC | Gastos del día a día. El PC descarga los del celular (`descargado = true`) y sube los suyos. |
| `resumen/{yyyy-MM}` | PC | Resumen del mes anterior, el actual y el siguiente; se vuelve a publicar solo si cambió. |
| `acciones` | Celular | Lo que el celular pide al PC: `pagarCuenta`, `pagarCuota`, `borrarGasto`. El PC las aplica y las borra. |

En el PC (`MisFinanzas/Modulos/SincronizacionCelular.cs`) la sincronización corre sola: al abrir la app, cada minuto,
al volver a la ventana y al recuperar internet. Si el PC está apagado, todo espera en la nube.

El intérprete de frases (`app/src/app/nucleo/interprete.ts`) es el mismo de `MisFinanzas/Modulos/GastoDiario.cs`,
y los colores de categoría (`categorias.ts`) son los de `Etiquetas.cs`: si se cambia uno, hay que cambiar el otro.

## Estructura

```
Celular/
├── firebase.json, .firebaserc   Publicación (Hosting) y reglas
├── firestore.rules              Seguridad: cada usuario solo lee y escribe lo suyo
└── app/                         Proyecto Angular
    └── src/app/
        ├── nucleo/              Firebase, sesión, gastos, resumen, intérprete, categorías, formatos
        ├── compartido/icono.ts  Íconos de línea (como los del escritorio)
        └── paginas/             cuenta, registrar, gastos, resumen
```

## Desarrollo

Con [Node.js](https://nodejs.org) instalado:

```
cd Celular/app
npm install
npx ng serve          # http://localhost:4200
```

## Publicar

```
cd Celular/app && npx ng build
cd .. && npx firebase-tools deploy --only firestore:rules,hosting
```

La primera vez hay que iniciar sesión con `npx firebase-tools login`. Los celulares reciben la versión nueva
la próxima vez que abren la app (service worker de Angular).

## Instalar en el celular

- **Android (Chrome):** abrir la dirección → menú ⋮ → **Instalar app**.
- **iPhone (Safari):** abrir la dirección → **Compartir** → **Agregar a inicio**.


## Dinero disponible y pagos quincenales

El salario es el presupuesto mensual. El celular usa ingresos confirmados (primera quincena, segunda quincena, extras), registrados en Resumen > Dinero recibido. No anticipa nómina. Los cobros se guardan en `usuarios/{uid}/ingresosRecibidos` y se pueden eliminar para corregirlos.

El saldo inicial es el dinero **al comenzar el mes** elegido, no el saldo de hoy si ya registraste gastos. Un nuevo saldo inicial sustituye la cuenta desde ese mes. El sobrante pasa a meses siguientes. Se descuentan solo cuentas pagadas, pagos y abonos de créditos, ahorro, gastos sin tarjeta y vehículos sin duplicarlos. Las compras con tarjeta se descuentan al pagar la tarjeta. Las inversiones antiguas sin historial de aportes no se pueden reconstruir como salidas; es un saldo de registros, no una conexión bancaria.

Los ingresos y gastos nuevos del celular actualizan el saldo sin esperar al PC. Cuentas, créditos y ahorro requieren sincronización del PC. Para activarlo en dispositivos hay que publicar hosting y firestore:rules y ejecutar el PC actualizado.

Pruebas: `cd app` y `node tests/caja.cjs`.


## Calendario personal de nómina

En Configuración, tanto en el PC como en el celular, se definen las reglas de nómina: cada cuánto te pagan y qué días. El registro de dinero recibido se realiza en Resumen (celular) o desde el recordatorio de nómina. Puedes elegir Mensual (un día), Quincenal (dos días propios en casillas separadas) o Semanal (día de semana). El monto neto esperado por pago es obligatorio y editable. El día 31 se ajusta al último día de meses cortos. Dos pagos ajustados a la misma fecha mantienen identificadores independientes.

Configurar o editar el calendario no registra depósitos. «Registrar recibido» precarga la fecha esperada y el monto; confirma la fecha y el valor reales. Los cobros ya confirmados se conservan al cambiar el calendario y el mismo pago tiene el mismo identificador en ambos dispositivos.

La configuración se sincroniza en `usuarios/{uid}/configuracion/nomina`. Los cobros y sus eliminaciones se conservan en `ingresosRecibidos` (con marca de borrado). El PC guarda cambios pendientes sin internet y los reintenta; se aplica la revisión más reciente al reconciliar ediciones. Conecta la misma cuenta en ambos dispositivos. Las pruebas usan una nube simulada; no escriben en Firebase real.

Pruebas adicionales: `cd app` y `node tests/nomina.cjs`. Desde la raíz del repositorio: `dotnet run --project Pruebas/Nomina/Prueba.csproj -p:OutputPath=bin/VerificacionNomina/`. El programa usa bases temporales, una nube simulada y carga el formulario WPF fuera de pantalla.


## Recordatorio de nómina en el celular

Desde el día siguiente al pago esperado, la app muestra un diálogo al abrirla o volver a ella. Permite confirmar el monto y la fecha reales, o elegir **Recordarme mañana**. Confirmar usa el mismo identificador del calendario que el PC y Resumen para no duplicar ese pago y solo entonces suma el dinero al disponible. Los pagos ya confirmados no generan avisos.

Posponer guarda el siguiente día de calendario en el almacenamiento de ese navegador, separado por usuario y pago. Se conserva al cerrar y abrir la app; no se comparte entre dispositivos. Se revisa al regresar a la app y cada minuto mientras está abierta. Incluye los meses publicados desde el inicio del historial y el mes actual, para que un pago pendiente del último día del mes siga apareciendo el mes siguiente. No hay notificaciones push cuando la aplicación está cerrada.


## Meses visibles

El selector del escritorio y del celular permite el mes actual, el mes anterior únicamente desde el inicio del historial y un mes siguiente como proyección. El PC incluye `inicioRegistro` en cada resumen y no publica meses anteriores a ese inicio. El celular filtra documentos antiguos que puedan seguir en la nube y no ofrece meses pasados hasta recibir el inicio confirmado por el PC. Los datos históricos se conservan; esta regla limita las flechas de navegación. Al cambiar de mes, la ventana avanza con la fecha actual.


## Dictado al mantener pulsado

En Registrar se conserva el micrófono de un toque para empezar y otro para detener. Como alternativa, **Mantén pulsado para dictar** inicia al presionar y detiene al soltar, manteniendo el texto reconocido. Admite dedo, ratón y mantener Espacio o Enter desde el teclado. Soltar fuera del botón, cancelar el gesto, perder el foco o salir de la pantalla también detiene el reconocimiento. Si se soltó mientras se autorizaba el micrófono, no continúa escuchando al conceder el permiso: hay que mantener pulsado de nuevo. Si el navegador no admite reconocimiento de voz, se conserva la indicación para usar el micrófono del teclado.


## Actualizaciones con confirmación (1.9.0)

El celular conserva su versión hasta que la nueva descarga esté lista y el usuario acepte **Actualizar ahora**. **Más tarde** permite continuar y Configuración tiene búsqueda manual. Antes de recargar se guardan los campos pendientes de Registrar, Configuración y dinero recibido, separados por cuenta; se recuperan una sola vez. No se actualiza mientras se dicta o se lee un recibo. La foto original no se conserva tras recargar, pero sí los campos extraídos. El procedimiento de preparación, instalador de PC y publicación está en [Instalador/ACTUALIZACIONES.md](../Instalador/ACTUALIZACIONES.md).
