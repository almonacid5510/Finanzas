// ============================================================
//  Proyecto: Mis Finanzas
//  Autoria: Juan Almonacid
//  Copyright (c) 2026 Juan Almonacid. Todos los derechos reservados.
// ============================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace MisFinanzas
{
    /// <summary>
    /// Tema de colores (Claro, Oscuro o Rosado), elegido en Cuenta y aplicado al arrancar.
    /// - Los colores con nombre de AppTheme.xaml tienen su paleta propia en cada tema.
    /// - Los colores fijos de las pantallas (recursos "T_F_xxxxxx" de fondo/borde y "T_T_xxxxxx" de texto) y los que
    ///   arma el código (Fondo/Texto/Acento) se convierten con una regla según su uso: en Oscuro, los fondos claros se
    ///   oscurecen y los textos oscuros se aclaran; en Rosado, los grises se tiñen de rosa y los azules pasan a rosa.
    ///   Verdes, rojos y naranjas (pagado, vencido, por vencer) conservan su sentido en todos los temas.
    /// </summary>
    public static class Tema
    {
        public const string Claro = "Claro", Oscuro = "Oscuro", Rosado = "Rosado";
        public static readonly string[] Todos = { Claro, Oscuro, Rosado };

        /// <summary>Tema con que arrancó la app (cambiarlo requiere reiniciar).</summary>
        public static string Actual { get; private set; } = Claro;

        // Paletas de los colores con nombre (los que no están aquí se convierten con la regla)
        private static readonly Dictionary<string, string> PaletaOscura = new()
        {
            ["Fondo"] = "#0B1220", ["Tarjeta"] = "#141C2B", ["Borde"] = "#263248", ["BordeInput"] = "#34425B",
            ["GrisSuave"] = "#1A2335", ["GrisPista"] = "#7C889A", ["Texto"] = "#E6EAF2", ["Texto2"] = "#9CA8BA",
            ["Oscuro"] = "#162036", ["OscuroActivo"] = "#22314D", ["TextoMenu"] = "#C9D2DE",
            ["Verde"] = "#12A06F", ["VerdeTexto"] = "#6CE9A6", ["VerdeSuave"] = "#0F2E24",
            ["Rojo"] = "#F97066", ["RojoSuave"] = "#3B1714", ["RojoBorde"] = "#7A271A",
            ["Azul"] = "#4C84FF", ["AzulSuave"] = "#172443",
            ["Morado"] = "#A48AFB", ["MoradoSuave"] = "#241B40", ["MoradoTexto"] = "#C4B5FD", ["MoradoBorde"] = "#4C3A8A",
            ["Naranja"] = "#F79009", ["NaranjaTexto"] = "#FEC84B", ["NaranjaSuave"] = "#3A2A10",
            ["BrushFondo"] = "#0B1220", ["BrushTexto"] = "#E6EAF2", ["BrushTextoSuave"] = "#9CA8BA", ["BrushTextoTenue"] = "#7C889A",
            ["BrushBorde"] = "#34425B", ["BrushBordeSuave"] = "#263248", ["BrushInputFondo"] = "#1A2335",
            ["BrushAcento"] = "#4C84FF", ["BrushAcentoHover"] = "#3D74F0", ["BrushAcentoPress"] = "#2F63D6", ["BrushOscuro"] = "#162036",
        };

        private static readonly Dictionary<string, string> PaletaRosada = new()
        {
            ["Fondo"] = "#FFF4F8", ["Tarjeta"] = "#FFFFFF", ["Borde"] = "#F5D9E6", ["BordeInput"] = "#EBC3D7",
            ["GrisSuave"] = "#FFF0F6", ["GrisPista"] = "#B88AA5", ["Texto"] = "#3B1530", ["Texto2"] = "#8A5A78",
            ["Oscuro"] = "#6E1D53", ["OscuroActivo"] = "#8C2B6B", ["TextoMenu"] = "#F7CDE4",
            ["Azul"] = "#D6338A", ["AzulSuave"] = "#FFE3F1",
            ["Morado"] = "#B5338A", ["MoradoSuave"] = "#FCE7F3", ["MoradoTexto"] = "#9D2477", ["MoradoBorde"] = "#F2C4DF",
            ["BrushFondo"] = "#FFF4F8", ["BrushTexto"] = "#3B1530", ["BrushTextoSuave"] = "#8A5A78", ["BrushTextoTenue"] = "#B88AA5",
            ["BrushBorde"] = "#EBC3D7", ["BrushBordeSuave"] = "#F5D9E6", ["BrushInputFondo"] = "#FFFFFF",
            ["BrushAcento"] = "#D6338A", ["BrushAcentoHover"] = "#BF2A79", ["BrushAcentoPress"] = "#A52268", ["BrushOscuro"] = "#6E1D53",
        };

        // Colores del diseño claro que tienen su equivalente en la paleta (así fondos, textos y acentos fijos
        // quedan exactamente iguales a los colores con nombre del tema)
        private static readonly Dictionary<string, string> FondosConocidos = new(StringComparer.OrdinalIgnoreCase)
        {
            ["#FFFFFF"] = "Tarjeta", ["#F2F4F7"] = "Fondo", ["#F4F6F9"] = "GrisSuave", ["#E3E7EE"] = "Borde", ["#D5DBE4"] = "BordeInput",
            ["#E7F5EF"] = "VerdeSuave", ["#FEF3F2"] = "RojoSuave", ["#FECDCA"] = "RojoBorde", ["#EAF0FD"] = "AzulSuave",
            ["#F5F3FF"] = "MoradoSuave", ["#D9CEF5"] = "MoradoBorde", ["#FEF6E7"] = "NaranjaSuave",
        };
        private static readonly Dictionary<string, string> TextosConocidos = new(StringComparer.OrdinalIgnoreCase)
        {
            ["#0F1B2D"] = "Texto", ["#5B6676"] = "Texto2", ["#8A94A3"] = "GrisPista", ["#075E46"] = "VerdeTexto", ["#0B7A5C"] = "Verde",
            ["#B42318"] = "Rojo", ["#1D5BD8"] = "Azul", ["#5B34B5"] = "MoradoTexto", ["#6941C6"] = "Morado", ["#93370D"] = "NaranjaTexto",
            ["#E08A1E"] = "Naranja",
        };

        private static Dictionary<string, string> Paleta => Actual == Oscuro ? PaletaOscura : PaletaRosada;

        private static string? Conocido(Dictionary<string, string> conocidos, string hex) =>
            conocidos.TryGetValue(hex, out var clave) && Paleta.TryGetValue(clave, out var color) ? color : null;

        /// <summary>Aplica el tema a los recursos de la app. Debe llamarse antes de crear las ventanas.</summary>
        public static void Aplicar(ResourceDictionary recursos, string tema)
        {
            Actual = Todos.Contains(tema) ? tema : Claro;
            if (Actual == Claro) return;
            var paleta = Actual == Oscuro ? PaletaOscura : PaletaRosada;
            // Solo se leen las claves de colores: leer un estilo lo crea con los colores de ese momento (los viejos)
            foreach (var diccionario in recursos.MergedDictionaries.Prepend(recursos))
                foreach (var clave in diccionario.Keys.OfType<string>().Where(k => paleta.ContainsKey(k) || k.StartsWith("T_")).ToList())
                {
                    if (diccionario[clave] is not SolidColorBrush pincel) continue;
                    var original = ComoHex(pincel.Color);
                    string nuevo = paleta.TryGetValue(clave, out var fijo) ? fijo
                        : clave.StartsWith("T_F_") ? Fondo(original)
                        : clave.StartsWith("T_T_") ? Texto(original)
                        : Acento(original);
                    // Se cambia el color del mismo pincel: los estilos de AppTheme ya lo tienen tomado al cargar
                    if (!pincel.IsFrozen) pincel.Color = ((SolidColorBrush)Pincel(nuevo)).Color;
                    else diccionario[clave] = Pincel(nuevo);
                }
            if (recursos.MergedDictionaries.FirstOrDefault(d => d.Contains("ColorFondo")) is ResourceDictionary conColor)
                conColor["ColorFondo"] = ((SolidColorBrush)Pincel(paleta["Fondo"])).Color;
        }

        // ---------- Conversión de colores fijos según su uso ----------

        /// <summary>Color de fondo o borde (filas, chips, tarjetas…) en el tema actual.</summary>
        public static string Fondo(string hex)
        {
            if (Actual == Claro || !Leer(hex, out var h, out var s, out var l)) return hex;
            if (Conocido(FondosConocidos, hex) is string conocido) return conocido;
            if (Actual == Oscuro)
                return l >= 0.75 ? Escribir(h, Math.Min(s, 0.5) * 0.85, 0.09 + (1 - l) * 0.55) : hex;
            // Rosado: grises y azules claros se tiñen de rosa
            if (l >= 0.85 && (s < 0.2 || EsAzul(h))) return Escribir(330, Math.Max(s, 0.65), Math.Max(l, 0.95));
            if (EsAzul(h)) return Escribir(330, s, l);
            return hex;
        }

        /// <summary>Color de texto o de ícono en el tema actual.</summary>
        public static string Texto(string hex)
        {
            if (Actual == Claro || !Leer(hex, out var h, out var s, out var l)) return hex;
            if (Conocido(TextosConocidos, hex) is string conocido) return conocido;
            if (Actual == Oscuro)
            {
                if (l > 0.55) return hex;                                  // ya es claro (por ejemplo sobre un botón o el menú)
                return s < 0.25 ? Escribir(h, s, 0.93 - l * 0.45)          // grises: claros
                                : Escribir(h, Math.Min(1, s * 1.05), 0.62 + l * 0.25); // de color: el mismo tono, más claro
            }
            if (s < 0.2 && l < 0.6) return Escribir(320, 0.45, Math.Max(l, 0.15));
            if (EsAzul(h)) return Escribir(330, Math.Max(s, 0.6), l);
            return hex;
        }

        /// <summary>Color de barras, puntos y acentos (no es fondo ni texto).</summary>
        public static string Acento(string hex)
        {
            if (Actual == Claro || !Leer(hex, out var h, out var s, out var l)) return hex;
            if (Conocido(TextosConocidos, hex) is string conocido) return conocido;
            if (Actual == Oscuro)
            {
                if (l >= 0.85) return Fondo(hex);
                if (s < 0.2 && l < 0.3) return Texto(hex);
                return Escribir(h, s, Math.Min(0.7, l + 0.12));
            }
            return EsAzul(h) ? Escribir(330, Math.Max(s, 0.6), l) : s < 0.15 ? Fondo(hex) : hex;
        }

        public static Brush Pincel(string hex)
        {
            var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
            b.Freeze();
            return b;
        }

        private static bool EsAzul(double h) => h >= 190 && h <= 255;

        // ---------- HSL ----------
        private static string ComoHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

        private static bool Leer(string hex, out double h, out double s, out double l)
        {
            h = s = l = 0;
            if (string.IsNullOrEmpty(hex)) return false;
            var t = hex.TrimStart('#');
            if (t.Length == 8) t = t[2..]; // #AARRGGBB: se ignora la transparencia
            if (t.Length != 6 || !int.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) return false;
            double r = ((v >> 16) & 255) / 255.0, g = ((v >> 8) & 255) / 255.0, b = (v & 255) / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            l = (max + min) / 2;
            if (max == min) return true;
            double d = max - min;
            s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            h = max == r ? (g - b) / d + (g < b ? 6 : 0) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
            h *= 60;
            return true;
        }

        private static string Escribir(double h, double s, double l)
        {
            s = Math.Clamp(s, 0, 1); l = Math.Clamp(l, 0, 1);
            double c = (1 - Math.Abs(2 * l - 1)) * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = l - c / 2;
            (double r, double g, double b) = h < 60 ? (c, x, 0d) : h < 120 ? (x, c, 0d) : h < 180 ? (0d, c, x)
                                           : h < 240 ? (0d, x, c) : h < 300 ? (x, 0d, c) : (c, 0d, x);
            int R = (int)Math.Round((r + m) * 255), G = (int)Math.Round((g + m) * 255), B = (int)Math.Round((b + m) * 255);
            return $"#{R:X2}{G:X2}{B:X2}";
        }
    }
}
