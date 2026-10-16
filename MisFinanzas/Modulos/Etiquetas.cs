// ============================================================
//  Proyecto: Mis Finanzas
//  Autoria: Juan Almonacid
//  Copyright (c) 2026 Juan Almonacid. Todos los derechos reservados.
// ============================================================
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Windows.Controls;

namespace MisFinanzas.Modulos
{
    /// <summary>Categoría o etiqueta creada por el usuario para clasificar gastos o ingresos.</summary>
    public class Etiqueta
    {
        [Key]
        public int Id { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string Tipo { get; set; } = Etiquetas.Gasto; // "Gasto" o "Ingreso"
    }

    /// <summary>
    /// Opción "Nueva" al final de la lista de etiquetas (se dibuja con un lápiz, ver AppTheme).
    /// Su texto es vacío: al elegirla, el campo queda en blanco para escribir la etiqueta nueva.
    /// </summary>
    public sealed class NuevaEtiqueta
    {
        public static readonly NuevaEtiqueta Opcion = new();
        private NuevaEtiqueta() { }
        public override string ToString() => string.Empty;
    }

    public static class Etiquetas
    {
        public const string Gasto = "Gasto";
        public const string Ingreso = "Ingreso";
        public const string SinCategoria = "Sin categoría";

        private static readonly string[] PorDefectoGasto =
            { "Hogar", "Servicios", "Alimentación", "Transporte", "Vehículos", "Médicos", "Impuestos", "Educación", "Entretenimiento", "Otros" };
        private static readonly string[] PorDefectoIngreso =
            { "Freelance", "Bonos", "Ventas", "Otros" };

        /// <summary>Crea las etiquetas por defecto la primera vez (no las vuelve a crear si el usuario ya tiene).</summary>
        public static void Sembrar(AppDbContext db)
        {
            foreach (var (tipo, nombres) in new[] { (Gasto, PorDefectoGasto), (Ingreso, PorDefectoIngreso) })
            {
                if (db.Etiquetas.Any(e => e.Tipo == tipo)) continue;
                db.Etiquetas.AddRange(nombres.Select(n => new Etiqueta { Nombre = n, Tipo = tipo }));
            }
            db.SaveChanges();
        }

        public static List<string> Lista(AppDbContext db, string tipo) =>
            db.Etiquetas.Where(e => e.Tipo == tipo).Select(e => e.Nombre).AsEnumerable()
                        .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();

        /// <summary>Lista para los ComboBox: las etiquetas y, al final, la opción "Nueva".</summary>
        public static List<object> ListaConNueva(AppDbContext db, string tipo) =>
            Lista(db, tipo).Cast<object>().Append(NuevaEtiqueta.Opcion).ToList();

        /// <summary>
        /// Al elegir "Nueva" en el ComboBox (editable) se vacía el texto y queda el cursor
        /// listo para escribir el nombre; la etiqueta se crea al guardar (ver Asegurar).
        /// </summary>
        public static void ActivarOpcionNueva(ComboBox combo)
        {
            combo.SelectionChanged += (s, e) =>
            {
                if (combo.SelectedItem is not NuevaEtiqueta) return;
                // Solo cuando la elige el usuario (no al recargar la lista desde el código)
                bool elegida = combo.IsDropDownOpen || combo.IsKeyboardFocusWithin;
                combo.Dispatcher.BeginInvoke(() =>
                {
                    combo.SelectedItem = null;
                    combo.Text = string.Empty;
                    if (!elegida) return;
                    combo.IsDropDownOpen = false;
                    if (combo.Template.FindName("PART_EditableTextBox", combo) is TextBox caja) caja.Focus();
                    else combo.Focus();
                });
            };
        }

        /// <summary>Guarda la etiqueta si es nueva y devuelve el nombre limpio (o "" si está vacía).</summary>
        public static string Asegurar(AppDbContext db, string? nombre, string tipo)
        {
            var limpio = (nombre ?? string.Empty).Trim();
            if (limpio.Length == 0) return string.Empty;

            var existente = db.Etiquetas.Where(e => e.Tipo == tipo).AsEnumerable()
                              .FirstOrDefault(e => string.Equals(e.Nombre, limpio, StringComparison.CurrentCultureIgnoreCase));
            if (existente != null) return existente.Nombre;

            db.Etiquetas.Add(new Etiqueta { Nombre = limpio, Tipo = tipo });
            db.SaveChanges();
            return limpio;
        }

        public static string Mostrar(string? nombre) => string.IsNullOrWhiteSpace(nombre) ? SinCategoria : nombre;

        // --- Colores: fijos para las conocidas, estables por nombre para las propias ---
        // Texto (sobre Fondo, contraste ≥ 4.5:1), Fondo de la insignia y Barra para gráficos
        private static readonly (string Texto, string Fondo, string Barra)[] Paleta =
        {
            ("#1D4FB8", "#EAF0FD", "#1D5BD8"), ("#075E46", "#E7F5EF", "#0B7A5C"), ("#93370D", "#FEF6E7", "#E08A1E"),
            ("#5B34B5", "#F5F3FF", "#6941C6"), ("#B42318", "#FEF3F2", "#D92D20"), ("#0E6E6E", "#E6F6F6", "#14928F"),
            ("#854A0E", "#FEF7C3", "#CA8504"), ("#9E165F", "#FDF2FA", "#DD2590"),
        };

        private static readonly Dictionary<string, int> Fijos = new(StringComparer.CurrentCultureIgnoreCase)
        {
            ["Hogar"] = 0, ["Freelance"] = 1, ["Alimentación"] = 2, ["Impuestos"] = 3, ["Créditos"] = 3,
            ["Médicos"] = 4, ["Servicios"] = 5, ["Transporte"] = 6, ["Entretenimiento"] = 7,
        };

        private static (string Texto, string Fondo, string Barra) Color(string? nombre)
        {
            if (string.IsNullOrWhiteSpace(nombre) || nombre.Equals("Otros", StringComparison.CurrentCultureIgnoreCase))
                return ("#4B5565", "#EEF1F5", "#8A94A3");
            if (Fijos.TryGetValue(nombre, out var i)) return Paleta[i];
            int hash = nombre.ToLowerInvariant().Aggregate(17, (h, c) => unchecked(h * 31 + c));
            return Paleta[Math.Abs(hash % Paleta.Length)];
        }

        public static string ColorTexto(string? nombre) => Color(nombre).Texto;
        public static string ColorFondo(string? nombre) => Color(nombre).Fondo;
        public static string ColorBarra(string? nombre) => Color(nombre).Barra;
        /// <summary>Borde suave de la insignia (el color de la barra al 35 %) para que se vea sobre filas de color.</summary>
        public static string ColorBorde(string? nombre) => "#59" + Color(nombre).Barra.TrimStart('#');
    }
}
