using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Runtime.CompilerServices;

namespace MisFinanzas.Modulos
{
    public class Vehiculo : INotifyPropertyChanged
    {
        [Key]
        public int Id { get; set; }

        public string Nombre { get; set; } = string.Empty;
        public string Tipo { get; set; } = string.Empty;
        public string Placa { get; set; } = string.Empty;
        public DateTime Fecha { get; set; } = DateTime.Now;

        [NotMapped]
        public ObservableCollection<GastoVehiculo> Gastos { get; set; } = new ObservableCollection<GastoVehiculo>();

        // Ahora TotalInvertido es una propiedad completa que notifica cambios
        [NotMapped]
        public decimal TotalInvertido
        {
            get => Gastos.Sum(g => g.Valor);
        }
        public void RefrescarTotal()
        {
            // Esto obliga al Binding del XAML a leer el valor de nuevo
            OnPropertyChanged(nameof(TotalInvertido));
        }
        // Método para forzar a la interfaz a actualizarse
        public void NotificarCambioTotal()
        {
            // Esta línea es la que envía la señal a la interfaz XAML
            OnPropertyChanged(nameof(TotalInvertido));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = null!)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    public class GastoVehiculo
    {
        [Key]
        public int Id { get; set; }
        public string Concepto { get; set; } = string.Empty;
        public decimal Valor { get; set; }
        public DateTime Fecha { get; set; } = DateTime.Now;
        public int VehiculoId { get; set; }
    }
}