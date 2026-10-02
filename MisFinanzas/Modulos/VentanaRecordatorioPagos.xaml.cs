// ============================================================
//  Proyecto: Mis Finanzas
//  Autoria: Juan Almonacid
//  Copyright (c) 2026 Juan Almonacid. Todos los derechos reservados.
// ============================================================
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace MisFinanzas.Modulos
{
    public partial class VentanaRecordatorioPagos : Window
    {
        public ObservableCollection<CuentaMes> CuentasPendientes { get; set; }

        public VentanaRecordatorioPagos(List<CuentaMes> cuentas)
        {
            InitializeComponent();
            CuentasPendientes = new ObservableCollection<CuentaMes>(cuentas);
            icCuentasPendientes.ItemsSource = CuentasPendientes;
        }

        private void BtnPagar_Click(object sender, RoutedEventArgs e)
        {
            var boton = sender as Button;
            var cuenta = boton?.CommandParameter as CuentaMes;

            if (cuenta != null)
            {
                try
                {
                    // El pago se registra solo para el mes de este vencimiento
                    using (var db = new AppDbContext())
                    {
                        CuentasMensuales.MarcarPagada(db, cuenta.Cuenta.Id, cuenta.Mes, true, cuenta.Venta);
                    }

                    // Remover visualmente de la lista de pendientes
                    CuentasPendientes.Remove(cuenta);
                    
                    // Si ya no quedan cuentas, cerramos la ventana
                    if (CuentasPendientes.Count == 0)
                    {
                        this.Close();
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error al procesar el pago: {ex.Message}");
                }
            }
        }

        private void BtnCerrar_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        // Permite arrastrar la ventana sin barra de título nativa
        private void Marco_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
                this.DragMove();
        }
    }
}
