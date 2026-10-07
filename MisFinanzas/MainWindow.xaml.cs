// ============================================================
//  Proyecto: Mis Finanzas
//  Autoria: Juan Almonacid
//  Copyright (c) 2026 Juan Almonacid. Todos los derechos reservados.
// ============================================================
using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.EntityFrameworkCore;
using MisFinanzas.Modulos;

namespace MisFinanzas
{
    public partial class MainWindow : Window
    {
        // Ancho para el que está dibujado el diseño (contenido de ~1220 + menú cerrado). Si la ventana es más
        // angosta, todo se reduce proporcionalmente (hasta ZoomMinimo); por debajo, las grillas pasan a menos columnas.
        private const double AnchoDiseno = 1290;
        private const double ZoomMinimo = 0.8;

        public MainWindow()
        {
            InitializeComponent();
            AjustarTamanoInicial();
            InicializarBaseDeDatos();
            CargarDatosIniciales();
            VerificarVencimientosCuentas();
            ContenedorPrincipal.Content = new DashboardView();
            SetActiveNav(btnNavDashboard);
        }

        /// <summary>La ventana principal de la app (para navegar desde otras pantallas).</summary>
        public static MainWindow? Actual => Application.Current?.MainWindow as MainWindow;

        // Tamaño inicial: el del diseño, sin pasarse del área visible de la pantalla
        private void AjustarTamanoInicial()
        {
            var area = SystemParameters.WorkArea;
            Width = Math.Min(Width, area.Width * 0.96);
            Height = Math.Min(Height, area.Height * 0.96);
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Ancho útil de la ventana (sin bordes); no se usa el del contenido porque el zoom lo cambia
            double ancho = e.NewSize.Width - 16;
            double zoom = Math.Clamp(ancho / AnchoDiseno, ZoomMinimo, 1.0);
            Raiz.LayoutTransform = new ScaleTransform(zoom, zoom);
        }

        // --- Menú lateral desplegable: solo íconos; al pasar el mouse se extiende sobre el contenido ---
        private const double MenuCerrado = 68;
        private const double MenuAbierto = 210;

        private void Sidebar_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e) => AnimarMenu(abierto: true);
        private void Sidebar_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e) => AnimarMenu(abierto: false);

        private void AnimarMenu(bool abierto)
        {
            var duracion = new Duration(TimeSpan.FromMilliseconds(180));
            var suave = new QuadraticEase { EasingMode = EasingMode.EaseInOut };
            DoubleAnimation A(double hasta) => new DoubleAnimation(hasta, duracion) { EasingFunction = suave };

            Sidebar.BeginAnimation(WidthProperty, A(abierto ? MenuAbierto : MenuCerrado));
            panelNombreApp.BeginAnimation(OpacityProperty, A(abierto ? 1 : 0));
            refCerrado.BeginAnimation(OpacityProperty, A(abierto ? 0 : 1));
            sombraMenu.BeginAnimation(System.Windows.Media.Effects.DropShadowEffect.OpacityProperty, A(abierto ? 0.28 : 0));
        }

        // Marca visualmente el botón de navegación activo y limpia los demás
        private void SetActiveNav(System.Windows.Controls.Button activo)
        {
            foreach (var btn in new[] { btnNavDashboard, btnNavIngresos, btnNavCuentas, btnNavCreditos, btnNavVehiculo, btnNavNegocio })
            {
                NavState.SetIsActive(btn, btn == activo);
            }
        }

        private void InicializarBaseDeDatos()
        {
            try
            {
                using (var db = new AppDbContext())
                {
                    // Crea la BD si no existe y agrega/retira tablas y columnas según el modelo
                    EsquemaBD.Actualizar(db);
                    Etiquetas.Sembrar(db);
                }
            }
            catch (Exception ex)
            {
                MisFinanzas.Controles.Aviso.Mostrar($"Error crítico de BD: {ex.Message}. Verifica tu modelo de datos.");
            }
        }

        private void CargarDatosIniciales()
        {
            try
            {
                using (var db = new AppDbContext())
                {
                    // 1. Cargamos desde la BD
                    var vehiculosGuardados = db.Vehiculos.ToList();

                    // 2. ACTUALIZAMOS LA LISTA ESTATICA PARA QUE TODA LA APP VEA LOS DATOS
                    VehiculosView.listaVehiculos.Clear();
                    foreach (var v in vehiculosGuardados)
                    {
                        // Importante: Cargamos también los gastos asociados para que el cálculo funcione
                        v.Gastos = new System.Collections.ObjectModel.ObservableCollection<GastoVehiculo>(
                            db.GastosVehiculo.Where(g => g.VehiculoId == v.Id)
                        );
                        VehiculosView.listaVehiculos.Add(v);
                    }
                }
            }
            catch (Exception ex)
            {
                MisFinanzas.Controles.Aviso.Mostrar($"Error crítico al cargar datos iniciales: {ex.Message}");
            }
        }

        /// <summary>
        /// Cuenta los gastos del mes que vencen hoy o ya vencieron sin pagar y lo muestra en el menú.
        /// El aviso ya no abre una ventana al iniciar: el Dashboard lo muestra en línea.
        /// </summary>
        public void VerificarVencimientosCuentas()
        {
            try
            {
                int vencidas;
                using (var db = new AppDbContext())
                {
                    var hoy = DateTime.Today;
                    vencidas = CuentasMensuales.DelMes(db, hoy).Count(c => !c.Pagada && c.Vence <= hoy);
                }
                lblVencidas.Text = vencidas.ToString();
                insigniaVencidas.Visibility = vencidas > 0 ? Visibility.Visible : Visibility.Collapsed;
                NavState.SetTieneAlerta(btnNavCuentas, vencidas > 0);
                btnNavCuentas.ToolTip = vencidas > 0 ? $"{vencidas} pago(s) vencido(s)" : null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error al verificar vencimientos: " + ex.Message);
            }
        }

        // --- Navegación ---
        private void NavDashboard_Click(object sender, RoutedEventArgs e) { ContenedorPrincipal.Content = new DashboardView(); SetActiveNav(btnNavDashboard); }
        private void NavIngresos_Click(object sender, RoutedEventArgs e) { ContenedorPrincipal.Content = new IngresosView(); SetActiveNav(btnNavIngresos); }
        private void NavCuentas_Click(object sender, RoutedEventArgs e) { ContenedorPrincipal.Content = new CuentasView(); SetActiveNav(btnNavCuentas); }
        private void NavCreditos_Click(object sender, RoutedEventArgs e) { ContenedorPrincipal.Content = new CreditosView(); SetActiveNav(btnNavCreditos); }
        private void NavVehiculo_Click(object sender, RoutedEventArgs e) { ContenedorPrincipal.Content = new VehiculosView(); SetActiveNav(btnNavVehiculo); }
        private void NavNegocio_Click(object sender, RoutedEventArgs e) { ContenedorPrincipal.Content = new NegocioView(); SetActiveNav(btnNavNegocio); }

        // Accesos desde otras pantallas (enlaces "Ver cuentas", "Ver crédito", tarjetas del Dashboard)
        public void IrACuentas() => NavCuentas_Click(this, new RoutedEventArgs());
        public void IrAIngresos() => NavIngresos_Click(this, new RoutedEventArgs());
        public void IrAInversiones() => NavNegocio_Click(this, new RoutedEventArgs());
        public void IrACreditos(int? creditoId = null)
        {
            var vista = new CreditosView();
            ContenedorPrincipal.Content = vista;
            SetActiveNav(btnNavCreditos);
            if (creditoId != null) vista.Seleccionar(creditoId.Value);
        }
    }
}
