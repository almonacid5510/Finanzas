// ============================================================
//  Proyecto: Mis Finanzas
//  Autoria: Juan Almonacid
//  Copyright (c) 2026 Juan Almonacid. Todos los derechos reservados.
// ============================================================
// Categorías de gastos: los mismos colores de MisFinanzas/Modulos/Etiquetas.cs y un ícono por categoría.
import { NombreIcono } from '../compartido/icono';

export const CATEGORIAS = ['Alimentación', 'Entretenimiento', 'Hogar', 'Transporte', 'Vehículos', 'Médicos', 'Servicios', 'Educación', 'Otros'];

// [texto, fondo, barra] — fijos para las conocidas y estables por nombre para las propias
const PALETA: [string, string, string][] = [
  ['#1D4FB8', '#EAF0FD', '#1D5BD8'], ['#075E46', '#E7F5EF', '#0B7A5C'], ['#93370D', '#FEF6E7', '#E08A1E'],
  ['#5B34B5', '#F5F3FF', '#6941C6'], ['#B42318', '#FEF3F2', '#D92D20'], ['#0E6E6E', '#E6F6F6', '#14928F'],
  ['#854A0E', '#FEF7C3', '#CA8504'], ['#9E165F', '#FDF2FA', '#DD2590'],
];
const FIJOS: Record<string, number> = {
  'hogar': 0, 'freelance': 1, 'alimentación': 2, 'impuestos': 3, 'créditos': 3,
  'médicos': 4, 'servicios': 5, 'transporte': 6, 'entretenimiento': 7,
};

export interface ColorCategoria { texto: string; fondo: string; barra: string; }

export function colorCategoria(nombre: string | null | undefined): ColorCategoria {
  const n = (nombre || '').trim().toLowerCase();
  if (!n || n === 'otros' || n === 'sin categoría') return { texto: '#4B5565', fondo: '#EEF1F5', barra: '#8A94A3' };
  let i = FIJOS[n];
  if (i === undefined) {
    // Mismo cálculo que en C#: hash = 17; hash = hash * 31 + c (entero de 32 bits); |hash % 8|
    let h = 17;
    for (let k = 0; k < n.length; k++) h = (Math.imul(h, 31) + n.charCodeAt(k)) | 0;
    i = Math.abs(h % PALETA.length);
  }
  const [texto, fondo, barra] = PALETA[i];
  return { texto, fondo, barra };
}

export function iconoCategoria(nombre: string | null | undefined): NombreIcono {
  switch ((nombre || '').trim().toLowerCase()) {
    case 'alimentación': return 'comida';
    case 'transporte': return 'bus';
    case 'entretenimiento': return 'play';
    case 'médicos': return 'salud';
    case 'educación': return 'educacion';
    case 'servicios': return 'rayo';
    case 'hogar': return 'casa';
    case 'vehículos': return 'carro';
    default: return 'etiqueta';
  }
}

export const mostrarCategoria = (nombre: string | null | undefined) => (nombre || '').trim() || 'Sin categoría';
