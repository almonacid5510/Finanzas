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
            Etiquetas.ActivarOpcionNueva(cbEtiquetaIngreso);

            // La BD ya se crea/actualiza al iniciar la app (MainWindow -> EsquemaBD).
            // La carga va en Loaded, cuando los controles del XAML ya existen.
            this.Loaded += (s, e) =>
            {
                // Ahora icMetas ya no es null
                icMetas.ItemsSource = ListaMetas;
                CargarMetasDesdeBD();
                CargarSalarioDesdeBD();
                CambiarMes(_mesSeleccionado);
            };
        }

        // --- Navegación entre meses (aplica a "Otros ingresos") ---
        private DateTime _mesSeleccionado = Periodo.InicioMes(DateTime.Today);

        private void BtnMesAnterior_Click(object sender, RoutedEventArgs e) => CambiarMes(_mesSeleccionado.AddMonths(-1));
        private void BtnMesSiguiente_Click(object sender, RoutedEventArgs e) => CambiarMes(_mesSeleccionado.AddMonths(1));
        private void BtnMesActual_Click(object sender, RoutedEventArgs e) => CambiarMes(DateTime.Today);

        private void CambiarMes(DateTime mes)
        {
            using var dbInicio = new AppDbContext();
            var inicio = InicioRegistro.Obtener(dbInicio);
            _mesSeleccionado = Periodo.LimitarMes(mes, inicio, DateTime.Today);
            btnMesAnterior.IsEnabled = _mesSeleccionado > Periodo.PrimerMesVisible(inicio, DateTime.Today);
            btnMesSiguiente.IsEnabled = _mesSeleccionado < Periodo.InicioMes(DateTime.Today).AddMonths(1);
            lblPeriodo.Text = Periodo.Nombre(_mesSeleccionado);
            btnMesActual.Visibility = Periodo.EsMesActual(_mesSeleccionado) ? Visibility.Collapsed : Visibility.Visible;
            CargarIngresosExtra();
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
                MisFinanzas.Controles.Aviso.Mostrar($"Error al cargar: {ex.Message}");
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
                MisFinanzas.Controles.Aviso.Mostrar($"Error al cargar salario: {ex.Message}");
            }
        }

        // --- SALARIO ---
        // El salario mensual sale de la nómina (suma de los pagos del mes): se edita en Cuenta → Tu nómina
        private void BtnActivarEdicion_Click(object sender, RoutedEventArgs e) => MainWindow.Actual?.IrAConfiguracion(editarNomina: true);

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
                    MisFinanzas.Controles.Aviso.Mostrar($"Error crítico al guardar: {ex.Message}");
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
                        // También su historial de abonos, para no dejar registros huérfanos
                        db.AbonosMeta.RemoveRange(db.AbonosMeta.Where(a => a.MetaId == metaDb.Id));
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
                MisFinanzas.Controles.Aviso.Mostrar($"Error al eliminar la meta de la base de datos: {ex.Message}");
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

                if (textBox != null && NumeroHelper.TryParseMoneda(textBox.Text, out decimal montoAbono) && montoAbono > 0)
                {
                    // Si el abono deja la meta por encima del objetivo, se pregunta antes (p. ej. un cero de más)
                    if (meta.MontoObjetivo > 0 && meta.MontoActual + montoAbono > meta.MontoObjetivo)
                    {
                        _abonoPendiente = (meta, montoAbono, textBox);
                        var total = meta.MontoActual + montoAbono;
                        txtExcedeMsg.Text = $"Con este abono de {NumeroHelper.Moneda(montoAbono)} llegarías a {NumeroHelper.Moneda(total)}, " +
                                            $"que supera la meta \"{meta.NombreMostrar}\" de {NumeroHelper.Moneda(meta.MontoObjetivo)} " +
                                            $"por {NumeroHelper.Moneda(total - meta.MontoObjetivo)}. ¿Quieres editar la meta?";
                        MostrarOverlay(OverlayExcede, true);
                        return;
                    }
                    Abonar(meta, montoAbono, textBox);
                }
            }
        }

        private void Abonar(MetaAhorro meta, decimal montoAbono, TextBox caja)
        {
            try
            {
                using (var db = new AppDbContext())
                {
                    meta.MontoActual += montoAbono; // Sumamos el abono
                    db.Metas.Update(meta);          // Le decimos a la BD que esta meta cambió
                    // Historial: el Dashboard muestra a qué meta, cuándo y cuánto se abonó
                    db.AbonosMeta.Add(new AbonoMeta { MetaId = meta.Id, Fecha = DateTime.Now, Monto = montoAbono });
                    db.SaveChanges();               // Guardamos físicamente
                }

                caja.Text = "";
                icMetas.Items.Refresh();
                ActualizarAhorroAcumulado();
                toast.Show($"Abono de {NumeroHelper.Moneda(montoAbono)} registrado");
            }
            catch (Exception ex) { MisFinanzas.Controles.Aviso.Mostrar(ex.Message); }
        }

        // --- Abono que supera la meta ---
        private (MetaAhorro Meta, decimal Monto, TextBox Caja)? _abonoPendiente;

        private void ExcedeCancelar_Click(object sender, RoutedEventArgs e)
        {
            _abonoPendiente = null;
            MostrarOverlay(OverlayExcede, false);
        }

        // Abre la edición de la meta; el abono queda escrito en su campo para hacerlo después
        private void ExcedeEditar_Click(object sender, RoutedEventArgs e)
        {
            MostrarOverlay(OverlayExcede, false);
            if (_abonoPendiente is { } p) AbrirEdicionMeta(p.Meta);
            _abonoPendiente = null;
        }

        // --- Editar meta y corregir sus abonos ---
        private MetaAhorro? _metaEditando;

        private void BtnEditarMeta_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.CommandParameter is MetaAhorro meta) AbrirEdicionMeta(meta);
        }

        // Abonos de la meta en edición y los que se marcaron para borrar (se borran al Guardar; Cancelar los descarta)
        private List<AbonoMeta> _abonosMeta = new();
        private readonly List<AbonoMeta> _abonosAEliminar = new();

        private void AbrirEdicionMeta(MetaAhorro meta)
        {
            _metaEditando = meta;
            _abonosAEliminar.Clear();
            txtEditNombreMeta.Text = meta.NombreMostrar;
            txtEditObjetivoMeta.Text = NumeroHelper.FormatearMoneda(meta.MontoObjetivo);
            txtEditMesesMeta.Text = meta.PlazoMeses.ToString();
            txtEditAhorradoMeta.Text = NumeroHelper.FormatearMoneda(meta.MontoActual);
            CargarAbonosMeta();
            MostrarOverlay(OverlayEditarMeta, true);
            txtEditNombreMeta.Focus();
        }

        private void CargarAbonosMeta()
        {
            if (_metaEditando == null) return;
            try
            {
                using var db = new AppDbContext();
                _abonosMeta = db.AbonosMeta.Where(a => a.MetaId == _metaEditando.Id).AsEnumerable()
                                .OrderByDescending(a => a.Fecha).ToList();
            }
            catch (Exception ex)
            {
                _abonosMeta = new();
                MisFinanzas.Controles.Aviso.Mostrar($"Error al cargar los abonos: {ex.Message}");
            }
            MostrarAbonosMeta();
        }

        private void MostrarAbonosMeta()
        {
            var visibles = _abonosMeta.Except(_abonosAEliminar).ToList();
            icAbonosMeta.ItemsSource = visibles;
            lblSinAbonosMeta.Visibility = visibles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // Marca el abono para borrar y descuenta su valor de "Ahorrado"; se aplica al Guardar
        private void BtnEliminarAbono_Click(object sender, RoutedEventArgs e)
        {
            if (_metaEditando == null || ((Button)sender).DataContext is not AbonoMeta abono) return;
            _abonosAEliminar.Add(abono);

            // El campo "Ahorrado" sigue al cambio (conserva lo que el usuario haya corregido a mano)
            if (NumeroHelper.TryParseMoneda(txtEditAhorradoMeta.Text, out var ahorrado))
                txtEditAhorradoMeta.Text = NumeroHelper.FormatearMoneda(Math.Max(0, ahorrado - abono.Monto));

            MostrarAbonosMeta();
        }

        private void EditarMetaCancelar_Click(object sender, RoutedEventArgs e)
        {
            _metaEditando = null;
            _abonosAEliminar.Clear();
            MostrarOverlay(OverlayEditarMeta, false);
        }

        private void EditarMetaGuardar_Click(object sender, RoutedEventArgs e)
        {
            var meta = _metaEditando;
            if (meta == null) return;

            if (string.IsNullOrWhiteSpace(txtEditNombreMeta.Text) ||
                !NumeroHelper.TryParseMoneda(txtEditObjetivoMeta.Text, out var objetivo) || objetivo <= 0 ||
                !int.TryParse(txtEditMesesMeta.Text, out var meses) || meses <= 0 ||
                !NumeroHelper.TryParseMoneda(txtEditAhorradoMeta.Text, out var ahorrado) || ahorrado < 0)
            {
                MisFinanzas.Controles.Aviso.Mostrar("Revisa los datos: nombre, valor objetivo, meses (mayor a 0) y lo ahorrado.");
                return;
            }

            try
            {
                using (var db = new AppDbContext())
                {
                    var dbMeta = db.Metas.Find(meta.Id);
                    if (dbMeta != null)
                    {
                        dbMeta.Nombre = txtEditNombreMeta.Text.Trim();
                        dbMeta.NombreCustom = string.Empty;
                        dbMeta.MontoObjetivo = objetivo;
                        dbMeta.PlazoMeses = meses;
                        dbMeta.MontoActual = ahorrado; // ya descuenta los abonos borrados
                    }
                    // Abonos marcados para borrar: un solo SaveChanges, se guarda todo o nada
                    var ids = _abonosAEliminar.Select(a => a.Id).ToList();
                    db.AbonosMeta.RemoveRange(db.AbonosMeta.Where(a => ids.Contains(a.Id)));
                    db.SaveChanges();
                }
                _abonosAEliminar.Clear();
                meta.Nombre = txtEditNombreMeta.Text.Trim();
                meta.NombreCustom = string.Empty;
                meta.MontoObjetivo = objetivo;
                meta.PlazoMeses = meses;
                meta.MontoActual = ahorrado;

                _metaEditando = null;
                MostrarOverlay(OverlayEditarMeta, false);
                icMetas.Items.Refresh();
                ActualizarAhorroAcumulado();
                toast.Show("Meta actualizada");
            }
            catch (Exception ex)
            {
                MisFinanzas.Controles.Aviso.Mostrar($"Error al guardar la meta: {ex.Message}");
            }
        }

        private void MostrarOverlay(FrameworkElement overlay, bool visible)
        {
            overlay.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            if (visible)
                overlay.BeginAnimation(OpacityProperty,
                    new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
        }
        // --- OTROS INGRESOS (con etiqueta propia) ---

        private void CargarIngresosExtra()
        {
            try
            {
                var mes = _mesSeleccionado;
                using var db = new AppDbContext();

                if (cbFrecuenciaIngreso.ItemsSource == null)
                {
                    cbFrecuenciaIngreso.ItemsSource = new[] { Frecuencias.Unica, Frecuencias.Mensual };
                    cbFrecuenciaIngreso.SelectedItem = Frecuencias.Unica;
                }
                var texto = cbEtiquetaIngreso.Text;
                cbEtiquetaIngreso.ItemsSource = Etiquetas.ListaConNueva(db, Etiquetas.Ingreso);
                cbEtiquetaIngreso.Text = texto;

                // CuentaMes da el formato común (etiqueta con color, frecuencia) también a los ingresos
                var extras = CuentasMensuales.IngresosExtraDelMes(db, mes)
                                             .Select(i => new CuentaMes(i, mes, null))
                                             .ToList();
                icIngresosExtra.ItemsSource = extras;
                icIngresosExtra.Visibility = extras.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
                lblSinIngresosExtra.Visibility = extras.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                if (_ingresoEditando == null) lblTituloOtrosIngresos.Text = $"Otros ingresos · {Periodo.Nombre(mes)}";
                lblTituloOtrosIngresosMes.Text = $"OTROS INGRESOS · {mes.ToString("MMMM").ToUpperInvariant()}";
                lblOtrosIngresosMes.Text = extras.Sum(i => i.Venta).ToString("C0");

                var total = Analisis.IngresosDelMes(Analisis.Cargar(db), mes);
                lblTotalIngresosMes.Text = extras.Count > 0
                    ? $"{extras.Count} {(extras.Count == 1 ? "registro" : "registros")} · total del mes {total:C0}"
                    : "Sin registros este mes";
            }
            catch (Exception ex)
            {
                MisFinanzas.Controles.Aviso.Mostrar($"Error al cargar otros ingresos: {ex.Message}");
            }
        }

        private void BtnAgregarIngreso_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtDescIngreso.Text) ||
                !NumeroHelper.TryParseMoneda(txtValorIngreso.Text, out decimal valor) ||
                cbFrecuenciaIngreso.SelectedItem is not string frecuencia)
            {
                MisFinanzas.Controles.Aviso.Mostrar("Por favor, ingresa una descripción y un valor válido.");
                return;
            }

            try
            {
                using (var db = new AppDbContext())
                {
                    var categoria = Etiquetas.Asegurar(db, cbEtiquetaIngreso.Text, Etiquetas.Ingreso);

                    if (_ingresoEditando == null)
                    {
                        db.Movimientos.Add(new Movimiento
                        {
                            Descripcion = txtDescIngreso.Text.Trim(),
                            Categoria = categoria,
                            Venta = valor,
                            Tipo = CuentasMensuales.TipoIngresoExtra,
                            Frecuencia = frecuencia,
                            DiaPago = DateTime.Today.Day,
                            // Desde el mes que se está viendo
                            Fecha = CuentasMensuales.FechaInicio(frecuencia, _mesSeleccionado, _mesSeleccionado.Month, DateTime.Today.Day)
                        });
                        toast.Show(frecuencia == Frecuencias.Mensual ? "Ingreso registrado: se repetirá cada mes" : "Ingreso registrado");
                    }
                    else
                    {
                        var i = db.Movimientos.Find(_ingresoEditando.Id);
                        if (i != null)
                        {
                            // Si pasa a "Única", queda en el mes que se está viendo para que no desaparezca de la lista
                            if (frecuencia == Frecuencias.Unica && i.Frecuencia != Frecuencias.Unica)
                                i.Fecha = CuentasMensuales.Vencimiento(i, _mesSeleccionado);
                            i.Descripcion = txtDescIngreso.Text.Trim();
                            i.Categoria = categoria;
                            i.Venta = valor;
                            i.Frecuencia = frecuencia;
                        }
                        toast.Show("Cambios guardados");
                    }
                    db.SaveChanges();
                }

                LimpiarFormularioIngreso();
                CargarIngresosExtra();
            }
            catch (Exception ex)
            {
                MisFinanzas.Controles.Aviso.Mostrar($"Error al guardar el ingreso: {ex.Message}");
            }
        }

        // Ingreso que se está modificando en el formulario (null = se agrega uno nuevo)
        private Movimiento? _ingresoEditando;

        // Carga el ingreso en el formulario para modificarlo
        private void BtnEditarIngreso_Click(object sender, RoutedEventArgs e)
        {
            if (((Button)sender).DataContext is not CuentaMes ingreso) return;
            var i = ingreso.Cuenta;

            _ingresoEditando = i;
            txtDescIngreso.Text = i.Descripcion;
            cbEtiquetaIngreso.Text = i.Categoria;
            txtValorIngreso.Text = NumeroHelper.FormatearMoneda(i.Venta);
            cbFrecuenciaIngreso.SelectedItem = i.Frecuencia == Frecuencias.Mensual ? Frecuencias.Mensual : Frecuencias.Unica;

            lblTituloOtrosIngresos.Text = i.Frecuencia == Frecuencias.Mensual
                ? $"Editar \"{i.Descripcion}\" (aplica a todos sus meses)"
                : $"Editar \"{i.Descripcion}\"";
            btnGuardarIngreso.Content = "Guardar";
            btnCancelarEdicionIngreso.Visibility = Visibility.Visible;
            txtDescIngreso.Focus();
        }

        private void BtnCancelarEdicionIngreso_Click(object sender, RoutedEventArgs e)
        {
            LimpiarFormularioIngreso();
            CargarIngresosExtra(); // restaura el título
        }

        private void LimpiarFormularioIngreso()
        {
            _ingresoEditando = null;
            txtDescIngreso.Clear();
            txtValorIngreso.Clear();
            cbEtiquetaIngreso.Text = string.Empty;
            cbFrecuenciaIngreso.SelectedItem = Frecuencias.Unica;
            btnGuardarIngreso.Content = "Agregar";
            btnCancelarEdicionIngreso.Visibility = Visibility.Collapsed;
        }

        // Los mensuales dejan de contar desde este mes; los únicos de este mes se borran
        private void BtnEliminarIngreso_Click(object sender, RoutedEventArgs e)
        {
            if (((Button)sender).DataContext is not CuentaMes ingreso) return;
            try
            {
                using (var db = new AppDbContext())
                {
                    CuentasMensuales.EliminarDesde(db, ingreso.Cuenta.Id, _mesSeleccionado);
                }
                if (_ingresoEditando?.Id == ingreso.Cuenta.Id) LimpiarFormularioIngreso();
                CargarIngresosExtra();
                toast.Show("Ingreso eliminado");
            }
            catch (Exception ex)
            {
                MisFinanzas.Controles.Aviso.Mostrar($"Error al eliminar el ingreso: {ex.Message}");
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