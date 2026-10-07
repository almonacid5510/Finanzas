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
using System.Windows.Input;
using MisFinanzas.Modulos;

namespace MisFinanzas.Modulos
{
    public partial class VehiculosView : UserControl
    {
        // Se mantiene static para que el Dashboard pueda acceder al cálculo total
        public static ObservableCollection<Vehiculo> listaVehiculos { get; set; } = new ObservableCollection<Vehiculo>();

        public VehiculosView()
        {
            InitializeComponent();
            icListaVehiculos.ItemsSource = listaVehiculos;
            this.Loaded += (s, e) => CargarVehiculos();
        }

        // Vehículo que se muestra a la derecha
        private int _seleccionadoId;

        private void CargarVehiculos()
        {
            using (var db = new AppDbContext())
            {
                // 1. Traemos los vehículos actualizados de la BD
                var datos = db.Vehiculos.ToList();

                listaVehiculos.Clear();

                foreach (var v in datos)
                {
                    // 2. IMPORTANTE: Cargamos los gastos aquí mismo para que el total no sea 0
                    var gastosDeEsteVehiculo = db.GastosVehiculo
                                                 .Where(g => g.VehiculoId == v.Id)
                                                 .ToList();

                    v.Gastos = new ObservableCollection<GastoVehiculo>(gastosDeEsteVehiculo);

                    // 3. Agregamos a la lista que ve el XAML
                    listaVehiculos.Add(v);

                    // 4. Notificamos por si acaso
                    v.NotificarCambioTotal();
                    v.PropertyChanged += Vehiculo_PropertyChanged;
                }
            }
            Seleccionar(listaVehiculos.FirstOrDefault(v => v.Id == _seleccionadoId) ?? listaVehiculos.FirstOrDefault());
        }

        // El total del encabezado sigue a los gastos que se registran en el detalle
        private void Vehiculo_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Vehiculo.TotalInvertido)) ActualizarTotalGeneral();
        }

        private void ActualizarTotalGeneral() =>
            runCapitalTotal.Text = NumeroHelper.Moneda(listaVehiculos.Sum(v => v.TotalInvertido));

        // Muestra el detalle del vehículo a la derecha (sin cambiar de pantalla)
        private void Seleccionar(Vehiculo? vehiculo)
        {
            foreach (var v in listaVehiculos) v.Seleccionado = v == vehiculo;
            _seleccionadoId = vehiculo?.Id ?? 0;

            pnlDetalle.DataContext = vehiculo;
            contDetalle.Content = vehiculo == null ? null : new DetalleVehiculoView(vehiculo);
            pnlDetalle.Visibility = vehiculo == null ? Visibility.Collapsed : Visibility.Visible;
            lblSinDetalle.Visibility = vehiculo == null ? Visibility.Visible : Visibility.Collapsed;
            lblSinVehiculos.Visibility = listaVehiculos.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ActualizarTotalGeneral();
        }

        private void BtnAgregarVehiculo_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(txtNombreVehiculo.Text) && cbTipoVehiculo.SelectedItem != null)
            {
                var nuevo = new Vehiculo
                {
                    Nombre = txtNombreVehiculo.Text,
                    Tipo = (cbTipoVehiculo.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "Vehículo",
                    Placa = txtPlaca.Text.ToUpper(),
                    Fecha = DateTime.Now
                };

                try
                {
                    using (var db = new AppDbContext())
                    {
                        db.Vehiculos.Add(nuevo);
                        db.SaveChanges();
                    }

                    // ObservableCollection se encarga de actualizar la UI sola
                    listaVehiculos.Add(nuevo);
                    nuevo.PropertyChanged += Vehiculo_PropertyChanged;
                    Seleccionar(nuevo);

                    txtNombreVehiculo.Clear();
                    txtPlaca.Clear();
                    toast.Show("Vehículo registrado");
                }
                catch (Exception ex)
                {
                    MisFinanzas.Controles.Aviso.Mostrar($"Error: {ex.Message}");
                }
            }
        }

        // Vehículo pendiente de confirmar eliminación
        private Vehiculo? _vehiculoPendienteEliminar;

        private void BtnEliminarVehiculo_Click(object sender, RoutedEventArgs e)
        {
            var boton = sender as Button;
            _vehiculoPendienteEliminar = boton?.DataContext as Vehiculo;

            if (_vehiculoPendienteEliminar != null)
            {
                runNombreVehiculo.Text = $"\"{_vehiculoPendienteEliminar.Nombre}\"";
                MostrarOverlayConfirm(true);
            }
        }

        private void ConfirmCancelar_Click(object sender, RoutedEventArgs e)
        {
            _vehiculoPendienteEliminar = null;
            MostrarOverlayConfirm(false);
        }

        private void ConfirmEliminar_Click(object sender, RoutedEventArgs e)
        {
            var vehiculo = _vehiculoPendienteEliminar;
            MostrarOverlayConfirm(false);
            _vehiculoPendienteEliminar = null;

            if (vehiculo == null) return;

            try
            {
                using (var db = new AppDbContext())
                {
                    // Se eliminan también sus gastos para no dejar registros huérfanos.
                    // Un solo SaveChanges: o se borra todo o no se borra nada.
                    db.GastosVehiculo.RemoveRange(db.GastosVehiculo.Where(g => g.VehiculoId == vehiculo.Id));
                    db.Vehiculos.Remove(vehiculo);
                    db.SaveChanges();
                }
                listaVehiculos.Remove(vehiculo);
                Seleccionar(listaVehiculos.FirstOrDefault());
                toast.Show("Vehículo eliminado");
            }
            catch (Exception ex)
            {
                MisFinanzas.Controles.Aviso.Mostrar($"Error al eliminar: {ex.Message}");
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

        // Clic en un vehículo de la lista: muestra su detalle a la derecha
        private void TarjetaVehiculo_Click(object sender, RoutedEventArgs e)
        {
            if (((FrameworkElement)sender).DataContext is Vehiculo vehiculo) Seleccionar(vehiculo);
        }
    }
}