// ============================================================
//  Proyecto: Mis Finanzas
//  Autoria: Juan Almonacid
//  Copyright (c) 2026 Juan Almonacid. Todos los derechos reservados.
// ============================================================
// Entiende frases como "gasté 25 mil en almuerzo", "ayer 18 lucas en uber" o "mercado $85.000".
// Es la misma lógica de MisFinanzas/Modulos/GastoDiario.cs (InterpreteGasto): si se cambia una, cambiar la otra.

// (los números pegados a letras no cuentan: el "1" de "D1" no es un monto)
const MILLONES = /(?<![\p{L}\d])(\d+(?:[.,]\d+)?)\s*(millones|millón|millon|palos?)(?![\p{L}])/iu;
const MILES = /(?<![\p{L}\d])(\d+(?:[.,]\d+)?)\s*(mil|k|lucas?|barras?)(?![\p{L}])/iu;
const NUMERO = /\$?\s?(?<![\p{L}\d.,])(\d{1,3}(?:[.,]\d{3})+|\d+)(?![\p{L}\d])/giu;

const RELLENO = new Set([
  'gaste', 'gasto', 'gastamos', 'pague', 'pago', 'compre', 'compra', 'me', 'mi', 'en', 'de', 'del', 'por', 'para',
  'un', 'una', 'unos', 'unas', 'el', 'la', 'los', 'las', 'al', 'y', 'con', 'pesos', 'peso', 'hoy', 'ayer', 'antier',
  'anteayer', 'antes', 'valio', 'costo', 'salio', 'fueron', 'se', 'fue', 'que', 'lo', 'le',
]);

// Palabra clave (sin tildes) → categoría de gastos de la app
export const PALABRAS_CATEGORIA: [string, string[]][] = [
  ['Alimentación', ['almuerzo', 'almorce', 'desayuno', 'cena', 'comida', 'restaurante', 'mercado', 'supermercado', 'pan',
    'cafe', 'tinto', 'onces', 'domicilio', 'rappi', 'hamburguesa', 'pizza', 'empanada', 'fruta', 'carne',
    'mecato', 'gaseosa', 'helado', 'pollo', 'arepa', 'jugo', 'tienda', 'd1', 'ara', 'exito']],
  ['Transporte', ['uber', 'didi', 'taxi', 'bus', 'buses', 'transmilenio', 'sitp', 'metro', 'gasolina', 'tanqueo', 'tanqueada',
    'parqueadero', 'parqueo', 'peaje', 'pasaje', 'cabify', 'indrive', 'indriver', 'transporte', 'moto']],
  ['Entretenimiento', ['cine', 'netflix', 'spotify', 'fiesta', 'rumba', 'cerveza', 'cervezas', 'trago', 'tragos', 'concierto',
    'juego', 'videojuego', 'bar', 'salida', 'disney', 'boleta', 'boletas', 'paseo']],
  ['Médicos', ['drogueria', 'farmacia', 'medico', 'medicina', 'medicamento', 'cita', 'eps', 'dentista', 'odontologo', 'examen']],
  ['Educación', ['curso', 'libro', 'libros', 'universidad', 'colegio', 'matricula', 'cuaderno', 'clase', 'clases']],
  ['Servicios', ['luz', 'energia', 'internet', 'celular', 'recarga', 'plan', 'recibo', 'gas']],
  ['Hogar', ['arriendo', 'aseo', 'mueble', 'ferreteria', 'hogar', 'detergente', 'lavanderia', 'casa']],
];

export interface GastoInterpretado {
  monto: number | null;
  fecha: Date; // solo el día
  descripcion: string;
  categoria: string;
}

export const sinTildes = (t: string) => t.normalize('NFD').replace(/\p{Mn}/gu, '');

// ---------- ¿Cómo pagaste? ("con la visa", "en efectivo") ----------
export interface TarjetaBasica { id: number; nombre: string; }

// Pagos que no son con tarjeta de crédito
const SIN_TARJETA = new Set(['efectivo', 'cash', 'debito', 'nequi', 'daviplata', 'transferencia', 'pse']);
// Palabras genéricas que no identifican una tarjeta concreta
const GENERICAS = new Set(['tarjeta', 'credito', 'de', 'la', 'el', 'mi', 'con']);

/**
 * Medio de pago dicho en la frase. id = tarjeta elegida (null = efectivo/débito);
 * dicho = la frase lo menciona; palabras = las que se quitan de la descripción.
 */
export function detectarPago(frase: string, tarjetas: TarjetaBasica[]): { dicho: boolean; id: number | null; palabras: string[] } {
  const original = (frase || '').split(/[^\p{L}\p{N}]+/u).filter(Boolean);
  const bajas = original.map((p) => sinTildes(p.toLowerCase()));
  const nada = { dicho: false, id: null, palabras: [] as string[] };
  if (!bajas.length) return nada;

  const sin = original.filter((_, i) => SIN_TARJETA.has(bajas[i]));
  if (sin.length) return { dicho: true, id: null, palabras: sin };

  // Por el nombre de la tarjeta ("visa", "bancolombia", "mastercard"…)
  const coincide = tarjetas.map((t) => {
    const fichas = sinTildes(t.nombre.toLowerCase()).split(/[^\p{L}\p{N}]+/u).filter((f) => f.length >= 3 && !GENERICAS.has(f));
    return { t, usadas: original.filter((_, i) => fichas.includes(bajas[i])) };
  }).filter((x) => x.usadas.length);
  const genericas = original.filter((_, i) => bajas[i] === 'tarjeta' || bajas[i] === 'credito');
  if (coincide.length === 1) return { dicho: true, id: coincide[0].t.id, palabras: [...coincide[0].usadas, ...genericas] };

  // "con tarjeta" / "con la de crédito": si hay una sola, esa
  if (genericas.length && tarjetas.length === 1) return { dicho: true, id: tarjetas[0].id, palabras: genericas };
  return nada;
}

/** Quita de la descripción las palabras del medio de pago ("Mercado visa" → "Mercado") */
export function quitarPalabras(descripcion: string, palabras: string[]): string {
  if (!palabras.length) return descripcion;
  const fuera = new Set(palabras.map((p) => sinTildes(p.toLowerCase())));
  const resto = descripcion.split(/\s+/).filter((p) => p && !fuera.has(sinTildes(p.toLowerCase()))).join(' ');
  return resto ? resto[0].toUpperCase() + resto.slice(1) : '';
}

// ---------- Cuotas de una compra con tarjeta ("a 6 cuotas", "en 12 cuotas", "a una cuota", "diferido a 3 meses") ----------
const NUMEROS_CUOTAS: Record<string, number> = {
  un: 1, una: 1, uno: 1, dos: 2, tres: 3, cuatro: 4, cinco: 5, seis: 6, siete: 7, ocho: 8, nueve: 9, diez: 10,
  once: 11, doce: 12, dieciocho: 18, veinticuatro: 24, 'treinta y seis': 36,
};
const NUM_CUOTA = `(\\d{1,2}|${Object.keys(NUMEROS_CUOTAS).sort((a, b) => b.length - a.length).join('|')})`;
// "6 cuotas" sirve solo; "3 meses" solo si dice "a/en/diferido a" (para no confundir con "hace 3 meses")
const CUOTAS_RE = [
  new RegExp(`(?:\\b(?:diferid[oa]\\s+)?(?:a|en|de|con)\\s+)?${NUM_CUOTA}(?:\\s+sola)?\\s+cuotas?(?![\\p{L}])`, 'iu'),
  new RegExp(`\\b(?:diferid[oa]\\s+)?(?:a|en)\\s+${NUM_CUOTA}\\s+mes(?:es)?(?![\\p{L}])`, 'iu'),
  new RegExp(`\\bdiferid[oa]\\s+a\\s+${NUM_CUOTA}(?![\\p{L}\\d])`, 'iu'),
];

/** Cuotas dichas en la frase (null si no las dice) y la frase sin esas palabras (para no tomar ese número como el valor) */
export function detectarCuotas(frase: string): { cuotas: number | null; resto: string } {
  const texto = frase || '';
  const bajo = sinTildes(texto.toLowerCase()); // mismas posiciones: las tildes de estas palabras no cambian el largo
  for (const re of CUOTAS_RE) {
    const m = bajo.match(re);
    if (!m || m.index === undefined) continue;
    const n = /^\d+$/.test(m[1]) ? Number(m[1]) : NUMEROS_CUOTAS[m[1].replace(/\s+/g, ' ')];
    if (!n || n < 1 || n > 48) continue;
    const resto = (texto.slice(0, m.index) + ' ' + texto.slice(m.index + m[0].length)).replace(/\s+/g, ' ').trim();
    return { cuotas: n, resto };
  }
  return { cuotas: null, resto: texto };
}

/** Cuota fija aproximada de una compra a n cuotas con la tasa efectiva anual (%) de la tarjeta; a 1 cuota, sin intereses */
export function estimarCuotas(monto: number, cuotas: number, tasaEA: number): { cuota: number; intereses: number } {
  const n = Math.max(1, Math.round(cuotas));
  const r = tasaEA > 0 ? Math.pow(1 + tasaEA / 100, 1 / 12) - 1 : 0;
  if (n === 1 || r <= 0) return { cuota: Math.round(monto / n), intereses: 0 };
  const cuota = (monto * r) / (1 - Math.pow(1 + r, -n));
  return { cuota: Math.round(cuota), intereses: Math.max(0, Math.round(cuota * n - monto)) };
}

// ---------- Gasto directo de un vehículo (igual que InterpreteGasto.DetectarVehiculo en C#) ----------
export interface VehiculoBasico { id: number; nombre: string; tipo: string; placa: string; }

// Siempre son del vehículo (aunque no se diga cuál)
const DEL_VEHICULO = new Set(['soat', 'tecnomecanica', 'tecno', 'llanta', 'llantas', 'rin', 'rines', 'repuesto', 'repuestos', 'aceite',
  'filtro', 'filtros', 'bateria', 'freno', 'frenos', 'pastillas', 'taller', 'mecanico', 'latoneria', 'alineacion', 'balanceo', 'embrague',
  'clutch', 'bujia', 'bujias', 'amortiguador', 'amortiguadores', 'suspension', 'cadena', 'kit', 'correa', 'radiador']);
// Solo si se nombra el vehículo ("impuesto del carro", "seguro de la moto")
const DEL_VEHICULO_SI_SE_NOMBRA = new Set(['impuesto', 'impuestos', 'seguro', 'multa', 'comparendo', 'mantenimiento', 'revision', 'pintura', 'arreglo']);
// Nunca van al vehículo (son gasto de transporte del día a día)
const NO_DEL_VEHICULO = new Set(['gasolina', 'tanqueo', 'tanqueada', 'tanquear', 'peaje', 'parqueadero', 'parqueo', 'lavada', 'lavado']);
const PALABRAS_VEHICULO = ['carro', 'moto', 'auto', 'camioneta', 'vehiculo'];

/** ¿Es gasto directo de un vehículo? y ¿de cuál? (id null = es de un vehículo pero hay que elegir cuál) */
export function detectarVehiculo(frase: string, vehiculos: VehiculoBasico[]): { esDeVehiculo: boolean; id: number | null } {
  const texto = sinTildes((frase || '').toLowerCase());
  if (!vehiculos.length || !texto.trim()) return { esDeVehiculo: false, id: null };
  const palabras = new Set(texto.split(/[^\p{L}\p{N}]+/u).filter(Boolean));
  if ([...NO_DEL_VEHICULO].some((p) => palabras.has(p))) return { esDeVehiculo: false, id: null };

  const compacto = texto.replace(/\s/g, '');
  let nombrados = vehiculos.filter((v) =>
    (v.nombre.trim() && palabras.has(sinTildes(v.nombre.trim().toLowerCase()))) ||
    (v.placa.trim().length >= 5 && compacto.includes(sinTildes(v.placa.trim().toLowerCase()))));
  if (!nombrados.length) {
    const tipo = palabras.has('moto') ? 'moto' : ['carro', 'auto', 'camioneta', 'vehiculo'].some((p) => palabras.has(p)) ? 'carro' : null;
    if (tipo) nombrados = vehiculos.filter((v) => sinTildes(v.tipo.toLowerCase()) === tipo || (tipo === 'carro' && palabras.has('vehiculo')));
  }
  const seNombra = nombrados.length > 0 || PALABRAS_VEHICULO.some((p) => palabras.has(p));
  const esDeVehiculo = [...DEL_VEHICULO].some((p) => palabras.has(p)) || (seNombra && [...DEL_VEHICULO_SI_SE_NOMBRA].some((p) => palabras.has(p)));
  if (!esDeVehiculo) return { esDeVehiculo: false, id: null };
  if (nombrados.length === 1) return { esDeVehiculo: true, id: nombrados[0].id };
  if (!nombrados.length && vehiculos.length === 1) return { esDeVehiculo: true, id: vehiculos[0].id };
  return { esDeVehiculo: true, id: null };
}

const aNumero = (v: string) => Number(v.replace(/[.,]/g, ''));
const aDecimal = (v: string) => Number(v.replace(',', '.'));

export function interpretar(frase: string): GastoInterpretado {
  const hoy = new Date();
  hoy.setHours(0, 0, 0, 0);
  const r: GastoInterpretado = { monto: null, fecha: hoy, descripcion: '', categoria: '' };
  let texto = (frase || '').trim();
  if (!texto) return r;

  // 1. Monto (y se quita de la frase para dejar la descripción)
  let m: RegExpMatchArray | null = texto.match(MILLONES);
  if (m) r.monto = aDecimal(m[1]) * 1_000_000;
  else if ((m = texto.match(MILES))) r.monto = aDecimal(m[1]) * 1_000;
  else {
    // Sin "mil" ni "millones": si hay varios números ("2 boletas 40000") el monto es el mayor
    const todos = [...texto.matchAll(NUMERO)];
    m = todos.sort((a, b) => aNumero(b[1]) - aNumero(a[1]))[0] ?? null;
    if (m) r.monto = aNumero(m[1]);
  }
  if (m && m.index !== undefined) texto = texto.slice(0, m.index) + ' ' + texto.slice(m.index + m[0].length);
  if (r.monto !== null) r.monto = Math.round(r.monto);

  // 2. Fecha
  const bajo = sinTildes(texto.toLowerCase());
  if (/\b(antier|anteayer|antes de ayer)\b/.test(bajo)) r.fecha = new Date(hoy.getTime() - 2 * 864e5);
  else if (/\bayer\b/.test(bajo)) r.fecha = new Date(hoy.getTime() - 864e5);

  // 3. Descripción: lo que queda sin palabras de relleno
  const palabras = texto.split(/[^\p{L}\p{N}]+/u).filter((p) => p && !RELLENO.has(sinTildes(p.toLowerCase())));
  const desc = palabras.join(' ');
  r.descripcion = desc ? desc[0].toUpperCase() + desc.slice(1) : '';

  // 4. Categoría por palabras clave
  const claves = new Set(palabras.map((p) => sinTildes(p.toLowerCase())));
  const cat = PALABRAS_CATEGORIA.find(([, lista]) => lista.some((k) => claves.has(k)));
  r.categoria = cat ? cat[0] : '';
  return r;
}
