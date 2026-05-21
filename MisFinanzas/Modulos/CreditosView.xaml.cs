using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;


namespace MisFinanzas.Modulos
{
    public partial class CreditosView : UserControl
    {
        public static ObservableCollection<Credito> ListaCreditosPersistente { get; set; } = new ObservableCollection<Credito>();

        public CreditosView()
        {
            InitializeComponent();

            // 1. Vinculamos la lista persistente al control de esta pestaña
            // Asumiendo que tu ItemsControl en Créditos se llama 'icListaCreditos'
            icListaCreditos.ItemsSource = ListaCreditosPersistente;

            // 2. Si la lista está vacía (porque acabas de borrar la BD), 
            // intentamos cargarla de nuevo por si acaso
            if (ListaCreditosPersistente.Count == 0)
            {
                CargarDatosGlobal();
            }
        }

        private void BtnAgregarCredito_Click(object sender, RoutedEventArgs e)
        {
            if (decimal.TryParse(txtTotalCredito.Text, out decimal total) &&
                decimal.TryParse(txtCuotaCredito.Text, out decimal cuota) &&
                !string.IsNullOrWhiteSpace(txtNombreCredito.Text))
            {
                try
                {
                    using (var db = new AppDbContext())
                    {
                        var nuevo = new Credito
                        {
                            // IMPORTANTE: Usamos NombreCustom que es el que tienes en el modelo y DB
                            NombreCustom = txtNombreCredito.Text,
                            Tipo = txtNombreCredito.Text, // Llenamos ambos por si acaso
                            Total = total,
                            Disponible = total - (decimal.TryParse(txtAbonoCredito.Text, out var a) ? a : 0),
                            CuotaMensual = cuota,
                            TasaEA = double.TryParse(txtTasaEA.Text, out var t) ? t : 0,
                            PlazoMeses = int.TryParse(txtPlazo.Text, out var p) ? p : 0,
                            Pagado = false,
                            Editando = false
                        };

                        db.Creditos.Add(nuevo);
                        db.SaveChanges();
                    }

                    // Refrescamos la lista global
                    CargarDatosGlobal();

                    // Limpiar campos
                    txtNombreCredito.Clear();
                    txtTotalCredito.Clear();
                    txtCuotaCredito.Clear();
                    txtAbonoCredito.Clear(); 
                    txtTasaEA.Clear();       
                    txtPlazo.Clear();
                }
                catch (Exception ex) { MessageBox.Show("Error al guardar: " + ex.Message); }
            }
        }

        // Este es el método que hace la magia de la persistencia
        private void GuardarCreditoEnBD(string nom, decimal cuota, decimal total, decimal pagado, decimal tasa, int meses)
        {
            try
            {
                using (var connection = DatabaseConfig.GetConnection())
                {
                    connection.Open();
                    var command = connection.CreateCommand();

                    command.CommandText = @"INSERT INTO Creditos (NombreCustom, MontoTotal, SaldoPendiente, Tasa, PlazoMeses, CuotaMensual) 
                        VALUES (@nom, @total, @pendiente, @tasa, @meses, @cuota)";
                    command.Parameters.AddWithValue("@cuota", cuota); // Ya recibes 'cuota' como parámetro en este método

                    command.Parameters.AddWithValue("@nom", nom);
                    command.Parameters.AddWithValue("@total", total);
                    command.Parameters.AddWithValue("@pendiente", total - pagado);
                    command.Parameters.AddWithValue("@tasa", tasa);
                    command.Parameters.AddWithValue("@meses", meses);

                    command.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al guardar en la base de datos: " + ex.Message);
            }
        }

        private void BtnActivarEdicion_Click(object sender, RoutedEventArgs e) =>
            ((Credito)((Button)sender).DataContext).Editando = true;

        private void BtnGuardarEdicion_Click(object sender, RoutedEventArgs e)
        {
            // 1. Identificamos el crédito que se está editando
            var boton = sender as Button;
            var creditoEditado = boton?.DataContext as Credito;

            if (creditoEditado != null)
            {
                // 2. LLAMADA CRUCIAL: Guardamos los cambios en SQLite
                // Pasamos el objeto completo que ya tiene el ID y los datos nuevos
                ActualizarCreditoEnBD(creditoEditado);

                // 3. Cerramos el modo edición visual (lo que ya hacías)
                creditoEditado.Editando = false;

                // 4. Refrescamos la tabla para que se bloqueen los TextBox
                icListaCreditos.Items.Refresh();

                MessageBox.Show("Cambios guardados correctamente.");
            }
        }

        private void ActualizarCreditoEnBD(Credito c)
        {
            try
            {
                using (var connection = DatabaseConfig.GetConnection())
                {
                    connection.Open();
                    var command = connection.CreateCommand();

                    // Usamos el ID para asegurar que editamos la fila correcta
                    command.CommandText = @"UPDATE Creditos SET 
                        NombreCustom = @nom, MontoTotal = @total, SaldoPendiente = @pendiente, 
                        Tasa = @tasa, PlazoMeses = @meses, CuotaMensual = @cuota 
                        WHERE Id = @id";
                    command.Parameters.AddWithValue("@cuota", c.CuotaMensual);

                    command.Parameters.AddWithValue("@nom", c.Tipo);
                    command.Parameters.AddWithValue("@total", c.Total);
                    command.Parameters.AddWithValue("@pendiente", c.Disponible);
                    command.Parameters.AddWithValue("@tasa", (decimal)c.TasaEA);
                    command.Parameters.AddWithValue("@meses", c.PlazoMeses);
                    command.Parameters.AddWithValue("@id", c.Id);

                    command.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al guardar en la base de datos: " + ex.Message);
            }
        }

        private void BtnEliminar_Click(object sender, RoutedEventArgs e)
        {
            // 1. Identificamos el crédito desde el botón
            var button = sender as Button;
            var creditoAEliminar = button?.DataContext as Credito;

            if (creditoAEliminar != null)
            {
                // 2. Llamamos al método que tiene el MessageBox
                EliminarCreditoGlobal(creditoAEliminar);
            }
        }

        // Método de apoyo para la persistencia
        private void EliminarCreditoDeBD(string nombre)
        {
            try
            {
                using (var connection = DatabaseConfig.GetConnection())
                {
                    connection.Open();
                    var command = connection.CreateCommand();

                    // Usamos NombreCustom porque es el campo parametrizable en tu DB
                    command.CommandText = "DELETE FROM Creditos WHERE NombreCustom = @nom";
                    command.Parameters.AddWithValue("@nom", nombre);

                    command.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al borrar en base de datos: " + ex.Message);
            }
        }

        private void CargarCreditosDesdeBD()
        {
            try
            {
                using (var connection = DatabaseConfig.GetConnection())
                {
                    connection.Open();
                    var command = connection.CreateCommand();

                    // AGREGAMOS "Id" al principio de la consulta SQL
                    command.CommandText = "SELECT Id, NombreCustom, MontoTotal, SaldoPendiente, Tasa, PlazoMeses, CuotaMensual FROM Creditos";

                    using (var reader = command.ExecuteReader())
                    {
                        ListaCreditosPersistente.Clear();
                        while (reader.Read())
                        {
                            ListaCreditosPersistente.Add(new Credito
                            {
                                // Ahora los índices coinciden con el SELECT de arriba
                                Id = reader.GetInt32(0),          
                                Tipo = reader.GetString(1),        
                                Total = reader.GetDecimal(2),      
                                Disponible = reader.GetDecimal(3), 
                                TasaEA = reader.GetDouble(4),      
                                PlazoMeses = reader.GetInt32(5),
                                CuotaMensual = reader.GetDecimal(6), 
                                Editando = false
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error al cargar datos: " + ex.Message);
            }
        }
        // En CreditosView.xaml.cs
        public static void CargarDatosGlobal()
        {
            try
            {
                using (var db = new AppDbContext())
                {
                    var lista = db.Creditos.ToList();

                    // Usamos el Dispatcher para que la UI de "Cuentas" se entere
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        ListaCreditosPersistente.Clear();
                        foreach (var c in lista)
                        {
                            ListaCreditosPersistente.Add(c);
                        }
                    });
                }
            }
            catch (Exception ex) { /* ... */ }
        }
        // Método para eliminar de forma global
        public static void EliminarCreditoGlobal(Credito credito)
        {
            if (credito == null) return;

            var resultado = MessageBox.Show($"¿Estás seguro de que deseas eliminar el crédito '{credito.NombreCustom}'?",
                                          "Confirmar eliminación",
                                          MessageBoxButton.YesNo,
                                          MessageBoxImage.Warning);

            if (resultado == MessageBoxResult.Yes)
            {
                using (var db = new AppDbContext())
                {
                    var creditoDb = db.Creditos.Find(credito.Id);
                    if (creditoDb != null)
                    {
                        db.Creditos.Remove(creditoDb);
                        db.SaveChanges();
                    }
                }

                if (ListaCreditosPersistente.Contains(credito))
                {
                    ListaCreditosPersistente.Remove(credito);
                }
            }
        }
    }
}