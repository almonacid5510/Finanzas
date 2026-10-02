// ============================================================
//  Proyecto: Mis Finanzas
//  Autoria: Juan Almonacid
//  Copyright (c) 2026 Juan Almonacid. Todos los derechos reservados.
// ============================================================
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
        }

        // Recalcula el resumen cuando cambia cualquiera de las listas globales de la app
        private void ListaGlobal_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => CargarResumen();

        // Las listas son estáticas y viven toda la sesión: nos suscribimos solo mientras el
        // Dashboard está en pantalla, para no acumular manejadores cada vez que se navega aquí.
        private void Suscribir(bool activar)
        {
            INotifyCollectionChanged[] listas =
            {
                VehiculosView.listaVehiculos,
                CuentasView.listaCuentasPersistente,
                CreditosView.ListaCreditosPersistente,
                IngresosView.ListaMetas
            };

            foreach (var lista in listas)
            {
                lista.CollectionChanged -= ListaGlobal_CollectionChanged; // evita duplicados
                if (activar) lista.CollectionChanged += ListaGlobal_CollectionChanged;
            }
        }

        // Valores para el gráfico de barras Ingresos vs Gastos
        private decimal _graphIngresos;
        private decimal _graphGastos;

        // Ajusta el ancho de las barras proporcional al mayor valor, según el espacio disponible
        private void ActualizarBarras()
        {
            // El contenedor real de la barra es el Border padre (fondo gris)
            double disponible = (barIngresos.Parent as FrameworkElement)?.ActualWidth ?? 0;
            if (disponible <= 0) return; // aún sin layout; se reintentará en SizeChanged

            decimal maximo = Math.Max(_graphIngresos, _graphGastos);
            if (maximo <= 0)
            {
                barIngresos.Width = 0;
                barGastos.Width = 0;
                return;
            }

            barIngresos.Width = disponible * (double)(_graphIngresos / maximo);
            barGastos.Width = disponible * (double)(_graphGastos / maximo);
        }

        private void Barras_SizeChanged(object sender, SizeChangedEventArgs e) => ActualizarBarras();

        /// <summary>
        /// Escucha el evento Loaded que configuramos en el XAML.
        /// Se dispara automáticamente cada vez que el usuario hace clic o vuelve a la pestaña del Dashboard.
        /// </summary>
        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Forzamos una recarga fresca desde la Base de Datos para asegurar sincronización absoluta.
                // Se recarga antes de suscribirse para no recalcular el resumen por cada elemento cargado.
                IngresosView.CargarMetasGlobal();
                CreditosView.CargarDatosGlobal();
                CargarResumen();
                Suscribir(true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error en inicialización del Dashboard: " + ex.Message);
            }
        }

        private void UserControl_Unloaded(object sender, RoutedEventArgs e) => Suscribir(false);

        public void CargarResumen()
        {
            try
            {
                // Todo el resumen corresponde al mes en curso
                var mesActual = DateTime.Today;
                decimal totalIngresos;
                System.Collections.Generic.List<Movimiento> cuentasDelMes;

                // --- 1. CONSULTAS A LA BASE DE DATOS (Entity Framework) ---
                using (var db = new AppDbContext())
                {
                    // Ingreso mensual (salario) registrado como "Ingreso"
                    totalIngresos = db.Movimientos
                                      .Where(m => m.Tipo == "Ingreso")
                                      .AsEnumerable()
                                      .Sum(m => m.Venta);

                    // Cuentas del mes (incluye las pendientes arrastradas de meses anteriores)
                    cuentasDelMes = db.Movimientos
                                      .Where(m => m.Tipo == "Cuenta")
                                      .AsEnumerable()
                                      .Where(m => Periodo.CuentaDelMes(m, mesActual))
                                      .ToList();
                }

                // Los gastos del mes cuentan aunque ya estén pagados: pagar no cambia lo que se gastó.
                decimal totalGastosCuentas = cuentasDelMes.Sum(m => m.Venta);
                decimal pendienteCuentas = cuentasDelMes.Where(m => !m.EstaPagado).Sum(m => m.Venta);

                // --- 2. CONSULTA A LA LISTA PERSISTENTE DE CRÉDITOS ---
                // Cada crédito registrado es un compromiso mensual por el valor de su cuota
                var creditos = CreditosView.ListaCreditosPersistente;
                decimal totalCreditosMes = creditos.Sum(c => c.CuotaMensual);
                decimal pendienteCreditos = creditos.Where(c => !c.Pagado).Sum(c => c.CuotaMensual);

                // --- 3. CÁLCULO DE VEHÍCULOS ---
                decimal totalV = VehiculosView.listaVehiculos?.Sum(v => v.TotalInvertido) ?? 0;

                // --- 4. OPERACIONES Y BALANCES ---
                decimal totalGastosYCompromisos = totalGastosCuentas + totalCreditosMes;
                decimal balanceNeto = totalIngresos - totalGastosYCompromisos;

                // --- 5. ASIGNACIÓN A LAS ETIQUETAS DEL XAML ---
                this.lblDashIngresos.Text = totalIngresos.ToString("C0");
                this.lblDashGastos.Text = totalGastosYCompromisos.ToString("C0");
                this.lblDashPendiente.Text = $"Pendiente por pagar: {(pendienteCuentas + pendienteCreditos):C0}";
                this.lblPeriodo.Text = Periodo.Nombre(mesActual);
                this.lblDashBalance.Text = balanceNeto.ToString("C0");
                this.lblDashMoto.Text = totalV.ToString("C0");

                // --- 5b. GRÁFICO INGRESOS VS GASTOS ---
                _graphIngresos = totalIngresos;
                _graphGastos = totalGastosYCompromisos;
                lblGraphIngresos.Text = totalIngresos.ToString("C0");
                lblGraphGastos.Text = totalGastosYCompromisos.ToString("C0");
                lblGraphDiferencia.Text = balanceNeto.ToString("C0");
                lblGraphDiferencia.Foreground = new System.Windows.Media.SolidColorBrush(
                    balanceNeto < 0
                        ? System.Windows.Media.Color.FromRgb(197, 48, 48)
                        : System.Windows.Media.Color.FromRgb(47, 133, 90));
                ActualizarBarras();

                // Control dinámico del color para la tarjeta de Disponible/Neto
                if (balanceNeto < 0)
                {
                    // Rojo si el balance es negativo
                    this.lblDashBalance.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(197, 48, 48));
                    
                    // Actualizar tarjeta de salud financiera a estado de alerta
                    cardSaludFinanciera.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 245, 245)); // Rojo muy suave
                    cardSaludFinanciera.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(254, 178, 178));
                    txtEmojiSalud.Text = "😟";
                    txtEmojiSalud.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(197, 48, 48));
                    txtMensajeSalud.Text = "Atención: Estás gastando más de lo que ingresas.";
                    txtMensajeSalud.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(155, 44, 44));
                }
                else
                {
                    // Gris oscuro original (#4A5568) si estás en números positivos
                    this.lblDashBalance.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(74, 85, 104));
                    
                    // Actualizar tarjeta de salud financiera a estado saludable
                    cardSaludFinanciera.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(240, 255, 244)); // Verde muy suave
                    cardSaludFinanciera.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(154, 230, 180));
                    txtEmojiSalud.Text = "😊";
                    txtEmojiSalud.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(47, 133, 90));
                    txtMensajeSalud.Text = "¡Tus finanzas están saludables!";
                    txtMensajeSalud.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(39, 103, 73));
                }

                // --- 6. ASIGNACIÓN Y REFRESCO DE LAS METAS ---
                icMetasCirculares.ItemsSource = null;
                icMetasCirculares.ItemsSource = IngresosView.ListaMetas;

                // --- 7. ESTADO DE CUENTAS DEL MES ---
                var listaEstados = new System.Collections.Generic.List<CuentaEstadoViewModel>();
                var hoy = DateTime.Now.Date;

                foreach (var c in cuentasDelMes)
                {
                    var vm = new CuentaEstadoViewModel { Cuenta = c };
                    if (c.EstaPagado)
                    {
                        vm.BackgroundColor = "#F0FFF4"; // Verde
                        vm.BorderColor = "#9AE6B4";
                        vm.TextColor = "#276749";
                        vm.MensajeEstado = "Pagada ✔️";
                    }
                    else if (c.Fecha.Date < hoy)
                    {
                        vm.BackgroundColor = "#FFF5F5"; // Rojo
                        vm.BorderColor = "#FEB2B2";
                        vm.TextColor = "#C53030";
                        vm.MensajeEstado = $"Vencida el {c.Fecha:dd/MMM}";
                    }
                    else
                    {
                        vm.BackgroundColor = "#EBF8FF"; // Azul
                        vm.BorderColor = "#90CDF4";
                        vm.TextColor = "#2B6CB0";
                        vm.MensajeEstado = $"Vence el {c.Fecha:dd/MMM}";
                    }
                    listaEstados.Add(vm);
                }
                
                icEstadoCuentas.ItemsSource = null;
                icEstadoCuentas.ItemsSource = listaEstados.OrderBy(x => x.Cuenta.EstaPagado).ThenBy(x => x.Cuenta.Fecha).ToList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error al calcular el resumen del Dashboard: " + ex.Message);
            }
        }
    }

    public class CuentaEstadoViewModel
    {
        public Movimiento Cuenta { get; set; } = new Movimiento();
        public string BackgroundColor { get; set; } = string.Empty;
        public string BorderColor { get; set; } = string.Empty;
        public string TextColor { get; set; } = string.Empty;
        public string MensajeEstado { get; set; } = string.Empty;
    }
}