// Prueba cruzada: el Resumen que calcula el celular (src/app/nucleo/calculo.ts) debe ser igual al del PC
// (ResumenCelular.cs) con los mismos datos. El PC deja sus datos y su resultado en fixtures/resumen-pc.json
// (Pruebas/Nomina/ResumenCruzadoTest.cs).
const ts = require('typescript');
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');

const codigo = ts.transpileModule(fs.readFileSync('src/app/nucleo/calculo.ts', 'utf8'), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
}).outputText;
const modulo = { exports: {} };
new Function('require', 'exports', 'module', codigo)(require, modulo.exports, modulo);
const { construirResumen, desdeDocumentos, pagoDeCuota } = modulo.exports;

const ruta = path.join(__dirname, 'fixtures', 'resumen-pc.json');
const fixture = JSON.parse(fs.readFileSync(ruta, 'utf8'));

// Valores del formato REST de Firestore → valores como los entrega el SDK
const valor = (v) => v == null ? null
  : 'stringValue' in v ? v.stringValue
  : 'integerValue' in v ? Number(v.integerValue)
  : 'doubleValue' in v ? Number(v.doubleValue)
  : 'booleanValue' in v ? v.booleanValue
  : 'timestampValue' in v ? new Date(v.timestampValue)
  : null;
const colecciones = Object.fromEntries(Object.entries(fixture.colecciones).map(([nombre, docs]) =>
  [nombre, docs.map(d => ({ id: d.id, data: Object.fromEntries(Object.entries(d.fields).map(([k, v]) => [k, valor(v)])) }))]));
const gastos = fixture.gastos.map(g => ({ ...g, fecha: new Date(g.fecha), tarjetaId: g.tarjetaId ?? null }));
const datos = desdeDocumentos(colecciones, gastos);
const [y, m, d] = fixture.hoy.split('-').map(Number);
const hoy = new Date(y, m - 1, d);

const texto = (s) => String(s).replace(/[  \s]+/g, ' ').trim();
function igual(celular, pc, ruta) {
  if (pc === null || pc === undefined) { assert.ok(celular === null || celular === undefined || celular === 0 || celular === '', `${ruta}: el celular dio ${JSON.stringify(celular)} y el PC nada`); return; }
  if (typeof pc === 'number') { assert.ok(typeof celular === 'number' && Math.abs(celular - pc) < 1e-6, `${ruta}: celular ${celular} ≠ PC ${pc}`); return; }
  if (typeof pc === 'string') { assert.equal(texto(celular), texto(pc), `${ruta}`); return; }
  if (typeof pc === 'boolean') { assert.equal(celular, pc, `${ruta}`); return; }
  if (Array.isArray(pc)) {
    assert.ok(Array.isArray(celular), `${ruta}: el celular no dio una lista`);
    assert.equal(celular.length, pc.length, `${ruta}: ${celular.length} elementos en el celular y ${pc.length} en el PC\n celular: ${JSON.stringify(celular)}\n PC: ${JSON.stringify(pc)}`);
    pc.forEach((x, i) => igual(celular[i], x, `${ruta}[${i}]`));
    return;
  }
  for (const [k, v] of Object.entries(pc)) igual(celular?.[k], v, `${ruta}.${k}`);
}

let campos = 0;
for (const [mes, pc] of Object.entries(fixture.resumenes)) {
  const [ay, am] = mes.split('-').map(Number);
  const celular = construirResumen(datos, new Date(ay, am - 1, 1), hoy);
  delete pc.nombre; // nombre del mes para mostrar (no se compara el formato)
  pc.caja.gastosIncluidos.sort((a, b) => a.id.localeCompare(b.id));
  celular.caja.gastosIncluidos.sort((a, b) => a.id.localeCompare(b.id));
  igual(celular, pc, mes);
  campos += JSON.stringify(pc).length;
}
// Botón "Pagado" del celular: el reparto de la cuota es el mismo que hace el PC al pagarla
let probados = 0;
for (const [uid, pc] of Object.entries(fixture.pagosCuota)) {
  const credito = datos.creditos.find(c => c.uid === uid);
  const celular = pagoDeCuota(credito, datos.pagosCredito, hoy);
  if (pc === null) { assert.equal(celular, null, `Pago de ${credito.nombreCustom}: el PC no lo permite`); continue; }
  igual(celular, pc, `pagoCuota.${credito.nombreCustom}`);
  probados++;
}
assert.ok(probados > 0, 'Se probó al menos un pago de cuota');

// Que la prueba cubra los casos difíciles (no solo ceros)
const actual = fixture.resumenes[Object.keys(fixture.resumenes)[1]];
assert.ok(actual.porPagar.some(p => p.credito) && actual.vencidas.length > 0 && actual.gastos > 0 && actual.ahorroMes > 0,
  'Los datos de la prueba cubren cuotas de crédito, vencidas, gastos y ahorro');
console.log(`OK Resumen del celular igual al del PC: ${Object.keys(fixture.resumenes).length} meses (ingresos, gastos, disponible, vencidas, por pagar, tarjetas, deuda, ahorro y salidas de dinero) y el pago de la cuota de ${probados} crédito(s).`);
