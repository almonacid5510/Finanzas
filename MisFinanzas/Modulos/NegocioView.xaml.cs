// ============================================================
//  Proyecto: Mis Finanzas
//  Autoria: Juan Almonacid
//  Copyright (c) 2026 Juan Almonacid. Todos los derechos reservados.
// ============================================================
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;

namespace MisFinanzas.Modulos
{
    public partial class NegocioView : UserControl
    {
        // Usamos una propiedad estática para mantener los datos en memoria mientras la app esté abierta
        public static ObservableCollection<Movimiento> ListaMovimientos { get; set; } = new ObservableCollection<Movimiento>();

        public NegocioView()
        {
            InitializeComponent();

            // Las inversiones y, al final, la tarjeta que invita a sumar otra
            icListaNegocios.ItemsSource = new System.Windows.Data.CompositeCollection
            {
                new System.Windows.Data.CollectionContainer { Collection = ListaMovimientos },
                new SugerenciaInversion()
            };

            // EVENTO CRUCIAL: Cada vez que entres a la pestaña, se recargan los datos del disco
            this.Loaded += (s, e) => CargarMovimientosDesdeBD();
        }

        private void CargarMovimientosDesdeBD()
        {
            try
            {
                using (var db = new AppDbContext())
                {
                    // Traemos los movimientos guardados en el archivo .db
                    var movimientosBD = db.Movimientos.Where(m => m.Tipo == "Inversion").ToList();

                    ListaMovimientos.Clear();
                    foreach (var m in movimientosBD)
                    {
                        ListaMovimientos.Add(m);
                    }
                }
                ActualizarTotal();
            }
            catch (Exception ex)
            {
                MisFinanzas.Controles.Aviso.Mostrar($"Error al cargar datos desde la base de datos: {ex.Message}");
            }
        }

        private void BtnAgregar_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(txtDescNegocio.Text) &&
                NumeroHelper.TryParseMoneda(txtInversionNegocio.Text, out decimal montoInicial))
            {
                // Costo guarda el total invertido; los abonos se le van sumando (como las metas de ahorro)
                var nuevoMovimiento = new Movimiento
                {
                    Descripcion = txtDescNegocio.Text,
                    Costo = montoInicial,
                    Fecha = DateTime.Now,
                    Tipo = "Inversion"
                };

                try
                {
                    using (var db = new AppDbContext())
                    {
                        // Guardado físico en el archivo MisFinanzas_Real.db
                        db.Movimientos.Add(nuevoMovimiento);
                        db.SaveChanges();
                    }

                    // Actualización de la lista en pantalla
                    ListaMovimientos.Add(nuevoMovimiento);
                    ActualizarTotal();

                    // Limpieza de campos
                    txtDescNegocio.Clear();
                    txtInversionNegocio.Clear();
                    toast.Show("Inversión registrada");
                }
                catch (Exception ex)
                {
                    string detalle = ex.InnerException != null ? $"\nDetalle: {ex.InnerException.Message}" : "";
                    MisFinanzas.Controles.Aviso.Mostrar($"Error al guardar: {ex.Message}{detalle}");
                }
            }
            else
            {
                MisFinanzas.Controles.Aviso.Mostrar("Por favor, ingresa el nombre y un monto válido.");
            }
        }

        // Movimiento pendiente de confirmar eliminación
        private Movimiento? _movPendienteEliminar;

        private void BtnEliminar_Click(object sender, RoutedEventArgs e)
        {
            var btn = (Button)sender;
            _movPendienteEliminar = btn.DataContext as Movimiento;

            if (_movPendienteEliminar != null)
            {
                runNombreMov.Text = $"\"{_movPendienteEliminar.Descripcion}\"";
                MostrarOverlayConfirm(true);
            }
        }

        private void ConfirmCancelar_Click(object sender, RoutedEventArgs e)
        {
            _movPendienteEliminar = null;
            MostrarOverlayConfirm(false);
        }

        private void ConfirmEliminar_Click(object sender, RoutedEventArgs e)
        {
            var movimiento = _movPendienteEliminar;
            MostrarOverlayConfirm(false);
            _movPendienteEliminar = null;

            if (movimiento == null) return;

            try
            {
                using (var db = new AppDbContext())
                {
                    db.Movimientos.Remove(movimiento);
                    db.SaveChanges();
                }
                ListaMovimientos.Remove(movimiento);
                icListaNegocios.Items.Refresh();
                ActualizarTotal();
                toast.Show("Inversión eliminada");
            }
            catch (Exception ex)
            {
                MisFinanzas.Controles.Aviso.Mostrar($"Error al eliminar de la base de datos: {ex.Message}");
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

        // Suma un abono al total invertido (mismo flujo que abonar a una meta de ahorro)
        private void BtnAbonar_Click(object sender, RoutedEventArgs e)
        {
            var boton = (Button)sender;
            if (boton.DataContext is not Movimiento inversion) return;

            var textBox = (boton.Parent as Grid)?.Children.OfType<TextBox>().FirstOrDefault();
            if (textBox == null || !NumeroHelper.TryParseMoneda(textBox.Text, out decimal abono) || abono <= 0) return;

            try
            {
                using (var db = new AppDbContext())
                {
                    var inversionDb = db.Movimientos.Find(inversion.Id);
                    if (inversionDb == null) return;
                    inversionDb.Costo += abono;
                    db.SaveChanges();
                    inversion.Costo = inversionDb.Costo;
                }

                textBox.Clear();
                icListaNegocios.Items.Refresh();
                ActualizarTotal();
                toast.Show($"Abono de {abono:C0} registrado");
            }
            catch (Exception ex)
            {
                MisFinanzas.Controles.Aviso.Mostrar($"Error al registrar el abono: {ex.Message}");
            }
        }

        private void ActualizarTotal()
        {
            // Total invertido entre todas las inversiones
            decimal total = ListaMovimientos.Sum(m => m.Costo);
            lblTotalNegocio.Text = total.ToString("C0");
            MostrarDistribucion(total);
        }


        // Barra de distribución: qué porcentaje del total tiene cada inversión
        private void MostrarDistribucion(decimal total)
        {
            // Cada parte toma el color de su tipo (el de la tarjeta); si el tipo se repite, otro tono del mismo color
            var usados = new Dictionary<TipoInversion.Estilo, int>();
            var partes = new List<FilaDistribucion>();
            foreach (var m in ListaMovimientos.Where(m => m.Costo > 0))
            {
                var estilo = TipoInversion.De(m.Descripcion);
                int n = usados.GetValueOrDefault(estilo);
                usados[estilo] = n + 1;
                partes.Add(new FilaDistribucion
                {
                    Nombre = m.Descripcion,
                    Monto = m.Costo,
                    Porcentaje = total > 0 ? (double)(m.Costo / total) : 0,
                    Color = estilo.TonosBarra[n % estilo.TonosBarra.Length]
                });
            }

            icDistribucion.ItemsSource = partes;
            barraDistribucion.Children.Clear();
            barraDistribucion.ColumnDefinitions.Clear();
            for (int i = 0; i < partes.Count; i++)
            {
                barraDistribucion.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength((double)partes[i].Monto, GridUnitType.Star) });
                var parte = new Border
                {
                    Background = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(partes[i].Color)!,
                    CornerRadius = new CornerRadius(i == 0 ? 5 : 0, i == partes.Count - 1 ? 5 : 0, i == partes.Count - 1 ? 5 : 0, i == 0 ? 5 : 0),
                    Margin = new Thickness(0, 0, i == partes.Count - 1 ? 0 : 2, 0),
                    ToolTip = $"{partes[i].Nombre}: {partes[i].Monto:C0}"
                };
                Grid.SetColumn(parte, i);
                barraDistribucion.Children.Add(parte);
            }
        }
    }

    /// <summary>
    /// Tipo de inversión deducido de su nombre, con su ícono y colores:
    /// fondo/CDT/banco → banco (verde), préstamo → persona (morado), acciones/cripto → gráfico (azul),
    /// Nequi/Daviplata → celular (azul), casa/lote/finca → casa (naranja); el resto → gráfico (verde).
    /// </summary>
    public static class TipoInversion
    {
        public record Estilo(string Fondo, string Color, string Icono, string[] TonosBarra);

        private static readonly Estilo Banco = new("VerdeSuave", "Verde", "IcoBanco", new[] { "#3CCB8F", "#9BE3C4", "#1E9E6E" });
        private static readonly Estilo Prestamo = new("MoradoSuave", "Morado", "IcoPersona", new[] { "#C3B1F7", "#9F84EE", "#E0D6FB" });
        private static readonly Estilo Bolsa = new("AzulSuave", "Azul", "IcoSube", new[] { "#A9C5FF", "#7FA6F5", "#D4E2FF" });
        private static readonly Estilo Billetera = new("AzulSuave", "Azul", "IcoCelular", new[] { "#A9C5FF", "#7FA6F5", "#D4E2FF" });
        private static readonly Estilo Inmueble = new("NaranjaSuave", "NaranjaTexto", "IcoCasa", new[] { "#F5B963", "#F09A3E", "#FAD8A6" });
        private static readonly Estilo Otra = new("VerdeSuave", "Verde", "IcoSube", new[] { "#3CCB8F", "#9BE3C4", "#1E9E6E" });

        private static readonly (string[] Palabras, Estilo Estilo)[] Reglas =
        {
            (new[] { "prestamo", "prestado", "presté", "preste", "deuda", "me debe" }, Prestamo),
            (new[] { "nequi", "daviplata", "billetera", "movil", "móvil" }, Billetera),
            (new[] { "accion", "acción", "bolsa", "cripto", "bitcoin", "etf", "trading" }, Bolsa),
            (new[] { "casa", "apartamento", "apto", "lote", "finca", "inmueble", "local" }, Inmueble),
            (new[] { "fondo", "cdt", "banco", "ahorro", "cuenta", "fiducia" }, Banco),
        };

        public static Estilo De(string? nombre)
        {
            var n = (nombre ?? string.Empty).ToLowerInvariant()
                       .Replace('á', 'a').Replace('é', 'e').Replace('í', 'i').Replace('ó', 'o').Replace('ú', 'u');
            foreach (var (palabras, estilo) in Reglas)
                if (palabras.Any(p => n.Contains(p.Replace('é', 'e').Replace('ó', 'o').Replace('í', 'i'))))
                    return estilo;
            return Otra;
        }
    }

    /// <summary>Color e ícono de cada tarjeta según el tipo de inversión. ConverterParameter: "fondo", "color" o "icono".</summary>
    public class EstiloInversionConverter : System.Windows.Data.IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            var e = TipoInversion.De((value as Movimiento)?.Descripcion);
            var clave = (parameter as string) switch { "fondo" => e.Fondo, "icono" => e.Icono, _ => e.Color };
            return Application.Current.TryFindResource(clave);
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            throw new NotSupportedException();
    }

    /// <summary>Marcador de la tarjeta "Diversifica tu portafolio" al final de la lista.</summary>
    public class SugerenciaInversion
    {
    }
}
