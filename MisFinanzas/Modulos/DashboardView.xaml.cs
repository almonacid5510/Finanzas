// ============================================================
//  Proyecto: Mis Finanzas
//  Autoria: Juan Almonacid
//  Copyright (c) 2026 Juan Almonacid. Todos los derechos reservados.
// ============================================================
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Collections.Specialized; // Requerido para capturar los eventos CollectionChanged

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

        // Valores para el gráfico de barras Ingresos vs Gastos
        private decimal _graphIngresos;
        private decimal _graphGastos;

        // Ajusta el ancho de las barras proporcional al mayor valor, según el espacio disponible
        private void ActualizarBarras()
        {
            // El contenedor real de la barra es el Border padre (fondo gris)
            double disponible = (barIngresos.Parent as FrameworkElement)?.ActualWidth ?? 0;
            if (disponible <= 0) return; // aún sin layout; se reintentará en SizeChanged

            decimal maximo = Math.Max(_graphIngresos, _graphGastos);
            if (maximo <= 0)
            {
                barIngresos.Width = 0;
                barGastos.Width = 0;
                return;
            }

            barIngresos.Width = disponible * (double)(_graphIngresos / maximo);
            barGastos.Width = disponible * (double)(_graphGastos / maximo);
        }

        private void Barras_SizeChanged(object sender, SizeChangedEventArgs e) => ActualizarBarras();

        // Desplazamiento de la página con la rueda del mouse, más ágil que el predeterminado de WPF
        // (que avanza muy poco por cada paso de la rueda)
        private void Pagina_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            var pagina = (ScrollViewer)sender;
            pagina.ScrollToVerticalOffset(pagina.VerticalOffset - e.Delta * 0.8);
            e.Handled = true;
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

        public void CargarResumen()
        {
            try
            {
                // Todo el resumen corresponde al mes en curso
                var mesActual = DateTime.Today;
                // --- 1. CONSULTAS A LA BASE DE DATOS (una sola carga para todo el resumen) ---
                Analisis.Datos datos;
                using (var db = new AppDbContext())
                {
                    datos = Analisis.Cargar(db);
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
                decimal totalV = VehiculosView.listaVehiculos?.Sum(v => v.TotalInvertido) ?? 0;

                // --- 4. OPERACIONES Y BALANCES ---
                decimal totalGastosYCompromisos = totalGastosCuentas + totalCreditosMes;
                decimal balanceNeto = totalIngresos - totalGastosYCompromisos;

                // --- 5. ASIGNACIÓN A LAS ETIQUETAS DEL XAML ---
                this.lblDashIngresos.Text = totalIngresos.ToString("C0");
                this.lblDashGastos.Text = totalGastosYCompromisos.ToString("C0");
                this.lblDashPendiente.Text = $"Pendiente: {(pendienteCuentas + pendienteCreditos):C0}";
                this.lblPeriodo.Text = Periodo.Nombre(mesActual);
                this.lblDashBalance.Text = balanceNeto.ToString("C0");
                this.lblDashMoto.Text = totalV.ToString("C0");

                // --- 5b. GRÁFICO INGRESOS VS GASTOS ---
                _graphIngresos = totalIngresos;
                _graphGastos = totalGastosYCompromisos;
                lblGraphIngresos.Text = totalIngresos.ToString("C0");
                lblGraphGastos.Text = totalGastosYCompromisos.ToString("C0");
                lblGraphDiferencia.Text = balanceNeto.ToString("C0");
                lblGraphDiferencia.Foreground = new System.Windows.Media.SolidColorBrush(
                    balanceNeto < 0
                        ? System.Windows.Media.Color.FromRgb(197, 48, 48)
                        : System.Windows.Media.Color.FromRgb(47, 133, 90));
                ActualizarBarras();

                // Proyección del próximo mes, con la predicción de cada gasto
                var siguiente = Analisis.Proyeccion(datos, mesActual.AddMonths(1), 1)[0];
                lblProyeccionSiguiente.Text = $"Proyección {siguiente.Nombre}: gastos {siguiente.TotalGastos:C0} · balance {siguiente.Balance:C0}";

                // Gastos del mes por categoría (incluye las cuotas de créditos)
                var categorias = Analisis.PorCategoria(cuentasDelMes, totalCreditosMes);
                icCategoriasMes.ItemsSource = categorias;
                lblSinCategorias.Visibility = categorias.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

                // Próximos vencimientos e impuestos/gastos anuales
                var proximas = Analisis.ProximasFechas(datos, DateTime.Today).Take(8).ToList();
                icProximasFechas.ItemsSource = proximas;
                lblSinProximas.Visibility = proximas.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

                // Control dinámico del color para la tarjeta de Disponible/Neto
                if (balanceNeto < 0)
                {
                    // Rojo si el balance es negativo
                    this.lblDashBalance.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(197, 48, 48));
                    
                    // Actualizar tarjeta de salud financiera a estado de alerta
                    cardSaludFinanciera.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 245, 245)); // Rojo muy suave
                    cardSaludFinanciera.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(254, 178, 178));
                    txtEmojiSalud.Text = "😟";
                    txtEmojiSalud.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(197, 48, 48));
                    txtMensajeSalud.Text = "Gastas más de lo que ingresas";
                    txtMensajeSalud.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(155, 44, 44));
                }
                else
                {
                    // Gris oscuro original (#4A5568) si estás en números positivos
                    this.lblDashBalance.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(74, 85, 104));
                    
                    // Actualizar tarjeta de salud financiera a estado saludable
                    cardSaludFinanciera.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(240, 255, 244)); // Verde muy suave
                    cardSaludFinanciera.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(154, 230, 180));
                    txtEmojiSalud.Text = "😊";
                    txtEmojiSalud.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(47, 133, 90));
                    txtMensajeSalud.Text = "Finanzas saludables";
                    txtMensajeSalud.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(39, 103, 73));
                }

                // --- 6. ASIGNACIÓN Y REFRESCO DE LAS METAS ---
                icMetasCirculares.ItemsSource = null;
                icMetasCirculares.ItemsSource = IngresosView.ListaMetas;
                lblSinMetas.Visibility = IngresosView.ListaMetas.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

                // --- 7. ESTADO DE CUENTAS DEL MES ---
                var listaEstados = new System.Collections.Generic.List<CuentaEstadoViewModel>();
                foreach (var c in cuentasDelMes)
                {
                    var vm = new CuentaEstadoViewModel { Cuenta = c.Cuenta, Pagada = c.Pagada, Vence = c.Vence };
                    if (c.Pagada)
                    {
                        vm.BackgroundColor = "#F0FFF4"; // Verde
                        vm.BorderColor = "#9AE6B4";
                        vm.TextColor = "#276749";
                        vm.MensajeEstado = "Pagada ✔️";
                    }
                    else if (c.Vencida)
                    {
                        vm.BackgroundColor = "#FFF5F5"; // Rojo
                        vm.BorderColor = "#FEB2B2";
                        vm.TextColor = "#C53030";
                        vm.MensajeEstado = $"Vencida el {c.Vence:dd/MMM}";
                    }
                    else
                    {
                        vm.BackgroundColor = "#EBF8FF"; // Azul
                        vm.BorderColor = "#90CDF4";
                        vm.TextColor = "#2B6CB0";
                        vm.MensajeEstado = $"Vence el {c.Vence:dd/MMM}";
                    }
                    listaEstados.Add(vm);
                }
                
                icEstadoCuentas.ItemsSource = null;
                icEstadoCuentas.ItemsSource = listaEstados.OrderBy(x => x.Pagada).ThenBy(x => x.Vence).ToList();
                lblSinCuentas.Visibility = listaEstados.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error al calcular el resumen del Dashboard: " + ex.Message);
            }
        }
    }

    public class CuentaEstadoViewModel
    {
        public Movimiento Cuenta { get; set; } = new Movimiento();
        public bool Pagada { get; set; }
        public DateTime Vence { get; set; }
        public string BackgroundColor { get; set; } = string.Empty;
        public string BorderColor { get; set; } = string.Empty;
        public string TextColor { get; set; } = string.Empty;
        public string MensajeEstado { get; set; } = string.Empty;
    }
}