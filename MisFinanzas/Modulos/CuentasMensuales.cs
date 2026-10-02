// ============================================================
//  Proyecto: Mis Finanzas
//  Autoria: Juan Almonacid
//  Copyright (c) 2026 Juan Almonacid. Todos los derechos reservados.
// ============================================================
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace MisFinanzas.Modulos
{
    /// <summary>Registro de que una cuenta recurrente se pagó en un mes concreto.</summary>
    public class PagoCuenta
    {
        [Key]
        public int Id { get; set; }
        public int MovimientoId { get; set; }
        public string Mes { get; set; } = string.Empty; // "yyyy-MM"
    }

    /// <summary>Una cuenta recurrente vista en un mes: su vencimiento y si se pagó ese mes.</summary>
    public class CuentaMes : INotifyPropertyChanged
    {
        public CuentaMes(Movimiento cuenta, DateTime mes, bool pagada)
        {
            Cuenta = cuenta;
            Mes = Periodo.InicioMes(mes);
            Vence = CuentasMensuales.Vencimiento(cuenta, mes);
            _pagada = pagada;
        }

        public Movimiento Cuenta { get; }
        public DateTime Mes { get; }
        public DateTime Vence { get; }
        public string Descripcion => Cuenta.Descripcion;
        public decimal Venta => Cuenta.Venta;

        private bool _pagada;
        public bool Pagada
        {
            get => _pagada;
            set
            {
                _pagada = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Pagada)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Vencida)));
            }
        }

        /// <summary>Ya pasó la fecha de vencimiento y no se ha pagado.</summary>
        public bool Vencida => !Pagada && Vence < DateTime.Today;

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    /// <summary>
    /// Las cuentas (Movimiento con Tipo "Cuenta") son gastos recurrentes: se registran una vez
    /// y vencen el mismo día todos los meses, desde el mes en que se crearon. El estado de pago
    /// se guarda por mes en la tabla PagosCuenta, así cada mes empieza pendiente.
    /// </summary>
    public static class CuentasMensuales
    {
        public static int DiaDePago(Movimiento cuenta) => cuenta.DiaPago > 0 ? cuenta.DiaPago : cuenta.Fecha.Day;

        /// <summary>Fecha de vencimiento en el mes dado (si el mes es más corto, el último día).</summary>
        public static DateTime Vencimiento(Movimiento cuenta, DateTime mes)
        {
            var inicio = Periodo.InicioMes(mes);
            int dia = Math.Min(DiaDePago(cuenta), DateTime.DaysInMonth(inicio.Year, inicio.Month));
            return inicio.AddDays(dia - 1);
        }

        /// <summary>La cuenta se cobra desde su mes de registro hasta antes de MesFin.</summary>
        public static bool AplicaEn(Movimiento cuenta, DateTime mes)
        {
            var clave = Periodo.Clave(mes);
            return string.CompareOrdinal(Periodo.Clave(cuenta.Fecha), clave) <= 0
                && (string.IsNullOrEmpty(cuenta.MesFin) || string.CompareOrdinal(clave, cuenta.MesFin) < 0);
        }

        /// <summary>Cuentas que se cobran en el mes, con su estado de pago, ordenadas por vencimiento.</summary>
        public static List<CuentaMes> DelMes(AppDbContext db, DateTime mes)
        {
            var clave = Periodo.Clave(mes);
            var pagadas = db.PagosCuenta.Where(p => p.Mes == clave).Select(p => p.MovimientoId).ToHashSet();

            return db.Movimientos
                     .Where(m => m.Tipo == "Cuenta")
                     .AsEnumerable()
                     .Where(c => AplicaEn(c, mes))
                     .Select(c => new CuentaMes(c, mes, pagadas.Contains(c.Id)))
                     .OrderBy(c => c.Vence)
                     .ThenBy(c => c.Descripcion)
                     .ToList();
        }

        public static void MarcarPagada(AppDbContext db, int cuentaId, DateTime mes, bool pagada)
        {
            var clave = Periodo.Clave(mes);
            var pagos = db.PagosCuenta.Where(p => p.MovimientoId == cuentaId && p.Mes == clave).ToList();

            if (pagada && pagos.Count == 0)
                db.PagosCuenta.Add(new PagoCuenta { MovimientoId = cuentaId, Mes = clave });
            else if (!pagada)
                db.PagosCuenta.RemoveRange(pagos);

            db.SaveChanges();
        }

        /// <summary>
        /// Deja de cobrar la cuenta desde el mes dado y conserva el historial de meses anteriores.
        /// Si se registró ese mismo mes (o después) no tiene historial y se borra por completo.
        /// Devuelve true cuando se borró por completo.
        /// </summary>
        public static bool EliminarDesde(AppDbContext db, int cuentaId, DateTime mes)
        {
            var cuenta = db.Movimientos.Find(cuentaId);
            if (cuenta == null) return true;

            var clave = Periodo.Clave(mes);
            bool completa = string.CompareOrdinal(Periodo.Clave(cuenta.Fecha), clave) >= 0;

            var pagosABorrar = db.PagosCuenta
                                 .Where(p => p.MovimientoId == cuentaId)
                                 .AsEnumerable()
                                 .Where(p => completa || string.CompareOrdinal(p.Mes, clave) >= 0);
            db.PagosCuenta.RemoveRange(pagosABorrar);

            if (completa) db.Movimientos.Remove(cuenta);
            else cuenta.MesFin = clave;

            db.SaveChanges();
            return completa;
        }
    }
}
