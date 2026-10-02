// ============================================================
//  Proyecto: Mis Finanzas
//  Autoria: Juan Almonacid
//  Copyright (c) 2026 Juan Almonacid. Todos los derechos reservados.
// ============================================================
using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MisFinanzas
{
    /// <summary>
    /// Utilidades para formatear y parsear valores numéricos de moneda de forma
    /// consistente en toda la app, respetando la cultura actual (separador de miles/decimales).
    /// El formateo (separación de miles) se aplica EN TIEMPO REAL mientras el usuario escribe.
    /// No se fuerzan decimales ".00": solo aparecen si el usuario los escribe.
    /// </summary>
    public static class NumeroHelper
    {
        private static readonly CultureInfo Cultura = CultureInfo.CurrentCulture;
        private static readonly string SepMiles = Cultura.NumberFormat.NumberGroupSeparator;
        private static readonly string SepDecimal = Cultura.NumberFormat.NumberDecimalSeparator;

        // Evita reentrancia cuando reescribimos el texto dentro de TextChanged
        private static bool _formateando;

        public static bool TryParseMoneda(string? texto, out decimal valor)
        {
            valor = 0;
            if (string.IsNullOrWhiteSpace(texto)) return false;

            var limpio = texto.Replace("$", "").Trim();
            return decimal.TryParse(limpio, NumberStyles.Number | NumberStyles.AllowThousands,
                                    Cultura, out valor);
        }

        /// <summary>Formatea con miles y hasta 2 decimales, SIN forzar ".00".</summary>
        public static string FormatearMoneda(decimal valor)
        {
            // "#,##0.##" => miles siempre, decimales solo si existen
            return valor.ToString("#,##0.##", Cultura);
        }

        // ==================== PROPIEDAD ADJUNTA ====================
        // Uso en XAML: local:NumeroHelper.EsMoneda="True"

        public static readonly DependencyProperty EsMonedaProperty =
            DependencyProperty.RegisterAttached(
                "EsMoneda", typeof(bool), typeof(NumeroHelper),
                new PropertyMetadata(false, OnEsMonedaChanged));

        public static void SetEsMoneda(DependencyObject e, bool v) => e.SetValue(EsMonedaProperty, v);
        public static bool GetEsMoneda(DependencyObject e) => (bool)e.GetValue(EsMonedaProperty);

        private static void OnEsMonedaChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not TextBox tb) return;

            if ((bool)e.NewValue)
            {
                tb.PreviewTextInput += Tb_PreviewTextInput;
                tb.TextChanged += Tb_TextChanged;
                DataObject.AddPastingHandler(tb, Tb_Pasting);
            }
            else
            {
                tb.PreviewTextInput -= Tb_PreviewTextInput;
                tb.TextChanged -= Tb_TextChanged;
                DataObject.RemovePastingHandler(tb, Tb_Pasting);
            }
        }

        // Solo permitir dígitos y el separador decimal (un único separador)
        private static void Tb_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            var tb = (TextBox)sender;
            foreach (var ch in e.Text)
            {
                bool esDigito = char.IsDigit(ch);
                bool esDecimal = SepDecimal.IndexOf(ch) >= 0;

                if (!esDigito && !esDecimal) { e.Handled = true; return; }
                // No permitir un segundo separador decimal
                if (esDecimal && tb.Text.Contains(SepDecimal)) { e.Handled = true; return; }
            }
        }

        private static void Tb_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            if (e.DataObject.GetDataPresent(typeof(string)))
            {
                var texto = (string)e.DataObject.GetData(typeof(string))!;
                if (!TryParseMoneda(texto, out _)) e.CancelCommand();
            }
            else
            {
                e.CancelCommand();
            }
        }

        // Formateo EN VIVO: reinserta separadores de miles conservando la posición del cursor
        private static void Tb_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_formateando) return;
            var tb = (TextBox)sender;

            string original = tb.Text;
            if (string.IsNullOrEmpty(original)) return;

            // Separar parte entera y decimal (si el usuario escribió separador decimal)
            int idxSep = original.IndexOf(SepDecimal, StringComparison.Ordinal);
            string parteEntera = idxSep >= 0 ? original.Substring(0, idxSep) : original;
            string parteDecimal = idxSep >= 0 ? original.Substring(idxSep + SepDecimal.Length) : null;

            // Dígitos de la parte entera
            string soloDigitos = new string(parteEntera.Where(char.IsDigit).ToArray());
            if (soloDigitos.Length == 0 && idxSep < 0) return;

            // Limitar decimales a 2 dígitos
            if (parteDecimal != null)
                parteDecimal = new string(parteDecimal.Where(char.IsDigit).Take(2).ToArray());

            // Formatear la parte entera con separadores de miles
            string enteraFormateada = soloDigitos.Length > 0
                ? (long.TryParse(soloDigitos, out var n) ? n.ToString("#,##0", Cultura) : soloDigitos)
                : "0";

            string resultado = enteraFormateada;
            if (idxSep >= 0) // el usuario escribió el separador -> conservarlo
                resultado += SepDecimal + parteDecimal;

            if (resultado == original) return;

            // Conservar posición del cursor contando dígitos a la izquierda del caret
            int caret = tb.SelectionStart;
            int digitosIzquierda = original.Take(caret).Count(char.IsDigit);

            _formateando = true;
            tb.Text = resultado;

            // Recolocar el caret tras la misma cantidad de dígitos
            int nuevoCaret = 0, contados = 0;
            while (nuevoCaret < resultado.Length && contados < digitosIzquierda)
            {
                if (char.IsDigit(resultado[nuevoCaret])) contados++;
                nuevoCaret++;
            }
            tb.SelectionStart = nuevoCaret;
            _formateando = false;
        }
    }
}
