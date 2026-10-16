// ============================================================
//  Proyecto: Mis Finanzas
//  Autoria: Juan Almonacid
//  Copyright (c) 2026 Juan Almonacid. Todos los derechos reservados.
// ============================================================
using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using QRCoder;

namespace MisFinanzas.Modulos
{
    /// <summary>Cuenta: nómina, avisos y reglas, conexión con el celular y versión de la app.</summary>
    public partial class ConfiguracionView : UserControl
    {
        /// <summary>App del celular (PWA). Sin sesión abre la pantalla de entrar o crear la cuenta.</summary>
        public const string EnlaceApp = "https://mis-finanzas-4ea0b.web.app";

        private readonly System.Windows.Threading.DispatcherTimer _relojCelular = new() { Interval = TimeSpan.FromSeconds(30) };

        public ConfiguracionView()
        {
            InitializeComponent();
            DataContext = Actualizaciones.Instancia;

            var preferencias = Preferencias.Actual;
            chkRecordarPagos.IsChecked = preferencias.RecordarPagos;
            chkVencidosAlAbrir.IsChecked = preferencias.MostrarVencidosAlAbrir;
            lblDiasAviso.Text = $"{Preferencias.DiasAviso} días antes de cada vencimiento";

            _relojCelular.Tick += (_, _) => MostrarCelular();
            Loaded += (_, _) =>
            {
                Actualizaciones.Instancia.PropertyChanged += Actualizaciones_Cambio;
                SincronizacionCelular.GastosRecibidos += Celular_Cambio;
                _relojCelular.Start();
                MostrarVersion();
                MostrarCelular();
            };
            Unloaded += (_, _) =>
            {
                Actualizaciones.Instancia.PropertyChanged -= Actualizaciones_Cambio;
                SincronizacionCelular.GastosRecibidos -= Celular_Cambio;
                _relojCelular.Stop();
            };
        }

        // --- Versión ---
        private void Actualizaciones_Cambio(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Dispatcher.BeginInvoke(MostrarVersion);

        // Solo la versión instalada; el aviso y "Descargar" aparecen cuando hay una versión nueva
        // (la app la busca sola al abrir)
        private void MostrarVersion()
        {
            var a = Actualizaciones.Instancia;
            lblEstadoVersion.Text = a.Disponible ? $"· versión {a.VersionNueva} disponible" : string.Empty;
            btnDescargar.Visibility = a.Disponible ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Descargar_Click(object sender, RoutedEventArgs e) => MostrarActualizacion();

        public bool PuedeActualizar => !nomina.EditorAbierto;
        /// <summary>Abre el formulario de "Tu nómina" (desde el lápiz del salario en Ingresos).</summary>
        public void EditarNomina() => nomina.Editar();
        public void MostrarActualizacion()
        {
            if (!PuedeActualizar) { MessageBox.Show("Guarda o cancela la edición de tu nómina antes de actualizar.", "Mis Finanzas"); return; }
            new VentanaActualizacion(Actualizaciones.Instancia) { Owner = Window.GetWindow(this) }.ShowDialog();
        }

        // --- Avisos y reglas ---
        private void Preferencia_Click(object sender, RoutedEventArgs e)
        {
            var preferencias = Preferencias.Actual;
            preferencias.RecordarPagos = chkRecordarPagos.IsChecked == true;
            preferencias.MostrarVencidosAlAbrir = chkVencidosAlAbrir.IsChecked == true;
            try { preferencias.Guardar(); toast.Show("Preferencia guardada"); }
            catch (Exception ex) { MisFinanzas.Controles.Aviso.Mostrar("No se pudo guardar la preferencia: " + ex.Message); }
        }

        // --- Celular ---
        private void Celular_Cambio(int _) => Dispatcher.BeginInvoke(MostrarCelular);

        /// <summary>
        /// Enlace del QR: abre la app en la pantalla de cuenta. Con el PC conectado va a "Entrar" con el correo
        /// escrito (para usar la misma cuenta); si no, a "Crear cuenta".
        /// </summary>
        private static string EnlaceCuenta() => SincronizacionCelular.Conectado
            ? $"{EnlaceApp}/?cuenta=entrar&correo={Uri.EscapeDataString(SincronizacionCelular.Correo ?? string.Empty)}"
            : $"{EnlaceApp}/?cuenta=crear";

        private void MostrarCelular()
        {
            bool conectado = SincronizacionCelular.Conectado;
            pnlConectado.Visibility = conectado ? Visibility.Visible : Visibility.Collapsed;
            pnlConectar.Visibility = conectado ? Visibility.Collapsed : Visibility.Visible;

            var enlace = EnlaceCuenta();
            imgQr.Source = CodigoQr(enlace);
            imgQr.ToolTip = enlace;
            runEnlace.Text = enlace;
            lnkEnlace.ToolTip = enlace;

            if (!conectado) return;
            lblCuentaCelular.Text = "Tu celular";
            lblCuentaCelular.ToolTip = SincronizacionCelular.Correo;
            var ultima = SincronizacionCelular.UltimaSincronizacion;
            lblEstadoCelular.Text = $"{SincronizacionCelular.Correo} · " +
                (SincronizacionCelular.UltimoError is string error ? $"Último intento: {error}"
                 : ultima is DateTime u ? $"Última revisión: {(u.Date == DateTime.Today ? "hoy" : u.ToString("dd/MM"))}, {u:h:mm tt}"
                 : "Esperando la primera revisión");
        }

        /// <summary>Código QR dibujado como vector (nítido a cualquier escala).</summary>
        private static ImageSource CodigoQr(string texto)
        {
            using var generador = new QRCodeGenerator();
            // Corrección baja = menos módulos, más grandes y fáciles de leer; el margen lo pone el borde de la tarjeta
            var matriz = generador.CreateQrCode(texto, QRCodeGenerator.ECCLevel.L).ModuleMatrix;
            const int margen = 4; // zona en blanco que QRCoder agrega a cada lado
            int lado = matriz.Count - 2 * margen;
            var modulos = new GeometryGroup();
            for (int y = 0; y < lado; y++)
                for (int x = 0; x < lado; x++)
                    if (matriz[y + margen][x + margen]) modulos.Children.Add(new RectangleGeometry(new Rect(x, y, 1, 1)));
            var fondo = new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, lado, lado)));
            var dibujo = new DrawingGroup();
            dibujo.Children.Add(fondo);
            dibujo.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0x0F, 0x1B, 0x2D)), null, modulos));
            dibujo.Freeze();
            return new DrawingImage(dibujo);
        }

        private void AbrirEnlaceApp_Click(object sender, RoutedEventArgs e) => AbrirEnlace(EnlaceCuenta());

        private void CopiarEnlace_Click(object sender, RoutedEventArgs e)
        {
            try { Clipboard.SetText(EnlaceCuenta()); toast.Show("Enlace copiado"); }
            catch { toast.Show("No se pudo copiar el enlace"); }
        }

        private async void BtnConectar_Click(object sender, RoutedEventArgs e)
        {
            btnConectar.IsEnabled = false;
            lblErrorCelular.Visibility = Visibility.Collapsed;
            var error = await SincronizacionCelular.ConectarAsync(txtCorreo.Text.Trim(), txtClave.Password, chkCrearCuenta.IsChecked == true);
            btnConectar.IsEnabled = true;
            if (error != null)
            {
                lblErrorCelular.Text = error;
                lblErrorCelular.Visibility = Visibility.Visible;
                return;
            }
            txtClave.Clear();
            MostrarCelular();
            toast.Show("PC conectado. Escanea el código con tu celular para vincularlo.");
            await SincronizacionCelular.SincronizarAsync();
            MostrarCelular();
        }

        private void BtnDesconectar_Click(object sender, RoutedEventArgs e)
        {
            SincronizacionCelular.Desconectar();
            MostrarCelular();
            toast.Show("Celular desconectado de este computador");
        }

        private static void AbrirEnlace(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception ex) { MisFinanzas.Controles.Aviso.Mostrar("No se pudo abrir el enlace: " + ex.Message); }
        }
    }
}
