using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MisFinanzas.Modulos;

namespace MisFinanzas.Modulos
{
    public partial class DetalleVehiculoView : UserControl
    {
        public Vehiculo VehiculoActual { get; set; }

        public DetalleVehiculoView(int vehiculoId)
        {
            InitializeComponent();

            using (var db = new AppDbContext())
            {
                this.VehiculoActual = db.Vehiculos.FirstOrDefault(v => v.Id == vehiculoId);
            }

            if (this.VehiculoActual == null)
                this.VehiculoActual = new Vehiculo { Nombre = "Vehículo no encontrado" };

            if (this.VehiculoActual.Gastos == null)
                this.VehiculoActual.Gastos = new ObservableCollection<GastoVehiculo>();

            // --- AGREGA ESTO AQUÍ ---
            // Cada vez que se agregue un gasto a la lista, avisamos que el TotalInvertido cambió
            this.VehiculoActual.Gastos.CollectionChanged += (s, e) =>
            {
                VehiculoActual.NotificarCambioTotal();
            };

            this.DataContext = VehiculoActual;
            CargarGastos();
        }

        private void CargarGastos()
        {
            using (var db = new AppDbContext())
            {
                var gastosGuardados = db.GastosVehiculo
                                        .Where(g => g.VehiculoId == VehiculoActual.Id)
                                        .ToList();

                VehiculoActual.Gastos.Clear();
                foreach (var g in gastosGuardados)
                {
                    VehiculoActual.Gastos.Add(g);
                }
            }

            icRepuestos.ItemsSource = VehiculoActual.Gastos;

            // Notificar a la interfaz que el total cambió al cargar los gastos
            VehiculoActual.NotificarCambioTotal();
        }

        private void BtnAgregarGasto_Click(object sender, RoutedEventArgs e)
        {
            // 1. Declarar la variable AQUÍ (fuera del try/if)
            GastoVehiculo nuevoGasto = null;

            // Asumiendo que aquí tienes tu lógica de creación
            if (decimal.TryParse(txtValor.Text, out decimal valor))
            {
                nuevoGasto = new GastoVehiculo
                {
                    Concepto = txtConcepto.Text,
                    Valor = valor,
                    Fecha = DateTime.Now,
                    VehiculoId = VehiculoActual.Id
                };
            }
            else { return; } // Validación simple

            // 2. Ahora el bloque try puede "ver" a nuevoGasto
            try
            {
                using (var db = new AppDbContext())
                {
                    db.GastosVehiculo.Add(nuevoGasto);
                    db.SaveChanges();
                }

                VehiculoActual.Gastos.Add(nuevoGasto);
                VehiculoActual.NotificarCambioTotal();

                txtConcepto.Clear();
                txtValor.Clear();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}");
            }
        }

        private void BtnRegresar_Click(object sender, RoutedEventArgs e)
        {
            var mainWindow = Window.GetWindow(this) as MainWindow;
            if (mainWindow != null)
            {
                // Al crear un "new", forzamos a que se ejecute el constructor 
                // y el evento Loaded que configuramos arriba.
                mainWindow.ContenedorPrincipal.Content = new VehiculosView();
            }
        }
    }
}