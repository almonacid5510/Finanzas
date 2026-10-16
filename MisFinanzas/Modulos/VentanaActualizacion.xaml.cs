using System.ComponentModel;
using System.Windows;
namespace MisFinanzas.Modulos
{
    public partial class VentanaActualizacion : Window
    {
        private readonly Actualizaciones actualizaciones;
        public VentanaActualizacion(Actualizaciones actualizaciones)
        {
            InitializeComponent(); this.actualizaciones=actualizaciones; DataContext=actualizaciones;
            actualizaciones.PropertyChanged += Cambio;
            Closed += (_,_) => actualizaciones.PropertyChanged -= Cambio;
            Closing += (_,e) => { if(actualizaciones.Ocupado) e.Cancel=true; };
        }
        private void Cambio(object? sender, PropertyChangedEventArgs e) { btnActualizar.IsEnabled=btnLuego.IsEnabled=!actualizaciones.Ocupado; }
        private async void Actualizar_Click(object sender, RoutedEventArgs e) => await actualizaciones.ActualizarAsync();
        private void Luego_Click(object sender, RoutedEventArgs e) { actualizaciones.MasTarde(); Close(); }
    }
}
