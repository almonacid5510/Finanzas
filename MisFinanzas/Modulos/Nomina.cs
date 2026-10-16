using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;

namespace MisFinanzas.Modulos
{
    public class ConfiguracionNomina
    {
        public int Id { get; set; } = 1;
        public string Frecuencia { get; set; } = "Quincenal";
        public string Dias { get; set; } = "";
        public int DiaSemana { get; set; } = 1; // domingo = 0
        public DateTime FechaBase { get; set; } = DateTime.Today;
        public decimal MontoPorPago { get; set; }
        /// <summary>Quincenal: valor del pago del día más tardío del mes (0 = igual a MontoPorPago).</summary>
        public decimal MontoSegundoPago { get; set; }
        public bool Confirmada { get; set; }
        public DateTime ActualizadaUtc { get; set; }
        public bool Pendiente { get; set; }
    }

    public class IngresoRecibido
    {
        [Key] public string Uid { get; set; } = Guid.NewGuid().ToString("N");
        public DateTime Fecha { get; set; } = DateTime.Today;
        public decimal Monto { get; set; }
        public string Concepto { get; set; } = "Nómina";
        public bool Inicial { get; set; }
        public bool Borrado { get; set; }
        public bool Pendiente { get; set; }
        public DateTime ActualizadaUtc { get; set; }
        public string Detalle => Inicial ? "Saldo al iniciar el mes" : Fecha.ToString("dd/MM/yyyy");
    }

    public record PagoEsperado(DateTime Fecha, decimal Monto, string Referencia = "")
    {
        public string Uid => "nomina-" + Fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + Referencia;
        public string Descripcion => $"Nómina esperada · {Fecha:dd/MM/yyyy} · {Monto:C0}";
    }

    public static class Nomina
    {
        public static readonly string[] Frecuencias = { "Mensual", "Quincenal", "Semanal" };
        public static List<int> LeerDias(string texto) => texto.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => int.TryParse(x.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0).Distinct().OrderBy(n => n).ToList();

        public static void Validar(ConfiguracionNomina c)
        {
            if (c.Dias.Length > 30) throw new ArgumentException("La lista de días es demasiado larga.");
            // Se leen planes antiguos sin cambiar su calendario hasta que el usuario los edite.
            if (!Frecuencias.Contains(c.Frecuencia) && c.Frecuencia is not ("Cada 14 días" or "Variable")) throw new ArgumentException("Elige la frecuencia de pago.");
            if ((c.MontoPorPago < 0 || (c.Frecuencia != "Variable" && c.MontoPorPago == 0)) || c.MontoPorPago > 1000000000000m) throw new ArgumentException("Escribe el valor neto esperado por pago.");
            if (c.Frecuencia is "Mensual" or "Quincenal")
            {
                var dias = LeerDias(c.Dias);
                if (dias.Count != (c.Frecuencia == "Mensual" ? 1 : 2) || dias.Any(n => n < 1 || n > 31))
                    throw new ArgumentException("Indica un día mensual o dos días diferentes para pago quincenal (1 a 31).");
            }
            if (c.MontoSegundoPago < 0 || c.MontoSegundoPago > 1000000000000m) throw new ArgumentException("Escribe el valor neto del segundo pago.");
            if (c.DiaSemana < 0 || c.DiaSemana > 6) throw new ArgumentException("Elige un día de la semana.");
            if (c.FechaBase == DateTime.MinValue) throw new ArgumentException("Elige la fecha de referencia.");
        }

        public static List<PagoEsperado> DelMes(ConfiguracionNomina c, DateTime mes)
        {
            if (!c.Confirmada) return new();
            Validar(c);
            var inicio = Periodo.InicioMes(mes);
            var fin = inicio.AddMonths(1);
            var fechas = new List<DateTime>();
            if (c.Frecuencia is "Mensual" or "Quincenal")
                return LeerDias(c.Dias).Select((n, i) => new PagoEsperado(inicio.AddDays(Math.Min(n, DateTime.DaysInMonth(mes.Year, mes.Month)) - 1), MontoDelPago(c, i), "-dia" + n)).ToList();
            else if (c.Frecuencia == "Semanal")
                for (var d = inicio; d < fin; d = d.AddDays(1)) { if ((int)d.DayOfWeek == c.DiaSemana) fechas.Add(d); }
            else if (c.Frecuencia == "Cada 14 días")
            {
                var diferencia = (inicio - c.FechaBase.Date).Days;
                var d = c.FechaBase.Date.AddDays((int)Math.Ceiling(diferencia / 14d) * 14);
                for (; d < fin; d = d.AddDays(14)) fechas.Add(d);
            }
            return fechas.OrderBy(f => f).Select(f => new PagoEsperado(f, c.MontoPorPago)).ToList();
        }

        /// <summary>Valor de cada pago del mes: en quincenal el segundo (día más tardío) puede ser distinto.</summary>
        public static decimal MontoDelPago(ConfiguracionNomina c, int indice) =>
            c.Frecuencia == "Quincenal" && indice == 1 && c.MontoSegundoPago > 0 ? c.MontoSegundoPago : c.MontoPorPago;

        /// <summary>Salario mensual según la nómina: un pago, la suma de las dos quincenas o el promedio semanal (52 / 12).</summary>
        public static decimal SalarioMensual(ConfiguracionNomina c) => c.Frecuencia switch
        {
            "Mensual" => c.MontoPorPago,
            "Quincenal" => MontoDelPago(c, 0) + MontoDelPago(c, 1),
            "Semanal" => Math.Round(c.MontoPorPago * 52m / 12m),
            _ => 0
        };

        /// <summary>Lleva el salario mensual de la nómina a Ingresos (no guarda: lo hace quien llama).</summary>
        public static void ActualizarSalario(AppDbContext db, ConfiguracionNomina c)
        {
            var salario = SalarioMensual(c);
            if (!c.Confirmada || salario <= 0) return;
            var ingreso = db.Movimientos.FirstOrDefault(m => m.Tipo == "Ingreso");
            if (ingreso == null)
                db.Movimientos.Add(new Movimiento { Descripcion = "Salario Mensual", Venta = salario, Costo = 0, Tipo = "Ingreso", EstaPagado = true, Fecha = DateTime.Now });
            else if (ingreso.Venta != salario) { ingreso.Venta = salario; ingreso.Fecha = DateTime.Now; }
        }

        public static decimal Saldo(AppDbContext db, DateTime hasta) => Caja(db, hasta).Saldo;

        /// <summary>
        /// Dinero real hasta una fecha: saldo inicial + lo confirmado como recibido − lo efectivamente pagado
        /// (lo pendiente y las compras con tarjeta no descuentan hasta pagarse). Igual que el Resumen del celular.
        /// </summary>
        public static (decimal Inicial, decimal Recibido, decimal Pagado, decimal Saldo) Caja(AppDbContext db, DateTime hasta)
        {
            var registros = db.IngresosRecibidos.Where(i => !i.Borrado && i.Fecha < hasta.Date.AddDays(1)).ToList();
            var inicial = registros.Where(i => i.Inicial).OrderByDescending(i => i.Fecha).FirstOrDefault();
            var desde = Periodo.Clave(inicial?.Fecha ?? (registros.Count > 0 ? registros.Min(i => i.Fecha) : hasta));
            var recibido = registros.Where(i => !i.Inicial && string.CompareOrdinal(Periodo.Clave(i.Fecha), desde) >= 0).Sum(i => i.Monto);
            var salidas = (Dictionary<string, object?>)FlujoCaja.Construir(db, hasta)["egresosPorMes"]!;
            var pagado = salidas.Where(x => string.CompareOrdinal(x.Key, desde) >= 0).Sum(x => (decimal)x.Value!);
            var saldoInicial = inicial?.Monto ?? 0;
            return (saldoInicial, recibido, pagado, saldoInicial + recibido - pagado);
        }

        public static void GuardarCobro(AppDbContext db, string uid, DateTime fecha, decimal monto, string concepto, bool inicial)
        {
            if (monto < 0 || (!inicial && monto == 0) || monto > 1000000000000m || fecha.Date > DateTime.Today || (string.IsNullOrWhiteSpace(concepto) || concepto.Trim().Length > 120))
                throw new ArgumentException("Revisa la fecha, el concepto y el valor recibido.");
            if (inicial) { fecha = Periodo.InicioMes(fecha); uid = "saldo-" + Periodo.Clave(fecha); }
            var registro = db.IngresosRecibidos.Find(uid);
            if (registro == null) { registro = new IngresoRecibido { Uid = uid }; db.IngresosRecibidos.Add(registro); }
            registro.Fecha = fecha.Date; registro.Monto = monto; registro.Concepto = concepto.Trim();
            registro.Inicial = inicial; registro.Borrado = false; registro.Pendiente = true; registro.ActualizadaUtc = DateTime.UtcNow;
            db.SaveChanges();
        }
    }
}
