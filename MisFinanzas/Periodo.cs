// ============================================================
//  Proyecto: Mis Finanzas
//  Autoria: Juan Almonacid
//  Copyright (c) 2026 Juan Almonacid. Todos los derechos reservados.
// ============================================================
using System;
using System.Globalization;

namespace MisFinanzas
{
    /// <summary>
    /// Utilidades del periodo mensual con el que la app agrupa cuentas y pagos de créditos.
    /// </summary>
    public static class Periodo
    {
        /// <summary>Clave del mes en formato "yyyy-MM" (la que se guarda en la BD).</summary>
        public static string Clave(DateTime fecha) => fecha.ToString("yyyy-MM", CultureInfo.InvariantCulture);

        public static string MesActual => Clave(DateTime.Today);

        public static DateTime InicioMes(DateTime fecha) => new DateTime(fecha.Year, fecha.Month, 1);

        public static bool EsMesActual(DateTime mes) => Clave(mes) == MesActual;

        /// <summary>Nombre legible del mes, p. ej. "Octubre 2026".</summary>
        public static string Nombre(DateTime mes)
        {
            var texto = mes.ToString("MMMM yyyy", CultureInfo.CurrentCulture);
            return texto.Length > 0
                ? char.ToUpper(texto[0], CultureInfo.CurrentCulture) + texto.Substring(1)
                : texto;
        }
    }
}
