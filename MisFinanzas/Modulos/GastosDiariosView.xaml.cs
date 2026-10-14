// ============================================================
//  Proyecto: Mis Finanzas
//  Autoria: Juan Almonacid
//  Copyright (c) 2026 Juan Almonacid. Todos los derechos reservados.
// ============================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MisFinanzas.Modulos
{
    public partial class GastosDiariosView : UserControl
    {
        // Gastos de hasta este valor cuentan como "hormiga": pequeños pero frecuentes
        public const decimal TopeHormiga = 20_000m;

        private DateTime _mesSeleccionado = Periodo.InicioMes(DateTime.Today);

        // Gasto que se está modificando en el formulario (null = se agrega uno nuevo)
        private GastoDiario? _editando;

        public GastosDiariosView()
        {
            InitializeComponent();
            Etiquetas.ActivarOpcionNueva(cbCategoria);
            dpFecha.SelectedDate = DateTime.Today;
            Loaded += (s, e) => CambiarMes(_mesSeleccionado);
        }

        // --- Navegación entre meses ---
        private void BtnMesAnterior_Click(object sender, RoutedEventArgs e) => CambiarMes(_mesSeleccionado.AddMonths(-1));
        private void BtnMesSiguiente_Click(object sender, RoutedEventArgs e) => CambiarMes(_mesSeleccionado.AddMonths(1));
        private void BtnMesActual_Click(object sender, RoutedEventArgs e) => CambiarMes(DateTime.Today);

        private void CambiarMes(DateTime mes)
        {
            _mesSeleccionado = Periodo.InicioMes(mes);
            lblPeriodo.Text = Periodo.Nombre(_mesSeleccionado);
            btnMesActual.Visibility = Periodo.EsMesActual(_mesSeleccionado) ? Visibility.Collapsed : Visibility.Visible;
            Cargar();
        }

        // --- Carga y análisis del mes ---
        private void Cargar()
        {
            List<GastoDiario> gastos;
            try
            {
                using var db = new AppDbContext();
                var texto = cbCategoria.Text;
                cbCategoria.ItemsSource = Etiquetas.ListaConNueva(db, Etiquetas.Gasto);
                cbCategoria.Text = texto;

                var desde = _mesSeleccionado;
                var hasta = _mesSeleccionado.AddMonths(1);
                gastos = db.GastosDiarios.Where(g => g.Fecha >= desde && g.Fecha < hasta).ToList();
            }
            catch (Exception ex)
            {
                MisFinanzas.Controles.Aviso.Mostrar($"Error al cargar los gastos: {ex.Message}");
                return;
            }

            MostrarIndicadores(gastos);
            MostrarDias(gastos);
            MostrarCategorias(gastos);
            MostrarFrecuentes(gastos);
        }

        private void MostrarIndicadores(List<GastoDiario> gastos)
        {
            decimal total = gastos.Sum(g => g.Monto);
            lblTituloTotal.Text = $"GASTADO EN {_mesSeleccionado.ToString("MMMM", CultureInfo.CurrentCulture).ToUpperInvariant()}";
            lblTotal.Text = NumeroHelper.Moneda(total);
            lblTotalDetalle.Text = gastos.Count == 1 ? "1 gasto registrado" : $"{gastos.Count} gastos registrados";

            // Promedio sobre los días que han pasado del mes (el mes completo si ya terminó)
            int dias = DiasTranscurridos(_mesSeleccionado);
            lblPromedio.Text = dias > 0 ? NumeroHelper.Moneda(total / dias) : "$ —";
            int diasConGasto = gastos.Select(g => g.Fecha.Date).Distinct().Count();
            lblPromedioDetalle.Text = dias > 0 ? $"Gastaste {diasConGasto} de {dias} días" : "El mes aún no empieza";

            var top = gastos.GroupBy(g => Etiquetas.Mostrar(g.Categoria))
                            .Select(g => (Nombre: g.Key, Total: g.Sum(x => x.Monto)))
                            .OrderByDescending(g => g.Total).FirstOrDefault();
            lblCategoriaTop.Text = total > 0 ? top.Nombre : "—";
            lblCategoriaTop.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter()
                .ConvertFromString(total > 0 ? Etiquetas.ColorTexto(top.Nombre) : "#0F1B2D")!;
            lblCategoriaTopDetalle.Text = total > 0 ? $"{NumeroHelper.Moneda(top.Total)} · {top.Total / total:P0} de lo gastado" : "Sin gastos este mes";

            var hormiga = gastos.Where(g => g.Monto <= TopeHormiga).ToList();
            lblHormiga.Text = NumeroHelper.Moneda(hormiga.Sum(g => g.Monto));
            lblHormigaDetalle.Text = hormiga.Count == 0 ? "Ningún gasto pequeño"
                                   : $"{hormiga.Count} gastos de hasta {NumeroHelper.Moneda(TopeHormiga)}";
        }

        private static int DiasTranscurridos(DateTime mes)
        {
            var hoy = DateTime.Today;
            if (Periodo.EsMesActual(mes)) return hoy.Day;
            return mes < hoy ? DateTime.DaysInMonth(mes.Year, mes.Month) : 0;
        }

        private void MostrarDias(List<GastoDiario> gastos)
        {
            var hoy = DateTime.Today;
            var dias = gastos.GroupBy(g => g.Fecha.Date)
                             .OrderByDescending(g => g.Key)
                             .Select(g => new DiaGastos
                             {
                                 Titulo = (g.Key == hoy ? "HOY · " : g.Key == hoy.AddDays(-1) ? "AYER · " : string.Empty)
                                          + g.Key.ToString("dddd d 'de' MMMM", CultureInfo.CurrentCulture).ToUpperInvariant(),
                                 Total = g.Sum(x => x.Monto),
                                 Gastos = g.OrderByDescending(x => x.Fecha).Select(x => new FilaGastoDiario(x)).ToList()
                             })
                             .ToList();
            icDias.ItemsSource = dias;
            lblTituloLista.Text = $"Movimientos · {Periodo.Nombre(_mesSeleccionado)}";
            txtSinGastos.Text = Periodo.EsMesActual(_mesSeleccionado)
                ? "Aún no registras gastos este mes. Escribe el primero arriba."
                : $"No hay gastos registrados en {Periodo.Nombre(_mesSeleccionado)}.";
            lblSinGastos.Visibility = dias.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void MostrarCategorias(List<GastoDiario> gastos)
        {
            decimal total = gastos.Sum(g => g.Monto);
            var filas = gastos.GroupBy(g => Etiquetas.Mostrar(g.Categoria))
                              .Select(g => new FilaCategoriaDiaria
                              {
                                  Nombre = g.Key,
                                  Total = g.Sum(x => x.Monto),
                                  Veces = g.Count(),
                                  Proporcion = total > 0 ? (double)(g.Sum(x => x.Monto) / total) : 0,
                                  Color = Etiquetas.ColorBarra(g.Key)
                              })
                              .OrderByDescending(f => f.Total)
                              .ToList();
            icCategorias.ItemsSource = filas;
            lblSinCategorias.Visibility = filas.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // Lo que más se repite (por descripción): "Almuerzo · 14 veces"
        private void MostrarFrecuentes(List<GastoDiario> gastos)
        {
            var filas = gastos.GroupBy(g => InterpreteGasto.SinTildes(g.Descripcion.Trim().ToLowerInvariant()))
                              .Where(g => g.Count() >= 2)
                              .Select(g =>
                              {
                                  var categoria = Etiquetas.Mostrar(g.First().Categoria);
                                  decimal suma = g.Sum(x => x.Monto);
                                  return new FilaFrecuente
                                  {
                                      Nombre = g.First().Descripcion,
                                      Veces = g.Count(),
                                      Total = suma,
                                      Detalle = $"{g.Count()} veces · promedio {NumeroHelper.Moneda(suma / g.Count())} · {categoria}",
                                      FondoCategoria = Etiquetas.ColorFondo(categoria),
                                      ColorCategoria = Etiquetas.ColorTexto(categoria)
                                  };
                              })
                              .OrderByDescending(f => f.Veces).ThenByDescending(f => f.Total)
                              .Take(6)
                              .ToList();
            icFrecuentes.ItemsSource = filas;
            lblSinFrecuentes.Visibility = filas.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // --- Registro rápido con una frase ---
        // Al escribir la frase se llenan los campos; se pueden corregir antes de agregar
        private void TxtFrase_TextChanged(object sender, TextChangedEventArgs e)
        {
            var r = InterpreteGasto.Interpretar(txtFrase.Text);
            txtDescripcion.Text = r.Descripcion;
            if (r.Monto is decimal monto) txtMonto.Text = NumeroHelper.FormatearMoneda(monto);
            else txtMonto.Clear();
            if (r.Categoria.Length > 0) cbCategoria.Text = r.Categoria;
            // La fecha solo se cambia si la frase la dice (ayer, antier); si no, la que esté elegida
            if (r.Fecha != DateTime.Today || dpFecha.SelectedDate == null) dpFecha.SelectedDate = r.Fecha;
        }

        // Enter en la frase = Agregar
        private void TxtFrase_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { BtnGuardar_Click(sender, e); e.Handled = true; }
        }

        private void BtnGuardar_Click(object sender, RoutedEventArgs e)
        {
            if (!NumeroHelper.TryParseMoneda(txtMonto.Text, out var monto) || monto <= 0)
            {
                MisFinanzas.Controles.Aviso.Mostrar("Escribe cuánto gastaste (por ejemplo: \"almuerzo 25 mil\").");
                return;
            }
            var descripcion = string.IsNullOrWhiteSpace(txtDescripcion.Text) ? "Gasto" : txtDescripcion.Text.Trim();
            var dia = dpFecha.SelectedDate ?? DateTime.Today;

            try
            {
                using (var db = new AppDbContext())
                {
                    var categoria = Etiquetas.Asegurar(db, cbCategoria.Text, Etiquetas.Gasto);
                    if (_editando == null)
                    {
                        // Hoy con la hora actual; otro día, a mediodía (no se sabe la hora)
                        var fecha = dia.Date == DateTime.Today ? DateTime.Now : dia.Date.AddHours(12);
                        db.GastosDiarios.Add(new GastoDiario
                        {
                            Fecha = fecha,
                            Monto = monto,
                            Categoria = categoria,
                            Descripcion = descripcion,
                            Origen = GastoDiario.OrigenPC,
                            TextoOriginal = txtFrase.Text.Trim()
                        });
                        toast.Show($"Gasto registrado: {NumeroHelper.Moneda(monto)} · {Etiquetas.Mostrar(categoria)}");
                    }
                    else
                    {
                        var g = db.GastosDiarios.Find(_editando.Id);
                        if (g != null)
                        {
                            // Si cambia el día se conserva la hora original
                            g.Fecha = dia.Date + g.Fecha.TimeOfDay;
                            g.Monto = monto;
                            g.Categoria = categoria;
                            g.Descripcion = descripcion;
                        }
                        toast.Show("Cambios guardados");
                    }
                    db.SaveChanges();
                }
            }
            catch (Exception ex)
            {
                MisFinanzas.Controles.Aviso.Mostrar($"Error al guardar el gasto: {ex.Message}");
                return;
            }

            // Si el gasto quedó en otro mes, se muestra ese mes
            LimpiarFormulario();
            if (Periodo.Clave(dia) != Periodo.Clave(_mesSeleccionado)) CambiarMes(dia);
            else Cargar();
            txtFrase.Focus();
        }

        private void LimpiarFormulario()
        {
            _editando = null;
            txtFrase.TextChanged -= TxtFrase_TextChanged;
            txtFrase.Clear();
            txtFrase.TextChanged += TxtFrase_TextChanged;
            txtDescripcion.Clear();
            txtMonto.Clear();
            cbCategoria.Text = string.Empty;
            dpFecha.SelectedDate = DateTime.Today;
            lblTituloFormulario.Text = "Registrar gasto";
            btnGuardar.Content = "Agregar";
            btnCancelarEdicion.Visibility = Visibility.Collapsed;
        }

        private void BtnCancelarEdicion_Click(object sender, RoutedEventArgs e) => LimpiarFormulario();

        // Carga el gasto en el formulario para modificarlo
        private void BtnEditar_Click(object sender, RoutedEventArgs e)
        {
            if (((Button)sender).DataContext is not FilaGastoDiario fila) return;
            var g = fila.Gasto;
            _editando = g;

            txtFrase.TextChanged -= TxtFrase_TextChanged;
            txtFrase.Text = g.TextoOriginal;
            txtFrase.TextChanged += TxtFrase_TextChanged;
            txtDescripcion.Text = g.Descripcion;
            txtMonto.Text = NumeroHelper.FormatearMoneda(g.Monto);
            cbCategoria.Text = g.Categoria;
            dpFecha.SelectedDate = g.Fecha.Date;

            lblTituloFormulario.Text = $"Editar \"{g.Descripcion}\"";
            btnGuardar.Content = "Guardar";
            btnCancelarEdicion.Visibility = Visibility.Visible;
            txtDescripcion.Focus();
        }

        // --- Eliminación con confirmación ---
        private GastoDiario? _pendienteEliminar;

        private void BtnEliminar_Click(object sender, RoutedEventArgs e)
        {
            if (((Button)sender).DataContext is not FilaGastoDiario fila) return;
            _pendienteEliminar = fila.Gasto;
            txtConfirmMsg.Text = $"¿Seguro que deseas eliminar \"{fila.Descripcion}\" de {NumeroHelper.Moneda(fila.Monto)} " +
                                 $"del {fila.Gasto.Fecha:dd/MM/yyyy}? Esta acción no se puede deshacer.";
            MostrarOverlayConfirm(true);
        }

        private void ConfirmCancelar_Click(object sender, RoutedEventArgs e)
        {
            _pendienteEliminar = null;
            MostrarOverlayConfirm(false);
        }

        private void ConfirmEliminar_Click(object sender, RoutedEventArgs e)
        {
            var gasto = _pendienteEliminar;
            MostrarOverlayConfirm(false);
            _pendienteEliminar = null;
            if (gasto == null) return;

            try
            {
                using (var db = new AppDbContext())
                {
                    var g = db.GastosDiarios.Find(gasto.Id);
                    if (g != null) { db.GastosDiarios.Remove(g); db.SaveChanges(); }
                }
                if (_editando?.Id == gasto.Id) LimpiarFormulario();
                Cargar();
                toast.Show("Gasto eliminado");
            }
            catch (Exception ex)
            {
                MisFinanzas.Controles.Aviso.Mostrar($"Error al eliminar el gasto: {ex.Message}");
            }
        }

        private void MostrarOverlayConfirm(bool visible)
        {
            OverlayConfirm.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            if (visible)
                OverlayConfirm.BeginAnimation(OpacityProperty,
                    new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
        }
    }

    // ===== Filas de la pantalla (solo presentación) =====

    public class DiaGastos
    {
        public string Titulo { get; set; } = string.Empty;
        public decimal Total { get; set; }
        public List<FilaGastoDiario> Gastos { get; set; } = new();
    }

    public class FilaGastoDiario
    {
        public FilaGastoDiario(GastoDiario gasto) => Gasto = gasto;
        public GastoDiario Gasto { get; }
        public DateTime Fecha => Gasto.Fecha;
        public string Descripcion => Gasto.Descripcion;
        public decimal Monto => Gasto.Monto;
        public string Categoria => Etiquetas.Mostrar(Gasto.Categoria);
        public string ColorCategoria => Etiquetas.ColorTexto(Gasto.Categoria);
        public string FondoCategoria => Etiquetas.ColorFondo(Gasto.Categoria);
        public bool DesdeCelular => Gasto.Origen == GastoDiario.OrigenCelular;
        public string? TextoOriginal => string.IsNullOrWhiteSpace(Gasto.TextoOriginal) ? null : $"Escribiste: \"{Gasto.TextoOriginal}\"";
    }

    public class FilaCategoriaDiaria
    {
        public string Nombre { get; set; } = string.Empty;
        public decimal Total { get; set; }
        public int Veces { get; set; }
        public double Proporcion { get; set; }
        public double Resto => 1 - Proporcion;
        public string Color { get; set; } = "#8A94A3";
        public string Detalle => $"{Proporcion:P0} de lo gastado · {Veces} {(Veces == 1 ? "vez" : "veces")}";
    }

    public class FilaFrecuente
    {
        public string Nombre { get; set; } = string.Empty;
        public int Veces { get; set; }
        public string VecesTexto => $"{Veces}×";
        public decimal Total { get; set; }
        public string Detalle { get; set; } = string.Empty;
        public string FondoCategoria { get; set; } = "#EEF1F5";
        public string ColorCategoria { get; set; } = "#4B5565";
    }
}
