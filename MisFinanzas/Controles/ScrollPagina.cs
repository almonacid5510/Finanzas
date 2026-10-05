// ============================================================
//  Proyecto: Mis Finanzas
//  Autoria: Juan Almonacid
//  Copyright (c) 2026 Juan Almonacid. Todos los derechos reservados.
// ============================================================
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MisFinanzas.Controles
{
    /// <summary>
    /// Desplazamiento de página completa con la rueda del mouse (controles:ScrollPagina.Activo="True").
    /// Si el mouse está sobre una lista interna que aún puede desplazarse (p. ej. el plan de pagos),
    /// la rueda mueve esa lista; si no, mueve la página. Así las listas internas no "atrapan" la rueda.
    /// </summary>
    public static class ScrollPagina
    {
        // Cuánto se mueve la página por cada paso de la rueda (WPF por defecto avanza muy poco)
        private const double Factor = 0.8;

        public static readonly DependencyProperty ActivoProperty =
            DependencyProperty.RegisterAttached("Activo", typeof(bool), typeof(ScrollPagina),
                new PropertyMetadata(false, AlCambiarActivo));

        public static bool GetActivo(DependencyObject d) => (bool)d.GetValue(ActivoProperty);
        public static void SetActivo(DependencyObject d, bool valor) => d.SetValue(ActivoProperty, valor);

        private static void AlCambiarActivo(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not ScrollViewer pagina) return;
            pagina.PreviewMouseWheel -= Pagina_PreviewMouseWheel;
            if ((bool)e.NewValue) pagina.PreviewMouseWheel += Pagina_PreviewMouseWheel;
        }

        private static void Pagina_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var pagina = (ScrollViewer)sender;

            // ¿Hay una lista interna bajo el mouse que todavía puede moverse en esa dirección?
            for (var nodo = e.OriginalSource as DependencyObject; nodo != null && nodo != pagina; nodo = Padre(nodo))
            {
                if (nodo is ScrollViewer interno && interno.ScrollableHeight > 0)
                {
                    bool puede = e.Delta > 0 ? interno.VerticalOffset > 0
                                             : interno.VerticalOffset < interno.ScrollableHeight;
                    if (puede) return; // la lista interna se encarga
                }
            }

            pagina.ScrollToVerticalOffset(pagina.VerticalOffset - e.Delta * Factor);
            e.Handled = true;
        }

        private static DependencyObject? Padre(DependencyObject nodo) =>
            nodo is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(nodo)
                : LogicalTreeHelper.GetParent(nodo);
    }
}
