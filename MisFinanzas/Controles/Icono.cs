// ============================================================
//  Proyecto: Mis Finanzas
//  Autoria: Juan Almonacid
//  Copyright (c) 2026 Juan Almonacid. Todos los derechos reservados.
// ============================================================
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MisFinanzas.Controles
{
    /// <summary>
    /// Ícono de línea (estilo Lucide) dibujado sobre una cuadrícula de 24×24.
    /// Toma el color del texto (Foreground) de su contenedor. Uso:
    /// &lt;c:Icono Geometria="{StaticResource IcoPlus}" Tamano="16"/&gt;
    /// La plantilla está en Estilos/AppTheme.xaml.
    /// </summary>
    public class Icono : Control
    {
        static Icono()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(Icono), new FrameworkPropertyMetadata(typeof(Icono)));
            FocusableProperty.OverrideMetadata(typeof(Icono), new FrameworkPropertyMetadata(false));
            IsTabStopProperty.OverrideMetadata(typeof(Icono), new FrameworkPropertyMetadata(false));
        }

        public static readonly DependencyProperty GeometriaProperty =
            DependencyProperty.Register(nameof(Geometria), typeof(Geometry), typeof(Icono));

        public static readonly DependencyProperty TamanoProperty =
            DependencyProperty.Register(nameof(Tamano), typeof(double), typeof(Icono), new PropertyMetadata(16.0));

        public static readonly DependencyProperty GrosorProperty =
            DependencyProperty.Register(nameof(Grosor), typeof(double), typeof(Icono), new PropertyMetadata(2.0));

        public Geometry? Geometria { get => (Geometry?)GetValue(GeometriaProperty); set => SetValue(GeometriaProperty, value); }
        public double Tamano { get => (double)GetValue(TamanoProperty); set => SetValue(TamanoProperty, value); }
        public double Grosor { get => (double)GetValue(GrosorProperty); set => SetValue(GrosorProperty, value); }
    }
}
