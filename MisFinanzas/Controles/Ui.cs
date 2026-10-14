// ============================================================
//  Proyecto: Mis Finanzas
//  Autoria: Juan Almonacid
//  Copyright (c) 2026 Juan Almonacid. Todos los derechos reservados.
// ============================================================
using System.Windows;
using System.Windows.Media;

namespace MisFinanzas.Controles
{
    /// <summary>
    /// Propiedades adjuntas que leen las plantillas del tema (Estilos/AppTheme.xaml):
    /// - Pista: texto de ejemplo dentro de un campo vacío ("Ej. Arriendo, internet").
    /// - Icono: ícono de línea a la izquierda del texto de un botón.
    /// </summary>
    public static class Ui
    {
        public static readonly DependencyProperty PistaProperty =
            DependencyProperty.RegisterAttached("Pista", typeof(string), typeof(Ui), new PropertyMetadata(string.Empty));
        public static string GetPista(DependencyObject d) => (string)d.GetValue(PistaProperty);
        public static void SetPista(DependencyObject d, string valor) => d.SetValue(PistaProperty, valor);

        public static readonly DependencyProperty IconoProperty =
            DependencyProperty.RegisterAttached("Icono", typeof(Geometry), typeof(Ui), new PropertyMetadata(null));
        public static Geometry? GetIcono(DependencyObject d) => (Geometry?)d.GetValue(IconoProperty);
        public static void SetIcono(DependencyObject d, Geometry? valor) => d.SetValue(IconoProperty, valor);
    }
}
