// ============================================================
//  Proyecto: Mis Finanzas
//  Autoria: Juan Almonacid
//  Copyright (c) 2026 Juan Almonacid. Todos los derechos reservados.
// ============================================================
using System;
using System.Linq;
using System.Windows;
using System.Windows.Media.Animation;
using Microsoft.EntityFrameworkCore;
using MisFinanzas.Modulos;

namespace MisFinanzas
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            InicializarBaseDeDatos();
            CargarDatosIniciales();
            VerificarVencimientosCuentas();
            ContenedorPrincipal.Content = new DashboardView();
            SetActiveNav(btnNavDashboard);
        }

        // Anchos del sidebar
        private const double SidebarColapsado = 68;
        private const double SidebarExpandido = 210;

        // Marca visualmente el botón de navegación activo y limpia los demás
        private void SetActiveNav(System.Windows.Controls.Button activo)
        {
            foreach (var btn in new[] { btnNavDashboard, btnNavIngresos, btnNavCuentas, btnNavCreditos, btnNavVehiculo, btnNavNegocio })
            {
                NavState.SetIsActive(btn, btn == activo);
            }
        }

        // --- Sidebar colapsable con transición suave ---
        private void Sidebar_MouseEnter(object sender, RoutedEventArgs e) => AnimarSidebar(SidebarExpandido, 1);

        private void Sidebar_MouseLeave(object sender, RoutedEventArgs e) => AnimarSidebar(SidebarColapsado, 0);

        private void AnimarSidebar(double anchoDestino, double opacidadTexto)
        {
            var duracion = new Duration(TimeSpan.FromMilliseconds(200));
            var ease = new QuadraticEase { EasingMode = EasingMode.EaseInOut };

            var animAncho = new DoubleAnimation(anchoDestino, duracion) { EasingFunction = ease };
            Sidebar.BeginAnimation(FrameworkElement.WidthProperty, animAncho);

            var animTexto = new DoubleAnimation(opacidadTexto, duracion) { EasingFunction = ease };
            txtLogo.BeginAnimation(UIElement.OpacityProperty, animTexto);
        }

        private void InicializarBaseDeDatos()
        {
            try
            {
                using (var db = new AppDbContext())
                {
                    // Crea la BD si no existe y agrega/retira tablas y columnas según el modelo
                    EsquemaBD.Actualizar(db);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error crítico de BD: {ex.Message}. Verifica tu modelo de datos.");
            }
        }

        private void CargarDatosIniciales()
        {
            try
            {
                using (var db = new AppDbContext())
                {
                    // 1. Cargamos desde la BD
                    var vehiculosGuardados = db.Vehiculos.ToList();

                    // 2. ACTUALIZAMOS LA LISTA ESTATICA PARA QUE TODA LA APP VEA LOS DATOS
                    VehiculosView.listaVehiculos.Clear();
                    foreach (var v in vehiculosGuardados)
                    {
                        // Importante: Cargamos también los gastos asociados para que el cálculo funcione
                        v.Gastos = new System.Collections.ObjectModel.ObservableCollection<GastoVehiculo>(
                            db.GastosVehiculo.Where(g => g.VehiculoId == v.Id)
                        );
                        VehiculosView.listaVehiculos.Add(v);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error crítico al cargar datos iniciales: {ex.Message}");
            }
        }

        private async void VerificarVencimientosCuentas()
        {
            try
            {
                // Esperar 2 segundos antes de mostrar el pop-up
                await System.Threading.Tasks.Task.Delay(2000);

                using (var db = new AppDbContext())
                {
                    var hoy = DateTime.Now.Date;
                    var cuentasPendientes = db.Movimientos
                        .Where(m => m.Tipo == "Cuenta" && !m.EstaPagado && m.Fecha.Date <= hoy)
                        .ToList();

                    if (cuentasPendientes.Any())
                    {
                        var ventana = new VentanaRecordatorioPagos(cuentasPendientes);
                        ventana.ShowDialog();

                        // Las cuentas marcadas como pagadas en el recordatorio deben reflejarse ya
                        (ContenedorPrincipal.Content as DashboardView)?.CargarResumen();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al verificar vencimientos: {ex.Message}");
            }
        }

        // --- Navegación ---
        private void NavDashboard_Click(object sender, RoutedEventArgs e) { ContenedorPrincipal.Content = new DashboardView(); SetActiveNav(btnNavDashboard); }
        private void NavIngresos_Click(object sender, RoutedEventArgs e) { ContenedorPrincipal.Content = new IngresosView(); SetActiveNav(btnNavIngresos); }
        private void NavCuentas_Click(object sender, RoutedEventArgs e) { ContenedorPrincipal.Content = new CuentasView(); SetActiveNav(btnNavCuentas); }
        private void NavCreditos_Click(object sender, RoutedEventArgs e) { ContenedorPrincipal.Content = new CreditosView(); SetActiveNav(btnNavCreditos); }
        private void NavVehiculo_Click(object sender, RoutedEventArgs e) { ContenedorPrincipal.Content = new VehiculosView(); SetActiveNav(btnNavVehiculo); }
        private void NavNegocio_Click(object sender, RoutedEventArgs e) { ContenedorPrincipal.Content = new NegocioView(); SetActiveNav(btnNavNegocio); }
    }
}