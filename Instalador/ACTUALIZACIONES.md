# Publicación y actualizaciones

## Qué quedó implementado

- PC: Velopack 1.2.161. Al abrir busca en `https://mis-finanzas-4ea0b.web.app/actualizaciones/windows` y vuelve a buscar cada 6 horas mientras está abierta y muestra **Actualizar ahora / Más tarde** cuando hay una versión superior. Cuenta muestra la versión instalada y, solo si hay una nueva, el botón **Descargar**. Descarga con progreso, verifica el paquete mediante Velopack, crea una copia SQLite consistente y luego instala y reinicia. La aplicación no se actualiza al iniciar sin aceptación (`SetAutoApplyOnStartup(false)`).
- Celular: el worker descarga versiones nuevas y ofrece el aviso sin recargar automáticamente. Configuración muestra la versión y permite buscar manualmente. Antes de recargar, guarda los campos de Registrar, el formulario de nómina y el de ingresos recibidos en `sessionStorage`, separados por usuario; se restauran una sola vez. No recarga si está dictando, leyendo una foto o no puede guardar el borrador. La foto original no se conserva, pero sí los campos extraídos.
- Si falla la consulta, no se presenta el resultado como «actualizado». Si falla la descarga o el respaldo, se conserva la instalación vigente.

## Primera instalación de PC

Las copias antiguas instaladas con Inno Setup y las que se ejecutan desde el IDE no tienen Velopack. Deben instalar una vez `MisFinanzas-win-Setup.exe`. Las actualizaciones posteriores se aceptan desde la aplicación. La instalación nueva es por usuario; si queda una instalación antigua, puede retirarse con su desinstalador para evitar accesos directos duplicados.

La base permanece en Documentos (`MisFinanzas_Real.db`) y la sesión del celular en AppData. El instalador no debe copiar ni borrar esos archivos. Antes de actualizar se crea una copia `MisFinanzas.antes-de-actualizar-*.db` junto a la base. No se borran respaldos automáticamente. El actualizador impide reiniciar desde formularios de otras secciones y pide ir a Cuenta después de guardarlos; en Cuenta hay que guardar o cancelar la edición de nómina.

## Preparar un lanzamiento

Desde la raíz del repositorio, con .NET 8 y Node/npm instalados:

```powershell
.\Instalador\Preparar-Version.ps1 -Version 1.9.0 -Notas .\Instalador\Novedades-1.9.0.md
```

Este comando:

1. Actualiza las versiones del ensamblado, celular y worker y agrega las novedades.
2. Restaura la herramienta local fijada por `.config/dotnet-tools.json`.
3. Publica el PC autocontenido para Windows x64 y genera el instalador y paquete completo (sin deltas).
4. Conserva los artefactos en `Instalador/Salida/Actualizaciones/<version>` y copia los archivos públicos a `Celular/app/public/actualizaciones/windows`.
5. Instala las dependencias del celular según el lockfile, ejecuta sus pruebas y compila. Los paquetes de Windows se sirven como archivos estáticos; no se descargan al instalar la PWA.

No publica por defecto. Las carpetas de salida y los paquetes públicos generados se excluyen de Git. Usa una versión superior para cada lanzamiento; nunca sustituyas un paquete ya publicado con el mismo número.

Para firmar el instalador con un certificado ya instalado en el almacén de Windows:

```powershell
.\Instalador\Preparar-Version.ps1 -Version 1.9.1 -Notas .\Instalador\Novedades-1.9.1.md -HuellaCertificado HUELLA_DEL_CERTIFICADO
```

No se creó ni se compró ningún certificado. Sin esta opción, el instalador se genera sin firma digital y Windows puede pedir confirmación al ejecutarlo. Los paquetes y su índice se obtienen por HTTPS desde el proyecto propio de Firebase.

## Publicar los archivos ya preparados

Después de comprobar el instalador y los archivos generados, inicia sesión con una cuenta autorizada en ese proyecto y despliega desde `Celular`:

```powershell
npx --yes --package=firebase-tools@15.34.0 firebase login
npx --yes --package=firebase-tools@15.34.0 firebase deploy --only "hosting,firestore:rules" --project mis-finanzas-4ea0b
```

Debe conservarse el contenido previo de `app/public/actualizaciones/windows` en la máquina de publicación al preparar una versión solo de celular. Después de compilar, verifica que `app/dist/mis-finanzas/browser/actualizaciones/windows/releases.win.json` y sus paquetes estén presentes antes de desplegar hosting; desplegar una carpeta sin ellos retiraría el canal de PC de hosting.

Para preparar y publicar en una misma ejecución futura, añade `-Publicar` a `Preparar-Version.ps1`. `-SoloCelular` evita volver a empaquetar el PC. La publicación cambia la versión disponible para todos los usuarios; no instala el PC local automáticamente.

## Comprobaciones

- Pruebas de PC: `dotnet run --project Pruebas/Nomina/Prueba.csproj -p:OutputPath=bin/VerificacionActualizaciones/`. Usan bases temporales; verifican aceptación, posponer, errores de red y descarga, doble clic, respaldo fallido y copia consistente incluso en WAL.
- Pruebas de celular: `npm run test:caja` dentro de `Celular/app`. Incluyen aviso sin recarga automática, posponer, búsqueda manual, borrador por cuenta y no recargar si falla guardarlo.
- Prueba final en dos instalaciones de prueba: instalar 1.9.0, preparar y servir una versión superior, aceptar la actualización y comprobar reinicio y conservación de datos. Esa prueba debe hacerse con una base de prueba y un canal separado, sin publicar una versión ficticia para usuarios reales. También se verifica el paquete real localmente con TestVelopackLocator: detecta 1.9.0 desde 1.8.0, descarga y comprueba la integridad, ignora la misma versión y rechaza un paquete alterado.

Documentación de referencia: [Velopack](https://docs.velopack.io/integrating/overview) y [worker de Angular](https://angular.dev/ecosystem/service-workers/communications).
