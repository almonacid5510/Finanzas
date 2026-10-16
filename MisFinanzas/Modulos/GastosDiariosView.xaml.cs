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

namespace MisFinanzas.Modulos
{
    public partial class GastosDiariosView : UserControl
    {
        // Gastos de hasta este valor cuentan como "hormiga": pequeños pero frecuentes
        public const decimal TopeHormiga = 20_000m;

        private DateTime _mesSeleccionado = Periodo.InicioMes(DateTime.Today);

        // Vehículos, para mostrar a cuál pertenece cada gasto directo
        private List<Vehiculo> _vehiculos = new();

        public GastosDiariosView()
        {
            InitializeComponent();
            Loaded += (s, e) =>
            {
                CambiarMes(_mesSeleccionado);
                SincronizacionCelular.GastosRecibidos -= GastosDelCelular; // evita duplicados al volver a la pantalla
                SincronizacionCelular.GastosRecibidos += GastosDelCelular;
                SincronizacionCelular.AccionesAplicadas -= AccionesDelCelular;
                SincronizacionCelular.AccionesAplicadas += AccionesDelCelular;
            };
            Unloaded += (s, e) =>
            {
                SincronizacionCelular.GastosRecibidos -= GastosDelCelular;
                SincronizacionCelular.AccionesAplicadas -= AccionesDelCelular;
            };
        }

        // Un gasto borrado desde el celular
        private void AccionesDelCelular(string texto) => Dispatcher.BeginInvoke(() => { Cargar(); toast.Show(texto); });

        // --- Gastos que llegan del celular ---
        private void GastosDelCelular(int nuevos) => Dispatcher.BeginInvoke(() =>
        {
            Cargar();
            toast.Show(nuevos == 1 ? "Llegó 1 gasto del celular" : $"Llegaron {nuevos} gastos del celular");
        });

        // --- Navegación entre meses ---
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
            Cargar();
        }

        // --- Carga y análisis del mes ---
        private void Cargar()
        {
            List<GastoDiario> gastos;
            try
            {
                using var db = new AppDbContext();
                var desde = _mesSeleccionado;
                var hasta = _mesSeleccionado.AddMonths(1);
                gastos = db.GastosDiarios.Where(g => g.Fecha >= desde && g.Fecha < hasta).ToList();
                _vehiculos = db.Vehiculos.ToList();
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
                                 Relativo = g.Key == hoy ? "Hoy · " : g.Key == hoy.AddDays(-1) ? "Ayer · " : string.Empty,
                                 Fecha = g.Key.ToString("dddd d 'de' MMMM", CultureInfo.CurrentCulture),
                                 Total = g.Sum(x => x.Monto),
                                 Gastos = g.OrderByDescending(x => x.Fecha).Select(x => new FilaGastoDiario(x, NombreVehiculo(x.VehiculoId))).ToList()
                             })
                             .ToList();
            icDias.ItemsSource = dias;
            txtSinGastos.Text = Periodo.EsMesActual(_mesSeleccionado)
                ? (SincronizacionCelular.Conectado ? "Aún no registras gastos este mes. Regístralos desde el celular."
                                                   : "Aún no hay gastos este mes. Conecta tu celular en Cuenta para registrarlos.")
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

            // Una sola barra repartida entre las categorías
            barraCategorias.Children.Clear();
            barraCategorias.ColumnDefinitions.Clear();
            barraCategorias.Visibility = filas.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            for (int i = 0; i < filas.Count; i++)
            {
                barraCategorias.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(filas[i].Proporcion, 0.01), GridUnitType.Star) });
                var tramo = new Border
                {
                    CornerRadius = new CornerRadius(4),
                    Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(filas[i].Color)!,
                    Margin = new Thickness(0, 0, i < filas.Count - 1 ? 2 : 0, 0),
                    ToolTip = $"{filas[i].Nombre} · {filas[i].Proporcion:P0}"
                };
                Grid.SetColumn(tramo, i);
                barraCategorias.Children.Add(tramo);
            }
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

        private string? NombreVehiculo(int? id) => id is int x ? _vehiculos.FirstOrDefault(v => v.Id == x)?.Nombre : null;

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
                    if (g != null)
                    {
                        db.GastosDiarios.Remove(g);
                        VinculoVehiculo.Quitar(db, g.Uid);
                        VinculoTarjeta.Ajustar(db, g.TarjetaId, g.Monto, null, 0); // se devuelve el cupo
                        db.SaveChanges();
                    }
                }
                VinculoVehiculo.RecargarVehiculos();
                VinculoTarjeta.RecargarCreditos();
                // También se borra en el celular
                SincronizacionCelular.RegistrarCambio(gasto, borrado: true);
                Cargar();
                toast.Show("Gasto eliminado");
            }
            catch (Exception ex)
            {
                MisFinanzas.Controles.Aviso.Mostrar($"Error al eliminar el gasto: {ex.Message}");
            }
        }

        private void MostrarOverlayConfirm(bool visible) => MostrarOverlay(OverlayConfirm, visible);

        private static void MostrarOverlay(FrameworkElement overlay, bool visible)
        {
            overlay.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            if (visible)
                overlay.BeginAnimation(OpacityProperty,
                    new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
        }
    }

    // ===== Filas de la pantalla (solo presentación) =====

    public class DiaGastos
    {
        public string Relativo { get; set; } = string.Empty;   // "Hoy · ", "Ayer · " o vacío
        public string Fecha { get; set; } = string.Empty;      // "jueves 15 de octubre"
        public decimal Total { get; set; }
        public List<FilaGastoDiario> Gastos { get; set; } = new();
    }

    public class FilaGastoDiario
    {
        public FilaGastoDiario(GastoDiario gasto, string? vehiculo = null) { Gasto = gasto; Vehiculo = vehiculo; }
        public string? Vehiculo { get; }
        public bool TieneVehiculo => Vehiculo != null;
        public GastoDiario Gasto { get; }
        public DateTime Fecha => Gasto.Fecha;
        public string Descripcion => Gasto.Descripcion;
        public decimal Monto => Gasto.Monto;
        public string Categoria => Etiquetas.Mostrar(Gasto.Categoria);
        public string ColorCategoria => Etiquetas.ColorTexto(Gasto.Categoria);
        public string FondoCategoria => Etiquetas.ColorFondo(Gasto.Categoria);
        public object? Icono => Application.Current.TryFindResource(Etiquetas.Mostrar(Gasto.Categoria) switch
        {
            "Alimentación" => "IcoComida",
            "Vehículos" or "Transporte" => "IcoVehiculos",
            "Hogar" => "IcoCasa",
            "Entretenimiento" => "IcoSonrisa",
            "Médicos" => "IcoPersona",
            _ => TieneVehiculo ? "IcoVehiculos" : "IcoEtiqueta"
        });
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
        public string Detalle => $"{Proporcion:P0} · {Veces} {(Veces == 1 ? "vez" : "veces")}";
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
