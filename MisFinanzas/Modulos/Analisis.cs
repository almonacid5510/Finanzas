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
        public string ColorBalance => MisFinanzas.Tema.Texto(Balance < 0 ? "#C53030" : "#2F855A");
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
        private string _colorTendencia = "#718096";
        public string ColorTendencia { get => MisFinanzas.Tema.Texto(_colorTendencia); set => _colorTendencia = value; }

        // --- Tabla de la pestaña de proyección ---
        /// <summary>Cuota de un crédito: monto fijo según su plan de pagos.</summary>
        public bool EsCredito { get; set; }
        public string? Titulo { get; set; }
        public string Nombre => Titulo ?? Descripcion;
        public string Detalle => $"{Categoria} · {Frecuencia}";
        public string ColorBarra => Etiquetas.ColorBarra(Cuenta.Categoria);
        public string ProximoTexto { get; set; } = "—";
        /// <summary>12 meses de este gasto frente al gasto más alto de la tabla (0..1).</summary>
        public double Proporcion { get; set; }

        /// <summary>Qué tan exacta es la predicción: un segmento por pago real (máximo 6).</summary>
        public IReadOnlyList<string> Segmentos
        {
            get
            {
                int n = Math.Min(MesesConDatos, 6);
                var color = MisFinanzas.Tema.Acento(n <= 2 ? "#F79009" : n <= 5 ? "#2E90FA" : "#12B76A");
                var vacio = MisFinanzas.Tema.Fondo("#E4E7EC");
                return Enumerable.Range(0, 6).Select(i => i < n ? color : vacio).ToList();
            }
        }
        public string CalculadoCon => MesesConDatos switch
        {
            0 => "Lo que escribiste",
            1 => "1 pago real",
            _ => $"{Math.Min(MesesConDatos, 6)} pagos reales"
        };
        public string ColorCalculadoCon => MisFinanzas.Tema.Texto(MesesConDatos == 0 ? "#98A2B3" : "#344054");
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
        public string ColorBalance => MisFinanzas.Tema.Texto(Balance < 0 ? "#C53030" : "#2F855A");
    }

    /// <summary>Barra del gráfico de los próximos meses: gastos y cuotas de créditos apiladas.</summary>
    public class BarraProyeccion
    {
        public string Mes { get; set; } = string.Empty;
        public string Total { get; set; } = string.Empty;
        public double AltoGastos { get; set; }
        public double AltoCreditos { get; set; }
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
            public DateTime Inicio { get; init; } = Periodo.InicioMes(DateTime.Today);
            public decimal Salario { get; init; }
        }

        public static Datos Cargar(AppDbContext db) => new Datos
        {
            Inicio = InicioRegistro.Obtener(db),
            Cuentas = db.Movimientos.Where(m => m.Tipo == CuentasMensuales.TipoGasto).ToList(),
            Pagos = db.PagosCuenta.ToList(),
            IngresosExtra = db.Movimientos.Where(m => m.Tipo == CuentasMensuales.TipoIngresoExtra).ToList(),
            Creditos = db.Creditos.ToList(),
            PagosCredito = db.PagosCredito.ToList(),
            Salario = db.Movimientos.Where(m => m.Tipo == "Ingreso").Select(m => m.Venta).AsEnumerable().FirstOrDefault()
        };

        /// <summary>
        /// Gastos registrados directamente en Vehículos (repuestos, SOAT…) entre dos fechas, como gastos del día a día
        /// de la categoría "Vehículos", para sumarlos al gasto del mes. Los que vienen de Día a día no se incluyen
        /// (ya están como gasto del día a día: así cada gasto cuenta una sola vez).
        /// </summary>
        public static List<GastoDiario> GastosDeVehiculos(AppDbContext db, DateTime desde, DateTime hasta)
        {
            var nombres = db.Vehiculos.ToDictionary(v => v.Id, v => v.Nombre);
            return db.GastosVehiculo.Where(g => g.GastoDiarioUid == "" && g.Fecha >= desde && g.Fecha < hasta)
                     .AsEnumerable()
                     .Select(g => new GastoDiario
                     {
                         Uid = string.Empty,
                         Fecha = g.Fecha,
                         Monto = g.Valor,
                         Categoria = VinculoVehiculo.CategoriaVehiculos,
                         Descripcion = nombres.TryGetValue(g.VehiculoId, out var n) ? $"{n}: {g.Concepto}" : g.Concepto,
                         VehiculoId = g.VehiculoId
                     })
                     .ToList();
        }

        /// <summary>
        /// Intereses de las tarjetas de crédito en el mes, como gastos de la categoría "Créditos" (las compras
        /// ya contaron cuando se hicieron: solo los intereses son gasto nuevo). Si la cuota del mes ya se pagó,
        /// lo pagado; si no, lo que se calcula con las cuotas de cada compra.
        /// </summary>
        public static List<GastoDiario> InteresesTarjetas(AppDbContext db, DateTime mes)
        {
            mes = Periodo.InicioMes(mes);
            var clave = Periodo.Clave(mes);
            var lista = new List<GastoDiario>();
            foreach (var t in db.Creditos.AsEnumerable().Where(c => c.EsTarjeta).ToList())
            {
                var pagos = db.PagosCredito.Where(p => p.CreditoId == t.Id && p.Tipo == Amortizacion.TipoCuota && p.Mes == clave).ToList();
                decimal interes = pagos.Count > 0
                    ? pagos.Sum(p => p.Interes)
                    : Amortizacion.CuotaTarjeta(t, db.GastosDiarios.Where(g => g.TarjetaId == t.Id && g.Fecha >= mes.AddMonths(-37) && g.Fecha < mes).ToList(), mes).Interes;
                if (interes > 0)
                    lista.Add(new GastoDiario
                    {
                        Uid = string.Empty, Fecha = mes, Monto = interes, Categoria = CategoriaCreditos,
                        Descripcion = $"Intereses tarjeta · {t.NombreCustom}", TarjetaId = null
                    });
            }
            return lista;
        }

        public const string CategoriaCreditos = "Créditos";

        /// <summary>Salario mensual más los ingresos extra que aplican en el mes.</summary>
        public static decimal IngresosDelMes(Datos d, DateTime mes) =>
            Periodo.InicioMes(mes) < Periodo.InicioMes(d.Inicio) ? 0
                : d.Salario + d.IngresosExtra.Where(i => CuentasMensuales.AplicaEn(i, mes)).Sum(i => i.Venta);

        /// <summary>
        /// Resumen de los últimos meses (hasta 'meses'), del más reciente al más antiguo.
        /// Empieza en el primer mes que tiene gastos o ingresos registrados.
        /// </summary>
        public static List<ResumenMes> Historial(Datos d, DateTime hasta, int meses = 12)
        {
            var fin = Periodo.InicioMes(hasta);
            var desde = Periodo.InicioMes(d.Inicio);
            if (desde < fin.AddMonths(-(meses - 1))) desde = fin.AddMonths(-(meses - 1));

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
            if (creditos > 0)
            {
                // Con los intereses de tarjetas (que vienen en los diarios) quedan en una sola fila
                var fila = lista.FirstOrDefault(c => c.Nombre == CategoriaCreditos);
                if (fila != null) fila.Total += creditos;
                else lista.Add(new TotalCategoria { Nombre = CategoriaCreditos, Total = creditos });
            }

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

                // Un mes queda pagado cuando tiene todos sus pagos (dos si el gasto es quincenal)
                int pagosPorMes = CuentasMensuales.EsQuincenal(cuenta) ? 2 : 1;
                var mesesPagados = d.Pagos.Where(p => p.MovimientoId == cuenta.Id).GroupBy(p => p.Mes)
                                          .Where(g => g.Count() >= pagosPorMes).Select(g => g.Key).ToHashSet();
                var proximo = mesesQueAplica.FirstOrDefault(m => !mesesPagados.Contains(Periodo.Clave(m)));

                var pred = new PrediccionGasto
                {
                    Cuenta = cuenta,
                    MesesConDatos = pagos.Count,
                    Promedio = promedio,
                    Prediccion = prediccion,
                    Proyeccion12 = prediccion * mesesQueAplica.Count * pagosPorMes,
                    ProximoVence = proximo == default ? null : CuentasMensuales.Vencimiento(cuenta, proximo, QuincenaPendiente(d.Pagos, cuenta, proximo)),
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

            foreach (var p in lista) p.ProximoTexto = p.ProximoVence?.ToString("dd/MM/yyyy") ?? "—";
            return lista.OrderBy(p => p.ProximoVence ?? DateTime.MaxValue).ThenBy(p => p.Descripcion).ToList();
        }

        /// <summary>Quincenal: 2 si la primera quincena del mes ya está pagada, si no 1. Los demás gastos: 0.</summary>
        private static int QuincenaPendiente(IEnumerable<PagoCuenta> pagos, Movimiento cuenta, DateTime mes)
        {
            if (!CuentasMensuales.EsQuincenal(cuenta)) return 0;
            var clave = Periodo.Clave(mes);
            return pagos.Any(p => p.MovimientoId == cuenta.Id && p.Mes == clave && p.Quincena != 2) ? 2 : 1;
        }

        /// <summary>
        /// Tabla de la proyección: primero las cuotas de los créditos (montos fijos de su plan)
        /// y luego cada gasto con su predicción, con la proporción de sus 12 meses para la barra.
        /// </summary>
        public static List<PrediccionGasto> TablaProyeccion(Datos d, DateTime desde)
        {
            var inicio = Periodo.InicioMes(desde);
            var lista = new List<PrediccionGasto>();

            foreach (var c in d.Creditos.Where(c => !c.EsTarjeta))
            {
                var meses = Enumerable.Range(0, 12).Select(i => inicio.AddMonths(i))
                    .Select(m => (Mes: m, Cuota: Amortizacion.CuotasEnMes(new[] { c }, d.PagosCredito, m),
                                  Pagado: d.PagosCredito.Any(p => p.CreditoId == c.Id && p.Tipo == Amortizacion.TipoCuota && p.Mes == Periodo.Clave(m))))
                    .ToList();
                decimal total = meses.Sum(m => m.Cuota);
                if (total <= 0) continue; // crédito saldado

                var proximo = meses.FirstOrDefault(m => !m.Pagado && m.Cuota > 0);
                var nombreMes = proximo.Mes == default ? "—" : proximo.Mes.ToString("MMMM");
                lista.Add(new PrediccionGasto
                {
                    Cuenta = new Movimiento { Descripcion = c.NombreCustom, Categoria = CategoriaCreditos, Frecuencia = Frecuencias.Mensual },
                    Titulo = $"Cuota · {c.NombreCustom}",
                    EsCredito = true,
                    Prediccion = proximo.Mes == default ? 0 : proximo.Cuota,
                    Proyeccion12 = total,
                    ProximoTexto = nombreMes.Length > 0 ? char.ToUpper(nombreMes[0]) + nombreMes[1..] : nombreMes,
                });
            }

            lista.AddRange(Predicciones(d, inicio));
            var maximo = lista.Count > 0 ? lista.Max(p => p.Proyeccion12) : 0;
            foreach (var p in lista) p.Proporcion = maximo > 0 ? (double)(p.Proyeccion12 / maximo) : 0;
            return lista;
        }

        /// <summary>Barras del gráfico: gastos y cuotas de cada mes, a escala del mes más alto.</summary>
        public static List<BarraProyeccion> Barras(IEnumerable<ProyeccionMes> meses, double altoMaximo)
        {
            var lista = meses.ToList();
            var maximo = lista.Count > 0 ? lista.Max(m => m.TotalGastos) : 0;
            double Alto(decimal v) => maximo > 0 ? (double)(v / maximo) * altoMaximo : 0;
            return lista.Select(m =>
            {
                var corto = m.Mes.ToString("MMM").TrimEnd('.');
                return new BarraProyeccion
                {
                    Mes = corto.Length > 0 ? char.ToUpper(corto[0]) + corto[1..] : corto,
                    Total = $"{m.TotalGastos / 1_000_000m:0.00} M",
                    AltoGastos = Alto(m.Gastos),
                    AltoCreditos = Alto(m.Creditos),
                };
            }).ToList();
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
