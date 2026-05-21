using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MisFinanzas.Modulos
{
    [Table("MovimientosV2")]
    public class Movimiento
    {
        [Key]
        public int Id { get; set; }

        [Required] // Asegura que no sea null en la BD
        public string Descripcion { get; set; } = string.Empty;

        public decimal Costo { get; set; }
        public decimal Venta { get; set; }
        public DateTime Fecha { get; set; } = DateTime.Now; // Valor por defecto
        public bool EstaPagado { get; set; }
        public string Tipo { get; set; } = string.Empty;

        [NotMapped] // Esto le dice a EF que no cree esta columna en la tabla
        public decimal Ganancia => Venta - Costo;
    }
}