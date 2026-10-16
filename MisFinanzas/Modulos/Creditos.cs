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
    /// <summary>Tipos de crédito. La tarjeta funciona distinto: tiene cupo, las compras suben la deuda y el pago la baja.</summary>
    public static class TiposCredito
    {
        public const string LibreInversion = "Libre inversión";
        public const string Hipotecario = "Hipotecario";
        public const string Tarjeta = "Tarjeta de crédito";
        public static readonly string[] Todos = { LibreInversion, Hipotecario, Tarjeta };
    }

    public class Credito : INotifyPropertyChanged, IReplicable
    {
        public int Id { get; set; }
        /// <summary>Identidad en la nube (igual en el PC y el celular).</summary>
        public string Uid { get; set; } = string.Empty;
        /// <summary>Último cambio (UTC): en la nube gana el más reciente.</summary>
        public DateTime ActualizadaUtc { get; set; }

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

        // Tipo de crédito (TiposCredito). Vacío = registrado antes de pedirlo (se trata como préstamo)
        // Se llama "Modalidad" porque la columna "Tipo" ya existió y EsquemaBD la retira
        private string _modalidad = string.Empty;
        public string Modalidad
        {
            get => _modalidad;
            set { _modalidad = value ?? string.Empty; OnPropertyChanged(); OnPropertyChanged(nameof(EsTarjeta)); OnPropertyChanged(nameof(ModalidadTexto)); OnPropertyChanged(nameof(EtiquetaTotal)); }
        }

        // ===== Tarjeta de crédito =====
        // Total = cupo asignado; Disponible = lo que se debe; la cuota mensual es 0 (no tiene plan de cuotas fijas).
        // Las compras con la tarjeta son gastos del día a día con TarjetaId (VinculoTarjeta suben la deuda).
        [NotMapped] public bool EsTarjeta => Modalidad == TiposCredito.Tarjeta;
        [NotMapped] public string ModalidadTexto => string.IsNullOrWhiteSpace(Modalidad) ? "Crédito" : Modalidad;
        [NotMapped] public string EtiquetaTotal => EsTarjeta ? "Cupo total" : "Monto original";
        [NotMapped] public decimal CupoDisponible => Math.Max(0, Total - Disponible);
        [NotMapped] public double UsoCupo => Total > 0 ? Math.Clamp((double)(Disponible / Total), 0, 1) : 0;
        [NotMapped] public string UsoCupoTexto => $"{UsoCupo:P0} del cupo usado";
        [NotMapped] public bool CupoAlto => UsoCupo >= 0.8;

        // Compras recientes con la tarjeta (las carga CreditosView)
        private List<GastoDiario> _compras = new();
        [NotMapped]
        public List<GastoDiario> Compras { get => _compras; set { _compras = value; OnPropertyChanged(); OnPropertyChanged(nameof(SinCompras)); } }
        [NotMapped] public bool SinCompras => Compras.Count == 0;

        // Cuota de la tarjeta este mes (suma de las cuotas de sus compras); si ya se pagó, lo pagado
        [NotMapped] public decimal CuotaMesTarjeta { get; private set; }
        [NotMapped] public decimal InteresMesTarjeta { get; private set; }
        [NotMapped] public decimal CapitalMesTarjeta { get; private set; }
        [NotMapped] public bool CuotaTarjetaPagada => EsTarjeta && CuotaPagadaEsteMes != null;
        [NotMapped] public bool PuedePagarCuotaTarjeta => EsTarjeta && !CuotaTarjetaPagada && CuotaMesTarjeta > 0;
        [NotMapped] public bool SinCuotaTarjeta => EsTarjeta && !CuotaTarjetaPagada && CuotaMesTarjeta <= 0;

        /// <summary>Calcula la cuota del mes de la tarjeta con sus compras (todas las que aún tienen cuotas).</summary>
        public void CalcularCuotaTarjeta(IEnumerable<GastoDiario> compras)
        {
            if (!EsTarjeta) return;
            if (CuotaPagadaEsteMes is PagoCredito pagada)
                (CuotaMesTarjeta, InteresMesTarjeta, CapitalMesTarjeta) = (pagada.Monto, pagada.Interes, pagada.Capital);
            else
            {
                var r = Amortizacion.CuotaTarjeta(this, compras, DateTime.Today);
                (CuotaMesTarjeta, InteresMesTarjeta, CapitalMesTarjeta) = (r.Cuota, r.Interes, r.Capital);
            }
            foreach (var n in new[] { nameof(CuotaMesTarjeta), nameof(InteresMesTarjeta), nameof(CapitalMesTarjeta),
                                      nameof(CuotaTarjetaPagada), nameof(PuedePagarCuotaTarjeta), nameof(SinCuotaTarjeta) })
                OnPropertyChanged(n);
        }

        private int _plazoMeses;
        public int PlazoMeses { get => _plazoMeses; set { _plazoMeses = value; OnPropertyChanged(); } }

        // Día del mes en que se paga la cuota (1–31). 0 = sin definir (créditos registrados antes de pedirlo)
        private int _diaPago;
        public int DiaPago
        {
            get => _diaPago;
            set { _diaPago = Math.Clamp(value, 0, 31); OnPropertyChanged(); OnPropertyChanged(nameof(DiaPagoTexto)); }
        }

        [NotMapped] public string DiaPagoTexto => DiaPago > 0 ? $"día {DiaPago} de cada mes" : "sin definir";

        /// <summary>La cuota de este mes ya pasó su día de pago (o es hoy) y no se ha pagado.</summary>
        public bool CuotaVencidaHoy()
        {
            var hoy = DateTime.Today;
            bool pagadaEsteMes = Pagado && MesPagado == Periodo.MesActual;
            return !Saldado && CuotaMensual > 0 && !pagadaEsteMes && VencimientoEn(hoy) is DateTime vence && vence <= hoy;
        }

        /// <summary>Fecha de pago de la cuota en el mes dado (si el mes es más corto, el último día); null sin día.</summary>
        public DateTime? VencimientoEn(DateTime mes)
        {
            if (DiaPago <= 0) return null;
            var inicio = Periodo.InicioMes(mes);
            return inicio.AddDays(Math.Min(DiaPago, DateTime.DaysInMonth(inicio.Year, inicio.Month)) - 1);
        }

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
        private string _mesPagado = string.Empty;
        public string MesPagado
        {
            get => _mesPagado;
            set { _mesPagado=value; OnPropertyChanged(); AvisarCalculos(); }
        }

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
        // El pago pertenece a su mes; un pago anterior no colorea la cuota nueva.
        private EstadoPago EstadoCuota => EstadoPago.Evaluar((Pagado && MesPagado == Periodo.MesActual) || Saldado, VencimientoEn(DateTime.Today), DateTime.Today);
        [NotMapped] public string AccionPago => Pagado ? $"Deshacer pago de {NombreCustom}" : $"Marcar cuota de {NombreCustom} como pagada";
        [NotMapped] public string EstadoTexto => EsTarjeta ? "Tarjeta" : Saldado ? "Saldado" : EstadoCuota.Texto;
        [NotMapped] public string EstadoFondo => EsTarjeta ? MisFinanzas.Tema.Fondo("#EAF0FD") : EstadoCuota.Fondo;
        [NotMapped] public string EstadoColor => EsTarjeta ? MisFinanzas.Tema.Texto("#1D4FB8") : EstadoCuota.Color;
        [NotMapped] public string FondoFila => EsTarjeta ? MisFinanzas.Tema.Fondo("#FFFFFF") : EstadoCuota.FondoFila;
        [NotMapped] public string Acento => EsTarjeta ? MisFinanzas.Tema.Acento("#D0D5DD") : EstadoCuota.Acento;
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
            EsTarjeta ? 0 : // la tarjeta va aparte: las compras ya contaron como gasto; solo sus intereses son gasto nuevo
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
        // Mes en que termina según el plan ("ago 2031")
        [NotMapped]
        public string MesFinCorto
        {
            get
            {
                var plan = Plan;
                return Saldado || plan.Count == 0 || CuotaNoCubreIntereses ? string.Empty : Amortizacion.MesCorto(plan[^1].Mes).ToLower();
            }
        }

        private void AvisarCalculos()
        {
            foreach (var n in new[] { nameof(Saldado), nameof(AccionPago), nameof(EstadoTexto), nameof(EstadoFondo), nameof(EstadoColor), nameof(FondoFila), nameof(Acento), nameof(PuedePagar), nameof(PuedeMarcar),nameof(TasaMensualTexto),nameof(CapitalPagado), nameof(Progreso), nameof(ProgresoTexto),
                                      nameof(InteresesPagados), nameof(TotalPagado), nameof(InteresProximo), nameof(CapitalProximo),
                                      nameof(CuotaEsteMes), nameof(CuotaPagadaEsteMes), nameof(CuotasRestantes),
                                      nameof(CuotaNoCubreIntereses), nameof(FinEstimado), nameof(InteresesPorPagar), nameof(MesFinCorto), nameof(Plan),
                                      nameof(CupoDisponible), nameof(UsoCupo), nameof(UsoCupoTexto), nameof(CupoAlto) })
                OnPropertyChanged(n);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}