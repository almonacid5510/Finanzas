// ============================================================
//  Proyecto: Mis Finanzas
//  Autoria: Juan Almonacid
//  Copyright (c) 2026 Juan Almonacid. Todos los derechos reservados.
// ============================================================
using System;
using System.Collections.ObjectModel;
using System.Linq; // Necesario para el .Sum()
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MisFinanzas.Modulos
{
    public partial class IngresosView : UserControl
    {
        // Lista vinculada al ItemsControl del XAML
        public static ObservableCollection<MetaAhorro> ListaMetas { get; set; } = new ObservableCollection<MetaAhorro>();
        public static string SalarioGuardado { get; set; } = "$ 0";

        public IngresosView()
        {
            InitializeComponent();

            // La BD ya se crea/actualiza al iniciar la app (MainWindow -> EsquemaBD).
            // La carga va en Loaded, cuando los controles del XAML ya existen.
            this.Loaded += (s, e) =>
            {
                // Ahora icMetas ya no es null
                icMetas.ItemsSource = ListaMetas;
                lblPeriodo.Text = Periodo.Nombre(DateTime.Today);
                CargarMetasDesdeBD();
                CargarSalarioDesdeBD();
                CargarIngresosExtra();
            };
        }
        private void CargarMetasDesdeBD()
        {
            try
            {
                using (var db = new AppDbContext())
                {
                    var metasBD = db.Metas.ToList();

                    ListaMetas.Clear();
                    foreach (var m in metasBD)
                    {
                        ListaMetas.Add(m);
                    }
                }
                ActualizarAhorroAcumulado();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al cargar: {ex.Message}");
            }
        }

        private void CargarSalarioDesdeBD()
        {
            try
            {
                using (var db = new AppDbContext())
                {
                    var ingreso = db.Movimientos.FirstOrDefault(m => m.Tipo == "Ingreso");
                    if (ingreso != null)
                    {
                        SalarioGuardado = ingreso.Venta.ToString("C0");
                    }
                    else
                    {
                        SalarioGuardado = "$ 0";
                    }
                    lblIngresoTotal.Text = SalarioGuardado;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al cargar salario: {ex.Message}");
            }
        }

        // --- LÓGICA DE EDICIÓN DE SALARIO (IN-PLACE) ---

        private void BtnActivarEdicion_Click(object sender, RoutedEventArgs e)
        {
            // Oculta el texto y muestra el cuadro de edición
            pnlLecturaSalario.Visibility = Visibility.Collapsed;
            pnlEdicionSalario.Visibility = Visibility.Visible;

            // Pre-cargamos el valor ya formateado con separadores de miles
            NumeroHelper.TryParseMoneda(lblIngresoTotal.Text, out var actual);
            txtEditarSalario.Text = actual > 0 ? NumeroHelper.FormatearMoneda(actual) : "";
            txtEditarSalario.Focus();
        }

        private void BtnConfirmarSalario_Click(object sender, RoutedEventArgs e)
        {
            if (NumeroHelper.TryParseMoneda(txtEditarSalario.Text, out decimal nuevoSalario))
            {
                try
                {
                    using (var db = new AppDbContext())
                    {
                        var ingreso = db.Movimientos.FirstOrDefault(m => m.Tipo == "Ingreso");
                        if (ingreso != null)
                        {
                            ingreso.Venta = nuevoSalario;
                            ingreso.Fecha = DateTime.Now;
                            db.Movimientos.Update(ingreso);
                        }
                        else
                        {
                            var nuevoIngreso = new Movimiento
                            {
                                Descripcion = "Salario Mensual",
                                Venta = nuevoSalario,
                                Costo = 0,
                                Tipo = "Ingreso",
                                EstaPagado = true,
                                Fecha = DateTime.Now
                            };
                            db.Movimientos.Add(nuevoIngreso);
                        }
                        db.SaveChanges();
                    }

                    SalarioGuardado = nuevoSalario.ToString("C0");
                    lblIngresoTotal.Text = SalarioGuardado;
                    FinalizarEdicion();
                    CargarIngresosExtra(); // actualiza el total del mes
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error al guardar salario: {ex.Message}");
                }
            }
        }

        private void BtnCancelarEdicion_Click(object sender, RoutedEventArgs e)
        {
            FinalizarEdicion();
        }

        private void FinalizarEdicion()
        {
            pnlLecturaSalario.Visibility = Visibility.Visible;
            pnlEdicionSalario.Visibility = Visibility.Collapsed;
        }

        // --- LÓGICA DE METAS ---

        private void BtnNuevaMeta_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(txtNombreMeta.Text) &&
                NumeroHelper.TryParseMoneda(txtValorMeta.Text, out decimal objetivo) &&
                int.TryParse(txtMesesMeta.Text, out int meses))
            {
                var nuevaMeta = new MetaAhorro
                {
                    Nombre = txtNombreMeta.Text,
                    MontoObjetivo = objetivo,
                    MontoActual = 0,
                    PlazoMeses = meses
                };

                try
                {
                    using (var db = new AppDbContext())
                    {
                        db.Metas.Add(nuevaMeta);
                        db.SaveChanges(); // Aquí es donde se escribe en el archivo .db
                    }

                    ListaMetas.Add(nuevaMeta);
                    txtNombreMeta.Clear();
                    txtValorMeta.Clear();
                    txtMesesMeta.Clear();
                    ActualizarAhorroAcumulado();

                    toast.Show("Meta creada correctamente");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error crítico al guardar: {ex.Message}");
                }
            }
        }

        private void ActualizarAhorroAcumulado()
        {
            // El ahorro total es exclusivamente la suma del MontoActual de todas las metas
            decimal totalAcumulado = ListaMetas.Sum(m => m.MontoActual);
            lblAhorroAcumulado.Text = totalAcumulado.ToString("C0");
            lblMetasResumen.Text = ListaMetas.Count switch
            {
                0 => "Sin metas todavía",
                1 => "Repartido en 1 meta",
                _ => $"Repartido en {ListaMetas.Count} metas"
            };
            lblSinMetas.Visibility = ListaMetas.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // Cuota mensual estimada de la meta nueva, con la misma fórmula de MetaAhorro.CuotaMensual
        private void CamposMeta_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (lblCuotaEstimada == null) return;
            bool valido = NumeroHelper.TryParseMoneda(txtValorMeta.Text, out var objetivo) &&
                          int.TryParse(txtMesesMeta.Text, out var meses) && meses > 0;
            lblCuotaEstimada.Text = valido
                ? new MetaAhorro { MontoObjetivo = objetivo, PlazoMeses = int.Parse(txtMesesMeta.Text) }.CuotaMensual.ToString("C0")
                : "$ —";
        }
        // Meta pendiente de confirmar eliminación
        private MetaAhorro? _metaPendienteEliminar;

        private void BtnEliminarMeta_Click(object sender, RoutedEventArgs e)
        {
            var boton = (Button)sender;
            _metaPendienteEliminar = (MetaAhorro)boton.CommandParameter;

            if (_metaPendienteEliminar != null)
            {
                txtConfirmMsg.Text = $"¿Seguro que deseas eliminar la meta \"{_metaPendienteEliminar.Nombre}\"? Esta acción no se puede deshacer.";
                MostrarOverlayConfirm(true);
            }
        }

        private void ConfirmCancelar_Click(object sender, RoutedEventArgs e)
        {
            _metaPendienteEliminar = null;
            MostrarOverlayConfirm(false);
        }

        private void ConfirmEliminar_Click(object sender, RoutedEventArgs e)
        {
            var metaAEliminar = _metaPendienteEliminar;
            MostrarOverlayConfirm(false);
            _metaPendienteEliminar = null;

            if (metaAEliminar == null) return;

            try
            {
                using (var db = new AppDbContext())
                {
                    var metaDb = db.Metas.FirstOrDefault(m => m.Id == metaAEliminar.Id);
                    if (metaDb != null)
                    {
                        db.Metas.Remove(metaDb);
                        db.SaveChanges();
                    }
                }
                ListaMetas.Remove(metaAEliminar);
                ActualizarAhorroAcumulado();
                toast.Show("Meta eliminada");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al eliminar la meta de la base de datos: {ex.Message}");
            }
        }

        private void MostrarOverlayConfirm(bool visible)
        {
            if (visible)
            {
                OverlayConfirm.Visibility = Visibility.Visible;
                OverlayConfirm.BeginAnimation(OpacityProperty,
                    new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
            }
            else
            {
                OverlayConfirm.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnAbonarMeta_Click(object sender, RoutedEventArgs e)
        {
            var boton = sender as Button;
            var meta = boton?.CommandParameter as MetaAhorro; // Esta es la meta que ya existe

            if (meta != null)
            {
                var parent = VisualTreeHelper.GetParent(boton) as Grid;
                var textBox = parent?.Children.OfType<TextBox>().FirstOrDefault();

                if (textBox != null && NumeroHelper.TryParseMoneda(textBox.Text, out decimal montoAbono))
                {
                    try
                    {
                        using (var db = new AppDbContext())
                        {
                            // ACTUALIZACIÓN CORRECTA:
                            meta.MontoActual += montoAbono; // Sumamos el abono
                            db.Metas.Update(meta);          // Le decimos a la BD que esta meta cambió
                            db.SaveChanges();               // Guardamos físicamente
                        }

                        textBox.Text = "";
                        icMetas.Items.Refresh();
                        ActualizarAhorroAcumulado();
                    }
                    catch (Exception ex) { MessageBox.Show(ex.Message); }
                }
            }
        }
        // --- OTROS INGRESOS (con etiqueta propia) ---

        private void CargarIngresosExtra()
        {
            try
            {
                var mes = DateTime.Today;
                using var db = new AppDbContext();

                if (cbFrecuenciaIngreso.ItemsSource == null)
                {
                    cbFrecuenciaIngreso.ItemsSource = new[] { Frecuencias.Unica, Frecuencias.Mensual };
                    cbFrecuenciaIngreso.SelectedItem = Frecuencias.Unica;
                }
                var texto = cbEtiquetaIngreso.Text;
                cbEtiquetaIngreso.ItemsSource = Etiquetas.Lista(db, Etiquetas.Ingreso);
                cbEtiquetaIngreso.Text = texto;

                // CuentaMes da el formato común (etiqueta con color, frecuencia) también a los ingresos
                var extras = CuentasMensuales.IngresosExtraDelMes(db, mes)
                                             .Select(i => new CuentaMes(i, mes, null))
                                             .ToList();
                icIngresosExtra.ItemsSource = extras;
                icIngresosExtra.Visibility = extras.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
                lblSinIngresosExtra.Visibility = extras.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                lblTituloOtrosIngresos.Text = $"Otros ingresos · {Periodo.Nombre(mes)}";
                lblTituloOtrosIngresosMes.Text = $"OTROS INGRESOS · {mes.ToString("MMMM").ToUpperInvariant()}";
                lblOtrosIngresosMes.Text = extras.Sum(i => i.Venta).ToString("C0");

                var total = Analisis.IngresosDelMes(Analisis.Cargar(db), mes);
                lblTotalIngresosMes.Text = extras.Count > 0
                    ? $"{extras.Count} {(extras.Count == 1 ? "registro" : "registros")} · total del mes {total:C0}"
                    : "Sin registros este mes";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al cargar otros ingresos: {ex.Message}");
            }
        }

        private void BtnAgregarIngreso_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtDescIngreso.Text) ||
                !NumeroHelper.TryParseMoneda(txtValorIngreso.Text, out decimal valor) ||
                cbFrecuenciaIngreso.SelectedItem is not string frecuencia)
            {
                MessageBox.Show("Por favor, ingresa una descripción y un valor válido.");
                return;
            }

            try
            {
                using (var db = new AppDbContext())
                {
                    db.Movimientos.Add(new Movimiento
                    {
                        Descripcion = txtDescIngreso.Text.Trim(),
                        Categoria = Etiquetas.Asegurar(db, cbEtiquetaIngreso.Text, Etiquetas.Ingreso),
                        Venta = valor,
                        Tipo = CuentasMensuales.TipoIngresoExtra,
                        Frecuencia = frecuencia,
                        DiaPago = DateTime.Today.Day,
                        Fecha = DateTime.Today
                    });
                    db.SaveChanges();
                }

                txtDescIngreso.Clear();
                txtValorIngreso.Clear();
                cbEtiquetaIngreso.Text = string.Empty;
                CargarIngresosExtra();
                toast.Show(frecuencia == Frecuencias.Mensual ? "Ingreso registrado: se repetirá cada mes" : "Ingreso registrado");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al guardar el ingreso: {ex.Message}");
            }
        }

        // Los mensuales dejan de contar desde este mes; los únicos de este mes se borran
        private void BtnEliminarIngreso_Click(object sender, RoutedEventArgs e)
        {
            if (((Button)sender).DataContext is not CuentaMes ingreso) return;
            try
            {
                using (var db = new AppDbContext())
                {
                    CuentasMensuales.EliminarDesde(db, ingreso.Cuenta.Id, DateTime.Today);
                }
                CargarIngresosExtra();
                toast.Show("Ingreso eliminado");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error al eliminar el ingreso: {ex.Message}");
            }
        }

        public static void CargarMetasGlobal()
        {
            try
            {
                using (var db = new AppDbContext())
                {
                    // CORREGIDO: Tu DbSet real se llama 'Metas'
                    var metas = db.Metas.ToList();

                    ListaMetas.Clear();
                    foreach (var meta in metas)
                    {
                        ListaMetas.Add(meta);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Error al cargar metas globalmente: " + ex.Message);
            }
        }
    }
}