// ============================================================
//  Proyecto: Mis Finanzas
//  Autoria: Juan Almonacid
//  Copyright (c) 2026 Juan Almonacid. Todos los derechos reservados.
// ============================================================
using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace MisFinanzas.Controles
{
    /// <summary>
    /// Aviso modal con el estilo de la app. Los mensajes que empiezan por "Error" se muestran
    /// en rojo como fallo ("Algo salió mal"); el resto, en naranja como dato por revisar.
    /// </summary>
    public partial class Aviso : Window
    {
        private Aviso(string mensaje)
        {
            InitializeComponent();

            bool esError = mensaje.StartsWith("Error", StringComparison.OrdinalIgnoreCase);
            lblMensaje.Text = mensaje;
            if (esError)
            {
                lblTitulo.Text = "Algo salió mal";
                cajaIcono.Background = (Brush)FindResource("RojoSuave");
                icono.Foreground = (Brush)FindResource("Rojo");
            }
        }

        /// <summary>Muestra el aviso sobre la ventana activa de la app y espera a que se cierre.</summary>
        public static void Mostrar(string mensaje)
        {
            var dueno = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                        ?? Application.Current?.MainWindow;
            var aviso = new Aviso(mensaje);
            if (dueno != null && dueno.IsVisible) aviso.Owner = dueno;
            else aviso.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            aviso.ShowDialog();
        }

        private void BtnAceptar_Click(object sender, RoutedEventArgs e) => Close();

        // Se puede arrastrar desde cualquier parte del aviso
        private void Marco_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }
    }
}
