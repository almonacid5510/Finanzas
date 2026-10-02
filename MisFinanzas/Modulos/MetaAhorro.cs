// ============================================================
//  Proyecto: Mis Finanzas
//  Autoria: Juan Almonacid
//  Copyright (c) 2026 Juan Almonacid. Todos los derechos reservados.
// ============================================================
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MisFinanzas.Modulos
{
    public class MetaAhorro
    {
        [Key]
        public int Id { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string NombreCustom { get; set; } = string.Empty;
        public decimal MontoObjetivo { get; set; }
        public decimal MontoActual { get; set; }
        public int PlazoMeses { get; set; }

        // [NotMapped] significa que esto no se guarda como columna en la tabla, 
        // pero se calcula en memoria.
        [NotMapped]
        public decimal CuotaMensual => PlazoMeses > 0 ? MontoObjetivo / PlazoMeses : 0;

        [NotMapped]
        public double Progreso => MontoObjetivo > 0 ? (double)((MontoActual / MontoObjetivo) * 100) : 0;

        [NotMapped]
        public double ProgresoStrokeOffset => 32.2 * (1 - (System.Math.Min(100.0, System.Math.Max(0.0, Progreso)) / 100.0));

        [NotMapped]
        public double ProgresoStrokeOffsetSmall => 33.5 * (1 - (System.Math.Min(100.0, System.Math.Max(0.0, Progreso)) / 100.0));
    }
}