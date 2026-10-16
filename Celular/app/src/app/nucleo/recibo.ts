// ============================================================
//  Proyecto: Mis Finanzas
//  Autoria: Juan Almonacid
//  Copyright (c) 2026 Juan Almonacid. Todos los derechos reservados.
// ============================================================
// Lee la foto de un recibo en el celular (OCR con Tesseract, sin enviar la foto a ningún servidor)
// y encuentra el valor total y el nombre del comercio. La foto no se guarda.
import { sinTildes } from './interprete';

export interface DatosRecibo {
  total: number | null;
  comercio: string;   // primera línea con nombre (para la descripción)
  texto: string;      // todo lo leído (para adivinar la categoría)
}

// Palabras de la línea del total, de la más segura a la menos
const CLAVES_TOTAL = ['TOTAL A PAGAR', 'VALOR A PAGAR', 'NETO A PAGAR', 'TOTAL PAGADO', 'VALOR TOTAL', 'TOTAL COP', 'GRAN TOTAL', 'A PAGAR', 'TOTAL'];
// Líneas que tienen montos pero no son el total
const NO_TOTAL = ['SUBTOTAL', 'SUB TOTAL', 'SUB-TOTAL', 'TOTAL ITEMS', 'TOTAL ARTICULOS', 'TOTAL UNIDADES', 'CANT', 'EFECTIVO',
  'CAMBIO', 'RECIBIDO', 'VUELTO', 'IVA', 'IMPUESTO', 'IMPOCONSUMO', 'BASE', 'DESCUENTO', 'PROPINA', 'NIT', 'TEL', 'FACTURA', 'RESOLUCION'];

// Montos: "45.900", "45,900", "$ 45.900", "45900", "45.900,00", "45,900.00", "45900.00"
const MONTO = /\$?\s?(\d{1,3}(?:[.,\s]\d{3})+(?:[.,]\d{2})?|\d{3,}(?:[.,]\d{2})?)(?!\d)/g;

/** "45.900" → 45900 · "45.900,00" → 45900 · "45900.00" → 45900 · "1.250.000" → 1250000 */
export function leerMonto(texto: string): number | null {
  let t = texto.replace(/[$\s]/g, '');
  // Separador + 2 dígitos al final son centavos (se quitan); + 3 dígitos son miles
  if (/[.,]\d{2}$/.test(t)) t = t.slice(0, -3);
  const n = Number(t.replace(/[.,]/g, ''));
  return Number.isFinite(n) && n > 0 ? n : null;
}

function montosDe(linea: string): number[] {
  return [...linea.matchAll(MONTO)].map((m) => leerMonto(m[1])).filter((n): n is number => n !== null && n >= 100 && n < 100_000_000);
}

/** Busca el total y el comercio en el texto leído del recibo */
export function extraerDatos(texto: string): DatosRecibo {
  const lineas = texto.split(/\r?\n/).map((l) => l.trim()).filter(Boolean);
  const normal = lineas.map((l) => sinTildes(l.toUpperCase()));

  // 1. La línea del total (la clave más segura gana; si se repite, la última, que suele ser la final)
  let total: number | null = null;
  for (const clave of CLAVES_TOTAL) {
    for (let i = normal.length - 1; i >= 0 && total === null; i--) {
      const l = normal[i];
      if (!l.includes(clave)) continue;
      if (clave === 'TOTAL' && NO_TOTAL.some((n) => l.includes(n))) continue;
      if (clave !== 'TOTAL' && ['EFECTIVO', 'CAMBIO'].some((n) => l.includes(n))) continue;
      // El valor puede estar en la misma línea o en la siguiente
      const montos = montosDe(l).length ? montosDe(l) : montosDe(normal[i + 1] ?? '');
      if (montos.length) total = montos[montos.length - 1];
    }
    if (total !== null) break;
  }

  // 2. Si no hay línea de total: el monto más grande que no sea efectivo, cambio, NIT o teléfono
  if (total === null) {
    const candidatos = normal.filter((l) => !NO_TOTAL.some((n) => l.includes(n))).flatMap(montosDe);
    total = candidatos.length ? Math.max(...candidatos) : null;
  }

  // 3. Comercio: la primera línea con al menos 3 letras que no sea NIT, dirección o fecha
  const comercio = lineas.find((l, i) => {
    const n = normal[i];
    const letras = (n.match(/[A-Z]/g) ?? []).length;
    return letras >= 3 && letras >= n.replace(/\s/g, '').length * 0.6 &&
      !/(NIT|FACTURA|TEL|DIRECCION|CALLE|CRA|CARRERA|FECHA|HORA|CAJA|RESOLUCION|REGIMEN)/.test(n);
  }) ?? '';

  return { total, comercio: titulo(comercio), texto };
}

const titulo = (t: string) => t.toLowerCase().replace(/\s+/g, ' ').replace(/(^|\s)\p{L}/gu, (c) => c.toUpperCase()).slice(0, 60);

// ---------- OCR en el celular ----------

/** Achica la foto, la pasa a grises y sube el contraste: el OCR es más rápido y acierta más */
async function prepararImagen(archivo: File): Promise<HTMLCanvasElement> {
  const imagen = await createImageBitmap(archivo);
  const escala = Math.min(1, 1600 / Math.max(imagen.width, imagen.height));
  const lienzo = document.createElement('canvas');
  lienzo.width = Math.round(imagen.width * escala);
  lienzo.height = Math.round(imagen.height * escala);
  const ctx = lienzo.getContext('2d')!;
  ctx.filter = 'grayscale(1) contrast(1.4)';
  ctx.drawImage(imagen, 0, 0, lienzo.width, lienzo.height);
  imagen.close();
  return lienzo;
}

/**
 * Lee el recibo. La librería de OCR se descarga solo al usarla (no pesa al abrir la app);
 * la primera vez también baja el idioma español (~2 MB), que queda guardado en el celular.
 * alAvanzar recibe de 0 a 1.
 */
export async function leerRecibo(archivo: File, alAvanzar?: (p: number) => void): Promise<DatosRecibo> {
  const [modulo, lienzo] = await Promise.all([import('tesseract.js'), prepararImagen(archivo)]);
  // tesseract.js es CommonJS: al cargarlo bajo demanda sus funciones pueden venir dentro de "default"
  const createWorker = (modulo.createWorker ?? (modulo as any).default?.createWorker) as typeof modulo.createWorker;
  const worker = await createWorker('spa', 1, {
    logger: (m: { status: string; progress: number }) => {
      if (m.status === 'recognizing text') alAvanzar?.(0.3 + m.progress * 0.7);
      else alAvanzar?.(Math.min(0.3, m.progress * 0.3));
    },
  });
  try {
    const { data } = await worker.recognize(lienzo);
    return extraerDatos(data.text);
  } finally {
    await worker.terminate();
  }
}
