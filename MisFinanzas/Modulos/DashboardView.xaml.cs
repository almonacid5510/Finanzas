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

            try
            {
                // 1. Cargamos de forma global los datos de la BD desde el arranque del Dashboard
                // Esto despierta las colecciones persistentes sin obligar a visitar sus pestañas
                CreditosView.CargarDatosGlobal();
                IngresosView.CargarMetasGlobal();

                // 2. Cargamos el primer resumen numérico y visual
                CargarResumen();

                // 3. Nos suscribimos a los cambios de todas las listas.
                // Si agregas, marcas o eliminas algo en la app, el Dashboard se entera al instante.
                if (VehiculosView.listaVehiculos != null)
                    VehiculosView.listaVehiculos.CollectionChanged += (s, e) => CargarResumen();

                if (CuentasView.listaCuentasPersistente != null)
                    CuentasView.listaCuentasPersistente.CollectionChanged += (s, e) => CargarResumen();

                if (CreditosView.ListaCreditosPersistente != null)
                    CreditosView.ListaCreditosPersistente.CollectionChanged += (s, e) => CargarResumen();

                if (IngresosView.ListaMetas != null)
                    IngresosView.ListaMetas.CollectionChanged += (s, e) => CargarResumen();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error en inicialización del Dashboard: " + ex.Message);
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

        /// <summary>
        /// Escucha el evento Loaded que configuramos en el XAML.
        /// Se dispara automáticamente cada vez que el usuario hace clic o vuelve a la pestaña del Dashboard.
        /// </summary>
        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            // Forzamos una recarga fresca desde la Base de Datos para asegurar sincronización absoluta
            IngresosView.CargarMetasGlobal();
            CreditosView.CargarDatosGlobal();
            CargarResumen();
        }

        public void CargarResumen()
        {
            try
            {
                decimal totalIngresos = 0;
                decimal totalGastosCuentas = 0;
                decimal totalCreditosMes = 0;

                // --- 1. CONSULTAS A LA BASE DE DATOS (Entity Framework) ---
                using (var db = new AppDbContext())
                {
                    // Sumamos los movimientos registrados como "Ingreso"
                    totalIngresos = db.Movimientos
                                      .Where(m => m.Tipo == "Ingreso")
                                      .AsEnumerable()
                                      .Sum(m => m.Venta);

                    // Sumamos los gastos corrientes ("Cuenta") que NO estén pagados aún
                    totalGastosCuentas = db.Movimientos
                                           .Where(m => m.Tipo == "Cuenta" && !m.EstaPagado)
                                           .AsEnumerable()
                                           .Sum(m => m.Venta);
                }

                // --- 2. CONSULTA A LA LISTA PERSISTENTE DE CRÉDITOS ---
                // Sumamos las cuotas mensuales de compromisos activos que falten por pagar
                if (CreditosView.ListaCreditosPersistente != null)
                {
                    totalCreditosMes = CreditosView.ListaCreditosPersistente
                                                   .Where(c => !c.Pagado)
                                                   .Sum(c => c.CuotaMensual);
                }

                // --- 3. CÁLCULO DE VEHÍCULOS ---
                decimal totalV = VehiculosView.listaVehiculos?.Sum(v => v.TotalInvertido) ?? 0;

                // --- 4. OPERACIONES Y BALANCES ---
                decimal totalGastosYCompromisos = totalGastosCuentas + totalCreditosMes;
                decimal balanceNeto = totalIngresos - totalGastosYCompromisos;

                // --- 5. ASIGNACIÓN A LAS ETIQUETAS DEL XAML ---
                this.lblDashIngresos.Text = totalIngresos.ToString("C0");
                this.lblDashGastos.Text = totalGastosYCompromisos.ToString("C0");
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
                    txtMensajeSalud.Text = "Atención: Estás gastando más de lo que ingresas.";
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
                    txtMensajeSalud.Text = "¡Tus finanzas están saludables!";
                    txtMensajeSalud.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(39, 103, 73));
                }

                // --- 6. ASIGNACIÓN Y REFRESCO DE LAS METAS ---
                icMetasCirculares.ItemsSource = null;
                icMetasCirculares.ItemsSource = IngresosView.ListaMetas;

                // --- 7. ESTADO DE CUENTAS ---
                System.Collections.Generic.List<Movimiento> cuentasDb;
                using (var db = new AppDbContext())
                {
                    cuentasDb = db.Movimientos.Where(m => m.Tipo == "Cuenta").ToList();
                }

                var listaEstados = new System.Collections.Generic.List<CuentaEstadoViewModel>();
                var hoy = DateTime.Now.Date;

                foreach (var c in cuentasDb)
                {
                    var vm = new CuentaEstadoViewModel { Cuenta = c };
                    if (c.EstaPagado)
                    {
                        vm.BackgroundColor = "#F0FFF4"; // Verde
                        vm.BorderColor = "#9AE6B4";
                        vm.TextColor = "#276749";
                        vm.MensajeEstado = "Pagada ✔️";
                    }
                    else if (c.Fecha.Date < hoy)
                    {
                        vm.BackgroundColor = "#FFF5F5"; // Rojo
                        vm.BorderColor = "#FEB2B2";
                        vm.TextColor = "#C53030";
                        vm.MensajeEstado = $"Vencida el {c.Fecha:dd/MMM}";
                    }
                    else
                    {
                        vm.BackgroundColor = "#EBF8FF"; // Azul
                        vm.BorderColor = "#90CDF4";
                        vm.TextColor = "#2B6CB0";
                        vm.MensajeEstado = $"Vence el {c.Fecha:dd/MMM}";
                    }
                    listaEstados.Add(vm);
                }
                
                icEstadoCuentas.ItemsSource = null;
                icEstadoCuentas.ItemsSource = listaEstados.OrderBy(x => x.Cuenta.EstaPagado).ThenBy(x => x.Cuenta.Fecha).ToList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error al calcular el resumen del Dashboard: " + ex.Message);
            }
        }
    }

    public class CuentaEstadoViewModel
    {
        public Movimiento Cuenta { get; set; }
        public string BackgroundColor { get; set; }
        public string BorderColor { get; set; }
        public string TextColor { get; set; }
        public string MensajeEstado { get; set; }
    }
}