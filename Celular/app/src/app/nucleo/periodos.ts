export function mesesVisibles(disponibles: string[], inicio: string | null, hoy: Date): string[] {
  const clave = (d: Date) => `${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,'0')}`;
  const actual = clave(hoy);
  const anterior = clave(new Date(hoy.getFullYear(), hoy.getMonth()-1, 1));
  const siguiente = clave(new Date(hoy.getFullYear(), hoy.getMonth()+1, 1));
  const permitidos = new Set([actual, siguiente]);
  // Sin el inicio confirmado por el PC, no ofrecer resúmenes históricos antiguos.
  if (inicio && anterior >= inicio) permitidos.add(anterior);
  return [...new Set(disponibles)].filter(m => permitidos.has(m)).sort();
}
