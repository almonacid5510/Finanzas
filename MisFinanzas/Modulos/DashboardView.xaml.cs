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

        public void CargarResumen()
        {
            try
            {
                // Todo el resumen corresponde al mes en curso
                var mesActual = DateTime.Today;
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
                decimal totalCreditosMes = creditos.Sum(c => c.CuotaEsteMes);
                decimal pendienteCreditos = creditos.Where(c => !c.Pagado).Sum(c => c.CuotaEsteMes);

                // --- 3. CÁLCULO DE VEHÍCULOS ---
                var vehiculos = VehiculosView.listaVehiculos;
                decimal totalV = vehiculos?.Sum(v => v.TotalInvertido) ?? 0;

                // --- 4. OPERACIONES Y BALANCES ---
                decimal totalGastosYCompromisos = totalGastosCuentas + totalCreditosMes;
                decimal balanceNeto = totalIngresos - totalGastosYCompromisos;

                // --- 5. INDICADORES ---
                lblPeriodo.Text = Periodo.Nombre(mesActual);
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
                MostrarVencidas(cuentasDelMes);

                // --- 6. ¿A DÓNDE VA TU INGRESO? ---
                MostrarDistribucion(cuentasDelMes, creditos, totalIngresos, totalGastosYCompromisos, balanceNeto);

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
                var porPagar = FilasPorPagar(cuentasDelMes, creditos, Analisis.ProximasFechas(datos, DateTime.Today));
                icProximasFechas.ItemsSource = porPagar;
                lblSinProximas.Visibility = porPagar.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

                // --- 8. GASTOS POR CATEGORÍA (incluye las cuotas de créditos) ---
                var categorias = Analisis.PorCategoria(cuentasDelMes, totalCreditosMes);
                decimal sumaCategorias = categorias.Sum(c => c.Total);
                icCategoriasMes.ItemsSource = categorias.Select(c => new FilaCategoria
                {
                    Nombre = c.Nombre,
                    Total = c.Total,
                    Proporcion = sumaCategorias > 0 ? (double)(c.Total / sumaCategorias) : 0,
                    ColorBarra = c.ColorBarra
                }).ToList();
                lblTotalCategorias.Text = sumaCategorias.ToString("C0");
                lblSinCategorias.Visibility = categorias.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

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

        private void MostrarDistribucion(List<CuentaMes> cuentasDelMes, IEnumerable<Credito> creditos,
                                         decimal ingresos, decimal gastos, decimal disponible)
        {
            var filas = new List<FilaDistribucion>();
            foreach (var c in creditos.Where(c => c.CuotaEsteMes > 0))
                filas.Add(new FilaDistribucion { Nombre = $"Crédito · {c.NombreCustom}", Monto = c.CuotaEsteMes, Color = "#6941C6" });
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
        private static List<FilaPorPagar> FilasPorPagar(List<CuentaMes> cuentasDelMes, IEnumerable<Credito> creditos, List<CuentaMes> proximas)
        {
            var hoy = DateTime.Today;
            var filas = new List<FilaPorPagar>();

            foreach (var c in cuentasDelMes.Where(c => !c.Pagada).OrderBy(c => c.Vence))
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

            foreach (var cr in creditos.Where(c => c.PuedePagar))
                filas.Add(new FilaPorPagar
                {
                    Dia = "—", MesCorto = MesCorto(hoy), Titulo = $"Cuota crédito · {cr.NombreCustom}", Monto = cr.CuotaEsteMes,
                    Detalle = "Pendiente este mes", Fondo = "#FFFFFF", Borde = "#E3E7EE", ColorDia = "#6941C6", ColorDetalle = "#93370D",
                });

            // Próxima ocurrencia (meses siguientes): nunca se confunde con lo vencido de este mes
            var mesActual = Periodo.MesActual;
            foreach (var p in proximas.Where(p => string.CompareOrdinal(Periodo.Clave(p.Vence), mesActual) > 0).Take(2))
            {
                int dias = (p.Vence - hoy).Days;
                filas.Add(new FilaPorPagar
                {
                    Dia = p.Vence.Day.ToString("00"), MesCorto = MesCorto(p.Vence), Titulo = $"Próxima: {p.Descripcion}", Monto = p.Venta,
                    Detalle = $"{p.Categoria} · en {dias} {(dias == 1 ? "día" : "días")}",
                    Fondo = "#F4F6F9", Borde = "#F4F6F9", ColorDia = "#5B6676", ColorDetalle = "#5B6676", ColorTitulo = "#3B4656",
                });
            }
            return filas.Take(5).ToList();
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

            // El crédito que termina más tarde, según su plan de pagos
            var ultimo = activos.Select(c => (Credito: c, Plan: c.Plan))
                                .Where(x => x.Plan.Count > 0 && !x.Credito.CuotaNoCubreIntereses)
                                .OrderByDescending(x => x.Plan[^1].Mes)
                                .FirstOrDefault();
            if (ultimo.Credito == null)
            {
                lblFinCredito.Text = activos.Count == 0 ? "Sin deudas" : "Sin fecha";
                lblFinCreditoDetalle.Text = activos.Count == 0 ? "No tienes créditos activos" : "Revisa la cuota del crédito";
            }
            else
            {
                lblFinCredito.Text = Periodo.Nombre(ultimo.Plan[^1].Mes);
                lblFinCreditoDetalle.Text = $"{ultimo.Plan.Count} cuotas restantes" + (activos.Count > 1 ? $" · {ultimo.Credito.NombreCustom}" : "");
            }
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

    public class FilaCategoria
    {
        public string Nombre { get; set; } = string.Empty;
        public decimal Total { get; set; }
        public double Proporcion { get; set; }
        public string ColorBarra { get; set; } = "#8A94A3";
        public string Detalle => $"{Proporcion:P0} de tus gastos";
    }
}
