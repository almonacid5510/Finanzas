// ============================================================
//  Proyecto: Mis Finanzas
//  Autoria: Juan Almonacid
//  Copyright (c) 2026 Juan Almonacid. Todos los derechos reservados.
// ============================================================
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using System.Runtime.CompilerServices;

namespace MisFinanzas.Modulos
{
    public class Credito : INotifyPropertyChanged
    {
        public int Id { get; set; }

        private string _tipo = string.Empty;
        public string Tipo { get => _tipo; set { _tipo = value; OnPropertyChanged(); } }

        private decimal _total;
        public decimal Total { get => _total; set { _total = value; OnPropertyChanged(); } }

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
            set { _cuotaMensual = value; OnPropertyChanged(); }
        }

        private decimal _disponible;
        public decimal Disponible { get => _disponible; set { _disponible = value; OnPropertyChanged(); } }

        private double _tasaEA;
        public double TasaEA { get => _tasaEA; set { _tasaEA = value; OnPropertyChanged(); } }

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
            set { _pagado = value; OnPropertyChanged(); }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}