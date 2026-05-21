using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MisFinanzas.Modulos
{
    public class ItemNegocio
    {
        [Key] // Esto le dice a la BD que es el identificador único
        public int Id { get; set; }

        public string Descripcion { get; set; } = string.Empty;

        public string Tipo { get; set; } = "Venta"; // "Venta" o "Prestamo"

        public decimal MontoTotal { get; set; } // Lo que recibes

        public decimal CostoInversion { get; set; } // Lo que te costó o prestaste

        public DateTime Fecha { get; set; } = DateTime.Now;

        // [NotMapped] indica que esta propiedad no se guarda en la base de datos
        // porque se calcula automáticamente en tiempo real.
        [NotMapped]
        public decimal Ganancia => MontoTotal - CostoInversion;
    }
}