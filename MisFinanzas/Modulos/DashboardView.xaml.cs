// ============================================================
//  Proyecto: Mis Finanzas
//  Autoria: Juan Almonacid
//  Copyright (c) 2026 Juan Almonacid. Todos los derechos reservados.
// ============================================================
using System;
using System.Collections.Generic;
using System.Collections.Specialized; // Requerido para capturar los eventos CollectionChanged
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

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

            // Gastos que llegan del celular mientras el Dashboard está abierto
            SincronizacionCelular.GastosRecibidos -= GastosDelCelular;
            SincronizacionCelular.AccionesAplicadas -= AccionesDelCelular;
            if (activar)
            {
                SincronizacionCelular.GastosRecibidos += GastosDelCelular;
                SincronizacionCelular.AccionesAplicadas += AccionesDelCelular;
            }
        }

        // "Pagado" o un gasto borrado desde el celular
        private void AccionesDelCelular(string texto) => Dispatcher.BeginInvoke(() =>
        {
            CreditosView.CargarDatosGlobal();
            CargarResumen();
            toast.Show(texto);
        });

        private void GastosDelCelular(int nuevos) => Dispatcher.BeginInvoke(() =>
        {
            CargarResumen();
            toast.Show(nuevos == 1 ? "Llegó 1 gasto del celular" : $"Llegaron {nuevos} gastos del celular");
        });

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

        // Gastos vencidos sin pagar que se muestran en el aviso rojo
        private List<CuentaMes> _vencidas = new();

        // Mes que se está viendo (siempre el día 1). Empieza en el mes en curso.
        private DateTime _mes = Periodo.InicioMes(DateTime.Today);

        // --- Navegación entre meses ---
        private void BtnMesAnterior_Click(object sender, RoutedEventArgs e) { if (btnMesAnterior.IsEnabled) { _mes = _mes.AddMonths(-1); CargarResumen(); } }
        private void BtnMesSiguiente_Click(object sender, RoutedEventArgs e) { _mes = _mes.AddMonths(1); CargarResumen(); }
        private void BtnMesActual_Click(object sender, RoutedEventArgs e) { _mes = Periodo.InicioMes(DateTime.Today); CargarResumen(); }

        /// <summary>Cuota de un crédito en el mes que se está viendo.</summary>
        internal record CuotaDelMes(Credito Credito, decimal Cuota, bool Pagada);

        // Mes en curso: lo de la pantalla de Créditos. Otros meses: lo pagado (pasados) o lo del plan de pagos (futuros).
        internal static List<CuotaDelMes> CuotasDelMes(IEnumerable<Credito> creditos, List<PagoCredito> pagos, DateTime mes)
        {
            if (Periodo.EsMesActual(mes))
                return creditos.Select(c => new CuotaDelMes(c, c.CuotaEsteMes, c.Pagado)).ToList();

            var clave = Periodo.Clave(mes);
            return creditos.Select(c => new CuotaDelMes(c,
                                Amortizacion.CuotasEnMes(new[] { c }, pagos, mes),
                                pagos.Any(p => p.CreditoId == c.Id && p.Tipo == Amortizacion.TipoCuota && p.Mes == clave)))
                           .Where(x => x.Cuota > 0)
                           .ToList();
        }

        public void CargarResumen()
        {
            try
            {
                // El resumen corresponde al mes elegido con las flechas (por defecto, el mes en curso)
                var mesActual = _mes;
                bool esActual = Periodo.EsMesActual(mesActual);
                bool esFuturo = string.CompareOrdinal(Periodo.Clave(mesActual), Periodo.MesActual) > 0;
                // --- 1. CONSULTAS A LA BASE DE DATOS (una sola carga para todo el resumen) ---
                Analisis.Datos datos;
                List<Movimiento> inversiones;
                List<AbonoMeta> abonos;
                List<GastoDiario> diariosMes, diariosAnterior;
                (decimal Inicial, decimal Recibido, decimal Pagado, decimal Saldo) caja = default;
                using (var db = new AppDbContext())
                {
                    datos = Analisis.Cargar(db);
                    _mes = Periodo.LimitarMes(_mes, datos.Inicio, DateTime.Today);
                    mesActual = _mes;
                    esActual = Periodo.EsMesActual(mesActual);
                    esFuturo = mesActual > Periodo.InicioMes(DateTime.Today);
                    btnMesAnterior.IsEnabled = mesActual > Periodo.PrimerMesVisible(datos.Inicio, DateTime.Today);
                    btnMesSiguiente.IsEnabled = mesActual < Periodo.InicioMes(DateTime.Today).AddMonths(1);
                    tarjetaComparacion.Visibility = mesActual > datos.Inicio ? Visibility.Visible : Visibility.Collapsed;
                    inversiones = db.Movimientos.Where(m => m.Tipo == "Inversion").ToList();
                    abonos = db.AbonosMeta.ToList();
                    // Gastos del día a día de este mes y del anterior (para la comparación)
                    var desde = mesActual.AddMonths(-1);
                    var hasta = mesActual.AddMonths(1);
                    // Incluye lo registrado directamente en Vehículos (categoría "Vehículos")
                    var diarios = db.GastosDiarios.Where(g => g.Fecha >= desde && g.Fecha < hasta).ToList()
                                    .Concat(Analisis.GastosDeVehiculos(db, desde, hasta)).ToList();
                    diariosMes = diarios.Where(g => g.Fecha >= mesActual).Concat(Analisis.InteresesTarjetas(db, mesActual)).ToList();
                    diariosAnterior = diarios.Where(g => g.Fecha < mesActual).Concat(Analisis.InteresesTarjetas(db, desde)).ToList();
                    // Dinero real: hoy en el mes actual, o al cierre de un mes pasado
                    if (!esFuturo) caja = Nomina.Caja(db, esActual ? DateTime.Today : mesActual.AddMonths(1).AddDays(-1));
                }

                // Salario + ingresos extra que aplican este mes
                decimal totalIngresos = Analisis.IngresosDelMes(datos, mesActual);

                // Gastos que aplican este mes (mensuales, únicos y anuales), con su estado de pago
                var cuentasDelMes = CuentasMensuales.DelMes(datos.Cuentas, datos.Pagos, mesActual);

                // Los gastos del mes cuentan aunque ya estén pagados (por lo pagado realmente):
                // pagar no cambia lo que se gastó.
                decimal totalGastosCuentas = cuentasDelMes.Sum(m => m.Real);
                decimal pendienteCuentas = cuentasDelMes.Where(m => !m.Pagada).Sum(m => m.Venta);

                // --- 2. CONSULTA A LA LISTA PERSISTENTE DE CRÉDITOS ---
                // Cada crédito cobra su cuota del mes (lo pagado, o lo que falte si ya va en la última); los saldados no
                var creditos = CreditosView.ListaCreditosPersistente;
                var cuotas = CuotasDelMes(creditos, datos.PagosCredito, mesActual);
                decimal totalCreditosMes = cuotas.Sum(c => c.Cuota);
                decimal pendienteCreditos = cuotas.Where(c => !c.Pagada).Sum(c => c.Cuota);

                // --- 3. CÁLCULO DE VEHÍCULOS ---
                var vehiculos = VehiculosView.listaVehiculos;
                decimal totalV = vehiculos?.Sum(v => v.TotalInvertido) ?? 0;

                // --- 4. OPERACIONES Y BALANCES ---
                // Los gastos del día a día ya están pagados: suman al gasto, no a lo pendiente
                decimal totalDiarios = diariosMes.Sum(g => g.Monto);
                decimal totalGastosYCompromisos = totalGastosCuentas + totalCreditosMes + totalDiarios;
                decimal balanceNeto = totalIngresos - totalGastosYCompromisos;

                // --- 5. INDICADORES ---
                lblPeriodo.Text = Periodo.Nombre(mesActual);
                btnMesActual.Visibility = esActual ? Visibility.Collapsed : Visibility.Visible;
                lblSubtitulo.Text = esActual ? "Así va tu mes, de un vistazo"
                                  : esFuturo ? "Así se proyecta este mes con tus gastos e ingresos registrados"
                                  : "Así fue este mes";
                lblDashIngresos.Text = totalIngresos.ToString("C0");
                lblDashGastos.Text = totalGastosYCompromisos.ToString("C0");
                // El monto pendiente solo se muestra cuando difiere del total (si no, repetiría la cifra grande)
                decimal pendiente = pendienteCuentas + pendienteCreditos;
                lblDashPendiente.Text = pendiente <= 0 ? "Todo pagado"
                                      : pendiente >= totalGastosYCompromisos ? "Pendiente por pagar"
                                      : $"Pendiente por pagar: {pendiente:C0}";
                // Disponible: dinero real = lo confirmado como recibido − lo efectivamente pagado (abonos a metas incluidos).
                // Lo pendiente no se descuenta hasta pagarlo. Un mes futuro solo se puede proyectar.
                var ahorroDelMes = AbonosDelMes(abonos, mesActual);
                decimal totalAhorroMes = ahorroDelMes.Sum(f => f.Monto);
                decimal disponible = esFuturo ? balanceNeto - totalAhorroMes : caja.Saldo;
                lblDashBalance.Text = disponible.ToString("C0");
                lblDashBalanceDetalle.Text = esFuturo ? "Proyección: ingresos menos gastos"
                    : $"Recibido {caja.Inicial + caja.Recibido:C0}\nPagado {caja.Pagado:C0}";
                // Informativo: cuánto quedaría después de pagar lo pendiente (no se descuenta del disponible)
                decimal trasPagar = disponible - pendiente;
                lblDashTrasPagar.Visibility = !esFuturo && pendiente > 0 ? Visibility.Visible : Visibility.Collapsed;
                lblDashTrasPagar.Text = $"Tras pagar lo pendiente: {trasPagar:C0}";
                lblDashTrasPagar.Foreground = Pincel(trasPagar < 0 ? "#FDA29B" : "#6CE9A6");
                lblDashBalanceDetalle.ToolTip = esFuturo ? null
                    : "Lo que confirmaste como recibido (y tu saldo inicial) menos lo que ya pagaste. Lo pendiente no se descuenta hasta que lo pagues.";
                lblPorcentajeLibre.Text = disponible < 0 ? "En déficit"
                    : !esFuturo && pendiente > 0 ? $"Por pagar {pendiente:C0}" : string.Empty;
                lblPorcentajeLibre.Foreground = Pincel(disponible < 0 ? "#FDA29B" : "#FEC84B");
                int nVehiculos = vehiculos?.Count ?? 0;
                lblDashMoto.Text = totalV.ToString("C0");
                lblDashVehiculosDetalle.Text = $"Capital invertido · {nVehiculos} {(nVehiculos == 1 ? "vehículo" : "vehículos")}";

                MostrarSalud(balanceNeto);
                MostrarVencidas(esActual ? cuentasDelMes : new List<CuentaMes>(),
                                esActual ? creditos.Where(c => c.CuotaVencidaHoy()).ToList() : new List<Credito>());

                // --- 6. ¿A DÓNDE VA TU INGRESO? ---
                MostrarDistribucion(cuentasDelMes, cuotas, diariosMes, ahorroDelMes, totalIngresos, totalGastosYCompromisos, balanceNeto);

                // Proyección del próximo mes, con la predicción de cada gasto
                var siguiente = Analisis.Proyeccion(datos, mesActual.AddMonths(1), 1)[0];
                lblProyeccionSiguiente.Inlines.Clear();
                lblProyeccionSiguiente.Inlines.Add(new Run("Proyección "));
                lblProyeccionSiguiente.Inlines.Add(new Run(siguiente.Mes.ToString("MMMM")) { FontWeight = FontWeights.Bold, Foreground = Pincel("#0F1B2D") });
                lblProyeccionSiguiente.Inlines.Add(new Run($": gastos {NumeroHelper.Moneda(siguiente.TotalGastos)} · balance "));
                lblProyeccionSiguiente.Inlines.Add(new Run(NumeroHelper.Moneda(siguiente.Balance))
                {
                    FontWeight = FontWeights.Bold,
                    Foreground = Pincel(siguiente.Balance < 0 ? "#B42318" : "#075E46")
                });

                // --- 7. POR PAGAR ESTE MES ---
                // Próximas: lo que se cobra el mes siguiente al que se está viendo
                var delSiguiente = CuentasMensuales.DelMes(datos.Cuentas, datos.Pagos, mesActual.AddMonths(1)).Where(c => !c.Pagada).ToList();
                var porPagar = FilasPorPagar(cuentasDelMes, cuotas, delSiguiente, mesActual);
                icProximasFechas.ItemsSource = porPagar;
                lblSinProximas.Visibility = porPagar.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

                // --- 8. COMPARADO CON EL MES ANTERIOR (por categoría, incluye las cuotas de créditos) ---
                var mesAnterior = mesActual.AddMonths(-1);
                var cuentasAnterior = CuentasMensuales.DelMes(datos.Cuentas, datos.Pagos, mesAnterior);
                decimal creditosAnterior = CuotasDelMes(creditos, datos.PagosCredito, mesAnterior).Sum(c => c.Cuota);
                MostrarComparacion(Analisis.PorCategoria(cuentasDelMes, totalCreditosMes, diariosMes),
                                   Analisis.PorCategoria(cuentasAnterior, creditosAnterior, diariosAnterior), mesAnterior);

                // --- 9. ACCESOS: AHORRO, INVERSIONES Y CRÉDITOS ---
                MostrarAccesos(inversiones, creditos, abonos);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error al calcular el resumen del Dashboard: " + ex.Message);
            }
        }

        private static SolidColorBrush Pincel(string hex) => (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;

        private void MostrarSalud(decimal balanceNeto)
        {
            bool sano = balanceNeto >= 0;
            cardSaludFinanciera.Background = Pincel(sano ? "#E7F5EF" : "#FEF3F2");
            var color = Pincel(sano ? "#075E46" : "#B42318");
            icoSalud.Geometria = (Geometry)FindResource(sano ? "IcoCheck" : "IcoAlerta");
            icoSalud.Foreground = color;
            txtMensajeSalud.Foreground = color;
            txtMensajeSalud.Text = sano ? "Finanzas saludables" : "Gastas más de lo que ingresas";
        }

        // Cuotas de crédito que pasaron su día de pago sin pagarse (se pagan desde Créditos)
        private List<Credito> _creditosVencidos = new();

        // Aviso rojo: gastos y cuotas de crédito del mes que vencen hoy o ya vencieron y siguen sin pagar
        private void MostrarVencidas(List<CuentaMes> cuentasDelMes, List<Credito> creditosVencidos)
        {
            var hoy = DateTime.Today;
            _vencidas = cuentasDelMes.Where(c => !c.Pagada && c.Vence <= hoy).OrderBy(c => c.Vence).ToList();
            _creditosVencidos = creditosVencidos;

            // Todo lo vencido en una sola lista: nombre, valor y fecha
            var items = _vencidas.Select(c => (Nombre: c.Descripcion, Monto: c.Venta, Vence: c.Vence))
                .Concat(creditosVencidos.Select(c => (Nombre: $"Cuota crédito · {c.NombreCustom}", Monto: c.CuotaEsteMes, Vence: c.VencimientoEn(hoy)!.Value)))
                .OrderBy(i => i.Vence).ToList();

            bannerVencidos.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (items.Count == 0) return;

            lblVencidos.Inlines.Clear();
            if (items.Count == 1)
            {
                var i = items[0];
                lblVencidos.Inlines.Add(new Run("1 pago vencido: ") { FontWeight = FontWeights.Bold });
                lblVencidos.Inlines.Add(new Run($"{i.Nombre} · "));
                lblVencidos.Inlines.Add(new Run(i.Monto.ToString("C0")) { FontWeight = FontWeights.Bold });
                lblVencidos.Inlines.Add(new Run(i.Vence == hoy ? " · vence hoy" : $" · venció el {i.Vence:dd/MM/yyyy}"));
            }
            else
            {
                lblVencidos.Inlines.Add(new Run($"{items.Count} pagos vencidos: ") { FontWeight = FontWeights.Bold });
                lblVencidos.Inlines.Add(new Run(string.Join(", ", items.Select(i => i.Nombre)) + " · total "));
                lblVencidos.Inlines.Add(new Run(items.Sum(i => i.Monto).ToString("C0")) { FontWeight = FontWeights.Bold });
            }

            // Botón verde: las cuentas se marcan aquí; las cuotas se pagan en Créditos (registra intereses y capital)
            btnMarcarPagado.Content = _vencidas.Count == 1 ? "Marcar pagado"
                                    : _vencidas.Count > 1 ? "Revisar pagos"
                                    : "Pagar cuota";
            lnkVencidos.Content = _creditosVencidos.Count > 0 ? "Ver créditos" : "Ver cuentas";
        }

        private void LnkVencidos_Click(object sender, RoutedEventArgs e)
        {
            if (_creditosVencidos.Count > 0) MainWindow.Actual?.IrACreditos(_creditosVencidos[0].Id);
            else MainWindow.Actual?.IrACuentas();
        }

        // Abonos del mes agrupados por meta (las metas eliminadas no cuentan)
        internal static List<FilaDistribucion> AbonosDelMes(List<AbonoMeta> abonos, DateTime mes)
        {
            var metas = IngresosView.ListaMetas.ToDictionary(m => m.Id);
            var clave = Periodo.Clave(mes);
            return abonos.Where(a => Periodo.Clave(a.Fecha) == clave && metas.ContainsKey(a.MetaId))
                         .GroupBy(a => a.MetaId)
                         .Select(g => new FilaDistribucion
                         {
                             Nombre = $"Ahorro · {NombreMeta(metas[g.Key])}",
                             Monto = g.Sum(a => a.Monto),
                             Color = "#14928F",
                             ColorMonto = "#0E6E6E",
                             // Al pasar el mouse: cuándo y cuánto fue cada abono
                             Detalle = string.Join("\n", g.OrderBy(a => a.Fecha)
                                                          .Select(a => $"{a.Fecha:dd MMM · h:mm tt}  {NumeroHelper.Moneda(a.Monto)}"))
                         })
                         .OrderByDescending(f => f.Monto)
                         .ToList();
        }

        private void MostrarDistribucion(List<CuentaMes> cuentasDelMes, List<CuotaDelMes> cuotas, List<GastoDiario> diarios, List<FilaDistribucion> ahorro,
                                         decimal ingresos, decimal gastos, decimal disponible)
        {
            var filas = new List<FilaDistribucion>();
            foreach (var c in cuotas.Where(c => c.Cuota > 0))
                filas.Add(new FilaDistribucion { Nombre = $"Crédito · {c.Credito.NombreCustom}", Monto = c.Cuota, Color = "#6941C6" });
            foreach (var cat in Analisis.PorCategoria(cuentasDelMes, 0, diarios))
                filas.Add(new FilaDistribucion { Nombre = cat.Nombre, Monto = cat.Total, Color = cat.ColorBarra });
            filas = filas.OrderByDescending(f => f.Monto).ToList();

            // Lo abonado a metas sale de lo disponible
            decimal totalAhorro = ahorro.Sum(f => f.Monto);
            filas.AddRange(ahorro);
            decimal libre = disponible - totalAhorro;
            if (libre > 0)
                filas.Add(new FilaDistribucion { Nombre = "Disponible", Monto = libre, Color = "#0B7A5C", ColorMonto = "#075E46" });

            // Si se gasta (y ahorra) más de lo que entra, el 100 % es lo que salió
            decimal baseTotal = Math.Max(ingresos, gastos + totalAhorro);
            foreach (var f in filas) f.Porcentaje = baseTotal > 0 ? (double)(f.Monto / baseTotal) : 0;

            icDistribucion.ItemsSource = filas;
            lblSinIngresoDistribucion.Visibility = filas.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            // Barra apilada: una columna proporcional por parte, con un pequeño espacio entre ellas
            barraIngreso.Children.Clear();
            barraIngreso.ColumnDefinitions.Clear();
            for (int i = 0; i < filas.Count; i++)
            {
                barraIngreso.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength((double)filas[i].Monto, GridUnitType.Star) });
                var parte = new Border
                {
                    Background = Pincel(filas[i].Color),
                    CornerRadius = new CornerRadius(i == 0 ? 7 : 0, i == filas.Count - 1 ? 7 : 0, i == filas.Count - 1 ? 7 : 0, i == 0 ? 7 : 0),
                    Margin = new Thickness(0, 0, i == filas.Count - 1 ? 0 : 2, 0),
                    ToolTip = filas[i].ToolTipFila
                };
                Grid.SetColumn(parte, i);
                barraIngreso.Children.Add(parte);
            }
            barraIngreso.Visibility = filas.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // Pendientes del mes (vencidos primero), cuotas de crédito sin pagar y las próximas fechas
        internal static List<FilaPorPagar> FilasPorPagar(List<CuentaMes> cuentasDelMes, List<CuotaDelMes> cuotas, List<CuentaMes> proximas, DateTime mes)
        {
            var hoy = DateTime.Today;
            var filas = new List<FilaPorPagar>();

            // Fila con fecha (cuenta o cuota de crédito con día de pago): roja si ya venció
            FilaPorPagar ConFecha(DateTime vence, string titulo, decimal monto)
            {
                bool vencida = vence <= hoy;
                return new FilaPorPagar
                {
                    Dia = vence.Day.ToString("00"), MesCorto = MesCorto(vence), Titulo = titulo, Monto = monto,
                    Detalle = vence == hoy ? "Vence hoy" : vencida ? $"Vencida el {vence:dd/MM}" : $"Vence el {vence:dd/MM}",
                    Fondo = vencida ? "#FEF3F2" : "#FFFFFF", Borde = vencida ? "#FECDCA" : "#E3E7EE",
                    ColorDia = vencida ? "#B42318" : "#0F1B2D", ColorDetalle = vencida ? "#B42318" : "#93370D",
                };
            }

            var conFecha = cuentasDelMes.Where(c => !c.Pagada)
                                        .Select(c => (Vence: c.Vence, Fila: ConFecha(c.Vence, c.Descripcion, c.Venta)))
                                        .ToList();
            var sinFecha = new List<FilaPorPagar>();
            bool esActual = Periodo.EsMesActual(mes);
            foreach (var cr in cuotas.Where(c => !c.Pagada && c.Cuota > 0))
            {
                var titulo = $"Cuota crédito · {cr.Credito.NombreCustom}";
                if (cr.Credito.VencimientoEn(mes) is DateTime vence)
                {
                    var fila = ConFecha(vence, titulo, cr.Cuota);
                    fila.EsCredito = true;
                    conFecha.Add((vence, fila));
                }
                else // créditos sin día de pago registrado
                    sinFecha.Add(new FilaPorPagar
                    {
                        Dia = "—", MesCorto = MesCorto(mes), Titulo = titulo, Monto = cr.Cuota,
                        Detalle = esActual ? "Pendiente este mes" : $"Cuota de {mes:MMMM}", Fondo = "#FFFFFF", Borde = "#E3E7EE",
                        ColorDia = "#6941C6", ColorDetalle = "#93370D", EsCredito = true,
                    });
            }
            // Tarjetas: recordatorio de la cuota del mes (suma de las cuotas de cada compra; solo sus intereses son gasto)
            if (esActual)
                foreach (var cr in cuotas.Where(c => c.Credito.EsTarjeta && !c.Credito.CuotaTarjetaPagada && c.Credito.CuotaMesTarjeta > 0))
                {
                    var titulo = $"Cuota tarjeta · {cr.Credito.NombreCustom}";
                    if (cr.Credito.VencimientoEn(mes) is DateTime vence)
                    {
                        var fila = ConFecha(vence, titulo, cr.Credito.CuotaMesTarjeta);
                        fila.EsCredito = true;
                        conFecha.Add((vence, fila));
                    }
                    else
                        sinFecha.Add(new FilaPorPagar
                        {
                            Dia = "—", MesCorto = MesCorto(mes), Titulo = titulo, Monto = cr.Credito.CuotaMesTarjeta,
                            Detalle = cr.Credito.InteresMesTarjeta > 0 ? $"Incluye {cr.Credito.InteresMesTarjeta:C0} de intereses" : "Pendiente este mes", Fondo = "#FFFFFF", Borde = "#E3E7EE",
                            ColorDia = "#6941C6", ColorDetalle = "#93370D", EsCredito = true,
                        });
                }

            // Los vencidos van primero; luego las cuotas sin día de pago y el resto por fecha
            var ordenadas = conFecha.OrderBy(x => x.Vence).ToList();
            var todas = ordenadas.Where(x => x.Vence <= hoy).Select(x => x.Fila)
                                 .Concat(sinFecha)
                                 .Concat(ordenadas.Where(x => x.Vence > hoy).Select(x => x.Fila))
                                 .ToList();

            // Máximo 5 filas, pero las cuotas de crédito siempre se ven (son los pagos más grandes)
            int cupo = Math.Max(0, Maximo - todas.Count(f => f.EsCredito));
            foreach (var f in todas)
                if (f.EsCredito || cupo-- > 0) filas.Add(f);

            // Próxima ocurrencia (mes siguiente al que se ve), solo si queda espacio: nunca se confunde con lo vencido
            foreach (var p in proximas.OrderBy(p => p.Vence).Take(Math.Min(2, Maximo - filas.Count)))
            {
                int dias = (p.Vence - hoy).Days;
                filas.Add(new FilaPorPagar
                {
                    Dia = p.Vence.Day.ToString("00"), MesCorto = MesCorto(p.Vence), Titulo = $"Próxima: {p.Descripcion}", Monto = p.Venta,
                    Detalle = dias >= 0 ? $"{p.Categoria} · en {dias} {(dias == 1 ? "día" : "días")}" : $"{p.Categoria} · {p.Vence:dd/MM/yyyy}",
                    Fondo = "#F4F6F9", Borde = "#F4F6F9", ColorDia = "#5B6676", ColorDetalle = "#5B6676", ColorTitulo = "#3B4656",
                });
            }
            return filas;
        }

        // Filas que caben en "Por pagar este mes"
        private const int Maximo = 5;

        // En qué categorías se gasta más o menos que el mes anterior
        private void MostrarComparacion(List<TotalCategoria> actual, List<TotalCategoria> anterior, DateTime mesAnterior)
        {
            string nombreAnterior = mesAnterior.ToString("MMMM");
            decimal total = actual.Sum(c => c.Total), totalAnterior = anterior.Sum(c => c.Total);
            decimal cambio = total - totalAnterior;

            lblTituloComparacion.Text = $"Comparado con {nombreAnterior}";
            lblTotalCategorias.Text = NumeroHelper.Moneda(total);
            lblNombreAnterior.Text = char.ToUpper(nombreAnterior[0]) + nombreAnterior.Substring(1);
            lblTotalAnterior.Text = NumeroHelper.Moneda(totalAnterior);

            if (totalAnterior == 0)
            {
                lblCambioTotal.Text = "Sin comparación";
                lblCambioTotal.Foreground = Pincel("#5B6676");
                lblCambioDetalle.Text = $"No hay gastos registrados en {nombreAnterior} para comparar.";
            }
            else if (cambio == 0)
            {
                lblCambioTotal.Text = "Igual";
                lblCambioTotal.Foreground = Pincel("#5B6676");
                lblCambioDetalle.Text = $"Gastas lo mismo que en {nombreAnterior}.";
            }
            else
            {
                bool mas = cambio > 0;
                lblCambioTotal.Text = $"{(mas ? "▲" : "▼")} {Math.Abs(cambio) / totalAnterior:P0}";
                lblCambioTotal.Foreground = Pincel(mas ? "#B42318" : "#075E46");
                lblCambioDetalle.Text = $"Gastas {NumeroHelper.Moneda(Math.Abs(cambio))} {(mas ? "más" : "menos")} que en {nombreAnterior}.";
            }

            // Todas las categorías de los dos meses; primero las que más cambiaron
            var filas = actual.Select(c => c.Nombre).Union(anterior.Select(c => c.Nombre))
                .Select(nombre =>
                {
                    decimal ahora = actual.FirstOrDefault(c => c.Nombre == nombre)?.Total ?? 0;
                    decimal antes = anterior.FirstOrDefault(c => c.Nombre == nombre)?.Total ?? 0;
                    return new FilaComparacion { Nombre = nombre, Total = ahora, Anterior = antes, ColorBarra = Etiquetas.ColorBarra(nombre) };
                })
                .OrderByDescending(f => Math.Abs(f.Total - f.Anterior)).ThenByDescending(f => f.Total)
                .Take(5)
                .ToList();
            icCategoriasMes.ItemsSource = filas;
            lblSinCategorias.Visibility = filas.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private static string MesCorto(DateTime fecha) => fecha.ToString("MMM").TrimEnd('.').ToUpperInvariant();

        private void MostrarAccesos(List<Movimiento> inversiones, IEnumerable<Credito> creditos, List<AbonoMeta> abonos)
        {
            var metas = IngresosView.ListaMetas;
            lblAhorro.Text = metas.Sum(m => m.MontoActual).ToString("C0");
            // Con abonos registrados se muestra el último; si no, cómo va el ahorro
            var ultimo = abonos.Where(a => metas.Any(m => m.Id == a.MetaId)).OrderByDescending(a => a.Fecha).FirstOrDefault();
            lblAhorroDetalle.Text = ultimo != null
                ? $"Último abono: {NumeroHelper.Moneda(ultimo.Monto)} a “{NombreMeta(metas.First(m => m.Id == ultimo.MetaId))}” · {ultimo.Fecha:dd MMM}"
                : metas.Count switch
                {
                    0 => "Aún no tienes metas de ahorro",
                    1 => $"Meta “{NombreMeta(metas[0])}” al {metas[0].Progreso / 100:P0}",
                    _ => $"Repartido en {metas.Count} metas"
                };

            lblInversiones.Text = inversiones.Sum(i => i.Costo).ToString("C0");
            lblInversionesDetalle.Text = inversiones.Count == 0
                ? "Sin inversiones registradas"
                : string.Join(" · ", inversiones.Select(i => i.Descripcion).Take(3)) + (inversiones.Count > 3 ? "…" : "");

            // La deuda incluye lo que se debe de las tarjetas; el avance ("ya has pagado") es solo de los préstamos
            var prestamos = creditos.Where(c => !c.EsTarjeta).ToList();
            var activos = prestamos.Where(c => !c.Saldado).ToList();
            decimal deuda = creditos.Sum(c => Math.Max(0, c.Disponible));
            decimal total = prestamos.Sum(c => c.Total);
            lblDeuda.Text = deuda.ToString("C0");
            barDeuda.Value = total > 0 ? (double)(prestamos.Sum(c => c.CapitalPagado) / total) : 0;

            // Avance: capital ya pagado de los préstamos y cuánto representa del total prestado
            decimal pagado = prestamos.Sum(c => c.CapitalPagado);
            lblFinCredito.Text = NumeroHelper.Moneda(pagado);
            if (total <= 0)
                lblFinCreditoDetalle.Text = "No tienes créditos registrados";
            else if (activos.Count == 0)
                lblFinCreditoDetalle.Text = "¡Todos tus créditos están saldados!";
            else
                lblFinCreditoDetalle.Text = $"{pagado / total:P0} de tus créditos · ¡vas avanzando!";
        }

        private static string NombreMeta(MetaAhorro m) => string.IsNullOrWhiteSpace(m.NombreCustom) ? m.Nombre : m.NombreCustom;

        // --- Acciones ---

        // Un solo vencido: se marca pagado por su valor. Varios: se abre la ventana para revisarlos uno a uno.
        private void BtnMarcarPagado_Click(object sender, RoutedEventArgs e)
        {
            if (_vencidas.Count == 0)
            {
                // Solo hay cuotas de crédito vencidas: se pagan desde su detalle en Créditos
                if (_creditosVencidos.Count > 0) MainWindow.Actual?.IrACreditos(_creditosVencidos[0].Id);
                return;
            }
            try
            {
                if (_vencidas.Count == 1)
                {
                    var c = _vencidas[0];
                    using (var db = new AppDbContext())
                    {
                        CuentasMensuales.MarcarPagada(db, c.Cuenta.Id, c.Mes, true, c.Venta, c.Quincena);
                    }
                    toast.Show($"“{c.Descripcion}” marcado como pagado");
                }
                else
                {
                    new VentanaRecordatorioPagos(_vencidas) { Owner = Window.GetWindow(this) }.ShowDialog();
                }
                CargarResumen();
                MainWindow.Actual?.VerificarVencimientosCuentas();
            }
            catch (Exception ex)
            {
                MisFinanzas.Controles.Aviso.Mostrar("Error al marcar el pago: " + ex.Message);
            }
        }

        private void BtnVerCuentas_Click(object sender, RoutedEventArgs e) => MainWindow.Actual?.IrACuentas();
        private void TarjetaAhorro_Click(object sender, RoutedEventArgs e) => MainWindow.Actual?.IrAIngresos();
        private void TarjetaInversiones_Click(object sender, RoutedEventArgs e) => MainWindow.Actual?.IrAInversiones();
        private void TarjetaCreditos_Click(object sender, RoutedEventArgs e) => MainWindow.Actual?.IrACreditos();
    }

    // ===== Filas que muestra el Dashboard (solo presentación) =====

    public class FilaDistribucion
    {
        public string Nombre { get; set; } = string.Empty;
        public decimal Monto { get; set; }
        public double Porcentaje { get; set; }
        public string PorcentajeTexto => Porcentaje.ToString("P0");
        public string Color { get; set; } = "#8A94A3";
        public string ColorMonto { get; set; } = "#0F1B2D";
        public string? Detalle { get; set; } // texto extra del tooltip (p. ej. cuándo y cuánto se abonó a una meta)
        public string ToolTipFila => Detalle is null ? $"{Nombre}: {NumeroHelper.Moneda(Monto)}" : $"{Nombre}: {NumeroHelper.Moneda(Monto)}\n{Detalle}";
    }

    public class FilaPorPagar
    {
        public string Dia { get; set; } = string.Empty;
        public string MesCorto { get; set; } = string.Empty;
        public string Titulo { get; set; } = string.Empty;
        public string Detalle { get; set; } = string.Empty;
        public decimal Monto { get; set; }
        public string Fondo { get; set; } = "#FFFFFF";
        public string Borde { get; set; } = "#E3E7EE";
        public string ColorDia { get; set; } = "#0F1B2D";
        public string ColorTitulo { get; set; } = "#0F1B2D";
        public string ColorDetalle { get; set; } = "#5B6676";
        public bool EsCredito { get; set; } // cuota de crédito: siempre se muestra
    }

    /// <summary>Una categoría comparada con el mes anterior.</summary>
    public class FilaComparacion
    {
        public string Nombre { get; set; } = string.Empty;
        public decimal Total { get; set; }
        public decimal Anterior { get; set; }
        public string ColorBarra { get; set; } = "#8A94A3";
        private decimal Cambio => Total - Anterior;

        // ▲ más gasto (rojo), ▼ menos gasto (verde), Nuevo (no había el mes anterior), Igual
        public string TextoCambio => Anterior == 0 ? "Nuevo"
                                   : Total == 0 ? "Ya no"
                                   : Cambio == 0 ? "Igual"
                                   : $"{(Cambio > 0 ? "▲" : "▼")} {NumeroHelper.Moneda(Math.Abs(Cambio))}";
        public string FondoCambio => Anterior == 0 ? "#EAF0FD" : Cambio > 0 ? "#FEF3F2" : Cambio < 0 ? "#E7F5EF" : "#EEF1F5";
        public string ColorCambio => Anterior == 0 ? "#1D4FB8" : Cambio > 0 ? "#B42318" : Cambio < 0 ? "#075E46" : "#4B5565";
    }
}
