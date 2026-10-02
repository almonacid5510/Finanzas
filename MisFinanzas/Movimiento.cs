// ============================================================
//  Proyecto: Mis Finanzas
//  Autoria: Juan Almonacid
//  Copyright (c) 2026 Juan Almonacid. Todos los derechos reservados.
// ============================================================
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

        // --- Para Tipo "Cuenta" e "IngresoExtra" (valores que se repiten, ver CuentasMensuales) ---
        // Fecha marca el mes desde el que aplica; DiaPago es el día en que vence.
        public int DiaPago { get; set; }

        // Mes ("yyyy-MM") desde el que se dejó de cobrar. Vacío = se sigue cobrando.
        public string MesFin { get; set; } = string.Empty;

        // Cada cuánto se repite: Mensual, Única (solo el mes de Fecha) o Anual (cada año en el mes de Fecha)
        public string Frecuencia { get; set; } = Frecuencias.Mensual;

        // Categoría o etiqueta libre: Hogar, Médicos, Impuestos, Freelance...
        public string Categoria { get; set; } = string.Empty;

        [NotMapped] // Esto le dice a EF que no cree esta columna en la tabla
        public decimal Ganancia => Venta - Costo;
    }
}