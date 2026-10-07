// ============================================================
//  Proyecto: Mis Finanzas
//  Autoria: Juan Almonacid
//  Copyright (c) 2026 Juan Almonacid. Todos los derechos reservados.
// ============================================================
using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace MisFinanzas.Controles
{
    /// <summary>
    /// Convierte un avance (0 a 1, o 0 a 100 si ConverterParameter empieza por "%") en el trazo de un
    /// anillo de progreso dibujado con un Ellipse: StrokeDashArray="{Binding Avance, Converter=...}".
    /// El parámetro indica "diámetro,grosor" del Ellipse (por ejemplo "78,8" o "%78,8").
    /// </summary>
    public class AnilloConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var p = (parameter as string ?? "78,8");
            bool porcentaje = p.StartsWith("%");
            var partes = p.TrimStart('%').Split(',');
            double diametro = double.Parse(partes[0], CultureInfo.InvariantCulture);
            double grosor = double.Parse(partes[1], CultureInfo.InvariantCulture);

            double avance = System.Convert.ToDouble(value, CultureInfo.InvariantCulture);
            if (porcentaje) avance /= 100.0;
            avance = Math.Clamp(avance, 0, 1);

            // El patrón del trazo se mide en múltiplos del grosor
            double vuelta = Math.PI * (diametro - grosor) / grosor;
            return new DoubleCollection { avance * vuelta, vuelta * 2 };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
