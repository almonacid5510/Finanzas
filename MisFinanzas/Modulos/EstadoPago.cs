using System;
namespace MisFinanzas.Modulos
{
    /// <summary>Colores de una obligación según su pago y fecha límite, sin modificar el historial.</summary>
    public record EstadoPago(string Texto, string Fondo, string Color, string FondoFila, string Acento)
    {
        public static EstadoPago Evaluar(bool pagado, DateTime? vence, DateTime hoy)
        {
            if (pagado) return new("Pagado", "#D1FADF", "#075E46", "#E7F5EF", "#12B76A");
            if (vence is DateTime fecha)
            {
                var dias = (fecha.Date - hoy.Date).Days;
                if (dias < 0) return new("Vencida", "#FEE4E2", "#B42318", "#FEF3F2", "#D92D20");
                if (dias <= 7) return new(dias == 0 ? "Vence hoy" : "Por vencer", "#FEF08A", "#854D0E", "#FEFCE8", "#F79009");
            }
            return new("Pendiente", "#F4F6F9", "#5B6676", "#FFFFFF", "#D0D5DD");
        }

        /// <summary>Filtro de la lista de Cuentas al que pertenece un estado: Pagadas, Vencidas o Pendientes.</summary>
        public static string Grupo(string texto) => texto switch
        {
            "Pagado" or "Saldado" => "Pagadas",
            "Vencida" => "Vencidas",
            _ => "Pendientes"
        };
    }
}
