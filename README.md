# Mis Finanzas

Aplicación de escritorio para Windows para llevar las finanzas personales: ingresos, gastos del mes, créditos, vehículos, inversiones y metas de ahorro. Todo se guarda en una base de datos local, en tu propio equipo.

## Ejecutarla desde la terminal

Requisito: [SDK de .NET 8](https://dotnet.microsoft.com/download/dotnet/8.0) o superior, en Windows.

Los comandos se ejecutan desde la carpeta donde está este README, la que contiene `MisFinanzas.sln`. Si la terminal está en la carpeta de afuera (`Documents\MisFinanzas`), entra primero:

```powershell
cd MisFinanzas
dotnet run --project MisFinanzas/MisFinanzas.csproj
```

La primera vez descarga los paquetes NuGet y compila, así que tarda un poco más.

> Si aparece *"MSB1009: El archivo de proyecto no existe"*, la terminal está en otra carpeta. Revisa con `ls` que se vea `MisFinanzas.sln`.

Otros comandos útiles (desde la misma carpeta):

| Para… | Comando |
|---|---|
| Solo compilar | `dotnet build MisFinanzas.sln` |
| Ejecutar sin volver a compilar | `dotnet run --no-build --project MisFinanzas/MisFinanzas.csproj` |
| Abrir el ejecutable ya compilado | `MisFinanzas\bin\Debug\net8.0-windows\win-x64\MisFinanzas.exe` |
| Generar la versión para instalar | `dotnet publish MisFinanzas/MisFinanzas.csproj -c Release -o MisFinanzas/publish` |

> Si la compilación falla con *"The process cannot access the file … MisFinanzas.exe"*, la app está abierta: ciérrala y vuelve a compilar.

### Instalador

El instalador se arma con [Inno Setup](https://jrsoftware.org/isinfo.php) a partir de la carpeta `publish`:

1. Ejecuta el comando `dotnet publish` de la tabla anterior.
2. Abre `Instalador/MisFinanzas.iss` en Inno Setup y compílalo (o usa `iscc Instalador\MisFinanzas.iss`).
3. El instalador queda en `Instalador/Salida/MisFinanzas_Setup_<versión>.exe`.

La publicación es *self-contained*: quien instala la app no necesita tener .NET instalado.

## Dónde quedan los datos

- Base de datos: `Documentos\MisFinanzas_Real.db` (SQLite).
- Cada vez que una versión nueva cambia la estructura de la base de datos, primero se guarda una copia automática junto a ella: `MisFinanzas_Real.respaldo-AAAAMMDD-HHMMSS.db`.

Borrar `bin`, `obj` o `publish` no afecta tus datos: esas carpetas se regeneran al compilar.

## Qué hace cada pantalla

| Pantalla | Para qué sirve |
|---|---|
| **Dashboard** | Resumen del mes: ingresos, gastos, disponible, próximas fechas de pago, gastos por categoría, metas y proyección del mes siguiente. |
| **Ingresos** | Salario mensual, otros ingresos con etiqueta propia (únicos o mensuales) y metas de ahorro con abonos. |
| **Cuentas** | Gastos que se repiten cada mes el mismo día, una sola vez o una vez al año (impuestos, declaración de renta). Se marcan como pagados mes a mes registrando el monto real. Incluye historial y proyección/predicción de cada gasto. |
| **Créditos** | Cada crédito con su tabla de amortización: al pagar la cuota del mes se reparte entre interés y capital y baja el saldo que se debe. Permite abonos extra a capital y deshacer pagos. |
| **Vehículos** | Cada vehículo con sus gastos (concepto, valor y fecha) y el total invertido en él. |
| **Inversiones** | Monto invertido en cada inversión, con abonos. |

Al abrir la app, un recordatorio muestra los gastos del mes sin pagar que vencen hoy o ya vencieron, para marcarlos como pagados ahí mismo.

## Cómo está hecha

### Tecnología

- **WPF sobre .NET 8** (`net8.0-windows`), escrita en C# y XAML.
- **Entity Framework Core 8 + SQLite** para guardar los datos.
- Sin librerías de interfaz externas: estilos, íconos (vectoriales, en XAML) y controles son propios.

### Organización del código

```
MisFinanzas/
├── App.xaml, MainWindow.xaml   Ventana principal y menú lateral
├── Estilos/AppTheme.xaml       Tema global: colores, campos, botones, tarjetas
├── Controles/                  Controles reutilizables (Toast, ScrollPagina)
├── Modulos/                    Una vista por pantalla + su lógica
│   ├── *View.xaml(.cs)         Pantallas (Dashboard, Ingresos, Cuentas, Créditos…)
│   ├── AppDbContext.cs         Tablas de la base de datos (EF Core)
│   ├── EsquemaBD.cs            Actualización automática de la base de datos
│   ├── CuentasMensuales.cs     Recurrencia de gastos e ingresos y pagos por mes
│   ├── Amortizacion.cs         Cálculo de créditos (cuota fija)
│   ├── GastoDiario.cs          Gastos del día a día e intérprete de frases ("gasté 25 mil en almuerzo")
│   ├── Analisis.cs             Historial, predicción y proyección
│   └── Etiquetas.cs            Categorías de gastos e ingresos con color
├── Periodo.cs                  Manejo del mes ("yyyy-MM")
├── NumeroHelper.cs             Formato y lectura de valores en pesos
└── Recursos/app.ico            Ícono de la app
Instalador/MisFinanzas.iss      Script de Inno Setup
Herramientas/                   Scripts de mantenimiento (PowerShell)
├── GenerarIcono.ps1            Regenera Recursos/app.ico
├── AgregarEncabezados.ps1      Agrega el encabezado de autoría a .cs y .xaml
└── RepararEncoding.ps1         Corrige tildes dañadas (mojibake) en .cs y .xaml
```

Las pantallas usan *code-behind* (la lógica vive en el `.xaml.cs` de cada vista). Las listas que comparten varias pantallas (créditos, metas, vehículos) son colecciones estáticas, para que un cambio en una pantalla se refleje en las demás, por ejemplo en el Dashboard.

### Decisiones principales

**La base de datos se actualiza sola.** `EnsureCreated()` de EF Core solo crea la base cuando no existe; no agrega tablas ni columnas nuevas a una base que ya tiene datos. Por eso, al iniciar, `EsquemaBD.Actualizar` compara el modelo con la base real y:

- crea las tablas y columnas que falten, con valores por defecto;
- elimina columnas que ya no se usan, rescatando antes sus datos si hace falta;
- borra registros huérfanos (gastos de un vehículo que ya no existe, pagos de una cuenta borrada…);
- completa los datos de las columnas nuevas (por ejemplo, el día de pago a partir de la fecha original);
- hace todo dentro de una transacción y después de un respaldo (`VACUUM INTO`).

Para agregar un campo nuevo basta con declararlo en la clase del modelo.

**Los gastos se registran una vez y se repiten.** Cada gasto (`Movimiento` de tipo `Cuenta`) tiene una frecuencia (Mensual, Única o Anual), un día de pago y, si se deja de pagar, un mes de fin. El estado de pago no está en el gasto sino en la tabla `PagosCuenta`, una fila por mes pagado con el **monto real** pagado. Así cada mes tiene su propio historial y borrar un gasto "desde este mes" conserva los meses anteriores.

**Predicción y proyección.** La predicción de cada gasto es el promedio de los últimos 6 pagos reales (si no hay pagos, su valor registrado). La proyección de los próximos 12 meses suma esas predicciones, los ingresos que aplican cada mes y las cuotas de los créditos según su plan de pagos.

**Créditos con cuota fija (sistema francés).** La tasa se registra como efectiva anual (E.A.) y se convierte a mensual: `(1 + EA)^(1/12) − 1`. Cada cuota paga primero el interés del mes sobre el saldo y el resto baja el capital; la última solo cobra lo que falta. Cada pago queda en la tabla `PagosCredito` con interés, capital, saldo antes y después, y cuántos meses redujo el plazo, para poder deshacerlo exactamente. Los abonos extra van completos a capital y acortan el plazo manteniendo la cuota.

**Interfaz compacta.** El tema global (`AppTheme.xaml`) define tamaños pensados para ventanas medianas (campos de 34 px, tarjetas con poco relleno). Cada pantalla se desplaza completa con la rueda del mouse (`ScrollPagina`), y las tablas internas, como el plan de pagos, reciben la rueda cuando el mouse está sobre ellas.

## Limitaciones conocidas

- Los montos se ven en dos formatos según dónde aparecen: `$1,526,000` en tablas y `$ 1.526.000` en totales y mensajes.
- El historial de meses anteriores usa el salario actual (no guarda cambios de salario por mes).
- El recordatorio al abrir la app muestra gastos, no cuotas de créditos.

## Autor

© 2026 Juan Almonacid. Todos los derechos reservados.
