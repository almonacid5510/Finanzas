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
using Microsoft.EntityFrameworkCore; // Asegúrate de tener esto para ToList()

namespace MisFinanzas.Modulos
{
    public partial class CuentasView : UserControl
    {
        // Nota: Si en Inversiones usas el mismo nombre, cámbialo allí por 'listaInversionesPersistente'
        public static ObservableCollection<Movimiento> listaCuentasPersistente { get; set; } = new ObservableCollection<Movimiento>();

        public CuentasView()
        {
            InitializeComponent();

            // 1. Cargamos los datos
            CreditosView.CargarDatosGlobal();
            CargarDatosDesdeBD();

            // 2. Vinculamos las listas
            icCreditosCompromisos.ItemsSource = CreditosView.ListaCreditosPersistente;
            icCuentas.ItemsSource = listaCuentasPersistente;

            // 3. ¡ESTA ES LA CLAVE! 
            // Suscribimos la pestaña Cuentas a cualquier cambio que ocurra en la lista de Créditos
            CreditosView.ListaCreditosPersistente.CollectionChanged += (s, e) =>
            {
                // Esto refresca el total pendiente automáticamente si borras un crédito
                ActualizarTotal();
            };

            ActualizarTotal();
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

        private void Lista_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs? e)
        {
            ActualizarTotal();
        }

        private void BtnAgregarCuenta_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(txtDescCuenta.Text) && NumeroHelper.TryParseMoneda(txtValorCuenta.Text, out decimal valor))
            {
                var nuevaCuenta = new Movimiento
                {
                    Fecha = dpFechaGasto.SelectedDate ?? DateTime.Now,
                    Descripcion = txtDescCuenta.Text,
                    Costo = 0,
                    Venta = valor,
                    EstaPagado = false,
                    Tipo = "Cuenta"
                };

                // 1. Agregamos a la lista visual
                listaCuentasPersistente.Add(nuevaCuenta);

                // 2. Guardamos en la base de datos
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
                }

                // --- ESTA ES LA LÍNEA QUE DEBES AGREGAR ---
                ActualizarTotal();

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
            // 1. Sumar Movimientos (Gastos) no pagados
            decimal totalMovimientos = listaCuentasPersistente
                .Where(m => !m.EstaPagado)
                .Sum(m => m.Venta);

            // 2. Sumar Créditos no pagados (Accediendo a la lista estática de CreditosView)
            // Cambia 'CreditosView' por el nombre exacto de tu clase de créditos si es distinto
            decimal totalCreditos = CreditosView.ListaCreditosPersistente
                .Where(c => !c.Pagado)
                .Sum(c => c.CuotaMensual);

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

            // 2. Usamos 'pattern matching' para saber qué tipo de dato es
            using (var db = new AppDbContext()) // Asegúrate de que este sea el nombre de tu contexto
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
                // CASO B: Es un Compromiso de Crédito (Credito)
                else if (cb.DataContext is Credito credito)
                {
                    // Buscamos el crédito en la BD y lo actualizamos
                    var dbCredito = db.Creditos.Find(credito.Id);
                    if (dbCredito != null)
                    {
                        dbCredito.Pagado = credito.Pagado; // Sincronizamos el estado
                        db.SaveChanges();
                    }
                }
            }

            // 3. Recalculamos el total de la pantalla automáticamente
            ActualizarTotal();
        }
    }
}