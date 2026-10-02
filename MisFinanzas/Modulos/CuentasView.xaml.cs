// ============================================================
//  Proyecto: Mis Finanzas
//  Autoria: Juan Almonacid
//  Copyright (c) 2026 Juan Almonacid. Todos los derechos reservados.
// ============================================================
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace MisFinanzas.Modulos
{
    public partial class CuentasView : UserControl
    {
        // Todas las cuentas registradas (plantillas recurrentes). El Dashboard escucha sus cambios.
        public static ObservableCollection<Movimiento> listaCuentasPersistente { get; set; } = new ObservableCollection<Movimiento>();

        // Mes que se está consultando (siempre el día 1)
        private DateTime _mesSeleccionado = Periodo.InicioMes(DateTime.Today);

        // Cuentas del mes seleccionado, con su vencimiento y estado de pago de ese mes
        private readonly ObservableCollection<CuentaMes> _cuentasDelMes = new ObservableCollection<CuentaMes>();

        public CuentasView()
        {
            InitializeComponent();

            // Días posibles de pago; por defecto el día de hoy
            cbDiaPago.ItemsSource = Enumerable.Range(1, 31).ToList();
            cbDiaPago.SelectedItem = DateTime.Today.Day;

            // 1. Cargamos los datos
            CreditosView.CargarDatosGlobal();
            CargarDatosDesdeBD();

            // 2. Vinculamos las listas
            icCuentas.ItemsSource = _cuentasDelMes;
            icCreditosCompromisos.ItemsSource = CreditosView.ListaCreditosPersistente;

            // 3. Escuchamos los cambios en los créditos solo mientras la pantalla está visible,
            // para no acumular suscripciones sobre la lista estática cada vez que se navega aquí
            Loaded += (s, e) => CreditosView.ListaCreditosPersistente.CollectionChanged += Lista_CollectionChanged;
            Unloaded += (s, e) => CreditosView.ListaCreditosPersistente.CollectionChanged -= Lista_CollectionChanged;

            MostrarMes();
        }

        private static void CargarDatosDesdeBD()
        {
            try
            {
                using (var db = new AppDbContext())
                {
                    // 1. Traemos los datos de la BD (solo tipo "Cuenta")
                    var movimientos = db.Movimientos
                                       .Where(m => m.Tipo == "Cuenta")
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

        private void BtnAgregarCuenta_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(txtDescCuenta.Text) &&
                NumeroHelper.TryParseMoneda(txtValorCuenta.Text, out decimal valor) &&
                cbDiaPago.SelectedItem is int dia)
            {
                // El gasto se repite cada mes el día elegido, a partir del mes que se está viendo
                var nuevaCuenta = new Movimiento
                {
                    Descripcion = txtDescCuenta.Text,
                    Costo = 0,
                    Venta = valor,
                    EstaPagado = false,
                    Tipo = "Cuenta",
                    DiaPago = dia
                };
                nuevaCuenta.Fecha = CuentasMensuales.Vencimiento(nuevaCuenta, _mesSeleccionado);

                // 1. Guardamos en la base de datos
                try
                {
                    using (var db = new AppDbContext())
                    {
                        db.Movimientos.Add(nuevaCuenta);
                        db.SaveChanges();
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error al persistir: " + ex.Message);
                    return;
                }

                // 2. Actualizamos las listas visuales
                listaCuentasPersistente.Add(nuevaCuenta);
                CargarCuentasDelMes();
                toast.Show($"Gasto registrado: se repetirá el día {dia} de cada mes");

                // 3. Limpiar campos
                txtDescCuenta.Clear();
                txtValorCuenta.Clear();

                // Opcional: poner el foco de nuevo en la descripción
                txtDescCuenta.Focus();
            }
            else
            {
                MessageBox.Show("Por favor, ingresa una descripción, un valor válido y el día de pago.");
            }
        }

        public void ActualizarTotal()
        {
            // 1. Sumar las cuentas del mes seleccionado que no se han pagado
            decimal totalMovimientos = _cuentasDelMes.Where(c => !c.Pagada).Sum(c => c.Venta);

            // 2. Sumar cuotas de créditos no pagadas (solo aplica al mes en curso)
            decimal totalCreditos = Periodo.EsMesActual(_mesSeleccionado)
                ? CreditosView.ListaCreditosPersistente.Where(c => !c.Pagado).Sum(c => c.CuotaMensual)
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

        // Método para actualizar el total apenas marcas el CheckBox
        private void CheckBox_Click(object sender, RoutedEventArgs e)
        {
            // 1. Obtenemos el CheckBox que disparó el evento
            var cb = sender as CheckBox;
            if (cb == null) return;

            try
            {
                // 2. Usamos 'pattern matching' para saber qué tipo de dato es
                using (var db = new AppDbContext())
                {
                    // CASO A: Es un gasto del mes: el pago se registra solo para ese mes
                    if (cb.DataContext is CuentaMes cuentaMes)
                    {
                        CuentasMensuales.MarcarPagada(db, cuentaMes.Cuenta.Id, cuentaMes.Mes, cuentaMes.Pagada);
                    }
                    // CASO B: Es un Compromiso de Crédito (Credito): el pago vale para el mes actual
                    else if (cb.DataContext is Credito credito)
                    {
                        credito.MesPagado = credito.Pagado ? Periodo.MesActual : string.Empty;

                        var dbCredito = db.Creditos.Find(credito.Id);
                        if (dbCredito != null)
                        {
                            dbCredito.Pagado = credito.Pagado; // Sincronizamos el estado
                            dbCredito.MesPagado = credito.MesPagado;
                            db.SaveChanges();
                        }
                    }
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
