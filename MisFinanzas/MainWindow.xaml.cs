// ============================================================
//  Proyecto: Mis Finanzas
//  Autoria: Juan Almonacid
//  Copyright (c) 2026 Juan Almonacid. Todos los derechos reservados.
// ============================================================
using System;
using System.Collections.Generic;
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
            IniciarSincronizacionCelular();
            avisoActualizacion.DataContext=Actualizaciones.Instancia;
            Actualizaciones.Instancia.PropertyChanged += Actualizaciones_Cambio;
            Closed += (_,_) => Actualizaciones.Instancia.PropertyChanged -= Actualizaciones_Cambio;
            ContentRendered += async (_,_) => await Actualizaciones.Instancia.BuscarAsync();
            // La app puede quedar abierta varios días: vuelve a buscar versiones nuevas cada 6 horas
            var relojActualizaciones = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromHours(6) };
            relojActualizaciones.Tick += async (_,_) => { if (!Actualizaciones.Instancia.Disponible) await Actualizaciones.Instancia.BuscarAsync(); };
            relojActualizaciones.Start();
            Closed += (_,_) => relojActualizaciones.Stop();
            ContentRendered += (_,_) => Dispatcher.BeginInvoke(MostrarAvisosAlAbrir, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }

        // Avisos y reglas de Cuenta: pagos vencidos (para pagarlos ahí mismo) y los que vencen en los próximos días
        private void MostrarAvisosAlAbrir()
        {
            try
            {
                var preferencias = Preferencias.Actual;
                var hoy = DateTime.Today;
                List<CuentaMes> vencidas, proximas;
                using (var db = new AppDbContext())
                {
                    vencidas = CuentasMensuales.DelMes(db, hoy).Where(c => !c.Pagada && c.Vence <= hoy).OrderBy(c => c.Vence).ToList();
                    proximas = Preferencias.PagosProximos(db, hoy);
                }

                if (preferencias.MostrarVencidosAlAbrir && vencidas.Count > 0)
                {
                    new VentanaRecordatorioPagos(vencidas) { Owner = this }.ShowDialog();
                    VerificarVencimientosCuentas();
                    (ContenedorPrincipal.Content as DashboardView)?.CargarResumen();
                }

                if (preferencias.RecordarPagos && proximas.Count > 0)
                {
                    var lineas = proximas.Select(c =>
                        $"• {c.Descripcion}: {NumeroHelper.Moneda(c.Venta)}, vence {((c.Vence - hoy).Days == 1 ? "mañana" : $"el {c.Vence:dd/MM}")}");
                    MisFinanzas.Controles.Aviso.Mostrar((proximas.Count == 1 ? "Tienes un pago próximo:" : "Tienes pagos próximos:")
                                            + Environment.NewLine + string.Join(Environment.NewLine, lineas));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error en los avisos al abrir: " + ex.Message);
            }
        }

        private void Actualizaciones_Cambio(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
            => avisoActualizacion.Visibility=Actualizaciones.Instancia.MostrarAviso ? Visibility.Visible : Visibility.Collapsed;
        private void PosponerActualizacion_Click(object sender, RoutedEventArgs e) => Actualizaciones.Instancia.MasTarde();
        private void AbrirActualizacion_Click(object sender, RoutedEventArgs e)
        {
            if (ContenedorPrincipal.Content is ConfiguracionView configuracion) { configuracion.MostrarActualizacion(); return; }
            if (ContenedorPrincipal.Content is not DashboardView)
            {
                MessageBox.Show("Guarda tus cambios y abre Cuenta para instalar la actualización.","Mis Finanzas"); return;
            }
            new VentanaActualizacion(Actualizaciones.Instancia) { Owner=this }.ShowDialog();
        }

        // Gastos del celular, sin que el usuario haga nada: al abrir la app, cada minuto, al volver a la
        // ventana y al recuperar internet. Una revisión sin gastos nuevos cuesta 1 lectura de Firestore
        // (cada minuto ≈ 1.440 al día por PC; el plan gratuito da 50.000 al día para todo el proyecto).
        private System.Windows.Threading.DispatcherTimer? _relojCelular;
        private DateTime _ultimaRevisionCelular = DateTime.MinValue;

        private void IniciarSincronizacionCelular()
        {
            ContentRendered += (s, e) => RevisarCelular();
            _relojCelular = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
            _relojCelular.Tick += (s, e) => RevisarCelular();
            _relojCelular.Start();

            // Al volver a la ventana (si no se revisó hace poco)
            Activated += (s, e) =>
            {
                if (DateTime.Now - _ultimaRevisionCelular > TimeSpan.FromSeconds(20)) RevisarCelular();
            };
            // Al recuperar internet
            System.Net.NetworkInformation.NetworkChange.NetworkAvailabilityChanged += (s, e) =>
            {
                if (e.IsAvailable) Dispatcher.BeginInvoke(RevisarCelular);
            };
            // Algo pagado desde el celular cambia los avisos de vencidos del menú
            SincronizacionCelular.AccionesAplicadas += _ => Dispatcher.BeginInvoke(VerificarVencimientosCuentas);

            // La nube es la base principal: se revisa su marca de cambios cada 5 s con la ventana activa
            // (cada minuto en segundo plano); 1 lectura por revisión, y solo se sincroniza si alguien escribió.
            _relojMarca = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _relojMarca.Tick += async (s, e) =>
            {
                if (!IsActive && DateTime.Now - _ultimaMarca < TimeSpan.FromMinutes(1)) return;
                _ultimaMarca = DateTime.Now;
                await SincronizacionCelular.RevisarMarcaAsync();
            };
            _relojMarca.Start();

            // Lo guardado aquí se sube enseguida (se agrupan los cambios de 1,5 s)
            _subirPronto = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
            _subirPronto.Tick += (s, e) => { _subirPronto.Stop(); RevisarCelular(); };
            AppDbContext.CambiosLocales += () => Dispatcher.BeginInvoke(() => { _subirPronto.Stop(); _subirPronto.Start(); });

            // Llegó de la nube algo hecho en otro dispositivo: se recargan los datos en memoria y el resumen
            SincronizacionCelular.DatosNubeActualizados += () => Dispatcher.BeginInvoke(DatosDeLaNube);
        }

        private System.Windows.Threading.DispatcherTimer? _relojMarca, _subirPronto;
        private DateTime _ultimaMarca = DateTime.MinValue;

        private void DatosDeLaNube()
        {
            CargarDatosIniciales();
            CreditosView.CargarDatosGlobal();
            VerificarVencimientosCuentas();
            (ContenedorPrincipal.Content as DashboardView)?.CargarResumen();
        }

        private async void RevisarCelular()
        {
            if (!SincronizacionCelular.Conectado) return;
            _ultimaRevisionCelular = DateTime.Now;
            await SincronizacionCelular.SincronizarAsync();
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
            foreach (var btn in new[] { btnNavDashboard, btnNavIngresos, btnNavCuentas, btnNavDiario, btnNavCreditos, btnNavVehiculo, btnNavNegocio, btnNavConfiguracion })
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
                    // Registros anteriores a la nube: reciben su identidad (una sola vez)
                    ReplicaNube.AsignarIdentidades(db);
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

                // Punto rojo en Créditos: cuotas que pasaron su día de pago sin pagarse
                int cuotasVencidas;
                using (var db = new AppDbContext())
                    cuotasVencidas = db.Creditos.AsEnumerable().Count(c => c.CuotaVencidaHoy());
                NavState.SetTieneAlerta(btnNavCreditos, cuotasVencidas > 0);
                btnNavCreditos.ToolTip = cuotasVencidas > 0 ? $"{cuotasVencidas} cuota(s) de crédito vencida(s)" : "Créditos";
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
        private void NavDiario_Click(object sender, RoutedEventArgs e) { ContenedorPrincipal.Content = new GastosDiariosView(); SetActiveNav(btnNavDiario); }
        private void NavCreditos_Click(object sender, RoutedEventArgs e) { ContenedorPrincipal.Content = new CreditosView(); SetActiveNav(btnNavCreditos); }
        private void NavVehiculo_Click(object sender, RoutedEventArgs e) { ContenedorPrincipal.Content = new VehiculosView(); SetActiveNav(btnNavVehiculo); }
        private void NavConfiguracion_Click(object sender, RoutedEventArgs e) { ContenedorPrincipal.Content = new ConfiguracionView(); SetActiveNav(btnNavConfiguracion); }
        public void IrAConfiguracion(bool editarNomina = false)
        {
            NavConfiguracion_Click(this, new RoutedEventArgs());
            if (editarNomina && ContenedorPrincipal.Content is ConfiguracionView cuenta) cuenta.EditarNomina();
        }
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
