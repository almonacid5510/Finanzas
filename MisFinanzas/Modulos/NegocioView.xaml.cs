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

            // Aseguramos que la base de datos y sus tablas existan
            using (var db = new AppDbContext())
            {
                db.Database.EnsureCreated();
            }

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
                NumeroHelper.TryParseMoneda(txtInversionNegocio.Text, out decimal costo) &&
                NumeroHelper.TryParseMoneda(txtValorNegocio.Text, out decimal venta))
            {
                var nuevoMovimiento = new Movimiento
                {
                    Descripcion = txtDescNegocio.Text,
                    Costo = costo,
                    Venta = venta,
                    Fecha = DateTime.Now,
                    Tipo = "Inversion" // <--- AGREGA ESTA LÍNEA AQUÍ
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
                    txtValorNegocio.Clear();
                }
                catch (Exception ex)
                {
                    string detalle = ex.InnerException != null ? $"\nDetalle: {ex.InnerException.Message}" : "";
                    MessageBox.Show($"Error al guardar: {ex.Message}{detalle}");
                }
            }
            else
            {
                MessageBox.Show("Por favor, ingresa datos válidos en todos los campos.");
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
                toast.Show("Movimiento eliminado");
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

        private void ActualizarTotal()
        {
            // Sumamos las ganancias de la lista actual
            decimal total = ListaMovimientos.Sum(m => m.Ganancia);
            lblTotalNegocio.Text = total.ToString("C0");
        }
    }
}