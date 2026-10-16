// ============================================================
//  Proyecto: Mis Finanzas
//  Autoria: Juan Almonacid
//  Copyright (c) 2026 Juan Almonacid. Todos los derechos reservados.
// ============================================================
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
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
            cbDiaPagoCredito.ItemsSource = Enumerable.Range(1, 31).ToList();
            cbDiaPagoCredito.SelectedItem = DateTime.Today.Day;
            cbTipoCredito.ItemsSource = TiposCredito.Todos;
            cbTipoCredito.SelectedItem = TiposCredito.LibreInversion;

            // 1. Vinculamos la lista persistente al control de esta pestaña
            // Asumiendo que tu ItemsControl en Créditos se llama 'icListaCreditos'
            icListaCreditos.ItemsSource = ListaCreditosPersistente;

            // 2. Si la lista está vacía (porque acabas de borrar la BD), 
            // intentamos cargarla de nuevo por si acaso
            if (ListaCreditosPersistente.Count == 0)
            {
                CargarDatosGlobal();
            }

            // Al entrar se ve solo la lista; el detalle se abre al elegir un crédito
            Seleccionar(0);
        }

        // "Deuda total $ X · N créditos activos"
        private void ActualizarEncabezado()
        {
            var activos = ListaCreditosPersistente.Where(c => !c.Saldado).ToList();
            lblResumenDeuda.Inlines.Clear();
            lblResumenDeuda.Inlines.Add(new System.Windows.Documents.Run("Deuda total "));
            lblResumenDeuda.Inlines.Add(new System.Windows.Documents.Run(NumeroHelper.Moneda(ListaCreditosPersistente.Sum(c => Math.Max(0, c.Disponible))))
            {
                FontWeight = FontWeights.Bold,
                Foreground = (System.Windows.Media.Brush)FindResource("Rojo")
            });
            lblResumenDeuda.Inlines.Add(new System.Windows.Documents.Run(
                $" · {activos.Count} {(activos.Count == 1 ? "crédito activo" : "créditos activos")}"));

            // Pagar, deshacer, editar el día de pago o eliminar cambian el punto rojo de cuotas vencidas
            MainWindow.Actual?.VerificarVencimientosCuentas();
        }

        // Muestra u oculta el formulario de nuevo crédito
        private void BtnNuevoCredito_Click(object sender, RoutedEventArgs e)
        {
            bool abrir = pnlNuevoCredito.Visibility != Visibility.Visible;
            pnlNuevoCredito.Visibility = abrir ? Visibility.Visible : Visibility.Collapsed;
            if (abrir) txtNombreCredito.Focus();
        }

        private bool EsTarjetaNueva => cbTipoCredito.SelectedItem as string == TiposCredito.Tarjeta;

        // La tarjeta se registra con su cupo y lo que se debe hoy; no tiene cuota fija ni plazo
        private void CbTipoCredito_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lblTotalCredito == null) return; // aún cargando la pantalla
            bool tarjeta = EsTarjetaNueva;
            lblTotalCredito.Content = tarjeta ? "Cupo total" : "Monto total";
            lblAbonoCredito.Content = tarjeta ? "Ya debes (opcional)" : "Ya pagado";
            txtAbonoCredito.ToolTip = tarjeta ? "Solo lo que debes hoy de compras anteriores. Si no debes nada, déjalo vacío." : null;
            var visible = tarjeta ? Visibility.Hidden : Visibility.Visible;
            lblCuotaCredito.Visibility = txtCuotaCredito.Visibility = visible;
            lblPlazo.Visibility = txtPlazo.Visibility = visible;
            Controles.Ui.SetPista(txtNombreCredito, tarjeta ? "Ej. Visa Bancolombia" : "Ej. Libre inversión");
        }

        private void BtnAgregarCredito_Click(object sender, RoutedEventArgs e)
        {
            if (cbDiaPagoCredito.SelectedItem is not int diaPago)
            {
                MisFinanzas.Controles.Aviso.Mostrar("Elige el día del mes en que pagas la cuota.");
                return;
            }
            bool tarjeta = EsTarjetaNueva;
            decimal cuota = 0;
            if (NumeroHelper.TryParseMoneda(txtTotalCredito.Text, out decimal total) &&
                (tarjeta || NumeroHelper.TryParseMoneda(txtCuotaCredito.Text, out cuota)) &&
                !string.IsNullOrWhiteSpace(txtNombreCredito.Text))
            {
                // Tarjeta: lo que ya se debe es aparte del cupo (una tarjeta nueva o al día no debe nada)
                if (tarjeta && NumeroHelper.TryParseMoneda(txtAbonoCredito.Text, out var debe) && total > 0 && debe >= total)
                {
                    MisFinanzas.Controles.Aviso.Mostrar(debe == total
                        ? "Escribiste el cupo también en «Ya debes»: la tarjeta quedaría sin cupo disponible. En «Ya debes» va solo lo que debes hoy de compras anteriores; si no debes nada, déjalo vacío."
                        : "Lo que ya debes no puede ser mayor que el cupo total de la tarjeta.");
                    txtAbonoCredito.Focus();
                    return;
                }
                try
                {
                    using (var db = new AppDbContext())
                    {
                        var nuevo = new Credito
                        {
                            NombreCustom = txtNombreCredito.Text,
                            Modalidad = cbTipoCredito.SelectedItem as string ?? TiposCredito.LibreInversion,
                            Total = total,
                            // Tarjeta: el segundo campo es lo que se debe hoy. Préstamo: lo que ya se pagó.
                            Disponible = tarjeta
                                ? (NumeroHelper.TryParseMoneda(txtAbonoCredito.Text, out var deuda) ? deuda : 0)
                                : total - (NumeroHelper.TryParseMoneda(txtAbonoCredito.Text, out var a) ? a : 0),
                            CuotaMensual = tarjeta ? 0 : cuota,
                            TasaEA = double.TryParse(txtTasaEA.Text, out var t) ? t : 0,
                            PlazoMeses = tarjeta ? 0 : (int.TryParse(txtPlazo.Text, out var p) ? p : 0),
                            DiaPago = diaPago,
                            Pagado = false,
                            Editando = false
                        };

                        db.Creditos.Add(nuevo);
                        db.SaveChanges();
                    }

                    // Refrescamos la lista global y mostramos el crédito nuevo
                    CargarDatosGlobal();
                    Seleccionar(ListaCreditosPersistente.Count > 0 ? ListaCreditosPersistente.Max(c => c.Id) : 0);
                    pnlNuevoCredito.Visibility = Visibility.Collapsed;
                    toast.Show(tarjeta ? "Tarjeta registrada: elígela en «Pagado con» al registrar un gasto" : "Crédito registrado");

                    // Limpiar campos
                    txtNombreCredito.Clear();
                    txtTotalCredito.Clear();
                    txtCuotaCredito.Clear();
                    txtAbonoCredito.Clear(); 
                    txtTasaEA.Clear();
                    txtPlazo.Clear();
                    cbDiaPagoCredito.SelectedItem = DateTime.Today.Day;
                    cbTipoCredito.SelectedItem = TiposCredito.LibreInversion;
                }
                catch (Exception ex) { MisFinanzas.Controles.Aviso.Mostrar("Error al guardar: " + ex.Message); }
            }
        }

        /// <summary>
        /// Muestra el detalle de un crédito (también desde "Ver crédito" en otras pantallas).
        /// Con 0 (o un id que no existe) cierra el detalle y la lista ocupa todo el ancho.
        /// </summary>
        public void Seleccionar(int creditoId)
        {
            var elegido = ListaCreditosPersistente.FirstOrDefault(c => c.Id == creditoId);
            foreach (var c in ListaCreditosPersistente)
            {
                c.VerDetalle = c == elegido;
                if (c != elegido) c.Editando = false;
            }
            detalleCredito.Content = elegido;
            pnlDetalle.Visibility = elegido == null ? Visibility.Collapsed : Visibility.Visible;
            Grid.SetColumnSpan(pnlLista, elegido == null ? 3 : 1);
            lblSinCreditos.Visibility = Visibility.Collapsed;
            ActualizarEncabezado();
        }

        // Clic en un crédito de la lista: abre su detalle; clic otra vez en el mismo, lo cierra
        private void Resumen_Click(object sender, RoutedEventArgs e)
        {
            if (((FrameworkElement)sender).DataContext is Credito credito)
                Seleccionar(credito.VerDetalle && !credito.Editando ? 0 : credito.Id);
        }

        private void BtnCerrarDetalle_Click(object sender, RoutedEventArgs e) => Seleccionar(0);

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
                ActualizarEncabezado();
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
                MisFinanzas.Controles.Aviso.Mostrar("Error al guardar en la base de datos: " + ex.Message);
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
                Seleccionar(0);

                toast.Show("Crédito eliminado");
            }
            catch (Exception ex)
            {
                MisFinanzas.Controles.Aviso.Mostrar("Error al eliminar: " + ex.Message);
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
            var pago = RegistrarCuota(credito, true);
            ActualizarEncabezado();
            if (pago != null)
                toast.Show(credito.Saldado
                    ? "¡Última cuota pagada! Crédito saldado"
                    : $"Cuota pagada: {pago.Interes:C0} a intereses y {pago.Capital:C0} a capital");
        }

        // Tarjeta: pagar la cuota del mes (la parte de capital baja la deuda; los intereses son gasto del mes)
        private void BtnPagarCuotaTarjeta_Click(object sender, RoutedEventArgs e)
        {
            if (((Button)sender).DataContext is not Credito tarjeta || !tarjeta.PuedePagarCuotaTarjeta) return;
            try
            {
                PagoCredito pago;
                using (var db = new AppDbContext())
                    pago = Amortizacion.PagarCuotaTarjeta(db, tarjeta.Id, DateTime.Today, tarjeta.InteresMesTarjeta, tarjeta.CapitalMesTarjeta);
                Recargar(tarjeta);
                ActualizarEncabezado();
                toast.Show($"Cuota pagada: {pago.Interes:C0} de intereses y {pago.Capital:C0} a capital");
            }
            catch (Exception ex) { MisFinanzas.Controles.Aviso.Mostrar("Error al pagar la cuota: " + ex.Message); }
        }

        private void BtnDeshacerCuota_Click(object sender, RoutedEventArgs e)
        {
            if (((Button)sender).DataContext is not Credito credito) return;
            RegistrarCuota(credito, false);
            ActualizarEncabezado();
            toast.Show($"Pago deshecho. Saldo {credito.Disponible:C0}");
        }

        private void BtnAbonar_Click(object sender, RoutedEventArgs e)
        {
            var boton = (Button)sender;
            if (boton.DataContext is not Credito credito || boton.Tag is not TextBox txt) return;

            if (!NumeroHelper.TryParseMoneda(txt.Text, out var monto) || monto <= 0)
            {
                MisFinanzas.Controles.Aviso.Mostrar("Ingresa un valor válido para el abono.");
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
                ActualizarEncabezado();
                txt.Clear();
                toast.Show(credito.EsTarjeta
                    ? $"Pago de {pago.Capital:C0} a la tarjeta. Cupo disponible {credito.CupoDisponible:C0}"
                    : credito.Saldado
                        ? "¡Crédito saldado!"
                        : $"Abono de {pago.Capital:C0} a capital. Nuevo saldo {pago.SaldoNuevo:C0}");
            }
            catch (Exception ex)
            {
                MisFinanzas.Controles.Aviso.Mostrar("Error al registrar el abono: " + ex.Message);
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
                ActualizarEncabezado();
                toast.Show($"Pago deshecho. Saldo {credito.Disponible:C0}");
            }
            catch (Exception ex)
            {
                MisFinanzas.Controles.Aviso.Mostrar("Error al deshacer el pago: " + ex.Message);
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
                MisFinanzas.Controles.Aviso.Mostrar("Error al guardar el pago: " + ex.Message);
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
            if (credito.EsTarjeta)
            {
                credito.Compras = ComprasDe(db, credito.Id);
                credito.CalcularCuotaTarjeta(ComprasVigentes(db, credito.Id));
            }
        }

        /// <summary>Recarga la deuda y las compras de todos los créditos en pantalla (sin cambiar los objetos).</summary>
        public static void RecargarDeudas()
        {
            foreach (var c in ListaCreditosPersistente.ToList()) Recargar(c);
        }

        // Compras con la tarjeta, de la más reciente a la más antigua
        private static List<GastoDiario> ComprasDe(AppDbContext db, int tarjetaId) =>
            db.GastosDiarios.Where(g => g.TarjetaId == tarjetaId).OrderByDescending(g => g.Fecha).Take(30).ToList();

        // Compras que aún pueden tener cuotas (hasta 36 cuotas atrás)
        public static List<GastoDiario> ComprasVigentes(AppDbContext db, int tarjetaId)
        {
            var desde = Periodo.InicioMes(DateTime.Today).AddMonths(-37);
            return db.GastosDiarios.Where(g => g.TarjetaId == tarjetaId && g.Fecha >= desde).ToList();
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
                        if (c.EsTarjeta)
                        {
                            c.Compras = ComprasDe(db, c.Id);
                            c.CalcularCuotaTarjeta(ComprasVigentes(db, c.Id));
                        }
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