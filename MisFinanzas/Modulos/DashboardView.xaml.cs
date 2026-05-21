using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Collections.Specialized; // Requerido para capturar los eventos CollectionChanged

namespace MisFinanzas.Modulos
{
    public partial class DashboardView : UserControl
    {
        public DashboardView()
        {
            InitializeComponent();

            try
            {
                // 1. Cargamos de forma global los datos de la BD desde el arranque del Dashboard
                // Esto despierta las colecciones persistentes sin obligar a visitar sus pestañas
                CreditosView.CargarDatosGlobal();
                IngresosView.CargarMetasGlobal();

                // 2. Cargamos el primer resumen numérico y visual
                CargarResumen();

                // 3. Nos suscribimos a los cambios de todas las listas.
                // Si agregas, marcas o eliminas algo en la app, el Dashboard se entera al instante.
                if (VehiculosView.listaVehiculos != null)
                    VehiculosView.listaVehiculos.CollectionChanged += (s, e) => CargarResumen();

                if (CuentasView.listaCuentasPersistente != null)
                    CuentasView.listaCuentasPersistente.CollectionChanged += (s, e) => CargarResumen();

                if (CreditosView.ListaCreditosPersistente != null)
                    CreditosView.ListaCreditosPersistente.CollectionChanged += (s, e) => CargarResumen();

                if (IngresosView.ListaMetas != null)
                    IngresosView.ListaMetas.CollectionChanged += (s, e) => CargarResumen();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error en inicialización del Dashboard: " + ex.Message);
            }
        }

        /// <summary>
        /// Escucha el evento Loaded que configuramos en el XAML.
        /// Se dispara automáticamente cada vez que el usuario hace clic o vuelve a la pestaña del Dashboard.
        /// </summary>
        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            // Forzamos una recarga fresca desde la Base de Datos para asegurar sincronización absoluta
            IngresosView.CargarMetasGlobal();
            CreditosView.CargarDatosGlobal();
            CargarResumen();
        }

        public void CargarResumen()
        {
            try
            {
                decimal totalIngresos = 0;
                decimal totalGastosCuentas = 0;
                decimal totalCreditosMes = 0;

                // --- 1. CONSULTAS A LA BASE DE DATOS (Entity Framework) ---
                using (var db = new AppDbContext())
                {
                    // Sumamos los movimientos registrados como "Ingreso"
                    totalIngresos = db.Movimientos
                                      .Where(m => m.Tipo == "Ingreso")
                                      .AsEnumerable()
                                      .Sum(m => m.Venta);

                    // Sumamos los gastos corrientes ("Cuenta") que NO estén pagados aún
                    totalGastosCuentas = db.Movimientos
                                           .Where(m => m.Tipo == "Cuenta" && !m.EstaPagado)
                                           .AsEnumerable()
                                           .Sum(m => m.Venta);
                }

                // --- 2. CONSULTA A LA LISTA PERSISTENTE DE CRÉDITOS ---
                // Sumamos las cuotas mensuales de compromisos activos que falten por pagar
                if (CreditosView.ListaCreditosPersistente != null)
                {
                    totalCreditosMes = CreditosView.ListaCreditosPersistente
                                                   .Where(c => !c.Pagado)
                                                   .Sum(c => c.CuotaMensual);
                }

                // --- 3. CÁLCULO DE VEHÍCULOS ---
                decimal totalV = VehiculosView.listaVehiculos?.Sum(v => v.TotalInvertido) ?? 0;

                // --- 4. OPERACIONES Y BALANCES ---
                decimal totalGastosYCompromisos = totalGastosCuentas + totalCreditosMes;
                decimal balanceNeto = totalIngresos - totalGastosYCompromisos;

                // --- 5. ASIGNACIÓN A LAS ETIQUETAS DEL XAML ---
                this.lblDashIngresos.Text = totalIngresos.ToString("C0");
                this.lblDashGastos.Text = totalGastosYCompromisos.ToString("C0");
                this.lblDashBalance.Text = balanceNeto.ToString("C0");
                this.lblDashMoto.Text = totalV.ToString("C0");

                // Control dinámico del color para la tarjeta de Disponible/Neto
                if (balanceNeto < 0)
                {
                    // Rojo si el balance es negativo
                    this.lblDashBalance.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(197, 48, 48));
                }
                else
                {
                    // Gris oscuro original (#4A5568) si estás en números positivos
                    this.lblDashBalance.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(74, 85, 104));
                }

                // --- 6. ASIGNACIÓN Y REFRESCO DE LAS METAS ---
                icMetasCirculares.ItemsSource = null;
                icMetasCirculares.ItemsSource = IngresosView.ListaMetas;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error al calcular el resumen del Dashboard: " + ex.Message);
            }
        }
    }
}