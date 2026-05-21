using System;
using System.Linq;
using System.Windows;
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
            ContenedorPrincipal.Content = new DashboardView();
        }

        private void InicializarBaseDeDatos()
        {
            try
            {
                using (var db = new AppDbContext())
                {
                    // EnsureCreated solo crea si NO existe.
                    db.Database.EnsureCreated();
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

        // --- Navegación ---
        private void NavDashboard_Click(object sender, RoutedEventArgs e) => ContenedorPrincipal.Content = new DashboardView();
        private void NavIngresos_Click(object sender, RoutedEventArgs e) => ContenedorPrincipal.Content = new IngresosView();
        private void NavCuentas_Click(object sender, RoutedEventArgs e) => ContenedorPrincipal.Content = new CuentasView();
        private void NavCreditos_Click(object sender, RoutedEventArgs e) => ContenedorPrincipal.Content = new CreditosView();
        private void NavVehiculo_Click(object sender, RoutedEventArgs e) => ContenedorPrincipal.Content = new VehiculosView();
        private void NavNegocio_Click(object sender, RoutedEventArgs e) => ContenedorPrincipal.Content = new NegocioView();
    }
}