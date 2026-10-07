// ============================================================
//  Proyecto: Mis Finanzas
//  Autoria: Juan Almonacid
//  Copyright (c) 2026 Juan Almonacid. Todos los derechos reservados.
// ============================================================
using System;
using System.Collections.Generic;
using System.Collections.Specialized; // Requerido para capturar los eventos CollectionChanged
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace MisFinanzas.Modulos
{
    public partial class DashboardView : UserControl
    {
        public DashboardView()
        {
            InitializeComponent();
        }

        // Recalcula el resumen cuando cambia cualquiera de las listas globales de la app
        private void ListaGlobal_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => CargarResumen();

        // Las listas son estáticas y viven toda la sesión: nos suscribimos solo mientras el
        // Dashboard está en pantalla, para no acumular manejadores cada vez que se navega aquí.
        private void Suscribir(bool activar)
        {
            INotifyCollectionChanged[] listas =
            {
                VehiculosView.listaVehiculos,
                CuentasView.listaCuentasPersistente,
                CreditosView.ListaCreditosPersistente,
                IngresosView.ListaMetas
            };

            foreach (var lista in listas)
            {
                lista.CollectionChanged -= ListaGlobal_CollectionChanged; // evita duplicados
                if (activar) lista.CollectionChanged += ListaGlobal_CollectionChanged;
            }
        }

        /// <summary>
        /// Escucha el evento Loaded que configuramos en el XAML.
        /// Se dispara automáticamente cada vez que el usuario hace clic o vuelve a la pestaña del Dashboard.
        /// </summary>
        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Forzamos una recarga fresca desde la Base de Datos para asegurar sincronización absoluta.
                // Se recarga antes de suscribirse para no recalcular el resumen por cada elemento cargado.
                IngresosView.CargarMetasGlobal();
                CreditosView.CargarDatosGlobal();
                CargarResumen();
                Suscribir(true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error en inicialización del Dashboard: " + ex.Message);
            }
        }

        private void UserControl_Unloaded(object sender, RoutedEventArgs e) => Suscribir(false);

        // Gastos vencidos sin pagar que se muestran en el aviso rojo
        private List<CuentaMes> _vencidas = new();

        // Mes que se está viendo (siempre el día 1). Empieza en el mes en curso.
        private DateTime _mes = Periodo.InicioMes(DateTime.Today);

        // --- Navegación entre meses ---
        private void BtnMesAnterior_Click(object sender, RoutedEventArgs e) { _mes = _mes.AddMonths(-1); CargarResumen(); }
        private void BtnMesSiguiente_Click(object sender, RoutedEventArgs e) { _mes = _mes.AddMonths(1); CargarResumen(); }
        private void BtnMesActual_Click(object sender, RoutedEventArgs e) { _mes = Periodo.InicioMes(DateTime.Today); CargarResumen(); }

        /// <summary>Cuota de un crédito en el mes que se está viendo.</summary>
        private record CuotaDelMes(Credito Credito, decimal Cuota, bool Pagada);

        // Mes en curso: lo de la pantalla de Créditos. Otros meses: lo pagado (pasados) o lo del plan de pagos (futuros).
        private List<CuotaDelMes> CuotasDelMes(IEnumerable<Credito> creditos, List<PagoCredito> pagos, DateTime mes)
        {
            if (Periodo.EsMesActual(mes))
                return creditos.Select(c => new CuotaDelMes(c, c.CuotaEsteMes, c.Pagado)).ToList();

            var clave = Periodo.Clave(mes);
            return creditos.Select(c => new CuotaDelMes(c,
                                Amortizacion.CuotasEnMes(new[] { c }, pagos, mes),
                                pagos.Any(p => p.CreditoId == c.Id && p.Tipo == Amortizacion.TipoCuota && p.Mes == clave)))
                           .Where(x => x.Cuota > 0)
                           .ToList();
        }

        public void CargarResumen()
        {
            try
            {
                // El resumen corresponde al mes elegido con las flechas (por defecto, el mes en curso)
                var mesActual = _mes;
                bool esActual = Periodo.EsMesActual(mesActual);
                bool esFuturo = string.CompareOrdinal(Periodo.Clave(mesActual), Periodo.MesActual) > 0;
                // --- 1. CONSULTAS A LA BASE DE DATOS (una sola carga para todo el resumen) ---
                Analisis.Datos datos;
                List<Movimiento> inversiones;
                using (var db = new AppDbContext())
                {
                    datos = Analisis.Cargar(db);
                    inversiones = db.Movimientos.Where(m => m.Tipo == "Inversion").ToList();
                }

                // Salario + ingresos extra que aplican este mes
                decimal totalIngresos = Analisis.IngresosDelMes(datos, mesActual);

                // Gastos que aplican este mes (mensuales, únicos y anuales), con su estado de pago
                var cuentasDelMes = CuentasMensuales.DelMes(datos.Cuentas, datos.Pagos, mesActual);

                // Los gastos del mes cuentan aunque ya estén pagados (por lo pagado realmente):
                // pagar no cambia lo que se gastó.
                decimal totalGastosCuentas = cuentasDelMes.Sum(m => m.Real);
                decimal pendienteCuentas = cuentasDelMes.Where(m => !m.Pagada).Sum(m => m.Venta);

                // --- 2. CONSULTA A LA LISTA PERSISTENTE DE CRÉDITOS ---
                // Cada crédito cobra su cuota del mes (lo pagado, o lo que falte si ya va en la última); los saldados no
                var creditos = CreditosView.ListaCreditosPersistente;
                var cuotas = CuotasDelMes(creditos, datos.PagosCredito, mesActual);
                decimal totalCreditosMes = cuotas.Sum(c => c.Cuota);
                decimal pendienteCreditos = cuotas.Where(c => !c.Pagada).Sum(c => c.Cuota);

                // --- 3. CÁLCULO DE VEHÍCULOS ---
                var vehiculos = VehiculosView.listaVehiculos;
                decimal totalV = vehiculos?.Sum(v => v.TotalInvertido) ?? 0;

                // --- 4. OPERACIONES Y BALANCES ---
                decimal totalGastosYCompromisos = totalGastosCuentas + totalCreditosMes;
                decimal balanceNeto = totalIngresos - totalGastosYCompromisos;

                // --- 5. INDICADORES ---
                lblPeriodo.Text = Periodo.Nombre(mesActual);
                btnMesActual.Visibility = esActual ? Visibility.Collapsed : Visibility.Visible;
                lblSubtitulo.Text = esActual ? "Así va tu mes, de un vistazo"
                                  : esFuturo ? "Así se proyecta este mes con tus gastos e ingresos registrados"
                                  : "Así fue este mes";
                lblDashIngresos.Text = totalIngresos.ToString("C0");
                lblDashGastos.Text = totalGastosYCompromisos.ToString("C0");
                lblDashPendiente.Text = $"Pendiente por pagar: {(pendienteCuentas + pendienteCreditos):C0}";
                lblDashBalance.Text = balanceNeto.ToString("C0");
                lblPorcentajeLibre.Text = totalIngresos > 0 && balanceNeto >= 0
                    ? $"{balanceNeto / totalIngresos:P0} libre"
                    : balanceNeto < 0 ? "En déficit" : string.Empty;
                lblPorcentajeLibre.Foreground = Pincel(balanceNeto < 0 ? "#FDA29B" : "#6CE9A6");
                int nVehiculos = vehiculos?.Count ?? 0;
                lblDashMoto.Text = totalV.ToString("C0");
                lblDashVehiculosDetalle.Text = $"Capital invertido · {nVehiculos} {(nVehiculos == 1 ? "vehículo" : "vehículos")}";

                MostrarSalud(balanceNeto);
                MostrarVencidas(esActual ? cuentasDelMes : new List<CuentaMes>());

                // --- 6. ¿A DÓNDE VA TU INGRESO? ---
                MostrarDistribucion(cuentasDelMes, cuotas, totalIngresos, totalGastosYCompromisos, balanceNeto);

                // Proyección del próximo mes, con la predicción de cada gasto
                var siguiente = Analisis.Proyeccion(datos, mesActual.AddMonths(1), 1)[0];
                lblProyeccionSiguiente.Inlines.Clear();
                lblProyeccionSiguiente.Inlines.Add(new Run("Proyección "));
                lblProyeccionSiguiente.Inlines.Add(new Run(siguiente.Mes.ToString("MMMM")) { FontWeight = FontWeights.Bold, Foreground = Pincel("#0F1B2D") });
                lblProyeccionSiguiente.Inlines.Add(new Run($": gastos {NumeroHelper.Moneda(siguiente.TotalGastos)} · balance "));
                lblProyeccionSiguiente.Inlines.Add(new Run(NumeroHelper.Moneda(siguiente.Balance))
                {
                    FontWeight = FontWeights.Bold,
                    Foreground = Pincel(siguiente.Balance < 0 ? "#B42318" : "#075E46")
                });

                // --- 7. POR PAGAR ESTE MES ---
                // Próximas: lo que se cobra el mes siguiente al que se está viendo
                var delSiguiente = CuentasMensuales.DelMes(datos.Cuentas, datos.Pagos, mesActual.AddMonths(1)).Where(c => !c.Pagada).ToList();
                var porPagar = FilasPorPagar(cuentasDelMes, cuotas, delSiguiente, mesActual);
                icProximasFechas.ItemsSource = porPagar;
                lblSinProximas.Visibility = porPagar.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

                // --- 8. COMPARADO CON EL MES ANTERIOR (por categoría, incluye las cuotas de créditos) ---
                var mesAnterior = mesActual.AddMonths(-1);
                var cuentasAnterior = CuentasMensuales.DelMes(datos.Cuentas, datos.Pagos, mesAnterior);
                decimal creditosAnterior = CuotasDelMes(creditos, datos.PagosCredito, mesAnterior).Sum(c => c.Cuota);
                MostrarComparacion(Analisis.PorCategoria(cuentasDelMes, totalCreditosMes),
                                   Analisis.PorCategoria(cuentasAnterior, creditosAnterior), mesAnterior);

                // --- 9. ACCESOS: AHORRO, INVERSIONES Y CRÉDITOS ---
                MostrarAccesos(inversiones, creditos);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error al calcular el resumen del Dashboard: " + ex.Message);
            }
        }

        private static SolidColorBrush Pincel(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;

        private void MostrarSalud(decimal balanceNeto)
        {
            bool sano = balanceNeto >= 0;
            cardSaludFinanciera.Background = Pincel(sano ? "#E7F5EF" : "#FEF3F2");
            var color = Pincel(sano ? "#075E46" : "#B42318");
            icoSalud.Geometria = (Geometry)FindResource(sano ? "IcoCheck" : "IcoAlerta");
            icoSalud.Foreground = color;
            txtMensajeSalud.Foreground = color;
            txtMensajeSalud.Text = sano ? "Finanzas saludables" : "Gastas más de lo que ingresas";
        }

        // Aviso rojo: gastos del mes que vencen hoy o ya vencieron y siguen sin pagar
        private void MostrarVencidas(List<CuentaMes> cuentasDelMes)
        {
            var hoy = DateTime.Today;
            _vencidas = cuentasDelMes.Where(c => !c.Pagada && c.Vence <= hoy).OrderBy(c => c.Vence).ToList();
            bannerVencidos.Visibility = _vencidas.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (_vencidas.Count == 0) return;

            lblVencidos.Inlines.Clear();
            if (_vencidas.Count == 1)
            {
                var c = _vencidas[0];
                lblVencidos.Inlines.Add(new Run("1 pago vencido: ") { FontWeight = FontWeights.Bold });
                lblVencidos.Inlines.Add(new Run($"{c.Descripcion} · "));
                lblVencidos.Inlines.Add(new Run(c.Venta.ToString("C0")) { FontWeight = FontWeights.Bold });
                lblVencidos.Inlines.Add(new Run(c.Vence == hoy ? " · vence hoy" : $" · venció el {c.Vence:dd/MM/yyyy}"));
                btnMarcarPagado.Content = "Marcar pagado";
            }
            else
            {
                lblVencidos.Inlines.Add(new Run($"{_vencidas.Count} pagos vencidos: ") { FontWeight = FontWeights.Bold });
                lblVencidos.Inlines.Add(new Run(string.Join(", ", _vencidas.Select(c => c.Descripcion)) + " · total "));
                lblVencidos.Inlines.Add(new Run(_vencidas.Sum(c => c.Venta).ToString("C0")) { FontWeight = FontWeights.Bold });
                btnMarcarPagado.Content = "Revisar pagos";
            }
        }

        private void MostrarDistribucion(List<CuentaMes> cuentasDelMes, List<CuotaDelMes> cuotas,
                                         decimal ingresos, decimal gastos, decimal disponible)
        {
            var filas = new List<FilaDistribucion>();
            foreach (var c in cuotas.Where(c => c.Cuota > 0))
                filas.Add(new FilaDistribucion { Nombre = $"Crédito · {c.Credito.NombreCustom}", Monto = c.Cuota, Color = "#6941C6" });
            foreach (var cat in Analisis.PorCategoria(cuentasDelMes))
                filas.Add(new FilaDistribucion { Nombre = cat.Nombre, Monto = cat.Total, Color = cat.ColorBarra });
            filas = filas.OrderByDescending(f => f.Monto).ToList();
            if (disponible > 0)
                filas.Add(new FilaDistribucion { Nombre = "Disponible", Monto = disponible, Color = "#0B7A5C", ColorMonto = "#075E46" });

            // Si se gasta más de lo que entra, el 100 % son los gastos
            decimal baseTotal = Math.Max(ingresos, gastos);
            foreach (var f in filas) f.Porcentaje = baseTotal > 0 ? (double)(f.Monto / baseTotal) : 0;

            icDistribucion.ItemsSource = filas;
            lblSinIngresoDistribucion.Visibility = filas.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            // Barra apilada: una columna proporcional por parte, con un pequeño espacio entre ellas
            barraIngreso.Children.Clear();
            barraIngreso.ColumnDefinitions.Clear();
            for (int i = 0; i < filas.Count; i++)
            {
                barraIngreso.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength((double)filas[i].Monto, GridUnitType.Star) });
                var parte = new Border
                {
                    Background = Pincel(filas[i].Color),
                    CornerRadius = new CornerRadius(i == 0 ? 7 : 0, i == filas.Count - 1 ? 7 : 0, i == filas.Count - 1 ? 7 : 0, i == 0 ? 7 : 0),
                    Margin = new Thickness(0, 0, i == filas.Count - 1 ? 0 : 2, 0),
                    ToolTip = $"{filas[i].Nombre}: {filas[i].Monto:C0}"
                };
                Grid.SetColumn(parte, i);
                barraIngreso.Children.Add(parte);
            }
            barraIngreso.Visibility = filas.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // Pendientes del mes (vencidos primero), cuotas de crédito sin pagar y las próximas fechas
        private static List<FilaPorPagar> FilasPorPagar(List<CuentaMes> cuentasDelMes, List<CuotaDelMes> cuotas, List<CuentaMes> proximas, DateTime mes)
        {
            var hoy = DateTime.Today;
            var filas = new List<FilaPorPagar>();

            var pendientes = cuentasDelMes.Where(c => !c.Pagada).OrderBy(c => c.Vence).ToList();
            foreach (var c in pendientes)
            {
                bool vencida = c.Vence <= hoy;
                filas.Add(new FilaPorPagar
                {
                    Dia = c.Vence.Day.ToString("00"), MesCorto = MesCorto(c.Vence), Titulo = c.Descripcion, Monto = c.Venta,
                    Detalle = c.Vence == hoy ? "Vence hoy" : vencida ? $"Vencida el {c.Vence:dd/MM}" : $"Vence el {c.Vence:dd/MM}",
                    Fondo = vencida ? "#FEF3F2" : "#FFFFFF", Borde = vencida ? "#FECDCA" : "#E3E7EE",
                    ColorDia = vencida ? "#B42318" : "#0F1B2D", ColorDetalle = vencida ? "#B42318" : "#93370D",
                });
            }
            // Los vencidos van primero; luego las cuotas de crédito (las más grandes) y el resto de pendientes
            int nVencidos = pendientes.Count(c => c.Vence <= hoy);
            var restantes = filas.Skip(nVencidos).ToList();
            filas = filas.Take(nVencidos).ToList();

            bool esActual = Periodo.EsMesActual(mes);
            foreach (var cr in cuotas.Where(c => !c.Pagada && c.Cuota > 0))
                filas.Add(new FilaPorPagar
                {
                    Dia = "—", MesCorto = MesCorto(mes), Titulo = $"Cuota crédito · {cr.Credito.NombreCustom}", Monto = cr.Cuota,
                    Detalle = esActual ? "Pendiente este mes" : $"Cuota de {mes:MMMM}", Fondo = "#FFFFFF", Borde = "#E3E7EE",
                    ColorDia = "#6941C6", ColorDetalle = "#93370D",
                });

            filas.AddRange(restantes);

            // Próxima ocurrencia (mes siguiente al que se ve): nunca se confunde con lo vencido
            foreach (var p in proximas.OrderBy(p => p.Vence).Take(2))
            {
                int dias = (p.Vence - hoy).Days;
                filas.Add(new FilaPorPagar
                {
                    Dia = p.Vence.Day.ToString("00"), MesCorto = MesCorto(p.Vence), Titulo = $"Próxima: {p.Descripcion}", Monto = p.Venta,
                    Detalle = dias >= 0 ? $"{p.Categoria} · en {dias} {(dias == 1 ? "día" : "días")}" : $"{p.Categoria} · {p.Vence:dd/MM/yyyy}",
                    Fondo = "#F4F6F9", Borde = "#F4F6F9", ColorDia = "#5B6676", ColorDetalle = "#5B6676", ColorTitulo = "#3B4656",
                });
            }
            return filas.Take(5).ToList();
        }

        // En qué categorías se gasta más o menos que el mes anterior
        private void MostrarComparacion(List<TotalCategoria> actual, List<TotalCategoria> anterior, DateTime mesAnterior)
        {
            string nombreAnterior = mesAnterior.ToString("MMMM");
            decimal total = actual.Sum(c => c.Total), totalAnterior = anterior.Sum(c => c.Total);
            decimal cambio = total - totalAnterior;

            lblTituloComparacion.Text = $"Comparado con {nombreAnterior}";
            lblTotalCategorias.Text = NumeroHelper.Moneda(total);
            lblNombreAnterior.Text = char.ToUpper(nombreAnterior[0]) + nombreAnterior.Substring(1);
            lblTotalAnterior.Text = NumeroHelper.Moneda(totalAnterior);

            if (totalAnterior == 0)
            {
                lblCambioTotal.Text = "Sin comparación";
                lblCambioTotal.Foreground = Pincel("#5B6676");
                lblCambioDetalle.Text = $"No hay gastos registrados en {nombreAnterior} para comparar.";
            }
            else if (cambio == 0)
            {
                lblCambioTotal.Text = "Igual";
                lblCambioTotal.Foreground = Pincel("#5B6676");
                lblCambioDetalle.Text = $"Gastas lo mismo que en {nombreAnterior}.";
            }
            else
            {
                bool mas = cambio > 0;
                lblCambioTotal.Text = $"{(mas ? "▲" : "▼")} {Math.Abs(cambio) / totalAnterior:P0}";
                lblCambioTotal.Foreground = Pincel(mas ? "#B42318" : "#075E46");
                lblCambioDetalle.Text = $"Gastas {NumeroHelper.Moneda(Math.Abs(cambio))} {(mas ? "más" : "menos")} que en {nombreAnterior}.";
            }

            // Todas las categorías de los dos meses; primero las que más cambiaron
            var filas = actual.Select(c => c.Nombre).Union(anterior.Select(c => c.Nombre))
                .Select(nombre =>
                {
                    decimal ahora = actual.FirstOrDefault(c => c.Nombre == nombre)?.Total ?? 0;
                    decimal antes = anterior.FirstOrDefault(c => c.Nombre == nombre)?.Total ?? 0;
                    return new FilaComparacion { Nombre = nombre, Total = ahora, Anterior = antes, ColorBarra = Etiquetas.ColorBarra(nombre) };
                })
                .OrderByDescending(f => Math.Abs(f.Total - f.Anterior)).ThenByDescending(f => f.Total)
                .Take(5)
                .ToList();
            icCategoriasMes.ItemsSource = filas;
            lblSinCategorias.Visibility = filas.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private static string MesCorto(DateTime fecha) => fecha.ToString("MMM").TrimEnd('.').ToUpperInvariant();

        private void MostrarAccesos(List<Movimiento> inversiones, IEnumerable<Credito> creditos)
        {
            var metas = IngresosView.ListaMetas;
            lblAhorro.Text = metas.Sum(m => m.MontoActual).ToString("C0");
            lblAhorroDetalle.Text = metas.Count switch
            {
                0 => "Aún no tienes metas de ahorro",
                1 => $"Meta “{NombreMeta(metas[0])}” al {metas[0].Progreso / 100:P0}",
                _ => $"Repartido en {metas.Count} metas"
            };

            lblInversiones.Text = inversiones.Sum(i => i.Costo).ToString("C0");
            lblInversionesDetalle.Text = inversiones.Count == 0
                ? "Sin inversiones registradas"
                : string.Join(" · ", inversiones.Select(i => i.Descripcion).Take(3)) + (inversiones.Count > 3 ? "…" : "");

            var activos = creditos.Where(c => !c.Saldado).ToList();
            decimal deuda = creditos.Sum(c => Math.Max(0, c.Disponible));
            decimal total = creditos.Sum(c => c.Total);
            lblDeuda.Text = deuda.ToString("C0");
            barDeuda.Value = total > 0 ? (double)(creditos.Sum(c => c.CapitalPagado) / total) : 0;

            // Avance: capital ya pagado de todos los créditos y cuánto representa del total prestado
            decimal pagado = creditos.Sum(c => c.CapitalPagado);
            lblFinCredito.Text = NumeroHelper.Moneda(pagado);
            if (total <= 0)
                lblFinCreditoDetalle.Text = "No tienes créditos registrados";
            else if (activos.Count == 0)
                lblFinCreditoDetalle.Text = "¡Todos tus créditos están saldados!";
            else
                lblFinCreditoDetalle.Text = $"{pagado / total:P0} de tus créditos · ¡vas avanzando!";
        }

        private static string NombreMeta(MetaAhorro m) => string.IsNullOrWhiteSpace(m.NombreCustom) ? m.Nombre : m.NombreCustom;

        // --- Acciones ---

        // Un solo vencido: se marca pagado por su valor. Varios: se abre la ventana para revisarlos uno a uno.
        private void BtnMarcarPagado_Click(object sender, RoutedEventArgs e)
        {
            if (_vencidas.Count == 0) return;
            try
            {
                if (_vencidas.Count == 1)
                {
                    var c = _vencidas[0];
                    using (var db = new AppDbContext())
                    {
                        CuentasMensuales.MarcarPagada(db, c.Cuenta.Id, c.Mes, true, c.Venta);
                    }
                    toast.Show($"“{c.Descripcion}” marcado como pagado");
                }
                else
                {
                    new VentanaRecordatorioPagos(_vencidas) { Owner = Window.GetWindow(this) }.ShowDialog();
                }
                CargarResumen();
                MainWindow.Actual?.VerificarVencimientosCuentas();
            }
            catch (Exception ex)
            {
                MisFinanzas.Controles.Aviso.Mostrar("Error al marcar el pago: " + ex.Message);
            }
        }

        private void BtnVerCuentas_Click(object sender, RoutedEventArgs e) => MainWindow.Actual?.IrACuentas();
        private void TarjetaAhorro_Click(object sender, RoutedEventArgs e) => MainWindow.Actual?.IrAIngresos();
        private void TarjetaInversiones_Click(object sender, RoutedEventArgs e) => MainWindow.Actual?.IrAInversiones();
        private void TarjetaCreditos_Click(object sender, RoutedEventArgs e) => MainWindow.Actual?.IrACreditos();
    }

    // ===== Filas que muestra el Dashboard (solo presentación) =====

    public class FilaDistribucion
    {
        public string Nombre { get; set; } = string.Empty;
        public decimal Monto { get; set; }
        public double Porcentaje { get; set; }
        public string PorcentajeTexto => Porcentaje.ToString("P0");
        public string Color { get; set; } = "#8A94A3";
        public string ColorMonto { get; set; } = "#0F1B2D";
    }

    public class FilaPorPagar
    {
        public string Dia { get; set; } = string.Empty;
        public string MesCorto { get; set; } = string.Empty;
        public string Titulo { get; set; } = string.Empty;
        public string Detalle { get; set; } = string.Empty;
        public decimal Monto { get; set; }
        public string Fondo { get; set; } = "#FFFFFF";
        public string Borde { get; set; } = "#E3E7EE";
        public string ColorDia { get; set; } = "#0F1B2D";
        public string ColorTitulo { get; set; } = "#0F1B2D";
        public string ColorDetalle { get; set; } = "#5B6676";
    }

    /// <summary>Una categoría comparada con el mes anterior.</summary>
    public class FilaComparacion
    {
        public string Nombre { get; set; } = string.Empty;
        public decimal Total { get; set; }
        public decimal Anterior { get; set; }
        public string ColorBarra { get; set; } = "#8A94A3";
        private decimal Cambio => Total - Anterior;

        // ▲ más gasto (rojo), ▼ menos gasto (verde), Nuevo (no había el mes anterior), Igual
        public string TextoCambio => Anterior == 0 ? "Nuevo"
                                   : Total == 0 ? "Ya no"
                                   : Cambio == 0 ? "Igual"
                                   : $"{(Cambio > 0 ? "▲" : "▼")} {NumeroHelper.Moneda(Math.Abs(Cambio))}";
        public string FondoCambio => Anterior == 0 ? "#EAF0FD" : Cambio > 0 ? "#FEF3F2" : Cambio < 0 ? "#E7F5EF" : "#EEF1F5";
        public string ColorCambio => Anterior == 0 ? "#1D4FB8" : Cambio > 0 ? "#B42318" : Cambio < 0 ? "#075E46" : "#4B5565";
    }
}
