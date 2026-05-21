using System.Configuration;
using System.Data;
using System.Windows;

namespace MisFinanzas
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        // El método debe ir AQUÍ adentro
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Esto asegura que la base de datos SQLite se cree 
            // apenas abras la aplicación por primera vez
            DatabaseConfig.InitializeDatabase();
        }
    }
}