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
    public class PagoCuenta : IReplicable
    {
        [Key]
        public int Id { get; set; }
        /// <summary>Identidad en la nube (igual en el PC y el celular).</summary>
        public string Uid { get; set; } = string.Empty;
        /// <summary>Último cambio (UTC): en la nube gana el más reciente.</summary>
        public DateTime ActualizadaUtc { get; set; }
        public int MovimientoId { get; set; }
        public string Mes { get; set; } = string.Empty; // "yyyy-MM"
        public decimal Monto { get; set; }              // valor real pagado ese mes
        /// <summary>Gastos quincenales: 2 = segunda quincena del mes; 0 o 1 = primera (o el pago único del mes).</summary>
        public int Quincena { get; set; }
    }

    /// <summary>Cada cuánto se repite un gasto o ingreso.</summary>
    public static class Frecuencias
    {
        public const string Mensual = "Mensual";
        /// <summary>Dos veces al mes: el día elegido y 15 días después (o antes, si el día es mayor que 15).</summary>
        public const string Quincenal = "Quincenal";
        public const string Unica = "Única";
        public const string Anual = "Anual";

        public static readonly string[] Todas = { Mensual, Unica, Anual };

        // Gastos que se cobran cada varios meses (Mensual con IntervaloMeses > 1)
        public const int IntervaloMaximo = 6;
        public static string CadaMeses(int meses) => meses <= 1 ? Mensual : $"{meses} meses";

        /// <summary>Opciones del formulario de gastos: Mensual, Quincenal, Cada 2…6 meses, Anual y Única.</summary>
        public static readonly string[] Opciones =
            new[] { Mensual, Quincenal }
            .Concat(Enumerable.Range(2, IntervaloMaximo - 1).Select(CadaMeses))
            .Concat(new[] { Anual, Unica })
            .ToArray();

        /// <summary>De la opción elegida a lo que se guarda: frecuencia e intervalo en meses.</summary>
        public static (string Frecuencia, int Intervalo) Interpretar(string opcion)
        {
            for (int n = 2; n <= IntervaloMaximo; n++)
                if (opcion == CadaMeses(n)) return (Mensual, n);
            return (opcion, 1);
        }

        /// <summary>Texto de la frecuencia de un gasto ("Mensual", "2 meses", "Anual"...).</summary>
        public static string Mostrar(Movimiento m)
        {
            var f = string.IsNullOrEmpty(m.Frecuencia) ? Mensual : m.Frecuencia;
            return f == Mensual ? CadaMeses(m.IntervaloMeses) : f;
        }
    }

    /// <summary>Una cuenta vista en un mes: su vencimiento, si se pagó y cuánto se pagó realmente.</summary>
    public class CuentaMes : INotifyPropertyChanged
    {
        public CuentaMes(Movimiento cuenta, DateTime mes, PagoCuenta? pago, int quincena = 0)
        {
            Cuenta = cuenta;
            Mes = Periodo.InicioMes(mes);
            Quincena = quincena;
            Vence = CuentasMensuales.Vencimiento(cuenta, mes, quincena);
            _pagada = pago != null;
            _montoPagado = pago?.Monto ?? cuenta.Venta;
        }

        public Movimiento Cuenta { get; }
        public DateTime Mes { get; }
        /// <summary>Gastos quincenales: 1 o 2. Los demás: 0.</summary>
        public int Quincena { get; }
        public DateTime Vence { get; }
        public string Descripcion => Cuenta.Descripcion;
        public decimal Venta => Cuenta.Venta;
        public string Frecuencia => Quincena > 0 ? $"Quincenal · {Quincena}.ª" : Frecuencias.Mostrar(Cuenta);
        public string Categoria => Etiquetas.Mostrar(Cuenta.Categoria);
        public string ColorCategoria => Etiquetas.ColorTexto(Cuenta.Categoria);
        public string FondoCategoria => Etiquetas.ColorFondo(Cuenta.Categoria);
        public string BordeCategoria => Etiquetas.ColorBorde(Cuenta.Categoria);

        private bool _pagada;
        public bool Pagada
        {
            get => _pagada;
            set { _pagada = value; Avisar(nameof(Pagada), nameof(AccionPago), nameof(Vencida), nameof(Real), nameof(EstadoTexto), nameof(EstadoFondo), nameof(EstadoColor), nameof(FondoFila), nameof(Acento), nameof(ColorFecha)); }
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

        private EstadoPago Estado => EstadoPago.Evaluar(Pagada, Vence, DateTime.Today);
        public string AccionPago => Pagada ? $"Deshacer pago de {Descripcion}" : $"Marcar {Descripcion} como pagado";
        public string EstadoTexto => Estado.Texto;
        public string EstadoFondo => Estado.Fondo;
        public string EstadoColor => Estado.Color;
        public string FondoFila => Estado.FondoFila;
        public string Acento => Estado.Acento;
        public string ColorFecha => Vencida ? "#B42318" : "#0F1B2D";

        /// <summary>Mes del vencimiento en corto y mayúsculas ("OCT").</summary>
        public string MesCortoVence => Vence.ToString("MMM").TrimEnd('.').ToUpperInvariant();

        /// <summary>"Vence hoy", "Vencida hace 1 día", "Vencida hace 4 días" (para el aviso de pagos pendientes).</summary>
        public string VencidaHace
        {
            get
            {
                int dias = (DateTime.Today - Vence).Days;
                return dias <= 0 ? "Vence hoy" : dias == 1 ? "Vencida hace 1 día" : $"Vencida hace {dias} días";
            }
        }

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
        public static DateTime Vencimiento(Movimiento cuenta, DateTime mes, int quincena = 0)
        {
            var inicio = Periodo.InicioMes(mes);
            int dia = Math.Min(quincena > 0 ? DiasQuincenales(cuenta)[quincena - 1] : DiaDePago(cuenta), DateTime.DaysInMonth(inicio.Year, inicio.Month));
            return inicio.AddDays(dia - 1);
        }

        public static bool EsQuincenal(Movimiento cuenta) => cuenta.Frecuencia == Frecuencias.Quincenal;

        /// <summary>Días de los dos pagos quincenales: el elegido y 15 días después (o antes): 15 → 15 y 30; 5 → 5 y 20; 31 → 16 y 31.</summary>
        public static int[] DiasQuincenales(Movimiento cuenta)
        {
            int dia = DiaDePago(cuenta);
            return dia <= 15 ? new[] { dia, dia + 15 } : new[] { dia - 15, dia };
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
                // Mensual: todos los meses, o cada N meses contando desde el mes de inicio
                _ => cuenta.IntervaloMeses <= 1 || MesesEntre(cuenta.Fecha, mes) % cuenta.IntervaloMeses == 0
            };
        }

        private static int MesesEntre(DateTime desde, DateTime hasta) =>
            (hasta.Year - desde.Year) * 12 + hasta.Month - desde.Month;

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
                                   .GroupBy(p => (p.MovimientoId, Quincena: p.Quincena == 2 ? 2 : 1))
                                   .ToDictionary(g => g.Key, g => g.First());

            return cuentas.Where(c => AplicaEn(c, mes))
                          .SelectMany(c => EsQuincenal(c)
                              ? new[] { 1, 2 }.Select(q => new CuentaMes(c, mes, pagosDelMes.GetValueOrDefault((c.Id, q)), q))
                              : new[] { new CuentaMes(c, mes, pagosDelMes.GetValueOrDefault((c.Id, 1))) })
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

        public static void MarcarPagada(AppDbContext db, int cuentaId, DateTime mes, bool pagada, decimal monto, int quincena = 0)
        {
            var clave = Periodo.Clave(mes);
            // La primera quincena (o el pago único del mes) se guarda con 0 o 1; la segunda con 2
            bool segunda = quincena == 2;
            var pagos = db.PagosCuenta.Where(p => p.MovimientoId == cuentaId && p.Mes == clave).AsEnumerable()
                          .Where(p => (p.Quincena == 2) == segunda).ToList();

            if (pagada && pagos.Count == 0)
                db.PagosCuenta.Add(new PagoCuenta { MovimientoId = cuentaId, Mes = clave, Monto = monto, Quincena = segunda ? 2 : 0 });
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
        public static DateTime FechaInicio(string frecuencia, DateTime mesVisto, int mesDelAnio, int dia, int intervalo = 1)
        {
            var inicio = Periodo.InicioMes(mesVisto);
            // Anual y "cada N meses": empiezan la próxima vez que llegue el mes elegido (primer cobro)
            if (frecuencia == Frecuencias.Anual || intervalo > 1)
            {
                int anio = mesDelAnio >= inicio.Month ? inicio.Year : inicio.Year + 1;
                inicio = new DateTime(anio, mesDelAnio, 1);
            }
            var temporal = new Movimiento { DiaPago = dia };
            return Vencimiento(temporal, inicio);
        }
    }
}
