// ============================================================
//  Proyecto: Mis Finanzas
//  Autoria: Juan Almonacid
//  Copyright (c) 2026 Juan Almonacid. Todos los derechos reservados.
// ============================================================
using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;


namespace MisFinanzas.Modulos
{
    public partial class CreditosView : UserControl
    {
        public static ObservableCollection<Credito> ListaCreditosPersistente { get; set; } = new ObservableCollection<Credito>();

        public CreditosView()
        {
            InitializeComponent();

            // 1. Vinculamos la lista persistente al control de esta pestaña
            // Asumiendo que tu ItemsControl en Créditos se llama 'icListaCreditos'
            icListaCreditos.ItemsSource = ListaCreditosPersistente;

            // 2. Si la lista está vacía (porque acabas de borrar la BD), 
            // intentamos cargarla de nuevo por si acaso
            if (ListaCreditosPersistente.Count == 0)
            {
                CargarDatosGlobal();
            }
        }

        private void BtnAgregarCredito_Click(object sender, RoutedEventArgs e)
        {
            if (NumeroHelper.TryParseMoneda(txtTotalCredito.Text, out decimal total) &&
                NumeroHelper.TryParseMoneda(txtCuotaCredito.Text, out decimal cuota) &&
                !string.IsNullOrWhiteSpace(txtNombreCredito.Text))
            {
                try
                {
                    using (var db = new AppDbContext())
                    {
                        var nuevo = new Credito
                        {
                            NombreCustom = txtNombreCredito.Text,
                            Total = total,
                            Disponible = total - (NumeroHelper.TryParseMoneda(txtAbonoCredito.Text, out var a) ? a : 0),
                            CuotaMensual = cuota,
                            TasaEA = double.TryParse(txtTasaEA.Text, out var t) ? t : 0,
                            PlazoMeses = int.TryParse(txtPlazo.Text, out var p) ? p : 0,
                            Pagado = false,
                            Editando = false
                        };

                        db.Creditos.Add(nuevo);
                        db.SaveChanges();
                    }

                    // Refrescamos la lista global
                    CargarDatosGlobal();

                    // Limpiar campos
                    txtNombreCredito.Clear();
                    txtTotalCredito.Clear();
                    txtCuotaCredito.Clear();
                    txtAbonoCredito.Clear(); 
                    txtTasaEA.Clear();       
                    txtPlazo.Clear();
                }
                catch (Exception ex) { MessageBox.Show("Error al guardar: " + ex.Message); }
            }
        }

        // Un solo botón alterna entre Editar (✎) y Guardar (✔)
        private void BtnToggleEdicion_Click(object sender, RoutedEventArgs e)
        {
            var creditoEditado = ((Button)sender).DataContext as Credito;
            if (creditoEditado == null) return;

            if (!creditoEditado.Editando)
            {
                // Estaba en solo lectura -> activar edición
                creditoEditado.Editando = true;
            }
            else
            {
                // Aseguramos que el TextBox con foco confirme su valor (UpdateSourceTrigger=LostFocus)
                var enfocado = System.Windows.Input.Keyboard.FocusedElement as System.Windows.Controls.TextBox;
                enfocado?.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();

                // Estaba en edición -> guardar y volver a solo lectura
                ActualizarCreditoEnBD(creditoEditado);
                creditoEditado.Editando = false;
                toast.Show("Cambios guardados correctamente");
            }
        }

        private void ActualizarCreditoEnBD(Credito c)
        {
            try
            {
                using (var db = new AppDbContext())
                {
                    db.Creditos.Update(c);
                    db.SaveChanges();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al guardar en la base de datos: " + ex.Message);
            }
        }

        // Crédito pendiente de confirmar eliminación
        private Credito? _creditoPendienteEliminar;

        private void BtnEliminar_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            _creditoPendienteEliminar = button?.DataContext as Credito;

            if (_creditoPendienteEliminar != null)
            {
                runNombreCredito.Text = $"\"{_creditoPendienteEliminar.NombreCustom}\"";
                MostrarOverlayConfirm(true);
            }
        }

        private void ConfirmCancelar_Click(object sender, RoutedEventArgs e)
        {
            _creditoPendienteEliminar = null;
            MostrarOverlayConfirm(false);
        }

        private void ConfirmEliminar_Click(object sender, RoutedEventArgs e)
        {
            var credito = _creditoPendienteEliminar;
            MostrarOverlayConfirm(false);
            _creditoPendienteEliminar = null;

            if (credito == null) return;

            try
            {
                using (var db = new AppDbContext())
                {
                    var creditoDb = db.Creditos.Find(credito.Id);
                    if (creditoDb != null)
                    {
                        db.Creditos.Remove(creditoDb);
                        db.SaveChanges();
                    }
                }

                if (ListaCreditosPersistente.Contains(credito))
                    ListaCreditosPersistente.Remove(credito);

                toast.Show("Crédito eliminado");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al eliminar: " + ex.Message);
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
        // En CreditosView.xaml.cs
        public static void CargarDatosGlobal()
        {
            try
            {
                using (var db = new AppDbContext())
                {
                    var lista = db.Creditos.ToList();

                    // El pago de la cuota vale solo para el mes en que se marcó:
                    // al iniciar un mes nuevo, la cuota vuelve a quedar pendiente.
                    var mesActual = Periodo.MesActual;
                    bool reiniciados = false;
                    foreach (var c in lista.Where(c => c.Pagado && c.MesPagado != mesActual))
                    {
                        c.Pagado = false;
                        reiniciados = true;
                    }
                    if (reiniciados) db.SaveChanges();

                    // Usamos el Dispatcher para que la UI de "Cuentas" se entere
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        ListaCreditosPersistente.Clear();
                        foreach (var c in lista)
                        {
                            c.Editando = false; // siempre inician en modo solo lectura
                            ListaCreditosPersistente.Add(c);
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error al cargar créditos: " + ex.Message);
            }
        }
    }
}