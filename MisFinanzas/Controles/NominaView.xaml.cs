using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MisFinanzas.Modulos;

namespace MisFinanzas.Controles
{
    public partial class NominaView : UserControl
    {
        public bool EditorAbierto => editor.Visibility == Visibility.Visible;
        public bool SoloConfiguracion { get; set; }
        private string? _pagoEsperado;
        private bool _enviando; // subiendo la nómina recién guardada al celular
        public NominaView()
        {
            InitializeComponent();
            dias.ItemsSource = Enumerable.Range(1, 31).ToArray();
            segundoDia.ItemsSource = Enumerable.Range(1, 31).ToArray();
            frecuencia.ItemsSource = Nomina.Frecuencias;
            semana.ItemsSource = new[] { "Domingo", "Lunes", "Martes", "Miércoles", "Jueves", "Viernes", "Sábado" };
            fecha.SelectedDate = DateTime.Today;
            fecha.DisplayDateEnd = DateTime.Today;
            Loaded += (_, _) => { SincronizacionCelular.NominaActualizada += RecibirCambio; Cargar(); };
            Unloaded += (_, _) => SincronizacionCelular.NominaActualizada -= RecibirCambio;
        }
        private void RecibirCambio() => Dispatcher.BeginInvoke(() => { if (editor.Visibility != Visibility.Visible) Cargar(); });
        private void Cargar(bool abrirSiFalta = true)
        {
            try
            {
                reglas.Visibility = SoloConfiguracion ? Visibility.Visible : Visibility.Collapsed;
                movimientos.Visibility = SoloConfiguracion ? Visibility.Collapsed : Visibility.Visible;
                using var db = new AppDbContext();
                var c = db.ConfiguracionesNomina.Find(1);
                var compatible = c != null && Nomina.Frecuencias.Contains(c.Frecuencia);
                bool configurada = c?.Confirmada == true && compatible;
                // Quincenas con valores distintos: el total del mes y el valor de cada día en sus chips
                bool dosValores = configurada && c!.Frecuencia == "Quincenal" && Nomina.MontoDelPago(c, 0) != Nomina.MontoDelPago(c, 1);
                txtResumen.Text = !configurada ? "Configura la frecuencia y los días en que recibes tu nómina."
                                : dosValores ? "Quincenal · total al mes" : $"{c!.Frecuencia} · por pago";
                txtMontoNomina.Text = !configurada ? string.Empty : NumeroHelper.Moneda(dosValores ? Nomina.SalarioMensual(c!) : c!.MontoPorPago);
                txtMontoNomina.Visibility = configurada ? Visibility.Visible : Visibility.Collapsed;
                var semana = new[] { "Domingo", "Lunes", "Martes", "Miércoles", "Jueves", "Viernes", "Sábado" };
                chipsDias.ItemsSource = !configurada ? null
                    : dosValores ? Nomina.LeerDias(c!.Dias).Select((d, i) => $"{d}  ·  {NumeroHelper.Moneda(Nomina.MontoDelPago(c, i))}").ToList()
                    : c!.Frecuencia is "Mensual" or "Quincenal" ? Nomina.LeerDias(c.Dias).Select(d => d.ToString()).ToList()
                    : new[] { semana[Math.Clamp(c.DiaSemana, 0, 6)] }.ToList();
                txtEtiquetaDias.Text = configurada && c!.Frecuencia is not ("Mensual" or "Quincenal") ? "Día de pago" : "Días de pago";
                filaDias.Visibility = configurada ? Visibility.Visible : Visibility.Collapsed;
                // Cambio guardado en el PC que aún no se sube a la nube
                bool conectado = configurada && SincronizacionCelular.Conectado;
                avisoPendiente.Visibility = conectado && c!.Pendiente && !_enviando ? Visibility.Visible : Visibility.Collapsed;
                avisoSincronizado.Visibility = conectado && (!c!.Pendiente || _enviando) ? Visibility.Visible : Visibility.Collapsed;
                iconoSincronizado.Visibility = _enviando ? Visibility.Collapsed : Visibility.Visible;
                txtSincronizado.Text = _enviando ? "Enviando a tu celular…" : "Sincronizado con tu celular";
                var registros = db.IngresosRecibidos.Where(i => !i.Borrado).ToList();
                esperados.ItemsSource = c == null ? null : Nomina.DelMes(c, DateTime.Today).Where(p => !registros.Any(i => i.Uid == p.Uid)).ToList();
                recibidos.ItemsSource = registros.Where(i => Periodo.EsMesActual(i.Fecha)).OrderByDescending(i => i.Fecha).ToList();
                error.Text = "";
                var saldo = Nomina.Saldo(db, DateTime.Today);
                txtSaldo.Text = $"Dinero disponible según tus registros: {saldo:C0}";
                if (abrirSiFalta && SoloConfiguracion && (c?.Confirmada != true || !compatible) && editor.Visibility != Visibility.Visible) AbrirEditor();
            }
            catch (Exception ex) { error.Text = ex.Message; }
        }
        /// <summary>Abre el formulario si está cerrado y pone el cursor en el valor del primer pago.</summary>
        public void Editar()
        {
            if (!EditorAbierto) AbrirEditor();
            Dispatcher.BeginInvoke(() => { montoEsperado.Focus(); montoEsperado.SelectAll(); }, System.Windows.Threading.DispatcherPriority.Input);
        }

        private void AbrirEditor()
        {
            using var db = new AppDbContext();
            var c = db.ConfiguracionesNomina.Find(1) ?? new ConfiguracionNomina();
            frecuencia.SelectedItem = Nomina.Frecuencias.Contains(c.Frecuencia) ? c.Frecuencia : "Quincenal";
            var guardados = Nomina.LeerDias(c.Dias);
            dias.SelectedItem = guardados.Count > 0 ? (object)guardados[0] : null;
            segundoDia.SelectedItem = guardados.Count > 1 ? (object)guardados[1] : null;
            semana.SelectedIndex = c.DiaSemana;
            montoEsperado.Text = c.MontoPorPago > 0 ? NumeroHelper.FormatearMoneda(c.MontoPorPago) : "";
            var segundo = Nomina.MontoDelPago(c, 1);
            montoSegundo.Text = segundo > 0 ? NumeroHelper.FormatearMoneda(segundo) : "";
            editor.Visibility = Visibility.Visible;
            btnEditar.Content = "Cancelar";
        }
        private void Configuracion_Click(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is MainWindow ventana) ventana.IrAConfiguracion();
        }
        private void Editar_Click(object sender, RoutedEventArgs e)
        {
            if (editor.Visibility != Visibility.Visible) { AbrirEditor(); return; }
            editor.Visibility = Visibility.Collapsed;
            btnEditar.Content = "Editar";
            Cargar(abrirSiFalta: false);
        }

        // Sube ya el cambio de la nómina en vez de esperar la próxima sincronización
        private async void EnviarCelular_Click(object sender, RoutedEventArgs e) => await EnviarAlCelularAsync();

        /// <summary>
        /// Envía la nómina al celular y espera la confirmación. Si ya corría una sincronización automática
        /// (que no recibe pedidos nuevos), reintenta unos segundos. Sin conexión queda el aviso "Enviar al celular".
        /// </summary>
        private async System.Threading.Tasks.Task EnviarAlCelularAsync()
        {
            if (!SincronizacionCelular.Conectado || _enviando) return;
            _enviando = true; Cargar(abrirSiFalta: false);
            try
            {
                for (int intento = 0; intento < 6 && PendienteEnPC(); intento++)
                {
                    await SincronizacionCelular.SincronizarAsync();
                    if (PendienteEnPC()) await System.Threading.Tasks.Task.Delay(1000);
                }
            }
            finally { _enviando = false; Cargar(abrirSiFalta: false); }
        }

        private static bool PendienteEnPC()
        {
            using var db = new AppDbContext();
            return db.ConfiguracionesNomina.Find(1)?.Pendiente == true;
        }
        private void Frecuencia_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (panelDias == null || panelSegundoDia == null || panelSegundoPago == null || panelSemana == null || etiquetaMonto == null) return;
            var f = frecuencia.SelectedItem as string;
            panelDias.Visibility = f is "Mensual" or "Quincenal" ? Visibility.Visible : Visibility.Collapsed;
            panelSemana.Visibility = f == "Semanal" ? Visibility.Visible : Visibility.Collapsed;
            panelSegundoPago.Visibility = panelSegundoDia.Visibility = f == "Quincenal" ? Visibility.Visible : Visibility.Collapsed;
            etiquetaDia.Text = f == "Quincenal" ? "Primer pago (día)" : "Día de pago";
            etiquetaMonto.Text = f == "Quincenal" ? "Valor del primer pago" : "Monto neto por pago";
        }
        private async void GuardarPlan_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                using var db = new AppDbContext();
                var c = db.ConfiguracionesNomina.Find(1) ?? new ConfiguracionNomina();
                c.Frecuencia = frecuencia.SelectedItem as string ?? "";
                if (!Nomina.Frecuencias.Contains(c.Frecuencia)) throw new ArgumentException("Elige la frecuencia de pago.");
                if (c.Frecuencia is "Mensual" or "Quincenal" && dias.SelectedItem is not int) throw new ArgumentException("Selecciona el día de pago.");
                if (c.Frecuencia == "Quincenal" && segundoDia.SelectedItem is not int) throw new ArgumentException("Selecciona el segundo día de pago.");
                c.DiaSemana = semana.SelectedIndex;
                if (!NumeroHelper.TryParseMoneda(montoEsperado.Text, out var valor)) throw new ArgumentException("Escribe el monto esperado por pago.");
                c.MontoPorPago = valor; c.MontoSegundoPago = 0;
                if (c.Frecuencia == "Quincenal")
                {
                    // Segundo valor vacío = igual al primero. Se guarda en orden de días: MontoPorPago es el del día más temprano.
                    var segundo = string.IsNullOrWhiteSpace(montoSegundo.Text) ? valor
                        : NumeroHelper.TryParseMoneda(montoSegundo.Text, out var v2) && v2 > 0 ? v2 : throw new ArgumentException("Escribe el valor del segundo pago.");
                    int d1 = (int)dias.SelectedItem!, d2 = (int)segundoDia.SelectedItem!;
                    if (d1 > d2) { (d1, d2) = (d2, d1); (valor, segundo) = (segundo, valor); }
                    c.Dias = $"{d1},{d2}"; c.MontoPorPago = valor; c.MontoSegundoPago = segundo;
                }
                else c.Dias = c.Frecuencia == "Mensual" ? $"{dias.SelectedItem}" : "";
                Nomina.Validar(c); c.Confirmada = true; c.Pendiente = true; c.ActualizadaUtc = DateTime.UtcNow;
                if (db.ConfiguracionesNomina.Find(1) == null) db.ConfiguracionesNomina.Add(c);
                Nomina.ActualizarSalario(db, c); // el salario mensual de Ingresos queda igual a la nómina
                db.SaveChanges(); editor.Visibility = Visibility.Collapsed; btnEditar.Content = "Editar"; error.Text = ""; Cargar(); await EnviarAlCelularAsync(); // se envía de una: no hay que pedirlo
            }
            catch (Exception ex) { error.Text = ex.Message; }
        }
        private void Recibir_Click(object sender, RoutedEventArgs e)
        {
            if (((Button)sender).DataContext is not PagoEsperado pago) return;
            _pagoEsperado = pago.Uid; monto.Text = NumeroHelper.FormatearMoneda(pago.Monto);
            concepto.Text = $"Nómina prevista {pago.Fecha:dd/MM/yyyy}"; fecha.SelectedDate = DateTime.Today;
            inicial.IsChecked = false; monto.Focus(); monto.SelectAll();
        }
        private void Confirmar_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!NumeroHelper.TryParseMoneda(monto.Text, out var valor)) throw new ArgumentException("Escribe cuánto recibiste.");
                using var db = new AppDbContext();
                Nomina.GuardarCobro(db, _pagoEsperado ?? Guid.NewGuid().ToString("N"), fecha.SelectedDate ?? DateTime.Today, valor, concepto.Text, inicial.IsChecked == true);
                _pagoEsperado = null; monto.Clear(); concepto.Text = "Nómina"; inicial.IsChecked = false; error.Text = "";
                Cargar(); SincronizacionCelular.SincronizarPronto();
            }
            catch (Exception ex) { error.Text = ex.Message; }
        }
        private void Eliminar_Click(object sender, RoutedEventArgs e)
        {
            if (((Button)sender).DataContext is not IngresoRecibido ingreso) return;
            try
            {
                using var db = new AppDbContext(); var i = db.IngresosRecibidos.Find(ingreso.Uid); if (i == null) return;
                i.Borrado = true; i.Pendiente = true; i.ActualizadaUtc = DateTime.UtcNow; db.SaveChanges(); Cargar(); SincronizacionCelular.SincronizarPronto();
            }
            catch (Exception ex) { error.Text = ex.Message; }
        }
    }
}
