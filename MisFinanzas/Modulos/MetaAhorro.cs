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

        // Lo que falta para llegar al objetivo (para mostrar)
        [NotMapped]
        public decimal Faltante => System.Math.Max(0, MontoObjetivo - MontoActual);

        // Cuánto se pasó del objetivo (0 si no lo ha superado)
        [NotMapped]
        public decimal Excedente => System.Math.Max(0, MontoActual - MontoObjetivo);

        [NotMapped]
        public bool Superada => MontoObjetivo > 0 && MontoActual > MontoObjetivo;

        [NotMapped]
        public string EstadoTexto => Superada
            ? $"Superaste la meta por {NumeroHelper.Moneda(Excedente)}"
            : $"Te faltan {NumeroHelper.Moneda(Faltante)}";

        [NotMapped]
        public string NombreMostrar => string.IsNullOrWhiteSpace(NombreCustom) ? Nombre : NombreCustom;

        [NotMapped]
        public double ProgresoStrokeOffset => 32.2 * (1 - (System.Math.Min(100.0, System.Math.Max(0.0, Progreso)) / 100.0));

        [NotMapped]
        public double ProgresoStrokeOffsetSmall => 33.5 * (1 - (System.Math.Min(100.0, System.Math.Max(0.0, Progreso)) / 100.0));
    }

    /// <summary>Cada abono a una meta (cuándo y cuánto). MetaAhorro.MontoActual es la suma acumulada.</summary>
    public class AbonoMeta
    {
        [Key]
        public int Id { get; set; }
        public int MetaId { get; set; }
        public System.DateTime Fecha { get; set; } = System.DateTime.Now;
        public decimal Monto { get; set; }
    }
}