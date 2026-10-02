// ============================================================
//  Proyecto: Mis Finanzas
//  Autoria: Juan Almonacid
//  Copyright (c) 2026 Juan Almonacid. Todos los derechos reservados.
// ============================================================
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace MisFinanzas.Modulos
{
    public partial class CuentasView : UserControl
    {
        // Todas las cuentas registradas (plantillas que se repiten). El Dashboard escucha sus cambios.
        public static ObservableCollection<Movimiento> listaCuentasPersistente { get; set; } = new ObservableCollection<Movimiento>();

        // Mes que se está consultando (siempre el día 1)
        private DateTime _mesSeleccionado = Periodo.InicioMes(DateTime.Today);

        // Cuentas del mes seleccionado, con su vencimiento y estado de pago de ese mes
        private readonly ObservableCollection<CuentaMes> _cuentasDelMes = new ObservableCollection<CuentaMes>();

        // Gasto que se está editando en el formulario (null = registrando uno nuevo)
        private Movimiento? _editando;

        public CuentasView()
        {
            InitializeComponent();

            // Opciones del formulario
            cbDiaPago.ItemsSource = Enumerable.Range(1, 31).ToList();
            cbFrecuencia.ItemsSource = Frecuencias.Todas;
            cbMesAnual.ItemsSource = Enumerable.Range(1, 12).Select(NombreMes).ToList();
            LimpiarFormulario();

            // 1. Cargamos los datos
            CreditosView.CargarDatosGlobal();
            CargarDatosDesdeBD();
            CargarCategorias();

            // 2. Vinculamos las listas
            icCuentas.ItemsSource = _cuentasDelMes;
            icCreditosCompromisos.ItemsSource = CreditosView.ListaCreditosPersistente;

            // 3. Escuchamos los cambios en los créditos solo mientras la pantalla está visible,
            // para no acumular suscripciones sobre la lista estática cada vez que se navega aquí
            Loaded += (s, e) => CreditosView.ListaCreditosPersistente.CollectionChanged += Lista_CollectionChanged;
            Unloaded += (s, e) => CreditosView.ListaCreditosPersistente.CollectionChanged -= Lista_CollectionChanged;

            MostrarMes();
        }

        private static string NombreMes(int mes)
        {
            var nombre = CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(mes);
            return nombre.Length > 0 ? char.ToUpper(nombre[0], CultureInfo.CurrentCulture) + nombre.Substring(1) : nombre;
        }

        private static void CargarDatosDesdeBD()
        {
            try
            {
                using (var db = new AppDbContext())
                {
                    // 1. Traemos los datos de la BD (solo tipo "Cuenta")
                    var movimientos = db.Movimientos
                                       .Where(m => m.Tipo == CuentasMensuales.TipoGasto)
                                       .ToList();

                    // 2. Limpiamos y llenamos la lista estática
                    listaCuentasPersistente.Clear();
                    foreach (var mov in movimientos)
                    {
                        listaCuentasPersistente.Add(mov);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al cargar cuentas: " + ex.Message);
            }
        }

        private void CargarCategorias()
        {
            try
            {
                using var db = new AppDbContext();
                var texto = cbCategoria.Text;
                cbCategoria.ItemsSource = Etiquetas.Lista(db, Etiquetas.Gasto);
                cbCategoria.Text = texto;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error al cargar categorías: " + ex.Message);
            }
        }

        // Recalcula qué cuentas aplican al mes seleccionado y su estado de pago
        private void CargarCuentasDelMes()
        {
            try
            {
                using (var db = new AppDbContext())
                {
                    _cuentasDelMes.Clear();
                    foreach (var c in CuentasMensuales.DelMes(db, _mesSeleccionado))
                        _cuentasDelMes.Add(c);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al cargar las cuentas del mes: " + ex.Message);
            }
            lblSinGastos.Visibility = _cuentasDelMes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ActualizarTotal();
        }

        private void Lista_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs? e)
        {
            ActualizarTotal();
        }

        // --- Navegación entre meses ---
        private void BtnMesAnterior_Click(object sender, RoutedEventArgs e) => CambiarMes(_mesSeleccionado.AddMonths(-1));
        private void BtnMesSiguiente_Click(object sender, RoutedEventArgs e) => CambiarMes(_mesSeleccionado.AddMonths(1));
        private void BtnMesActual_Click(object sender, RoutedEventArgs e) => CambiarMes(DateTime.Today);

        private void CambiarMes(DateTime mes)
        {
            _mesSeleccionado = Periodo.InicioMes(mes);
            MostrarMes();
        }

        private void MostrarMes()
        {
            bool esActual = Periodo.EsMesActual(_mesSeleccionado);
            lblMes.Text = Periodo.Nombre(_mesSeleccionado);
            lblTituloGastos.Text = esActual ? "GASTOS DEL MES" : $"GASTOS DE {Periodo.Nombre(_mesSeleccionado).ToUpper()}";
            btnMesActual.Visibility = esActual ? Visibility.Collapsed : Visibility.Visible;

            // El estado de pago de los créditos solo existe para el mes en curso
            pnlCreditos.Visibility = esActual ? Visibility.Visible : Visibility.Collapsed;
            CargarCuentasDelMes();
        }

        // --- Pestañas: Gastos del mes / Historial / Proyección ---
        private void Pestana_Checked(object sender, RoutedEventArgs e)
        {
            if (pnlMes == null || pnlHistorial == null || pnlProyeccion == null) return; // aún inicializando

            pnlMes.Visibility = tabMes.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            pnlHistorial.Visibility = tabHistorial.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            pnlProyeccion.Visibility = tabProyeccion.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            barraTotal.Visibility = pnlMes.Visibility;

            if (tabHistorial.IsChecked == true) CargarHistorial();
            if (tabProyeccion.IsChecked == true) CargarProyeccion();
        }

        private void CargarHistorial()
        {
            try
            {
                using var db = new AppDbContext();
                var datos = Analisis.Cargar(db);
                var historial = Analisis.Historial(datos, DateTime.Today);
                icHistorial.ItemsSource = historial;

                var gastos = historial.SelectMany(h => CuentasMensuales.DelMes(datos.Cuentas, datos.Pagos, h.Mes));
                icCategoriasHistorial.ItemsSource = Analisis.PorCategoria(gastos);
                lblTituloCategorias.Text = historial.Count == 1
                    ? "Gastos por categoría (este mes)"
                    : $"Gastos por categoría (últimos {historial.Count} meses)";
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al cargar el historial: " + ex.Message);
            }
        }

        private void CargarProyeccion()
        {
            try
            {
                using var db = new AppDbContext();
                var datos = Analisis.Cargar(db);
                icPredicciones.ItemsSource = Analisis.Predicciones(datos, DateTime.Today);
                icProyeccion.ItemsSource = Analisis.Proyeccion(datos, DateTime.Today);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al calcular la proyección: " + ex.Message);
            }
        }

        // Clic en un mes del historial: abre sus gastos
        private void BtnVerMes_Click(object sender, RoutedEventArgs e)
        {
            if (((Button)sender).DataContext is not ResumenMes resumen) return;
            CambiarMes(resumen.Mes);
            tabMes.IsChecked = true;
        }

        // --- Formulario ---
        private void CbFrecuencia_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lblMesAnual == null || cbMesAnual == null) return; // aún inicializando
            var visible = (cbFrecuencia.SelectedItem as string) == Frecuencias.Anual ? Visibility.Visible : Visibility.Collapsed;
            lblMesAnual.Visibility = visible;
            cbMesAnual.Visibility = visible;
        }

        // Atajos: Tag = "descripción|categoría|frecuencia"
        private void BtnAtajo_Click(object sender, RoutedEventArgs e)
        {
            var partes = ((((Button)sender).Tag as string) ?? string.Empty).Split('|');
            if (partes.Length != 3) return;

            txtDescCuenta.Text = partes[0];
            cbCategoria.Text = partes[1];
            cbFrecuencia.SelectedItem = partes[2];
            txtValorCuenta.Focus();

            toast.Show(partes[2] == Frecuencias.Anual
                ? "Elige el mes y el día en que vence cada año"
                : "Escribe el valor y el día del gasto");
        }

        private void LimpiarFormulario()
        {
            _editando = null;
            txtDescCuenta.Clear();
            txtValorCuenta.Clear();
            cbCategoria.Text = string.Empty;
            cbFrecuencia.SelectedItem = Frecuencias.Mensual;
            cbDiaPago.SelectedItem = DateTime.Today.Day;
            cbMesAnual.SelectedIndex = _mesSeleccionado.Month - 1;
            lblTituloFormulario.Text = "Registrar nuevo gasto";
            btnGuardarCuenta.Content = "AGREGAR GASTO";
            btnCancelarEdicion.Visibility = Visibility.Collapsed;
        }

        private void BtnCancelarEdicion_Click(object sender, RoutedEventArgs e) => LimpiarFormulario();

        // Carga el gasto en el formulario para modificarlo
        private void BtnEditar_Click(object sender, RoutedEventArgs e)
        {
            if (((Button)sender).DataContext is not CuentaMes cuentaMes) return;
            var c = cuentaMes.Cuenta;

            _editando = c;
            txtDescCuenta.Text = c.Descripcion;
            cbCategoria.Text = c.Categoria;
            txtValorCuenta.Text = NumeroHelper.FormatearMoneda(c.Venta);
            cbFrecuencia.SelectedItem = cuentaMes.Frecuencia;
            cbMesAnual.SelectedIndex = c.Fecha.Month - 1;
            cbDiaPago.SelectedItem = CuentasMensuales.DiaDePago(c);

            lblTituloFormulario.Text = $"Editar \"{c.Descripcion}\" (aplica a todos sus meses)";
            btnGuardarCuenta.Content = "GUARDAR CAMBIOS";
            btnCancelarEdicion.Visibility = Visibility.Visible;
            txtDescCuenta.Focus();
        }

        private void BtnAgregarCuenta_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtDescCuenta.Text) ||
                !NumeroHelper.TryParseMoneda(txtValorCuenta.Text, out decimal valor) ||
                cbDiaPago.SelectedItem is not int dia ||
                cbFrecuencia.SelectedItem is not string frecuencia)
            {
                MessageBox.Show("Por favor, ingresa una descripción, un valor válido y el día de pago.");
                return;
            }
            int mesDelAnio = cbMesAnual.SelectedIndex + 1;

            try
            {
                using (var db = new AppDbContext())
                {
                    var categoria = Etiquetas.Asegurar(db, cbCategoria.Text, Etiquetas.Gasto);

                    if (_editando == null)
                    {
                        // Nuevo: aplica desde el mes que se está viendo (los anuales, desde su próximo mes)
                        var nueva = new Movimiento
                        {
                            Descripcion = txtDescCuenta.Text.Trim(),
                            Categoria = categoria,
                            Venta = valor,
                            Tipo = CuentasMensuales.TipoGasto,
                            Frecuencia = frecuencia,
                            DiaPago = dia,
                            Fecha = CuentasMensuales.FechaInicio(frecuencia, _mesSeleccionado, mesDelAnio, dia)
                        };
                        db.Movimientos.Add(nueva);
                        db.SaveChanges();
                        listaCuentasPersistente.Add(nueva);

                        toast.Show(frecuencia switch
                        {
                            Frecuencias.Unica => $"Gasto registrado para el {nueva.Fecha:dd/MM/yyyy}",
                            Frecuencias.Anual => $"Gasto anual registrado: vence cada {nueva.Fecha:dd 'de' MMMM}",
                            _ => $"Gasto registrado: se repetirá el día {dia} de cada mes"
                        });
                    }
                    else
                    {
                        var c = db.Movimientos.Find(_editando.Id);
                        if (c != null)
                        {
                            c.Descripcion = txtDescCuenta.Text.Trim();
                            c.Categoria = categoria;
                            c.Venta = valor;
                            c.Frecuencia = frecuencia;
                            c.DiaPago = dia;

                            // Se conserva el mes de inicio; en los anuales se ajusta al mes elegido
                            var inicio = frecuencia == Frecuencias.Anual
                                ? new DateTime(c.Fecha.Year, mesDelAnio, 1)
                                : Periodo.InicioMes(c.Fecha);
                            c.Fecha = CuentasMensuales.Vencimiento(c, inicio);
                            db.SaveChanges();
                        }
                        toast.Show("Cambios guardados");
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al guardar el gasto: " + ex.Message);
                return;
            }

            LimpiarFormulario();
            CargarCategorias();
            CargarCuentasDelMes();
            txtDescCuenta.Focus();
        }

        public void ActualizarTotal()
        {
            // 1. Sumar las cuentas del mes seleccionado que no se han pagado
            decimal totalMovimientos = _cuentasDelMes.Where(c => !c.Pagada).Sum(c => c.Venta);

            // 2. Sumar cuotas de créditos no pagadas (solo aplica al mes en curso)
            decimal totalCreditos = Periodo.EsMesActual(_mesSeleccionado)
                ? CreditosView.ListaCreditosPersistente.Where(c => !c.Pagado).Sum(c => c.CuotaEsteMes)
                : 0;

            lblTotalPendiente.Text = (totalMovimientos + totalCreditos).ToString("C0");
        }


        // Deja de cobrar el gasto desde el mes que se está viendo (los meses anteriores se conservan)
        private void BtnEliminar_Click(object sender, RoutedEventArgs e)
        {
            if (((Button)sender).DataContext is not CuentaMes cuentaMes) return;

            try
            {
                bool borradaCompleta;
                using (var db = new AppDbContext())
                {
                    borradaCompleta = CuentasMensuales.EliminarDesde(db, cuentaMes.Cuenta.Id, _mesSeleccionado);
                }

                var enLista = listaCuentasPersistente.FirstOrDefault(m => m.Id == cuentaMes.Cuenta.Id);
                if (enLista != null) listaCuentasPersistente.Remove(enLista);
                if (_editando?.Id == cuentaMes.Cuenta.Id) LimpiarFormulario();
                CargarCuentasDelMes();

                toast.Show(borradaCompleta
                    ? "Gasto eliminado"
                    : $"\"{cuentaMes.Descripcion}\" ya no se cobrará desde {Periodo.Nombre(_mesSeleccionado)}");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al eliminar: " + ex.Message);
            }
        }

        // Guarda el monto real pagado cuando se termina de editar
        private void TxtMontoPagado_LostFocus(object sender, RoutedEventArgs e)
        {
            var tb = (TextBox)sender;
            if (tb.DataContext is not CuentaMes cuentaMes || !cuentaMes.Pagada) return;

            if (!NumeroHelper.TryParseMoneda(tb.Text, out var monto) || monto < 0)
            {
                tb.Text = cuentaMes.MontoPagadoTexto; // valor inválido: se restaura
                return;
            }
            if (monto == cuentaMes.MontoPagado) return;

            try
            {
                using (var db = new AppDbContext())
                {
                    CuentasMensuales.MarcarPagada(db, cuentaMes.Cuenta.Id, cuentaMes.Mes, true, monto);
                }
                cuentaMes.MontoPagado = monto;
                toast.Show($"Pago real de \"{cuentaMes.Descripcion}\" actualizado");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al guardar el monto pagado: " + ex.Message);
            }
        }

        // Método para actualizar el total apenas marcas el CheckBox
        private void CheckBox_Click(object sender, RoutedEventArgs e)
        {
            // 1. Obtenemos el CheckBox que disparó el evento
            var cb = sender as CheckBox;
            if (cb == null) return;

            try
            {
                // 2. Usamos 'pattern matching' para saber qué tipo de dato es
                // CASO A: Es un gasto del mes: el pago se registra solo para ese mes, por su valor
                // (luego se puede ajustar el monto real en la columna "Pagado real")
                if (cb.DataContext is CuentaMes cuentaMes)
                {
                    using var db = new AppDbContext();
                    if (cuentaMes.Pagada) cuentaMes.MontoPagado = cuentaMes.Venta;
                    CuentasMensuales.MarcarPagada(db, cuentaMes.Cuenta.Id, cuentaMes.Mes, cuentaMes.Pagada, cuentaMes.MontoPagado);
                }
                // CASO B: Es la cuota de un crédito: se reparte entre intereses y capital y baja el saldo
                else if (cb.DataContext is Credito credito)
                {
                    var pago = CreditosView.RegistrarCuota(credito, credito.Pagado);
                    if (pago != null)
                        toast.Show($"\"{credito.NombreCustom}\": {pago.Capital:C0} a capital. Ahora debes {credito.Disponible:C0}");
                    else if (!credito.Pagado)
                        toast.Show($"Pago deshecho. \"{credito.NombreCustom}\" vuelve a {credito.Disponible:C0}");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al guardar el pago: " + ex.Message);
            }

            // 3. Recalculamos el total de la pantalla automáticamente
            ActualizarTotal();
        }
    }
}
