// ============================================================
//  Proyecto: Mis Finanzas
//  Autoria: Juan Almonacid
//  Copyright (c) 2026 Juan Almonacid. Todos los derechos reservados.
// ============================================================
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MisFinanzas.Controles
{
    public partial class Toast : UserControl
    {
        public Toast()
        {
            InitializeComponent();
        }

        /// <summary>Muestra una notificación discreta con fade in/out.</summary>
        /// <param name="mensaje">Texto a mostrar.</param>
        /// <param name="exito">true = verde (éxito), false = rojo (error/aviso).</param>
        public void Show(string mensaje, bool exito = true)
        {
            Mensaje.Text = mensaje;
            IconWrap.Background = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString(exito ? "#27AE60" : "#E53E3E"));
            Icono.Text = exito ? "\uE73E" : "\uE783";

            Root.Visibility = Visibility.Visible;
            Root.BeginAnimation(OpacityProperty, null);

            var sb = new Storyboard();

            var aparecer = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
            Storyboard.SetTarget(aparecer, Root);
            Storyboard.SetTargetProperty(aparecer, new PropertyPath(OpacityProperty));

            var desvanecer = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(400))
            {
                BeginTime = TimeSpan.FromMilliseconds(2200)
            };
            Storyboard.SetTarget(desvanecer, Root);
            Storyboard.SetTargetProperty(desvanecer, new PropertyPath(OpacityProperty));

            sb.Children.Add(aparecer);
            sb.Children.Add(desvanecer);
            sb.Completed += (s, e) => Root.Visibility = Visibility.Collapsed;
            sb.Begin();
        }
    }
}
