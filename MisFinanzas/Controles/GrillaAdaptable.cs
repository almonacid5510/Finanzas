// ============================================================
//  Proyecto: Mis Finanzas
//  Autoria: Juan Almonacid
//  Copyright (c) 2026 Juan Almonacid. Todos los derechos reservados.
// ============================================================
using System;
using System.Windows;
using System.Windows.Controls;

namespace MisFinanzas.Controles
{
    /// <summary>
    /// Grilla de columnas iguales que pasa a menos columnas cuando no hay espacio:
    /// usa tantas columnas como quepan con al menos AnchoMinimo cada una (hasta Columnas).
    /// Cada fila toma la altura de su elemento más alto, así las tarjetas quedan parejas.
    /// </summary>
    public class GrillaAdaptable : Panel
    {
        public static readonly DependencyProperty ColumnasProperty =
            DependencyProperty.Register(nameof(Columnas), typeof(int), typeof(GrillaAdaptable),
                new FrameworkPropertyMetadata(3, FrameworkPropertyMetadataOptions.AffectsMeasure));
        public static readonly DependencyProperty AnchoMinimoProperty =
            DependencyProperty.Register(nameof(AnchoMinimo), typeof(double), typeof(GrillaAdaptable),
                new FrameworkPropertyMetadata(220.0, FrameworkPropertyMetadataOptions.AffectsMeasure));
        public static readonly DependencyProperty EspacioProperty =
            DependencyProperty.Register(nameof(Espacio), typeof(double), typeof(GrillaAdaptable),
                new FrameworkPropertyMetadata(14.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

        /// <summary>Opcional: ancho máximo de cada columna (las columnas no se estiran más; sobra espacio a la derecha).</summary>
        public static readonly DependencyProperty AnchoMaximoProperty =
            DependencyProperty.Register(nameof(AnchoMaximo), typeof(double), typeof(GrillaAdaptable),
                new FrameworkPropertyMetadata(double.PositiveInfinity, FrameworkPropertyMetadataOptions.AffectsMeasure));

        public int Columnas { get => (int)GetValue(ColumnasProperty); set => SetValue(ColumnasProperty, value); }
        public double AnchoMinimo { get => (double)GetValue(AnchoMinimoProperty); set => SetValue(AnchoMinimoProperty, value); }
        public double AnchoMaximo { get => (double)GetValue(AnchoMaximoProperty); set => SetValue(AnchoMaximoProperty, value); }
        public double Espacio { get => (double)GetValue(EspacioProperty); set => SetValue(EspacioProperty, value); }

        private int ColumnasPara(double ancho)
        {
            if (double.IsInfinity(ancho)) return Math.Max(1, Columnas);
            int cabe = (int)Math.Floor((ancho + Espacio) / (AnchoMinimo + Espacio));
            return Math.Clamp(cabe, 1, Math.Max(1, Columnas));
        }

        private double AnchoColumna(double ancho, int cols) =>
            double.IsInfinity(ancho) ? AnchoMinimo : Math.Min(AnchoMaximo, Math.Max(0, (ancho - Espacio * (cols - 1)) / cols));

        protected override Size MeasureOverride(Size disponible)
        {
            int cols = ColumnasPara(disponible.Width);
            double anchoCol = AnchoColumna(disponible.Width, cols);
            double alto = 0, altoFila = 0;
            int i = 0;
            foreach (UIElement hijo in InternalChildren)
            {
                if (hijo.Visibility == Visibility.Collapsed) continue;
                hijo.Measure(new Size(anchoCol, double.PositiveInfinity));
                altoFila = Math.Max(altoFila, hijo.DesiredSize.Height);
                if (++i % cols == 0) { alto += altoFila + Espacio; altoFila = 0; }
            }
            alto += i % cols != 0 ? altoFila : (i > 0 ? -Espacio : 0);
            double ancho = double.IsInfinity(disponible.Width) ? cols * anchoCol + (cols - 1) * Espacio : disponible.Width;
            return new Size(ancho, Math.Max(0, alto));
        }

        protected override Size ArrangeOverride(Size final)
        {
            int cols = ColumnasPara(final.Width);
            double anchoCol = AnchoColumna(final.Width, cols);

            // Altura de cada fila = el elemento más alto de la fila
            var visibles = new System.Collections.Generic.List<UIElement>();
            foreach (UIElement h in InternalChildren) if (h.Visibility != Visibility.Collapsed) visibles.Add(h);

            double y = 0;
            for (int inicio = 0; inicio < visibles.Count; inicio += cols)
            {
                double altoFila = 0;
                for (int k = inicio; k < Math.Min(inicio + cols, visibles.Count); k++)
                    altoFila = Math.Max(altoFila, visibles[k].DesiredSize.Height);
                for (int k = inicio; k < Math.Min(inicio + cols, visibles.Count); k++)
                {
                    int col = k - inicio;
                    visibles[k].Arrange(new Rect(col * (anchoCol + Espacio), y, anchoCol, altoFila));
                }
                y += altoFila + Espacio;
            }
            return final;
        }
    }
}
