// ============================================================
//  Proyecto: Mis Finanzas
//  Autoria: Juan Almonacid
//  Copyright (c) 2026 Juan Almonacid. Todos los derechos reservados.
// ============================================================
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

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

    public static class Etiquetas
    {
        public const string Gasto = "Gasto";
        public const string Ingreso = "Ingreso";
        public const string SinCategoria = "Sin categoría";

        private static readonly string[] PorDefectoGasto =
            { "Hogar", "Servicios", "Alimentación", "Transporte", "Médicos", "Impuestos", "Educación", "Entretenimiento", "Otros" };
        private static readonly string[] PorDefectoIngreso =
            { "Freelance", "Arriendo", "Bonos", "Ventas", "Otros" };

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

        // --- Colores de las insignias: fijos para las conocidas, estables por nombre para las propias ---
        private static readonly (string Texto, string Fondo)[] Paleta =
        {
            ("#2B6CB0", "#EBF8FF"), ("#2F855A", "#F0FFF4"), ("#C05621", "#FFFAF0"), ("#6B46C1", "#FAF5FF"),
            ("#C53030", "#FFF5F5"), ("#2C7A7B", "#E6FFFA"), ("#B7791F", "#FFFFF0"), ("#97266D", "#FFF5F7"),
        };

        private static readonly Dictionary<string, int> Fijos = new(StringComparer.CurrentCultureIgnoreCase)
        {
            ["Hogar"] = 0, ["Freelance"] = 1, ["Alimentación"] = 2, ["Impuestos"] = 3,
            ["Médicos"] = 4, ["Servicios"] = 5, ["Transporte"] = 6, ["Entretenimiento"] = 7,
        };

        private static (string Texto, string Fondo) Color(string? nombre)
        {
            if (string.IsNullOrWhiteSpace(nombre) || nombre.Equals("Otros", StringComparison.CurrentCultureIgnoreCase))
                return ("#4A5568", "#EDF2F7");
            if (Fijos.TryGetValue(nombre, out var i)) return Paleta[i];
            int hash = nombre.ToLowerInvariant().Aggregate(17, (h, c) => unchecked(h * 31 + c));
            return Paleta[Math.Abs(hash % Paleta.Length)];
        }

        public static string ColorTexto(string? nombre) => Color(nombre).Texto;
        public static string ColorFondo(string? nombre) => Color(nombre).Fondo;
    }
}
