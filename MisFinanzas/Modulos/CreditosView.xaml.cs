// ============================================================
//  Proyecto: Mis Finanzas
//  Autoria: Juan Almonacid
//  Copyright (c) 2026 Juan Almonacid. Todos los derechos reservados.
// ============================================================
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
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

        /// <summary>Muestra el detalle de un crédito (desde "Ver crédito" en otras pantallas).</summary>
        public void Seleccionar(int creditoId)
        {
            foreach (var c in ListaCreditosPersistente) c.VerDetalle = c.Id == creditoId;
        }

        // Clic en el resumen: despliega el detalle de ese crédito y recoge los demás
        private void Resumen_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (((FrameworkElement)sender).DataContext is not Credito credito) return;
            bool abrir = !credito.VerDetalle;
            foreach (var c in ListaCreditosPersistente)
                if (c != credito && !c.Editando) c.VerDetalle = false;
            if (!credito.Editando) credito.VerDetalle = abrir;
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
                        db.PagosCredito.RemoveRange(db.PagosCredito.Where(p => p.CreditoId == credito.Id));
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
        // --- PAGOS: cuota del mes, abono a capital y deshacer ---

        private void BtnPagarCuota_Click(object sender, RoutedEventArgs e)
        {
            if (((Button)sender).DataContext is not Credito credito) return;
            if (RegistrarCuota(credito, true) is PagoCredito pago)
                toast.Show(credito.Saldado
                    ? "¡Última cuota pagada! Crédito saldado"
                    : $"Cuota pagada: {pago.Interes:C0} a intereses y {pago.Capital:C0} a capital");
        }

        private void BtnDeshacerCuota_Click(object sender, RoutedEventArgs e)
        {
            if (((Button)sender).DataContext is not Credito credito) return;
            RegistrarCuota(credito, false);
            toast.Show($"Pago deshecho. Saldo {credito.Disponible:C0}");
        }

        private void BtnAbonar_Click(object sender, RoutedEventArgs e)
        {
            var boton = (Button)sender;
            if (boton.DataContext is not Credito credito || boton.Tag is not TextBox txt) return;

            if (!NumeroHelper.TryParseMoneda(txt.Text, out var monto) || monto <= 0)
            {
                MessageBox.Show("Ingresa un valor válido para el abono.");
                return;
            }
            try
            {
                PagoCredito pago;
                using (var db = new AppDbContext())
                {
                    pago = Amortizacion.Abonar(db, credito.Id, monto);
                }
                Recargar(credito);
                txt.Clear();
                toast.Show(credito.Saldado
                    ? "¡Crédito saldado!"
                    : $"Abono de {pago.Capital:C0} a capital. Nuevo saldo {pago.SaldoNuevo:C0}");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al registrar el abono: " + ex.Message);
            }
        }

        // Deshace un pago del historial (cuota o abono)
        private void BtnDeshacerPago_Click(object sender, RoutedEventArgs e)
        {
            var boton = (Button)sender;
            if (boton.DataContext is not PagoCredito pago || boton.Tag is not Credito credito) return;
            try
            {
                using (var db = new AppDbContext())
                {
                    Amortizacion.Deshacer(db, pago.Id);
                }
                Recargar(credito);
                toast.Show($"Pago deshecho. Saldo {credito.Disponible:C0}");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al deshacer el pago: " + ex.Message);
            }
        }

        /// <summary>
        /// Paga (o deshace) la cuota del mes en curso: reparte intereses y capital y baja el saldo.
        /// La usan también Cuentas y el recordatorio de pagos. Devuelve el pago registrado.
        /// </summary>
        public static PagoCredito? RegistrarCuota(Credito credito, bool pagar)
        {
            try
            {
                PagoCredito? pago = null;
                using (var db = new AppDbContext())
                {
                    if (pagar)
                        pago = Amortizacion.PagarCuota(db, credito.Id, DateTime.Today);
                    else if (Amortizacion.CuotaDelMes(db, credito.Id, DateTime.Today) is PagoCredito existente)
                        Amortizacion.Deshacer(db, existente.Id);
                }
                Recargar(credito);
                return pago;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al guardar el pago: " + ex.Message);
                Recargar(credito);
                return null;
            }
        }

        /// <summary>Copia en el objeto de pantalla lo que quedó en la BD (saldo, plazo, estado e historial).</summary>
        public static void Recargar(Credito credito)
        {
            using var db = new AppDbContext();
            var bd = db.Creditos.AsNoTracking().FirstOrDefault(c => c.Id == credito.Id);
            if (bd == null) return;

            credito.Disponible = bd.Disponible;
            credito.PlazoMeses = bd.PlazoMeses;
            credito.MesPagado = bd.MesPagado;
            credito.Historial = HistorialDe(db.PagosCredito.Where(p => p.CreditoId == credito.Id).ToList());
            credito.Pagado = credito.CuotaPagadaEsteMes != null;
        }

        // Del más reciente al más antiguo
        private static List<PagoCredito> HistorialDe(IEnumerable<PagoCredito> pagos) =>
            pagos.OrderByDescending(p => p.Fecha).ThenByDescending(p => p.Id).ToList();

        public static void CargarDatosGlobal()
        {
            try
            {
                using (var db = new AppDbContext())
                {
                    var lista = db.Creditos.ToList();
                    var pagos = db.PagosCredito.ToList();

                    // La cuota cuenta como pagada solo si hay un pago registrado este mes:
                    // al iniciar un mes nuevo, la cuota vuelve a quedar pendiente.
                    var mesActual = Periodo.MesActual;
                    bool cambios = false;
                    foreach (var c in lista)
                    {
                        c.Historial = HistorialDe(pagos.Where(p => p.CreditoId == c.Id));
                        bool pagado = c.CuotaPagadaEsteMes != null;
                        if (c.Pagado != pagado || c.MesPagado != (pagado ? mesActual : string.Empty))
                        {
                            c.Pagado = pagado;
                            c.MesPagado = pagado ? mesActual : string.Empty;
                            cambios = true;
                        }
                    }
                    if (cambios) db.SaveChanges();

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