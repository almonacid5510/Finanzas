// ============================================================
//  Proyecto: Mis Finanzas
//  Autoria: Juan Almonacid
//  Copyright (c) 2026 Juan Almonacid. Todos los derechos reservados.
// ============================================================
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace MisFinanzas.Modulos
{
    public partial class CuentasView : UserControl
    {
        // Todas las cuentas (de todos los meses). La pantalla muestra solo las del mes seleccionado.
        public static ObservableCollection<Movimiento> listaCuentasPersistente { get; set; } = new ObservableCollection<Movimiento>();

        // Mes que se está consultando (siempre el día 1)
        private DateTime _mesSeleccionado = Periodo.InicioMes(DateTime.Today);

        // Vista filtrada propia de esta pantalla sobre la lista global
        private readonly ListCollectionView _cuentasDelMes;

        public CuentasView()
        {
            InitializeComponent();

            // 1. Cargamos los datos
            CreditosView.CargarDatosGlobal();
            CargarDatosDesdeBD();

            // 2. Vinculamos las listas
            _cuentasDelMes = new ListCollectionView(listaCuentasPersistente)
            {
                Filter = o => Periodo.CuentaDelMes((Movimiento)o, _mesSeleccionado)
            };
            _cuentasDelMes.SortDescriptions.Add(new SortDescription(nameof(Movimiento.Fecha), ListSortDirection.Ascending));
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
            _cuentasDelMes.Refresh();
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
            ActualizarTotal();
        }

        private void BtnAgregarCuenta_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(txtDescCuenta.Text) && NumeroHelper.TryParseMoneda(txtValorCuenta.Text, out decimal valor))
            {
                // Sin fecha elegida: hoy si se ve el mes actual, o el día 1 del mes que se está viendo
                var fechaPorDefecto = Periodo.EsMesActual(_mesSeleccionado) ? DateTime.Now : _mesSeleccionado;

                var nuevaCuenta = new Movimiento
                {
                    Fecha = dpFechaGasto.SelectedDate ?? fechaPorDefecto,
                    Descripcion = txtDescCuenta.Text,
                    Costo = 0,
                    Venta = valor,
                    EstaPagado = false,
                    Tipo = "Cuenta"
                };

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

                // 2. Agregamos a la lista visual
                listaCuentasPersistente.Add(nuevaCuenta);
                ActualizarTotal();

                // Si la cuenta cae en otro mes, avisamos dónde quedó
                if (!Periodo.CuentaDelMes(nuevaCuenta, _mesSeleccionado))
                    toast.Show($"Gasto registrado en {Periodo.Nombre(nuevaCuenta.Fecha)}");

                // 3. Limpiar campos
                txtDescCuenta.Clear();
                txtValorCuenta.Clear();

                // Opcional: poner el foco de nuevo en la descripción
                txtDescCuenta.Focus();
            }
            else
            {
                MessageBox.Show("Por favor, ingresa una descripción y un valor válido.");
            }
        }

        public void ActualizarTotal()
        {
            // 1. Sumar Movimientos (Gastos) no pagados del mes seleccionado
            decimal totalMovimientos = listaCuentasPersistente
                .Where(m => !m.EstaPagado && Periodo.CuentaDelMes(m, _mesSeleccionado))
                .Sum(m => m.Venta);

            // 2. Sumar cuotas de créditos no pagadas (solo aplica al mes en curso)
            decimal totalCreditos = Periodo.EsMesActual(_mesSeleccionado)
                ? CreditosView.ListaCreditosPersistente.Where(c => !c.Pagado).Sum(c => c.CuotaMensual)
                : 0;

            lblTotalPendiente.Text = (totalMovimientos + totalCreditos).ToString("C0");
        }


        // Método para eliminar el gasto de la lista y de la base de datos
        private void BtnEliminar_Click(object sender, RoutedEventArgs e)
        {
            var boton = (Button)sender;
            var movimientoAEliminar = (Movimiento)boton.DataContext;

            if (movimientoAEliminar != null)
            {
                // 1. Eliminar de la base de datos física
                try
                {
                    using (var db = new AppDbContext())
                    {
                        db.Movimientos.Remove(movimientoAEliminar);
                        db.SaveChanges();
                    }

                    // 2. Eliminar de la lista visual
                    listaCuentasPersistente.Remove(movimientoAEliminar);
                    ActualizarTotal();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error al eliminar: " + ex.Message);
                }
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
                    // CASO A: Es un Gasto normal (Movimiento)
                    if (cb.DataContext is Movimiento mov)
                    {
                        // Buscamos el registro en la BD por su ID y lo actualizamos
                        var dbMov = db.Movimientos.Find(mov.Id);
                        if (dbMov != null)
                        {
                            dbMov.EstaPagado = mov.EstaPagado; // Sincronizamos el estado
                            db.SaveChanges();
                        }
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
