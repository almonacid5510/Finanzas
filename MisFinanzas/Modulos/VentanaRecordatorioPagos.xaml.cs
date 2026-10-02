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
        public ObservableCollection<Movimiento> CuentasPendientes { get; set; }

        public VentanaRecordatorioPagos(List<Movimiento> cuentas)
        {
            InitializeComponent();
            CuentasPendientes = new ObservableCollection<Movimiento>(cuentas);
            icCuentasPendientes.ItemsSource = CuentasPendientes;
        }

        private void BtnPagar_Click(object sender, RoutedEventArgs e)
        {
            var boton = sender as Button;
            var cuenta = boton?.CommandParameter as Movimiento;

            if (cuenta != null)
            {
                try
                {
                    using (var db = new AppDbContext())
                    {
                        var cuentaDb = db.Movimientos.Find(cuenta.Id);
                        if (cuentaDb != null)
                        {
                            cuentaDb.EstaPagado = true;
                            db.Movimientos.Update(cuentaDb);
                            db.SaveChanges();
                        }
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
