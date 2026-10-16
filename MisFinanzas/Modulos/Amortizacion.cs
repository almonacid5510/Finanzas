// ============================================================
//  Proyecto: Mis Finanzas
//  Autoria: Juan Almonacid
//  Copyright (c) 2026 Juan Almonacid. Todos los derechos reservados.
// ============================================================
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace MisFinanzas.Modulos
{
    /// <summary>
    /// Un movimiento de un crédito: la cuota de un mes o un abono extra a capital.
    /// Guarda cómo se repartió el pago y el saldo antes y después, para el historial.
    /// </summary>
    public class PagoCredito : IReplicable
    {
        [Key]
        public int Id { get; set; }
        /// <summary>Identidad en la nube (igual en el PC y el celular).</summary>
        public string Uid { get; set; } = string.Empty;
        /// <summary>Último cambio (UTC): en la nube gana el más reciente.</summary>
        public DateTime ActualizadaUtc { get; set; }
        public int CreditoId { get; set; }
        public string Mes { get; set; } = string.Empty; // "yyyy-MM"
        public DateTime Fecha { get; set; }
        public string Tipo { get; set; } = Amortizacion.TipoCuota;
        public decimal Monto { get; set; }          // lo pagado
        public decimal Interes { get; set; }        // parte que se fue a intereses
        public decimal Capital { get; set; }        // parte que bajó la deuda
        public decimal SaldoAnterior { get; set; }
        public decimal SaldoNuevo { get; set; }
        public int MesesReducidos { get; set; }     // cuánto bajó el plazo restante (para deshacer)

        public string NombreMes => DateTime.TryParse(Mes + "-01", out var m) ? Periodo.Nombre(m) : Mes;
        public string NombreMesCorto => DateTime.TryParse(Mes + "-01", out var m) ? Amortizacion.MesCorto(m) : Mes;
        public bool EsAbono => Tipo == Amortizacion.TipoAbono;
    }

    /// <summary>Una fila del plan de pagos que falta (tabla de amortización).</summary>
    public class FilaAmortizacion
    {
        public int Numero { get; set; }
        public DateTime Mes { get; set; }
        public string NombreMes => Periodo.Nombre(Mes);
        public string NombreMesCorto => Amortizacion.MesCorto(Mes);
        public decimal SaldoInicial { get; set; }
        public decimal Cuota { get; set; }
        public decimal Interes { get; set; }
        public decimal Capital { get; set; }
        public decimal SaldoFinal { get; set; }
    }

    /// <summary>
    /// Cálculo de créditos con cuota fija (sistema francés): cada cuota paga primero el interés
    /// del mes sobre el saldo y el resto baja el capital. La tasa se registra efectiva anual (E.A.).
    /// </summary>
    public static class Amortizacion
    {
        public const string TipoCuota = "Cuota";
        public const string TipoAbono = "Abono";

        /// <summary>"Oct 2026" (para tablas).</summary>
        public static string MesCorto(DateTime mes)
        {
            var m = mes.ToString("MMM").TrimEnd('.');
            return (m.Length > 0 ? char.ToUpper(m[0]) + m.Substring(1) : m) + " " + mes.Year;
        }

        // Tope del plan, para cuotas que no alcanzan a cubrir los intereses
        public const int MaximoCuotas = 600;

        /// <summary>Tasa mensual equivalente a la efectiva anual: (1 + EA)^(1/12) − 1.</summary>
        public static decimal TasaMensual(double tasaEA) =>
            tasaEA <= 0 ? 0 : (decimal)(Math.Pow(1 + tasaEA / 100.0, 1.0 / 12.0) - 1);

        public static decimal InteresDelMes(decimal saldo, double tasaEA) =>
            saldo <= 0 ? 0 : Math.Round(saldo * TasaMensual(tasaEA), 0, MidpointRounding.AwayFromZero);

        /// <summary>Reparte una cuota: interés del mes y lo que va a capital (la última cuota solo cobra lo que falta).</summary>
        public static (decimal Cuota, decimal Interes, decimal Capital) RepartirCuota(decimal saldo, double tasaEA, decimal cuota)
        {
            decimal interes = InteresDelMes(saldo, tasaEA);
            decimal capital = Math.Min(cuota - interes, saldo);
            return (interes + capital, interes, capital);
        }

        /// <summary>
        /// Plan de pagos desde el mes dado hasta saldar la deuda, con el saldo y la cuota actuales.
        /// Si la cuota no cubre los intereses la deuda nunca baja y el plan llega a MaximoCuotas.
        /// </summary>
        public static List<FilaAmortizacion> Plan(Credito credito, DateTime desde)
        {
            var filas = new List<FilaAmortizacion>();
            decimal saldo = credito.Disponible;
            var mes = Periodo.InicioMes(desde);

            for (int n = 1; saldo > 0 && credito.CuotaMensual > 0 && n <= MaximoCuotas; n++)
            {
                var (cuota, interes, capital) = RepartirCuota(saldo, credito.TasaEA, credito.CuotaMensual);
                filas.Add(new FilaAmortizacion
                {
                    Numero = n, Mes = mes, SaldoInicial = saldo,
                    Cuota = cuota, Interes = interes, Capital = capital, SaldoFinal = saldo - capital
                });
                if (capital <= 0) break; // la cuota no alcanza para los intereses
                saldo -= capital;
                mes = mes.AddMonths(1);
            }
            return filas;
        }

        /// <summary>Primer mes con cuota pendiente: el actual, o el siguiente si la de este mes ya se pagó.</summary>
        public static DateTime ProximoMes(bool pagadoEsteMes) =>
            Periodo.InicioMes(DateTime.Today).AddMonths(pagadoEsteMes ? 1 : 0);

        /// <summary>Registra la cuota del mes: calcula interés y capital y baja el saldo y el plazo.</summary>
        public static PagoCredito PagarCuota(AppDbContext db, int creditoId, DateTime mes)
        {
            var credito = db.Creditos.Find(creditoId) ?? throw new InvalidOperationException("El crédito ya no existe.");
            var clave = Periodo.Clave(mes);

            var existente = db.PagosCredito.FirstOrDefault(p => p.CreditoId == creditoId && p.Mes == clave && p.Tipo == TipoCuota);
            if (existente != null) return existente;
            if (credito.Disponible <= 0) throw new InvalidOperationException("Este crédito ya está saldado.");

            var (cuota, interes, capital) = RepartirCuota(credito.Disponible, credito.TasaEA, credito.CuotaMensual);
            var pago = new PagoCredito
            {
                CreditoId = creditoId, Mes = clave, Fecha = DateTime.Now, Tipo = TipoCuota,
                Monto = cuota, Interes = interes, Capital = capital,
                SaldoAnterior = credito.Disponible, SaldoNuevo = credito.Disponible - capital
            };
            db.PagosCredito.Add(pago);

            credito.Disponible = pago.SaldoNuevo;
            if (credito.PlazoMeses > 0)
            {
                credito.PlazoMeses--;
                pago.MesesReducidos = 1;
            }
            if (Periodo.EsMesActual(mes))
            {
                credito.Pagado = true;
                credito.MesPagado = clave;
            }
            db.SaveChanges();
            return pago;
        }

        /// <summary>Abono extra que va completo a capital (no paga intereses). Mantiene la cuota y acorta el plazo.</summary>
        public static PagoCredito Abonar(AppDbContext db, int creditoId, decimal monto)
        {
            var credito = db.Creditos.Find(creditoId) ?? throw new InvalidOperationException("El crédito ya no existe.");
            decimal capital = Math.Min(monto, credito.Disponible);
            var pago = new PagoCredito
            {
                CreditoId = creditoId, Mes = Periodo.MesActual, Fecha = DateTime.Now, Tipo = TipoAbono,
                Monto = capital, Interes = 0, Capital = capital,
                SaldoAnterior = credito.Disponible, SaldoNuevo = credito.Disponible - capital
            };
            db.PagosCredito.Add(pago);

            credito.Disponible = pago.SaldoNuevo;
            // Con la misma cuota y menos deuda quedan menos meses
            if (credito.PlazoMeses > 0)
            {
                int nuevoPlazo = Math.Min(credito.PlazoMeses, Plan(credito, ProximoMes(credito.Pagado)).Count);
                pago.MesesReducidos = credito.PlazoMeses - nuevoPlazo;
                credito.PlazoMeses = nuevoPlazo;
            }
            db.SaveChanges();
            return pago;
        }

        /// <summary>
        /// Deshace un pago (cuota o abono): devuelve a la deuda el capital que había bajado.
        /// El saldo es el monto menos todo el capital pagado, así que deshacer cualquier pago deja la deuda correcta.
        /// </summary>
        public static void Deshacer(AppDbContext db, int pagoId)
        {
            var pago = db.PagosCredito.Find(pagoId);
            if (pago == null) return;
            var credito = db.Creditos.Find(pago.CreditoId);

            if (credito != null)
            {
                credito.Disponible += pago.Capital;
                credito.PlazoMeses += pago.MesesReducidos;
                if (pago.Tipo == TipoCuota)
                {
                    if (pago.Mes == credito.MesPagado)
                    {
                        credito.Pagado = false;
                        credito.MesPagado = string.Empty;
                    }
                }
            }
            db.PagosCredito.Remove(pago);
            db.SaveChanges();
        }

        /// <summary>Cuota de la cuenta del mes dado (para deshacer desde la casilla de pagado).</summary>
        public static PagoCredito? CuotaDelMes(AppDbContext db, int creditoId, DateTime mes)
        {
            var clave = Periodo.Clave(mes);
            return db.PagosCredito.FirstOrDefault(p => p.CreditoId == creditoId && p.Mes == clave && p.Tipo == TipoCuota);
        }

        // ===== Tarjeta de crédito: cada compra se difiere a sus cuotas =====

        /// <summary>Cuota de una compra con tarjeta en un mes: número (1 = mes siguiente a la compra), total de cuotas y reparto.</summary>
        public record CuotaCompra(GastoDiario Compra, int Numero, int Total, decimal Cuota, decimal Interes, decimal Capital);

        /// <summary>
        /// Cuota de la tarjeta en el mes: la suma de la cuota de cada compra vigente.
        /// A 1 cuota se paga completa el mes siguiente, sin intereses; a más cuotas, cuota fija con la tasa de la tarjeta.
        /// El capital no pasa de lo que se debe (por si se hicieron pagos extra).
        /// </summary>
        public static (decimal Cuota, decimal Interes, decimal Capital, List<CuotaCompra> Detalle) CuotaTarjeta(
            Credito tarjeta, IEnumerable<GastoDiario> compras, DateTime mes)
        {
            double r = (double)TasaMensual(tarjeta.TasaEA);
            var detalle = new List<CuotaCompra>();
            foreach (var c in compras.Where(c => c.TarjetaId == tarjeta.Id && c.Monto > 0))
            {
                int n = Math.Max(1, c.Cuotas);
                int k = (mes.Year - c.Fecha.Year) * 12 + mes.Month - c.Fecha.Month; // 1 = el mes siguiente a la compra
                if (k < 1 || k > n) continue;

                decimal cuota, interes;
                if (n == 1 || r <= 0)
                {
                    cuota = Math.Round(c.Monto / n, 0, MidpointRounding.AwayFromZero);
                    interes = 0;
                }
                else
                {
                    double p = (double)c.Monto, fija = p * r / (1 - Math.Pow(1 + r, -n));
                    double saldo = p * Math.Pow(1 + r, k - 1) - fija * (Math.Pow(1 + r, k - 1) - 1) / r; // antes de esta cuota
                    cuota = Math.Round((decimal)fija, 0, MidpointRounding.AwayFromZero);
                    interes = Math.Round((decimal)(saldo * r), 0, MidpointRounding.AwayFromZero);
                }
                detalle.Add(new CuotaCompra(c, k, n, cuota, interes, cuota - interes));
            }
            decimal capital = Math.Min(detalle.Sum(d => d.Capital), Math.Max(0, tarjeta.Disponible));
            decimal intereses = detalle.Sum(d => d.Interes);
            return (capital + intereses, intereses, capital, detalle);
        }

        /// <summary>Registra el pago de la cuota de la tarjeta del mes: la parte de capital baja la deuda.</summary>
        public static PagoCredito PagarCuotaTarjeta(AppDbContext db, int tarjetaId, DateTime mes, decimal interes, decimal capital)
        {
            var tarjeta = db.Creditos.Find(tarjetaId) ?? throw new InvalidOperationException("La tarjeta ya no existe.");
            capital = Math.Min(capital, tarjeta.Disponible);
            var pago = new PagoCredito
            {
                CreditoId = tarjetaId, Mes = Periodo.Clave(mes), Fecha = DateTime.Now, Tipo = TipoCuota,
                Monto = interes + capital, Interes = interes, Capital = capital,
                SaldoAnterior = tarjeta.Disponible, SaldoNuevo = tarjeta.Disponible - capital
            };
            db.PagosCredito.Add(pago);
            tarjeta.Disponible = pago.SaldoNuevo;
            tarjeta.Pagado = true;
            tarjeta.MesPagado = pago.Mes;
            db.SaveChanges();
            return pago;
        }

        /// <summary>Cuota de cada mes futuro según el plan de cada crédito (para la proyección).</summary>
        public static decimal CuotasEnMes(IEnumerable<Credito> creditos, IEnumerable<PagoCredito> pagos, DateTime mes)
        {
            var clave = Periodo.Clave(mes);
            var cuotasPagadas = pagos.Where(p => p.Tipo == TipoCuota && p.Mes == clave)
                                     .GroupBy(p => p.CreditoId)
                                     .ToDictionary(g => g.Key, g => g.Sum(p => p.Monto));
            decimal total = 0;
            foreach (var c in creditos.Where(c => !c.EsTarjeta)) // las tarjetas van aparte (solo sus intereses son gasto)
            {
                if (cuotasPagadas.TryGetValue(c.Id, out var pagado)) { total += pagado; continue; }

                bool pagadoEsteMes = pagos.Any(p => p.CreditoId == c.Id && p.Tipo == TipoCuota && p.Mes == Periodo.MesActual);
                var fila = Plan(c, ProximoMes(pagadoEsteMes)).FirstOrDefault(f => Periodo.Clave(f.Mes) == clave);
                if (fila != null) total += fila.Cuota;
            }
            return total;
        }
    }
}
