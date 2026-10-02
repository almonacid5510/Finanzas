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
    /// <summary>Registro de que una cuenta se pagó en un mes concreto y cuánto se pagó realmente.</summary>
    public class PagoCuenta
    {
        [Key]
        public int Id { get; set; }
        public int MovimientoId { get; set; }
        public string Mes { get; set; } = string.Empty; // "yyyy-MM"
        public decimal Monto { get; set; }              // valor real pagado ese mes
    }

    /// <summary>Cada cuánto se repite un gasto o ingreso.</summary>
    public static class Frecuencias
    {
        public const string Mensual = "Mensual";
        public const string Unica = "Única";
        public const string Anual = "Anual";

        public static readonly string[] Todas = { Mensual, Unica, Anual };
    }

    /// <summary>Una cuenta vista en un mes: su vencimiento, si se pagó y cuánto se pagó realmente.</summary>
    public class CuentaMes : INotifyPropertyChanged
    {
        public CuentaMes(Movimiento cuenta, DateTime mes, PagoCuenta? pago)
        {
            Cuenta = cuenta;
            Mes = Periodo.InicioMes(mes);
            Vence = CuentasMensuales.Vencimiento(cuenta, mes);
            _pagada = pago != null;
            _montoPagado = pago?.Monto ?? cuenta.Venta;
        }

        public Movimiento Cuenta { get; }
        public DateTime Mes { get; }
        public DateTime Vence { get; }
        public string Descripcion => Cuenta.Descripcion;
        public decimal Venta => Cuenta.Venta;
        public string Frecuencia => string.IsNullOrEmpty(Cuenta.Frecuencia) ? Frecuencias.Mensual : Cuenta.Frecuencia;
        public string Categoria => Etiquetas.Mostrar(Cuenta.Categoria);
        public string ColorCategoria => Etiquetas.ColorTexto(Cuenta.Categoria);
        public string FondoCategoria => Etiquetas.ColorFondo(Cuenta.Categoria);

        private bool _pagada;
        public bool Pagada
        {
            get => _pagada;
            set { _pagada = value; Avisar(nameof(Pagada), nameof(Vencida), nameof(Real)); }
        }

        // Lo que realmente se pagó (por defecto, el valor registrado)
        private decimal _montoPagado;
        public decimal MontoPagado
        {
            get => _montoPagado;
            set { _montoPagado = value; Avisar(nameof(MontoPagado), nameof(MontoPagadoTexto), nameof(Real)); }
        }
        public string MontoPagadoTexto => NumeroHelper.FormatearMoneda(MontoPagado);

        /// <summary>Valor que cuenta para los totales: lo pagado si ya se pagó, si no el valor registrado.</summary>
        public decimal Real => Pagada ? MontoPagado : Venta;

        /// <summary>Ya pasó la fecha de vencimiento y no se ha pagado.</summary>
        public bool Vencida => !Pagada && Vence < DateTime.Today;

        /// <summary>Cuánto falta para el vencimiento: "Hoy", "Mañana", "En 5 días", "Vencida".</summary>
        public string Falta
        {
            get
            {
                int dias = (Vence - DateTime.Today).Days;
                return dias switch { < 0 => "Vencida", 0 => "Hoy", 1 => "Mañana", _ => $"En {dias} días" };
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void Avisar(params string[] nombres)
        {
            foreach (var n in nombres) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        }
    }

    /// <summary>
    /// Gastos (Movimiento con Tipo "Cuenta") e ingresos extra ("IngresoExtra") que se repiten:
    /// se registran una vez y aplican cada mes, una sola vez o cada año, según su Frecuencia.
    /// El estado de pago de los gastos se guarda por mes en PagosCuenta, con el monto real.
    /// </summary>
    public static class CuentasMensuales
    {
        public const string TipoGasto = "Cuenta";
        public const string TipoIngresoExtra = "IngresoExtra";

        public static int DiaDePago(Movimiento cuenta) => cuenta.DiaPago > 0 ? cuenta.DiaPago : cuenta.Fecha.Day;

        /// <summary>Fecha de vencimiento en el mes dado (si el mes es más corto, el último día).</summary>
        public static DateTime Vencimiento(Movimiento cuenta, DateTime mes)
        {
            var inicio = Periodo.InicioMes(mes);
            int dia = Math.Min(DiaDePago(cuenta), DateTime.DaysInMonth(inicio.Year, inicio.Month));
            return inicio.AddDays(dia - 1);
        }

        /// <summary>Si el valor aplica en el mes, según su frecuencia, su mes de inicio y su MesFin.</summary>
        public static bool AplicaEn(Movimiento cuenta, DateTime mes)
        {
            var clave = Periodo.Clave(mes);
            var inicio = Periodo.Clave(cuenta.Fecha);
            if (string.CompareOrdinal(inicio, clave) > 0) return false;
            if (!string.IsNullOrEmpty(cuenta.MesFin) && string.CompareOrdinal(clave, cuenta.MesFin) >= 0) return false;

            return cuenta.Frecuencia switch
            {
                Frecuencias.Unica => inicio == clave,
                Frecuencias.Anual => cuenta.Fecha.Month == mes.Month,
                _ => true
            };
        }

        /// <summary>Gastos que aplican en el mes, con su estado de pago, ordenados por vencimiento.</summary>
        public static List<CuentaMes> DelMes(AppDbContext db, DateTime mes)
        {
            var clave = Periodo.Clave(mes);
            var cuentas = db.Movimientos.Where(m => m.Tipo == TipoGasto).ToList();
            var pagos = db.PagosCuenta.Where(p => p.Mes == clave).ToList();
            return DelMes(cuentas, pagos, mes);
        }

        /// <summary>Versión en memoria, para recorrer muchos meses con una sola consulta.</summary>
        public static List<CuentaMes> DelMes(IEnumerable<Movimiento> cuentas, IEnumerable<PagoCuenta> pagos, DateTime mes)
        {
            var clave = Periodo.Clave(mes);
            var pagosDelMes = pagos.Where(p => p.Mes == clave)
                                   .GroupBy(p => p.MovimientoId)
                                   .ToDictionary(g => g.Key, g => g.First());

            return cuentas.Where(c => AplicaEn(c, mes))
                          .Select(c => new CuentaMes(c, mes, pagosDelMes.GetValueOrDefault(c.Id)))
                          .OrderBy(c => c.Vence)
                          .ThenBy(c => c.Descripcion)
                          .ToList();
        }

        /// <summary>Ingresos extra que aplican en el mes.</summary>
        public static List<Movimiento> IngresosExtraDelMes(AppDbContext db, DateTime mes) =>
            db.Movimientos.Where(m => m.Tipo == TipoIngresoExtra)
                          .AsEnumerable()
                          .Where(m => AplicaEn(m, mes))
                          .OrderBy(m => m.Descripcion)
                          .ToList();

        public static void MarcarPagada(AppDbContext db, int cuentaId, DateTime mes, bool pagada, decimal monto)
        {
            var clave = Periodo.Clave(mes);
            var pagos = db.PagosCuenta.Where(p => p.MovimientoId == cuentaId && p.Mes == clave).ToList();

            if (pagada && pagos.Count == 0)
                db.PagosCuenta.Add(new PagoCuenta { MovimientoId = cuentaId, Mes = clave, Monto = monto });
            else if (pagada)
                pagos[0].Monto = monto;
            else
                db.PagosCuenta.RemoveRange(pagos);

            db.SaveChanges();
        }

        /// <summary>
        /// Deja de aplicar el valor desde el mes dado y conserva el historial de meses anteriores.
        /// Si empezó ese mismo mes (o después) no tiene historial y se borra por completo.
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

        /// <summary>
        /// Fecha de inicio para un valor nuevo. Mensual/Única: el mes que se está viendo.
        /// Anual: la próxima vez que llegue el mes elegido, a partir del mes que se está viendo.
        /// </summary>
        public static DateTime FechaInicio(string frecuencia, DateTime mesVisto, int mesDelAnio, int dia)
        {
            var inicio = Periodo.InicioMes(mesVisto);
            if (frecuencia == Frecuencias.Anual)
            {
                int anio = mesDelAnio >= inicio.Month ? inicio.Year : inicio.Year + 1;
                inicio = new DateTime(anio, mesDelAnio, 1);
            }
            var temporal = new Movimiento { DiaPago = dia };
            return Vencimiento(temporal, inicio);
        }
    }
}
