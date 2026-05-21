using System;
using System.Collections.ObjectModel;
using System.Diagnostics.Metrics;
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

            // 1. Aseguramos la base de datos primero
            using (var db = new AppDbContext())
            {
                db.Database.EnsureCreated();
            }

            // 2. IMPORTANTE: No asignes icMetas aquí directamente.
            //lblIngresoTotal.Text = SalarioGuardado; // Si esta variable existe, déjala.

            // 3. Movemos la lógica al evento Loaded
            this.Loaded += (s, e) =>
            {
                // Ahora icMetas ya no es null
                icMetas.ItemsSource = ListaMetas;
                CargarMetasDesdeBD();
            };
        }
        private void CargarMetasDesdeBD()
        {
            try
            {
                using (var db = new AppDbContext())
                {
                    db.Database.EnsureCreated(); // Por seguridad
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
        // --- LÓGICA DE EDICIÓN DE SALARIO (IN-PLACE) ---

        private void BtnActivarEdicion_Click(object sender, RoutedEventArgs e)
        {
            // Oculta el texto y muestra el cuadro de edición
            pnlLecturaSalario.Visibility = Visibility.Collapsed;
            pnlEdicionSalario.Visibility = Visibility.Visible;

            // Limpia el formato de moneda para editar solo números
            txtEditarSalario.Text = lblIngresoTotal.Text.Replace("$", "").Replace(".", "").Trim();
            txtEditarSalario.Focus();
        }

        private void BtnConfirmarSalario_Click(object sender, RoutedEventArgs e)
        {
            if (decimal.TryParse(txtEditarSalario.Text, out decimal nuevoSalario))
            {
                SalarioGuardado = nuevoSalario.ToString("C0"); // Guardamos en la variable estática
                lblIngresoTotal.Text = SalarioGuardado;
                FinalizarEdicion();
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
                decimal.TryParse(txtValorMeta.Text, out decimal objetivo) &&
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

                    MessageBox.Show("Meta guardada con éxito en la base de datos.");
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
        }
        private void BtnEliminarMeta_Click(object sender, RoutedEventArgs e)
        {
            // Obtenemos el botón y la meta vinculada
            var boton = (Button)sender;
            var metaAEliminar = (MetaAhorro)boton.CommandParameter;

            if (metaAEliminar != null)
            {
                // Validación de seguridad con un MessageBox
                MessageBoxResult resultado = MessageBox.Show(
                    $"¿Estás seguro de que deseas eliminar la meta '{metaAEliminar.Nombre}'?",
                    "Confirmar eliminación",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                // Si el usuario presiona "Sí", procedemos a borrar
                if (resultado == MessageBoxResult.Yes)
                {
                    ListaMetas.Remove(metaAEliminar);
                    ActualizarAhorroAcumulado();
                }
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

                if (textBox != null && decimal.TryParse(textBox.Text, out decimal montoAbono))
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