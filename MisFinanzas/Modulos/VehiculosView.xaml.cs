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
            contDetalle.Content = vehiculo == null ? null : new DetalleVehiculoView(vehiculo) { PedirConfirmacion = PedirConfirmacion };
            pnlDetalle.Visibility = vehiculo == null ? Visibility.Collapsed : Visibility.Visible;
            lblSinDetalle.Visibility = vehiculo == null ? Visibility.Visible : Visibility.Collapsed;
            lblSinVehiculos.Visibility = listaVehiculos.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ActualizarTotalGeneral();
        }

        // Vehículo que se está modificando en el formulario (null = se registra uno nuevo)
        private Vehiculo? _editando;

        // Carga el vehículo en el formulario para modificarlo
        private void BtnEditarVehiculo_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is not Vehiculo vehiculo) return;

            _editando = vehiculo;
            txtNombreVehiculo.Text = vehiculo.Nombre;
            txtPlaca.Text = vehiculo.Placa;
            cbTipoVehiculo.SelectedItem = cbTipoVehiculo.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(i => (string)i.Content == vehiculo.Tipo) ?? cbTipoVehiculo.Items[0];

            lblTituloFormulario.Text = $"Editar \"{vehiculo.Nombre}\"";
            btnGuardarVehiculo.Content = "Guardar";
            btnCancelarEdicion.Visibility = Visibility.Visible;
            txtNombreVehiculo.Focus();
        }

        private void BtnCancelarEdicion_Click(object sender, RoutedEventArgs e) => LimpiarFormulario();

        private void LimpiarFormulario()
        {
            _editando = null;
            txtNombreVehiculo.Clear();
            txtPlaca.Clear();
            cbTipoVehiculo.SelectedIndex = 0;
            lblTituloFormulario.Text = "Registrar vehículo";
            btnGuardarVehiculo.Content = "Registrar";
            btnCancelarEdicion.Visibility = Visibility.Collapsed;
        }

        private void BtnAgregarVehiculo_Click(object sender, RoutedEventArgs e)
        {
            if (_editando != null)
            {
                GuardarEdicion(_editando);
                return;
            }
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

        private void GuardarEdicion(Vehiculo vehiculo)
        {
            if (string.IsNullOrWhiteSpace(txtNombreVehiculo.Text))
            {
                MisFinanzas.Controles.Aviso.Mostrar("Escribe el alias del vehículo.");
                return;
            }
            var nombre = txtNombreVehiculo.Text.Trim();
            var tipo = (cbTipoVehiculo.SelectedItem as ComboBoxItem)?.Content.ToString() ?? vehiculo.Tipo;
            var placa = txtPlaca.Text.ToUpper();

            try
            {
                using (var db = new AppDbContext())
                {
                    var v = db.Vehiculos.Find(vehiculo.Id);
                    if (v != null)
                    {
                        v.Nombre = nombre;
                        v.Tipo = tipo;
                        v.Placa = placa;
                        db.SaveChanges();
                    }
                }
                // Mismo objeto de la lista: la tarjeta y el detalle se actualizan solos
                vehiculo.Nombre = nombre;
                vehiculo.Tipo = tipo;
                vehiculo.Placa = placa;
                LimpiarFormulario();
                toast.Show("Cambios guardados");
            }
            catch (Exception ex)
            {
                MisFinanzas.Controles.Aviso.Mostrar($"Error al guardar: {ex.Message}");
            }
        }

        // Acción pendiente de confirmar (eliminar un vehículo o un registro del detalle)
        private Action? _alConfirmar;

        private void PedirConfirmacion(string titulo, string nombre, string final, Action alConfirmar)
        {
            txtTituloConfirm.Text = titulo;
            runNombreConfirm.Text = nombre;
            runFinalConfirm.Text = final;
            _alConfirmar = alConfirmar;
            MostrarOverlayConfirm(true);
        }

        private void BtnEliminarVehiculo_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.DataContext is Vehiculo vehiculo)
                PedirConfirmacion("Eliminar vehículo", $"\"{vehiculo.Nombre}\"",
                    " y todo su historial? Esta acción no se puede deshacer.", () => EliminarVehiculo(vehiculo));
        }

        private void ConfirmCancelar_Click(object sender, RoutedEventArgs e)
        {
            _alConfirmar = null;
            MostrarOverlayConfirm(false);
        }

        private void ConfirmEliminar_Click(object sender, RoutedEventArgs e)
        {
            var accion = _alConfirmar;
            MostrarOverlayConfirm(false);
            _alConfirmar = null;
            accion?.Invoke();
        }

        private void EliminarVehiculo(Vehiculo vehiculo)
        {
            List<GastoDiario> sinVehiculo = new();
            try
            {
                using (var db = new AppDbContext())
                {
                    // Se eliminan también sus gastos para no dejar registros huérfanos.
                    // Un solo SaveChanges: o se borra todo o no se borra nada.
                    db.GastosVehiculo.RemoveRange(db.GastosVehiculo.Where(g => g.VehiculoId == vehiculo.Id));
                    // Los gastos de Día a día de este vehículo se conservan (son gasto del mes), sin vehículo
                    sinVehiculo = db.GastosDiarios.Where(g => g.VehiculoId == vehiculo.Id).ToList();
                    foreach (var g in sinVehiculo) g.VehiculoId = null;
                    db.Vehiculos.Remove(vehiculo);
                    db.SaveChanges();
                }
                foreach (var g in sinVehiculo) SincronizacionCelular.RegistrarCambio(g, borrado: false);
                listaVehiculos.Remove(vehiculo);
                if (_editando == vehiculo) LimpiarFormulario();
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