// ============================================================
//  Proyecto: Mis Finanzas
//  Autoria: Juan Almonacid
//  Copyright (c) 2026 Juan Almonacid. Todos los derechos reservados.
// ============================================================
using System.Windows;

namespace MisFinanzas
{
    // Propiedad adjunta para marcar el ítem de navegación activo desde el code-behind.
    public static class NavState
    {
        public static readonly DependencyProperty IsActiveProperty =
            DependencyProperty.RegisterAttached(
                "IsActive",
                typeof(bool),
                typeof(NavState),
                new PropertyMetadata(false));

        public static void SetIsActive(DependencyObject element, bool value)
            => element.SetValue(IsActiveProperty, value);

        public static bool GetIsActive(DependencyObject element)
            => (bool)element.GetValue(IsActiveProperty);
    }
}
