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

        // Filtro de la lista ("Todas", "Vencidas", "Pendientes", "Pagadas") y las vistas filtradas
        private string _filtro = "Todas";
        private System.Windows.Data.ListCollectionView? _vistaCuentas;
        private System.Windows.Data.ListCollectionView? _vistaCompromisos;
        private bool PasaFiltro(string estadoTexto) => _filtro == "Todas" || EstadoPago.Grupo(estadoTexto) == _filtro;

        // Gasto que se está editando en el formulario (null = registrando uno nuevo)
        private Movimiento? _editando;
        private DateTime _fechaColores = DateTime.Today;
        private readonly System.Windows.Threading.DispatcherTimer _relojColores = new() { Interval=TimeSpan.FromMinutes(1) };

        private void RevisarFechaColores()
        {
            var hoy=DateTime.Today;
            if (hoy == _fechaColores) return;
            if (_mesSeleccionado == Periodo.InicioMes(_fechaColores)) _mesSeleccionado=Periodo.InicioMes(hoy);
            _fechaColores=hoy;
            CreditosView.CargarDatosGlobal();
            MostrarMes();
        }

        public CuentasView()
        {
            InitializeComponent();
            Etiquetas.ActivarOpcionNueva(cbCategoria);

            // Opciones del formulario
            cbDiaPago.ItemsSource = Enumerable.Range(1, 31).ToList();
            cbFrecuencia.ItemsSource = Frecuencias.Opciones;
            cbMesAnual.ItemsSource = Enumerable.Range(1, 12).Select(NombreMes).ToList();
            LimpiarFormulario();

            // 1. Cargamos los datos
            CreditosView.CargarDatosGlobal();
            CargarDatosDesdeBD();
            CargarCategorias();

            // 2. Vinculamos las listas
            _vistaCuentas = new System.Windows.Data.ListCollectionView(_cuentasDelMes) { Filter = c => PasaFiltro(((CuentaMes)c).EstadoTexto) };
            icCuentas.ItemsSource = _vistaCuentas;
            // Las tarjetas no tienen cuota fija: no van en los compromisos del mes
            _vistaCompromisos = new System.Windows.Data.ListCollectionView(CreditosView.ListaCreditosPersistente)
                { Filter = c => c is Credito cr && !cr.EsTarjeta && PasaFiltro(cr.EstadoTexto) };
            icCreditosCompromisos.ItemsSource = _vistaCompromisos;

            // 3. Escuchamos los cambios en los créditos solo mientras la pantalla está visible,
            // para no acumular suscripciones sobre la lista estática cada vez que se navega aquí
            _relojColores.Tick += (_,_) => RevisarFechaColores();
            Loaded += (s,e) => {
                CreditosView.ListaCreditosPersistente.CollectionChanged += Lista_CollectionChanged;
                RevisarFechaColores(); _relojColores.Start();
            };
            Unloaded += (s,e) => {
                CreditosView.ListaCreditosPersistente.CollectionChanged -= Lista_CollectionChanged;
                _relojColores.Stop();
            };

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
                MisFinanzas.Controles.Aviso.Mostrar("Error al cargar cuentas: " + ex.Message);
            }
        }

        private void CargarCategorias()
        {
            try
            {
                using var db = new AppDbContext();
                var texto = cbCategoria.Text;
                cbCategoria.ItemsSource = Etiquetas.ListaConNueva(db, Etiquetas.Gasto);
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
                MisFinanzas.Controles.Aviso.Mostrar("Error al cargar las cuentas del mes: " + ex.Message);
            }
            ActualizarTotal();
        }

        private void Filtro_Checked(object sender, RoutedEventArgs e)
        {
            _filtro = (sender as FrameworkElement)?.Tag as string ?? "Todas";
            ActualizarFiltros();
        }

        // Cuenta cuántos gastos (y cuotas del mes en curso) hay en cada estado y aplica el filtro elegido
        private void ActualizarFiltros()
        {
            if (_vistaCuentas == null || _vistaCompromisos == null) return; // aún inicializando
            bool esActual = Periodo.EsMesActual(_mesSeleccionado);
            var estados = _cuentasDelMes.Select(c => c.EstadoTexto)
                .Concat(esActual ? CreditosView.ListaCreditosPersistente.Where(c => !c.EsTarjeta).Select(c => c.EstadoTexto) : Enumerable.Empty<string>())
                .Select(EstadoPago.Grupo).ToList();            lblConteoTodas.Text = estados.Count.ToString();
            lblConteoVencidas.Text = estados.Count(g => g == "Vencidas").ToString();
            lblConteoPendientes.Text = estados.Count(g => g == "Pendientes").ToString();
            lblConteoPagadas.Text = estados.Count(g => g == "Pagadas").ToString();

            _vistaCuentas.Refresh();
            _vistaCompromisos.Refresh();
            pnlCreditos.Visibility = esActual && _vistaCompromisos.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            bool vacio = _vistaCuentas.Count == 0 && pnlCreditos.Visibility != Visibility.Visible;
            lblSinGastos.Text = _cuentasDelMes.Count == 0 && _filtro == "Todas"
                ? "No hay gastos para este mes. Registra el primero con el formulario de arriba."
                : "No hay gastos con este filtro.";
            lblSinGastos.Visibility = vacio ? Visibility.Visible : Visibility.Collapsed;
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
            using var db = new AppDbContext();
            var inicio = InicioRegistro.Obtener(db);
            _mesSeleccionado = Periodo.LimitarMes(mes, inicio, DateTime.Today);
            MostrarMes();
        }

        private void MostrarMes()
        {
            using var dbInicio = new AppDbContext();
            var inicio = InicioRegistro.Obtener(dbInicio);
            _mesSeleccionado = Periodo.LimitarMes(_mesSeleccionado, inicio, DateTime.Today);
            btnMesAnterior.IsEnabled = _mesSeleccionado > Periodo.PrimerMesVisible(inicio, DateTime.Today);
            btnMesSiguiente.IsEnabled = _mesSeleccionado < Periodo.InicioMes(DateTime.Today).AddMonths(1);
            bool esActual = Periodo.EsMesActual(_mesSeleccionado);
            lblMes.Text = Periodo.Nombre(_mesSeleccionado);
            lblTituloGastos.Text = esActual ? "GASTOS DEL MES" : $"GASTOS DE {Periodo.Nombre(_mesSeleccionado).ToUpper()}";
            lblTituloGastos.Visibility = esActual ? Visibility.Collapsed : Visibility.Visible;
            btnMesActual.Visibility = esActual ? Visibility.Collapsed : Visibility.Visible;

            // El estado de pago de los créditos solo existe para el mes en curso (ActualizarFiltros oculta su sección)
            CargarCuentasDelMes();
        }

        // --- Pestañas: Gastos del mes / Historial / Proyección ---
        private void Pestana_Checked(object sender, RoutedEventArgs e)
        {
            if (pnlMes == null || pnlHistorial == null || pnlProyeccion == null) return; // aún inicializando

            pnlMes.Visibility = tabMes.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            pnlHistorial.Visibility = tabHistorial.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            pnlProyeccion.Visibility = tabProyeccion.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

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
                MisFinanzas.Controles.Aviso.Mostrar("Error al cargar el historial: " + ex.Message);
            }
        }

        private void CargarProyeccion()
        {
            try
            {
                using var db = new AppDbContext();
                var datos = Analisis.Cargar(db);
                icPredicciones.ItemsSource = Analisis.TablaProyeccion(datos, DateTime.Today);
                var proyeccion = Analisis.Proyeccion(datos, DateTime.Today, 13);
                icProyeccion.ItemsSource = proyeccion.Take(12).ToList();

                // Mes siguiente frente al actual
                var (actual, siguiente) = (proyeccion[0], proyeccion[1]);
                var diferencia = siguiente.TotalGastos - actual.TotalGastos;
                lblKpiMesTitulo.Text = $"PREVISTO {siguiente.Mes:MMMM}".ToUpper();
                lblKpiMes.Text = siguiente.TotalGastos.ToString("C0");
                lblKpiMesNota.Text = diferencia == 0 ? $"Igual que {actual.Mes:MMMM}"
                                   : $"{Math.Abs(diferencia):C0} {(diferencia < 0 ? "menos" : "más")} que {actual.Mes:MMMM}";
                lblKpiMesNota.Foreground = (System.Windows.Media.Brush)FindResource(diferencia > 0 ? "Rojo" : diferencia < 0 ? "VerdeTexto" : "Texto2");

                // Próximos 12 meses y cuánto de eso son cuotas de créditos
                var anio = proyeccion.Take(12).ToList();
                decimal totalAnio = anio.Sum(m => m.TotalGastos), creditosAnio = anio.Sum(m => m.Creditos);
                lblKpiAnio.Text = totalAnio.ToString("C0");
                lblKpiAnioNota.Text = creditosAnio > 0 && totalAnio > 0
                    ? $"{creditosAnio / totalAnio:P0} es de cuotas de créditos"
                    : "Sin cuotas de créditos";

                icBarrasProyeccion.ItemsSource = Analisis.Barras(proyeccion.Skip(1).Take(6), 80);
            }
            catch (Exception ex)
            {
                MisFinanzas.Controles.Aviso.Mostrar("Error al calcular la proyección: " + ex.Message);
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
            var (frecuencia, intervalo) = Frecuencias.Interpretar(cbFrecuencia.SelectedItem as string ?? Frecuencias.Mensual);
            // Anual: mes en que vence cada año. Cada N meses: mes del primer cobro.
            var visible = frecuencia == Frecuencias.Anual || intervalo > 1 ? Visibility.Visible : Visibility.Collapsed;
            lblMesAnual.Content = intervalo > 1 ? "Primer cobro" : "Mes";
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
            lblTituloFormulario.Text = "Registrar gasto";
            btnGuardarCuenta.Content = "Agregar";
            btnCancelarEdicion.Visibility = Visibility.Collapsed;
            pnlAtajos.Visibility = Visibility.Visible;
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
            cbFrecuencia.SelectedItem = Frecuencias.Mostrar(c); // "Quincenal", no "Quincenal · 1.ª"
            cbMesAnual.SelectedIndex = c.Fecha.Month - 1;
            cbDiaPago.SelectedItem = CuentasMensuales.DiaDePago(c);

            lblTituloFormulario.Text = $"Editar \"{c.Descripcion}\" (aplica a todos sus meses)";
            btnGuardarCuenta.Content = "Guardar";
            btnCancelarEdicion.Visibility = Visibility.Visible;
            pnlAtajos.Visibility = Visibility.Collapsed;
            txtDescCuenta.Focus();
        }

        private void BtnAgregarCuenta_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtDescCuenta.Text) ||
                !NumeroHelper.TryParseMoneda(txtValorCuenta.Text, out decimal valor) ||
                cbDiaPago.SelectedItem is not int dia ||
                cbFrecuencia.SelectedItem is not string opcionFrecuencia)
            {
                MisFinanzas.Controles.Aviso.Mostrar("Por favor, ingresa una descripción, un valor válido y el día de pago.");
                return;
            }
            int mesDelAnio = cbMesAnual.SelectedIndex + 1;
            var (frecuencia, intervalo) = Frecuencias.Interpretar(opcionFrecuencia);

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
                            IntervaloMeses = intervalo,
                            DiaPago = dia,
                            Fecha = CuentasMensuales.FechaInicio(frecuencia, _mesSeleccionado, mesDelAnio, dia, intervalo)
                        };
                        db.Movimientos.Add(nueva);
                        db.SaveChanges();
                        listaCuentasPersistente.Add(nueva);

                        toast.Show(frecuencia switch
                        {
                            Frecuencias.Unica => $"Gasto registrado para el {nueva.Fecha:dd/MM/yyyy}",
                            Frecuencias.Anual => $"Gasto anual registrado: vence cada {nueva.Fecha:dd 'de' MMMM}",
                            _ when intervalo > 1 => $"Gasto registrado: se cobrará cada {intervalo} meses desde {Periodo.Nombre(nueva.Fecha)}",
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
                            c.IntervaloMeses = intervalo;
                            c.DiaPago = dia;

                            // Se conserva el mes de inicio; en los anuales se ajusta al mes elegido
                            var inicio = frecuencia == Frecuencias.Anual || intervalo > 1
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
                MisFinanzas.Controles.Aviso.Mostrar("Error al guardar el gasto: " + ex.Message);
                return;
            }

            LimpiarFormulario();
            CargarCategorias();
            CargarCuentasDelMes();
            txtDescCuenta.Focus();
        }

        public void ActualizarTotal()
        {
            ActualizarFiltros();

            // 1. Sumar las cuentas del mes seleccionado que no se han pagado
            decimal totalMovimientos = _cuentasDelMes.Where(c => !c.Pagada).Sum(c => c.Venta);

            // 2. Sumar cuotas de créditos no pagadas (solo aplica al mes en curso)
            decimal totalCreditos = Periodo.EsMesActual(_mesSeleccionado)
                ? CreditosView.ListaCreditosPersistente.Where(c => !c.Pagado).Sum(c => c.CuotaEsteMes)
                : 0;

            lblTotalPendiente.Text = (totalMovimientos + totalCreditos).ToString("C0");
            ActualizarResumenLateral(totalMovimientos + totalCreditos);
        }

        // Columna derecha: pagado frente al total del mes, gastos por categoría y proyección del mes siguiente
        private void ActualizarResumenLateral(decimal pendiente)
        {
            try
            {
                bool esActual = Periodo.EsMesActual(_mesSeleccionado);
                var creditos = esActual ? CreditosView.ListaCreditosPersistente.ToList() : new System.Collections.Generic.List<Credito>();

                decimal pagado = _cuentasDelMes.Where(c => c.Pagada).Sum(c => c.MontoPagado)
                               + creditos.Where(c => c.Pagado).Sum(c => c.CuotaEsteMes);
                decimal total = pagado + pendiente;
                lblPagadoMes.Text = $"Pagado {pagado:C0}";
                lblTotalMes.Text = $"Total {total:C0}";
                barPagado.Value = total > 0 ? (double)(pagado / total) : 0;

                var categorias = Analisis.PorCategoria(_cuentasDelMes, creditos.Sum(c => c.CuotaEsteMes));
                icCategoriasMes.ItemsSource = categorias;
                lblSinCategoriasMes.Visibility = categorias.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

                using var db = new AppDbContext();
                var siguiente = Analisis.Proyeccion(Analisis.Cargar(db), _mesSeleccionado.AddMonths(1), 1)[0];
                lblTituloProyeccionLateral.Text = $"Proyección {siguiente.Mes:MMMM}";
                lblProyGastos.Text = siguiente.TotalGastos.ToString("C0");
                lblProyBalance.Text = siguiente.Balance.ToString("C0");
                lblProyBalance.Foreground = (System.Windows.Media.Brush)FindResource(siguiente.Balance < 0 ? "Rojo" : "VerdeTexto");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error en el resumen del mes: " + ex.Message);
            }
        }

        // "Ver crédito": abre la pantalla de créditos con ese crédito seleccionado
        private void BtnVerCredito_Click(object sender, RoutedEventArgs e)
        {
            if (((Button)sender).DataContext is Credito credito) MainWindow.Actual?.IrACreditos(credito.Id);
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
                MisFinanzas.Controles.Aviso.Mostrar("Error al eliminar: " + ex.Message);
            }
        }

        // El estado se modifica en pantalla solo después de guardar el pago.
        private void EstadoPago_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button boton) return;
            try
            {
                if (boton.DataContext is CuentaMes cuenta)
                {
                    var pagar=!cuenta.Pagada;
                    using var db=new AppDbContext();
                    CuentasMensuales.MarcarPagada(db,cuenta.Cuenta.Id,cuenta.Mes,pagar,pagar ? cuenta.Venta : cuenta.MontoPagado,cuenta.Quincena);
                    if(pagar) cuenta.MontoPagado=cuenta.Venta;
                    cuenta.Pagada=pagar;
                    toast.Show(pagar ? "Pago registrado" : "Pago deshecho");
                }
                else if (boton.DataContext is Credito credito && credito.PuedeMarcar)
                {
                    var pagar=!credito.Pagado;
                    var pago=CreditosView.RegistrarCuota(credito,pagar);
                    if(pago!=null) toast.Show($"{pago.Capital:C0} a capital. Ahora debes {credito.Disponible:C0}");
                    else if(!pagar && !credito.Pagado) toast.Show($"Pago deshecho. Saldo {credito.Disponible:C0}");
                }
            }
            catch(Exception ex) { MisFinanzas.Controles.Aviso.Mostrar("Error al guardar el pago: " + ex.Message); }
            ActualizarTotal();
            MainWindow.Actual?.VerificarVencimientosCuentas();
        }
    }
}
