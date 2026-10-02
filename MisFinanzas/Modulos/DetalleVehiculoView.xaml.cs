// ============================================================
//  Proyecto: Mis Finanzas
//  Autoria: Juan Almonacid
//  Copyright (c) 2026 Juan Almonacid. Todos los derechos reservados.
// ============================================================
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
                this.VehiculoActual = db.Vehiculos.FirstOrDefault(v => v.Id == vehiculoId)
                                      ?? new Vehiculo { Nombre = "Vehículo no encontrado" };
            }

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
            if (!NumeroHelper.TryParseMoneda(txtValor.Text, out decimal valor)) return; // Validación simple

            var nuevoGasto = new GastoVehiculo
            {
                Concepto = txtConcepto.Text,
                Valor = valor,
                Fecha = DateTime.Now,
                VehiculoId = VehiculoActual.Id
            };

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

        // --- Edición / guardado de un gasto (mismo flujo que Créditos) ---
        private void BtnToggleEdicionGasto_Click(object sender, RoutedEventArgs e)
        {
            var gasto = ((Button)sender).DataContext as GastoVehiculo;
            if (gasto == null) return;

            if (!gasto.Editando)
            {
                gasto.Editando = true;
            }
            else
            {
                // Confirmar el valor del TextBox con foco (UpdateSourceTrigger=LostFocus)
                var enfocado = System.Windows.Input.Keyboard.FocusedElement as TextBox;
                enfocado?.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();

                try
                {
                    using (var db = new AppDbContext())
                    {
                        db.GastosVehiculo.Update(gasto);
                        db.SaveChanges();
                    }
                    gasto.Editando = false;
                    VehiculoActual.NotificarCambioTotal();
                    toast.Show("Cambios guardados correctamente");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error al guardar: {ex.Message}");
                }
            }
        }

        // --- Eliminación con modal de confirmación ---
        private GastoVehiculo? _gastoPendienteEliminar;

        private void BtnEliminarGasto_Click(object sender, RoutedEventArgs e)
        {
            _gastoPendienteEliminar = ((Button)sender).DataContext as GastoVehiculo;
            if (_gastoPendienteEliminar != null)
            {
                runNombreGasto.Text = $"\"{_gastoPendienteEliminar.Concepto}\"";
                MostrarOverlayConfirm(true);
            }
        }

        private void ConfirmCancelar_Click(object sender, RoutedEventArgs e)
        {
            _gastoPendienteEliminar = null;
            MostrarOverlayConfirm(false);
        }

        private void ConfirmEliminar_Click(object sender, RoutedEventArgs e)
        {
            var gasto = _gastoPendienteEliminar;
            MostrarOverlayConfirm(false);
            _gastoPendienteEliminar = null;

            if (gasto == null) return;

            try
            {
                using (var db = new AppDbContext())
                {
                    var dbGasto = db.GastosVehiculo.Find(gasto.Id);
                    if (dbGasto != null)
                    {
                        db.GastosVehiculo.Remove(dbGasto);
                        db.SaveChanges();
                    }
                }
                VehiculoActual.Gastos.Remove(gasto);
                VehiculoActual.NotificarCambioTotal();
                toast.Show("Registro eliminado");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al eliminar: {ex.Message}");
            }
        }

        private void MostrarOverlayConfirm(bool visible)
        {
            if (visible)
            {
                OverlayConfirm.Visibility = Visibility.Visible;
                OverlayConfirm.BeginAnimation(OpacityProperty,
                    new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
            }
            else
            {
                OverlayConfirm.Visibility = Visibility.Collapsed;
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