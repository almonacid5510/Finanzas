// ============================================================
//  Proyecto: Mis Finanzas
//  Autoria: Juan Almonacid
//  Copyright (c) 2026 Juan Almonacid. Todos los derechos reservados.
// ============================================================
using System;
using System.Collections.Generic;
using System.Linq;

namespace MisFinanzas.Modulos
{
    /// <summary>Totales de un mes ya vivido (o el actual).</summary>
    public class ResumenMes
    {
        public DateTime Mes { get; set; }
        public string Nombre => Periodo.Nombre(Mes);
        public bool EsActual => Periodo.EsMesActual(Mes);
        public decimal Ingresos { get; set; }
        public decimal Gastos { get; set; }      // lo pagado real + lo pendiente por su valor registrado
        public decimal Pagado { get; set; }
        public decimal Pendiente { get; set; }
        public decimal Balance => Ingresos - Gastos;
        public string ColorBalance => Balance < 0 ? "#C53030" : "#2F855A";
        public double Proporcion { get; set; }   // 0..1 respecto al mes con más gastos (para la barra)
    }

    /// <summary>Total de una categoría, con su proporción para dibujar la barra.</summary>
    public class TotalCategoria
    {
        public string Nombre { get; set; } = string.Empty;
        public decimal Total { get; set; }
        public double Proporcion { get; set; }
        public string ColorTexto => Etiquetas.ColorTexto(Nombre);
        public string ColorFondo => Etiquetas.ColorFondo(Nombre);
        public string ColorBarra => Etiquetas.ColorBarra(Nombre);
    }

    /// <summary>Predicción de un gasto a partir de lo que realmente se ha pagado.</summary>
    public class PrediccionGasto
    {
        public Movimiento Cuenta { get; set; } = new Movimiento();
        public string Descripcion => Cuenta.Descripcion;
        public string Categoria => Etiquetas.Mostrar(Cuenta.Categoria);
        public string ColorCategoria => Etiquetas.ColorTexto(Cuenta.Categoria);
        public string FondoCategoria => Etiquetas.ColorFondo(Cuenta.Categoria);
        public string Frecuencia => Frecuencias.Mostrar(Cuenta);
        public decimal Valor => Cuenta.Venta;
        public int MesesConDatos { get; set; }
        public decimal Promedio { get; set; }
        public decimal Prediccion { get; set; }
        public decimal Proyeccion12 { get; set; }
        public DateTime? ProximoVence { get; set; }
        public string Tendencia { get; set; } = string.Empty;
        public string ColorTendencia { get; set; } = "#718096";
    }

    /// <summary>Lo que se espera ingresar y gastar en un mes futuro.</summary>
    public class ProyeccionMes
    {
        public DateTime Mes { get; set; }
        public string Nombre => Periodo.Nombre(Mes);
        public decimal Ingresos { get; set; }
        public decimal Gastos { get; set; }
        public decimal Creditos { get; set; }
        public decimal TotalGastos => Gastos + Creditos;
        public decimal Balance => Ingresos - TotalGastos;
        public string ColorBalance => Balance < 0 ? "#C53030" : "#2F855A";
    }

    /// <summary>Historial, proyección y predicción de gastos e ingresos.</summary>
    public static class Analisis
    {
        // Meses de pagos reales que se promedian para predecir un gasto
        private const int MesesParaPromedio = 6;

        /// <summary>Datos de la BD cargados una sola vez para recorrer muchos meses.</summary>
        public class Datos
        {
            public List<Movimiento> Cuentas { get; init; } = new();
            public List<PagoCuenta> Pagos { get; init; } = new();
            public List<Movimiento> IngresosExtra { get; init; } = new();
            public List<Credito> Creditos { get; init; } = new();
            public List<PagoCredito> PagosCredito { get; init; } = new();
            public decimal Salario { get; init; }
        }

        public static Datos Cargar(AppDbContext db) => new Datos
        {
            Cuentas = db.Movimientos.Where(m => m.Tipo == CuentasMensuales.TipoGasto).ToList(),
            Pagos = db.PagosCuenta.ToList(),
            IngresosExtra = db.Movimientos.Where(m => m.Tipo == CuentasMensuales.TipoIngresoExtra).ToList(),
            Creditos = db.Creditos.ToList(),
            PagosCredito = db.PagosCredito.ToList(),
            Salario = db.Movimientos.Where(m => m.Tipo == "Ingreso").Select(m => m.Venta).AsEnumerable().FirstOrDefault()
        };

        /// <summary>Salario mensual más los ingresos extra que aplican en el mes.</summary>
        public static decimal IngresosDelMes(Datos d, DateTime mes) =>
            d.Salario + d.IngresosExtra.Where(i => CuentasMensuales.AplicaEn(i, mes)).Sum(i => i.Venta);

        /// <summary>
        /// Resumen de los últimos meses (hasta 'meses'), del más reciente al más antiguo.
        /// Empieza en el primer mes que tiene gastos o ingresos registrados.
        /// </summary>
        public static List<ResumenMes> Historial(Datos d, DateTime hasta, int meses = 12)
        {
            var fin = Periodo.InicioMes(hasta);
            var fechas = d.Cuentas.Concat(d.IngresosExtra).Select(m => Periodo.InicioMes(m.Fecha)).ToList();
            var desde = fechas.Count > 0 ? fechas.Min() : fin;
            if (desde < fin.AddMonths(-(meses - 1))) desde = fin.AddMonths(-(meses - 1));
            if (desde > fin) desde = fin;

            var lista = new List<ResumenMes>();
            for (var mes = fin; mes >= desde; mes = mes.AddMonths(-1))
            {
                var items = CuentasMensuales.DelMes(d.Cuentas, d.Pagos, mes);
                lista.Add(new ResumenMes
                {
                    Mes = mes,
                    Ingresos = IngresosDelMes(d, mes),
                    Gastos = items.Sum(i => i.Real),
                    Pagado = items.Where(i => i.Pagada).Sum(i => i.MontoPagado),
                    Pendiente = items.Where(i => !i.Pagada).Sum(i => i.Venta),
                });
            }

            var maximo = lista.Count > 0 ? lista.Max(r => r.Gastos) : 0;
            foreach (var r in lista) r.Proporcion = maximo > 0 ? (double)(r.Gastos / maximo) : 0;
            return lista;
        }

        /// <summary>Totales por categoría de un conjunto de gastos (opcionalmente con las cuotas de créditos).</summary>
        public static List<TotalCategoria> PorCategoria(IEnumerable<CuentaMes> items, decimal creditos = 0,
                                                        IEnumerable<GastoDiario>? diarios = null)
        {
            // Los gastos del día a día se suman a la categoría que les corresponde
            var lista = items.Select(i => (i.Categoria, Monto: i.Real))
                             .Concat((diarios ?? Enumerable.Empty<GastoDiario>()).Select(d => (Categoria: Etiquetas.Mostrar(d.Categoria), d.Monto)))
                             .GroupBy(i => i.Categoria)
                             .Select(g => new TotalCategoria { Nombre = g.Key, Total = g.Sum(i => i.Monto) })
                             .ToList();
            if (creditos > 0) lista.Add(new TotalCategoria { Nombre = "Créditos", Total = creditos });

            lista = lista.Where(c => c.Total > 0).OrderByDescending(c => c.Total).ToList();
            var maximo = lista.Count > 0 ? lista.Max(c => c.Total) : 0;
            foreach (var c in lista) c.Proporcion = maximo > 0 ? (double)(c.Total / maximo) : 0;
            return lista;
        }

        /// <summary>
        /// Predicción de cada gasto activo: promedio de lo realmente pagado en los últimos meses
        /// (o su valor registrado si aún no hay pagos) y proyección de los próximos 12 meses.
        /// </summary>
        public static List<PrediccionGasto> Predicciones(Datos d, DateTime desde)
        {
            var inicio = Periodo.InicioMes(desde);
            var meses = Enumerable.Range(0, 12).Select(i => inicio.AddMonths(i)).ToList();
            var lista = new List<PrediccionGasto>();

            foreach (var cuenta in d.Cuentas)
            {
                var mesesQueAplica = meses.Where(m => CuentasMensuales.AplicaEn(cuenta, m)).ToList();
                if (mesesQueAplica.Count == 0) continue; // ya no se cobra en el próximo año

                var pagos = d.Pagos.Where(p => p.MovimientoId == cuenta.Id)
                                   .OrderByDescending(p => p.Mes)
                                   .Take(MesesParaPromedio)
                                   .ToList();
                decimal promedio = pagos.Count > 0 ? Math.Round(pagos.Average(p => p.Monto)) : 0;
                decimal prediccion = pagos.Count > 0 ? promedio : cuenta.Venta;

                var mesesPagados = d.Pagos.Where(p => p.MovimientoId == cuenta.Id).Select(p => p.Mes).ToHashSet();
                var proximo = mesesQueAplica.FirstOrDefault(m => !mesesPagados.Contains(Periodo.Clave(m)));

                var pred = new PrediccionGasto
                {
                    Cuenta = cuenta,
                    MesesConDatos = pagos.Count,
                    Promedio = promedio,
                    Prediccion = prediccion,
                    Proyeccion12 = prediccion * mesesQueAplica.Count,
                    ProximoVence = proximo == default ? null : CuentasMensuales.Vencimiento(cuenta, proximo),
                };

                if (pagos.Count >= 2 && cuenta.Venta > 0)
                {
                    var cambio = (double)((prediccion - cuenta.Venta) / cuenta.Venta);
                    if (cambio > 0.05) { pred.Tendencia = $"▲ {cambio:P0} sobre lo previsto"; pred.ColorTendencia = "#C53030"; }
                    else if (cambio < -0.05) { pred.Tendencia = $"▼ {-cambio:P0} bajo lo previsto"; pred.ColorTendencia = "#2F855A"; }
                    else { pred.Tendencia = "= Estable"; pred.ColorTendencia = "#718096"; }
                }
                else
                {
                    pred.Tendencia = pagos.Count == 0 ? "Sin pagos registrados" : "Pocos datos";
                }
                lista.Add(pred);
            }

            return lista.OrderBy(p => p.ProximoVence ?? DateTime.MaxValue).ThenBy(p => p.Descripcion).ToList();
        }

        /// <summary>
        /// Proyección de los próximos meses (empezando por el actual). Los gastos ya pagados del mes
        /// actual cuentan por lo pagado; el resto por su predicción. Incluye las cuotas de créditos.
        /// </summary>
        public static List<ProyeccionMes> Proyeccion(Datos d, DateTime desde, int meses = 12)
        {
            var inicio = Periodo.InicioMes(desde);
            var predicciones = Predicciones(d, inicio).ToDictionary(p => p.Cuenta.Id, p => p.Prediccion);
            var lista = new List<ProyeccionMes>();

            for (int i = 0; i < meses; i++)
            {
                var mes = inicio.AddMonths(i);
                var items = CuentasMensuales.DelMes(d.Cuentas, d.Pagos, mes);
                decimal gastos = items.Sum(c => c.Pagada ? c.MontoPagado : predicciones.GetValueOrDefault(c.Cuenta.Id, c.Venta));

                // Cada crédito cobra según su plan de pagos: hasta que el saldo llegue a cero
                decimal creditos = Amortizacion.CuotasEnMes(d.Creditos, d.PagosCredito, mes);

                lista.Add(new ProyeccionMes { Mes = mes, Ingresos = IngresosDelMes(d, mes), Gastos = gastos, Creditos = creditos });
            }
            return lista;
        }

        /// <summary>Próximos vencimientos sin pagar: lo de los próximos 'dias' días y los gastos anuales del próximo año.</summary>
        public static List<CuentaMes> ProximasFechas(Datos d, DateTime hoy, int dias = 30)
        {
            var inicio = Periodo.InicioMes(hoy);
            return Enumerable.Range(0, 12)
                             .SelectMany(i => CuentasMensuales.DelMes(d.Cuentas, d.Pagos, inicio.AddMonths(i)))
                             .Where(c => !c.Pagada && c.Vence >= hoy.Date)
                             .Where(c => c.Vence <= hoy.Date.AddDays(dias) || c.Frecuencia == Frecuencias.Anual)
                             .OrderBy(c => c.Vence)
                             .ToList();
        }
    }
}
