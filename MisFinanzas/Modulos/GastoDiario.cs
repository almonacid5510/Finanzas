// ============================================================
//  Proyecto: Mis Finanzas
//  Autoria: Juan Almonacid
//  Copyright (c) 2026 Juan Almonacid. Todos los derechos reservados.
// ============================================================
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace MisFinanzas.Modulos
{
    /// <summary>
    /// Gasto del día a día (almuerzo, transporte, un café…): se registra una vez, con su fecha y hora.
    /// Puede venir del PC o, más adelante, del celular a través del buzón en la nube.
    /// </summary>
    public class GastoDiario
    {
        public const string OrigenPC = "PC";
        public const string OrigenCelular = "Celular";

        [Key]
        public int Id { get; set; }

        // Identificador único global: al sincronizar con el celular evita guardar el mismo gasto dos veces
        public string Uid { get; set; } = Guid.NewGuid().ToString("N");

        public DateTime Fecha { get; set; } = DateTime.Now;
        public decimal Monto { get; set; }
        public string Categoria { get; set; } = string.Empty;
        public string Descripcion { get; set; } = string.Empty;
        public string Origen { get; set; } = OrigenPC;

        // Lo que se escribió o dictó tal cual ("gasté 25 mil en almuerzo"), para revisar si algo se entendió mal
        public string TextoOriginal { get; set; } = string.Empty;
    }

    /// <summary>Lo que se entendió de una frase: cualquier parte puede faltar.</summary>
    public class GastoInterpretado
    {
        public decimal? Monto { get; set; }
        public DateTime Fecha { get; set; } = DateTime.Today;
        public string Descripcion { get; set; } = string.Empty;
        public string Categoria { get; set; } = string.Empty;
    }

    /// <summary>
    /// Entiende frases como "gasté 25 mil en almuerzo", "ayer 18 lucas en uber" o "mercado $85.000".
    /// Reglas simples, sin internet: monto, fecha (hoy/ayer/antier), descripción y categoría por palabras clave.
    /// </summary>
    public static class InterpreteGasto
    {
        // "1,5 millones", "2 palos"
        // (los números pegados a letras no cuentan: el "1" de "D1" no es un monto)
        private static readonly Regex Millones = new(@"(?<![\p{L}\d])(\d+(?:[.,]\d+)?)\s*(millones|millón|millon|palos?)\b", RegexOptions.IgnoreCase);
        // "25 mil", "25k", "18 lucas", "30 barras"
        private static readonly Regex Miles = new(@"(?<![\p{L}\d])(\d+(?:[.,]\d+)?)\s*(mil|k|lucas?|barras?)\b", RegexOptions.IgnoreCase);
        // "$25.000", "25000", "25,000"
        private static readonly Regex Numero = new(@"\$?\s?(?<![\p{L}\d.,])(\d{1,3}(?:[.,]\d{3})+|\d+)(?![\p{L}\d])", RegexOptions.IgnoreCase);

        private static readonly HashSet<string> Relleno = new(StringComparer.OrdinalIgnoreCase)
        {
            "gaste", "gasto", "gastamos", "pague", "pago", "compre", "compra", "me", "mi", "en", "de", "del", "por", "para",
            "un", "una", "unos", "unas", "el", "la", "los", "las", "al", "y", "con", "pesos", "peso", "hoy", "ayer", "antier",
            "anteayer", "antes", "valio", "costo", "salio", "fueron", "se", "fue", "que", "lo", "le",
        };

        // Palabra clave (sin tildes) → categoría de gastos de la app
        private static readonly (string Categoria, string[] Palabras)[] Categorias =
        {
            ("Alimentación", new[] { "almuerzo", "almorce", "desayuno", "cena", "comida", "restaurante", "mercado", "supermercado", "pan",
                                     "cafe", "tinto", "onces", "domicilio", "rappi", "hamburguesa", "pizza", "empanada", "fruta", "carne",
                                     "mecato", "gaseosa", "helado", "pollo", "arepa", "jugo", "tienda", "d1", "ara", "exito" }),
            ("Transporte", new[] { "uber", "didi", "taxi", "bus", "buses", "transmilenio", "sitp", "metro", "gasolina", "tanqueo", "tanqueada",
                                   "parqueadero", "parqueo", "peaje", "pasaje", "cabify", "indrive", "indriver", "transporte", "moto" }),
            ("Entretenimiento", new[] { "cine", "netflix", "spotify", "fiesta", "rumba", "cerveza", "cervezas", "trago", "tragos", "concierto",
                                        "juego", "videojuego", "bar", "salida", "disney", "boleta", "boletas", "paseo" }),
            ("Médicos", new[] { "drogueria", "farmacia", "medico", "medicina", "medicamento", "cita", "eps", "dentista", "odontologo", "examen" }),
            ("Educación", new[] { "curso", "libro", "libros", "universidad", "colegio", "matricula", "cuaderno", "clase", "clases" }),
            ("Servicios", new[] { "luz", "energia", "internet", "celular", "recarga", "plan", "recibo", "gas" }),
            ("Hogar", new[] { "arriendo", "aseo", "mueble", "ferreteria", "hogar", "detergente", "lavanderia", "casa" }),
        };

        public static GastoInterpretado Interpretar(string? frase)
        {
            var r = new GastoInterpretado();
            var texto = (frase ?? string.Empty).Trim();
            if (texto.Length == 0) return r;

            // 1. Monto (y se quita de la frase para dejar la descripción)
            var m = Millones.Match(texto);
            if (m.Success && Decimal(m.Groups[1].Value) is decimal mill) r.Monto = mill * 1_000_000m;
            else if ((m = Miles.Match(texto)).Success && Decimal(m.Groups[1].Value) is decimal miles) r.Monto = miles * 1_000m;
            else
            {
                // Sin "mil" ni "millones": si hay varios números ("2 boletas 40000") el monto es el mayor
                m = Numero.Matches(texto).Cast<Match>()
                          .OrderByDescending(x => decimal.TryParse(Regex.Replace(x.Groups[1].Value, "[.,]", ""), out var v) ? v : 0)
                          .FirstOrDefault() ?? Match.Empty;
                if (m.Success && decimal.TryParse(Regex.Replace(m.Groups[1].Value, "[.,]", ""), out var n)) r.Monto = n;
            }
            if (m.Success) texto = texto.Remove(m.Index, m.Length);

            // 2. Fecha
            var sinTildes = SinTildes(texto.ToLowerInvariant());
            if (Regex.IsMatch(sinTildes, @"\b(antier|anteayer|antes de ayer)\b")) r.Fecha = DateTime.Today.AddDays(-2);
            else if (Regex.IsMatch(sinTildes, @"\bayer\b")) r.Fecha = DateTime.Today.AddDays(-1);

            // 3. Descripción: lo que queda sin palabras de relleno
            var palabras = Regex.Split(texto, @"[^\p{L}\p{N}]+")
                                .Where(p => p.Length > 0 && !Relleno.Contains(SinTildes(p.ToLowerInvariant())))
                                .ToList();
            var descripcion = string.Join(" ", palabras);
            r.Descripcion = descripcion.Length == 0 ? string.Empty : char.ToUpper(descripcion[0]) + descripcion[1..];

            // 4. Categoría por palabras clave
            var claves = palabras.Select(p => SinTildes(p.ToLowerInvariant())).ToHashSet();
            r.Categoria = Categorias.FirstOrDefault(c => c.Palabras.Any(claves.Contains)).Categoria ?? string.Empty;
            return r;
        }

        // "1,5" o "1.5" → 1.5
        private static decimal? Decimal(string valor) =>
            decimal.TryParse(valor.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;

        public static string SinTildes(string texto)
        {
            var normal = texto.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(normal.Length);
            foreach (var ch in normal)
                if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }
    }
}
