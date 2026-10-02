// ============================================================
//  Proyecto: Mis Finanzas
//  Autoria: Juan Almonacid
//  Copyright (c) 2026 Juan Almonacid. Todos los derechos reservados.
// ============================================================
using System;
using System.Collections.Generic;
using System.Linq;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using System.Runtime.CompilerServices;

namespace MisFinanzas.Modulos
{
    public class Credito : INotifyPropertyChanged
    {
        public int Id { get; set; }

        private decimal _total;
        public decimal Total { get => _total; set { _total = value; OnPropertyChanged(); AvisarCalculos(); } }

        private string _nombreCustom = string.Empty;
        public string NombreCustom
        {
            get => _nombreCustom;
            set { _nombreCustom = value; OnPropertyChanged(); }
        }

        private decimal _cuotaMensual;
        public decimal CuotaMensual
        {
            get => _cuotaMensual;
            set { _cuotaMensual = value; OnPropertyChanged(); AvisarCalculos(); }
        }

        // Saldo pendiente del crédito (monto total menos lo ya pagado)
        private decimal _disponible;
        public decimal Disponible { get => _disponible; set { _disponible = value; OnPropertyChanged(); AvisarCalculos(); } }

        private double _tasaEA;
        public double TasaEA { get => _tasaEA; set { _tasaEA = value; OnPropertyChanged(); AvisarCalculos(); } }

        private int _plazoMeses;
        public int PlazoMeses { get => _plazoMeses; set { _plazoMeses = value; OnPropertyChanged(); } }

        // Estado solo de UI: NO se persiste en la base de datos.
        // Así los créditos siempre cargan en modo solo lectura al abrir la app.
        private bool _editando;
        [NotMapped]
        public bool Editando { get => _editando; set { _editando = value; OnPropertyChanged(); } }

        private bool _pagado;
        public bool Pagado
        {
            get => _pagado;
            set { _pagado = value; OnPropertyChanged(); AvisarCalculos(); }
        }

        // Mes ("yyyy-MM") en que se pagó la cuota. Pagado solo vale para ese mes:
        // al cambiar de mes la cuota vuelve a quedar pendiente (ver CreditosView.CargarDatosGlobal).
        public string MesPagado { get; set; } = string.Empty;

        // ===== Cálculos para la pantalla (no se guardan en la BD) =====

        // Pagos registrados (cuotas y abonos), del más reciente al más antiguo
        private List<PagoCredito> _historial = new();
        [NotMapped]
        public List<PagoCredito> Historial
        {
            get => _historial;
            set { _historial = value; OnPropertyChanged(); AvisarCalculos(); }
        }

        // Muestra u oculta el plan de pagos y el historial en la tarjeta
        private bool _verDetalle;
        [NotMapped]
        public bool VerDetalle { get => _verDetalle; set { _verDetalle = value; OnPropertyChanged(); OnPropertyChanged(nameof(Plan)); } }

        [NotMapped] public string NombreMesActual => Periodo.Nombre(DateTime.Today);
        [NotMapped] public bool Saldado => Disponible <= 0;
        [NotMapped] public bool PuedePagar => !Pagado && !Saldado && CuotaMensual > 0;
        // La casilla de Cuentas sirve para pagar o para deshacer el pago de este mes
        [NotMapped] public bool PuedeMarcar => Pagado || PuedePagar;
        [NotMapped] public string TasaMensualTexto => $"{Amortizacion.TasaMensual(TasaEA):P3} mes";
        [NotMapped] public decimal CapitalPagado => Math.Max(0, Total - Disponible);
        [NotMapped] public double Progreso => Total > 0 ? Math.Clamp((double)(CapitalPagado / Total), 0, 1) : 0;
        [NotMapped] public string ProgresoTexto => $"{Progreso:P0}";
        [NotMapped] public decimal InteresesPagados => Historial.Sum(p => p.Interes);
        [NotMapped] public decimal TotalPagado => Historial.Sum(p => p.Monto);

        // Cómo se repartiría la próxima cuota
        [NotMapped] public decimal InteresProximo => Amortizacion.RepartirCuota(Disponible, TasaEA, CuotaMensual).Interes;
        [NotMapped] public decimal CapitalProximo => Amortizacion.RepartirCuota(Disponible, TasaEA, CuotaMensual).Capital;

        /// <summary>Lo que cuesta este mes: lo pagado si ya se pagó, si no la cuota (o lo que falte si es la última).</summary>
        [NotMapped]
        public decimal CuotaEsteMes =>
            Pagado ? CuotaPagadaEsteMes?.Monto ?? CuotaMensual
                   : Saldado ? 0 : Amortizacion.RepartirCuota(Disponible, TasaEA, CuotaMensual).Cuota;

        [NotMapped]
        public PagoCredito? CuotaPagadaEsteMes =>
            Historial.FirstOrDefault(p => p.Tipo == Amortizacion.TipoCuota && p.Mes == Periodo.MesActual);

        // Plan de pagos desde la próxima cuota pendiente
        [NotMapped] public List<FilaAmortizacion> Plan => Amortizacion.Plan(this, Amortizacion.ProximoMes(Pagado));
        [NotMapped] public int CuotasRestantes => Plan.Count;
        [NotMapped] public bool CuotaNoCubreIntereses => !Saldado && CuotaMensual <= Amortizacion.InteresDelMes(Disponible, TasaEA);

        [NotMapped]
        public string FinEstimado
        {
            get
            {
                if (Saldado) return "Crédito saldado";
                if (CuotaMensual <= 0) return "Sin cuota registrada";
                if (CuotaNoCubreIntereses) return "La cuota no alcanza a cubrir los intereses";
                var plan = Plan;
                var fin = plan[^1];
                return $"Según la tasa y la cuota terminas en {Periodo.Nombre(fin.Mes)} ({plan.Count} cuotas)";
            }
        }

        [NotMapped] public decimal InteresesPorPagar => Plan.Sum(f => f.Interes);

        private void AvisarCalculos()
        {
            foreach (var n in new[] { nameof(Saldado), nameof(PuedePagar), nameof(PuedeMarcar),nameof(TasaMensualTexto),nameof(CapitalPagado), nameof(Progreso), nameof(ProgresoTexto),
                                      nameof(InteresesPagados), nameof(TotalPagado), nameof(InteresProximo), nameof(CapitalProximo),
                                      nameof(CuotaEsteMes), nameof(CuotaPagadaEsteMes), nameof(CuotasRestantes),
                                      nameof(CuotaNoCubreIntereses), nameof(FinEstimado), nameof(InteresesPorPagar), nameof(Plan) })
                OnPropertyChanged(n);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}