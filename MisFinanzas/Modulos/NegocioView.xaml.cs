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
using Microsoft.EntityFrameworkCore;

namespace MisFinanzas.Modulos
{
    public partial class NegocioView : UserControl
    {
        // Usamos una propiedad estática para mantener los datos en memoria mientras la app esté abierta
        public static ObservableCollection<Movimiento> ListaMovimientos { get; set; } = new ObservableCollection<Movimiento>();

        public NegocioView()
        {
            InitializeComponent();

            icListaNegocios.ItemsSource = ListaMovimientos;

            // EVENTO CRUCIAL: Cada vez que entres a la pestaña, se recargan los datos del disco
            this.Loaded += (s, e) => CargarMovimientosDesdeBD();
        }

        private void CargarMovimientosDesdeBD()
        {
            try
            {
                using (var db = new AppDbContext())
                {
                    // Traemos los movimientos guardados en el archivo .db
                    var movimientosBD = db.Movimientos.Where(m => m.Tipo == "Inversion").ToList();

                    ListaMovimientos.Clear();
                    foreach (var m in movimientosBD)
                    {
                        ListaMovimientos.Add(m);
                    }
                }
                ActualizarTotal();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al cargar datos desde la base de datos: {ex.Message}");
            }
        }

        private void BtnAgregar_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(txtDescNegocio.Text) &&
                NumeroHelper.TryParseMoneda(txtInversionNegocio.Text, out decimal montoInicial))
            {
                // Costo guarda el total invertido; los abonos se le van sumando (como las metas de ahorro)
                var nuevoMovimiento = new Movimiento
                {
                    Descripcion = txtDescNegocio.Text,
                    Costo = montoInicial,
                    Fecha = DateTime.Now,
                    Tipo = "Inversion"
                };

                try
                {
                    using (var db = new AppDbContext())
                    {
                        // Guardado físico en el archivo MisFinanzas_Real.db
                        db.Movimientos.Add(nuevoMovimiento);
                        db.SaveChanges();
                    }

                    // Actualización de la lista en pantalla
                    ListaMovimientos.Add(nuevoMovimiento);
                    ActualizarTotal();

                    // Limpieza de campos
                    txtDescNegocio.Clear();
                    txtInversionNegocio.Clear();
                    toast.Show("Inversión registrada");
                }
                catch (Exception ex)
                {
                    string detalle = ex.InnerException != null ? $"\nDetalle: {ex.InnerException.Message}" : "";
                    MessageBox.Show($"Error al guardar: {ex.Message}{detalle}");
                }
            }
            else
            {
                MessageBox.Show("Por favor, ingresa el nombre y un monto válido.");
            }
        }

        // Movimiento pendiente de confirmar eliminación
        private Movimiento? _movPendienteEliminar;

        private void BtnEliminar_Click(object sender, RoutedEventArgs e)
        {
            var btn = (Button)sender;
            _movPendienteEliminar = btn.DataContext as Movimiento;

            if (_movPendienteEliminar != null)
            {
                runNombreMov.Text = $"\"{_movPendienteEliminar.Descripcion}\"";
                MostrarOverlayConfirm(true);
            }
        }

        private void ConfirmCancelar_Click(object sender, RoutedEventArgs e)
        {
            _movPendienteEliminar = null;
            MostrarOverlayConfirm(false);
        }

        private void ConfirmEliminar_Click(object sender, RoutedEventArgs e)
        {
            var movimiento = _movPendienteEliminar;
            MostrarOverlayConfirm(false);
            _movPendienteEliminar = null;

            if (movimiento == null) return;

            try
            {
                using (var db = new AppDbContext())
                {
                    db.Movimientos.Remove(movimiento);
                    db.SaveChanges();
                }
                ListaMovimientos.Remove(movimiento);
                ActualizarTotal();
                toast.Show("Inversión eliminada");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al eliminar de la base de datos: {ex.Message}");
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

        // Suma un abono al total invertido (mismo flujo que abonar a una meta de ahorro)
        private void BtnAbonar_Click(object sender, RoutedEventArgs e)
        {
            var boton = (Button)sender;
            if (boton.DataContext is not Movimiento inversion) return;

            var textBox = (boton.Parent as Grid)?.Children.OfType<TextBox>().FirstOrDefault();
            if (textBox == null || !NumeroHelper.TryParseMoneda(textBox.Text, out decimal abono) || abono <= 0) return;

            try
            {
                using (var db = new AppDbContext())
                {
                    var inversionDb = db.Movimientos.Find(inversion.Id);
                    if (inversionDb == null) return;
                    inversionDb.Costo += abono;
                    db.SaveChanges();
                    inversion.Costo = inversionDb.Costo;
                }

                textBox.Clear();
                icListaNegocios.Items.Refresh();
                ActualizarTotal();
                toast.Show($"Abono de {abono:C0} registrado");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al registrar el abono: {ex.Message}");
            }
        }

        private void ActualizarTotal()
        {
            // Total invertido entre todas las inversiones
            decimal total = ListaMovimientos.Sum(m => m.Costo);
            lblTotalNegocio.Text = total.ToString("C0");
        }
    }
}