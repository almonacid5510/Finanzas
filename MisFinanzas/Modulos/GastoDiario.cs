// ============================================================
//  Proyecto: Mis Finanzas
//  Autoria: Juan Almonacid
//  Copyright (c) 2026 Juan Almonacid. Todos los derechos reservados.
// ============================================================
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
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

        // Gasto directo de un vehículo (SOAT, llantas, repuestos…): también aparece en su historial (VinculoVehiculo)
        public int? VehiculoId { get; set; }

        // Pagado con una tarjeta de crédito (Credito con Modalidad "Tarjeta de crédito"): sube su deuda (VinculoTarjeta)
        public int? TarjetaId { get; set; }

        // A cuántas cuotas se difirió la compra con tarjeta (1 = se paga completa el mes siguiente)
        public int Cuotas { get; set; } = 1;

        [NotMapped] public string CuotasTexto => TarjetaId != null && Cuotas > 1 ? $"{Cuotas} cuotas" : string.Empty;
    }

    /// <summary>
    /// Compras con tarjeta de crédito: un gasto pagado con la tarjeta sube su deuda (baja el cupo disponible).
    /// La compra cuenta como gasto del mes; el pago de la tarjeta solo baja la deuda (no es otro gasto).
    /// </summary>
    public static class VinculoTarjeta
    {
        /// <summary>
        /// Ajusta la deuda de las tarjetas cuando un gasto se crea (antes = null), se edita o se borra (después = null).
        /// Quien llama guarda (SaveChanges). Devuelve true si cambió alguna tarjeta.
        /// </summary>
        public static bool Ajustar(AppDbContext db, int? tarjetaAntes, decimal montoAntes, int? tarjetaDespues, decimal montoDespues)
        {
            bool cambio = false;
            if (tarjetaAntes is int a && db.Creditos.Find(a) is Credito anterior)
            {
                anterior.Disponible = Math.Max(0, anterior.Disponible - montoAntes);
                cambio = true;
            }
            if (tarjetaDespues is int d && db.Creditos.Find(d) is Credito nueva)
            {
                nueva.Disponible += montoDespues;
                cambio = true;
            }
            return cambio;
        }

        /// <summary>Recarga la lista de créditos (pantalla Créditos, Dashboard y celular ven la deuda nueva).</summary>
        public static void RecargarCreditos() => CreditosView.RecargarDeudas();
    }

    /// <summary>
    /// Enlaza un gasto del día a día con el historial de su vehículo: el vehículo tiene un registro
    /// (GastoVehiculo con GastoDiarioUid) que sigue al gasto. En el total del mes cuenta una sola vez.
    /// </summary>
    public static class VinculoVehiculo
    {
        public const string CategoriaVehiculos = "Vehículos";

        /// <summary>Crea, actualiza o quita el registro del vehículo según el gasto. Quien llama guarda (SaveChanges).</summary>
        public static void Sincronizar(AppDbContext db, GastoDiario g)
        {
            var enlazado = db.GastosVehiculo.FirstOrDefault(x => x.GastoDiarioUid == g.Uid);
            if (g.VehiculoId is int id && db.Vehiculos.Any(v => v.Id == id))
            {
                if (enlazado == null)
                {
                    enlazado = new GastoVehiculo { GastoDiarioUid = g.Uid };
                    db.GastosVehiculo.Add(enlazado);
                }
                enlazado.VehiculoId = id;
                enlazado.Concepto = g.Descripcion;
                enlazado.Valor = g.Monto;
                enlazado.Fecha = g.Fecha;
            }
            else if (enlazado != null)
                db.GastosVehiculo.Remove(enlazado);
        }

        public static void Quitar(AppDbContext db, string uidGasto) =>
            db.GastosVehiculo.RemoveRange(db.GastosVehiculo.Where(x => x.GastoDiarioUid == uidGasto));

        /// <summary>Recarga el historial de los vehículos en memoria (pantalla Vehículos y tarjeta del Dashboard).</summary>
        public static void RecargarVehiculos()
        {
            using var db = new AppDbContext();
            foreach (var v in VehiculosView.listaVehiculos)
            {
                var gastos = db.GastosVehiculo.Where(x => x.VehiculoId == v.Id).ToList();
                v.Gastos.Clear();
                foreach (var x in gastos) v.Gastos.Add(x);
                v.NotificarCambioTotal();
            }
        }
    }

    /// <summary>
    /// Gasto registrado, editado o borrado en el PC, pendiente de enviar a la nube para que se vea en el celular
    /// (si no hay internet se envía en la siguiente sincronización). Ver SincronizacionCelular.
    /// </summary>
    public class CambioNube
    {
        public const string Borrar = "Borrar";
        public const string Subir = "Actualizar";   // crea o actualiza el gasto en la nube (el valor se conserva por compatibilidad)

        [Key]
        public int Id { get; set; }
        public string Uid { get; set; } = string.Empty;   // del gasto (= id del documento en la nube)
        public string Accion { get; set; } = Subir;
        public DateTime Fecha { get; set; }
        public decimal Monto { get; set; }
        public string Descripcion { get; set; } = string.Empty;
        public string Categoria { get; set; } = string.Empty;
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

        // ---------- Gasto directo de un vehículo ----------
        // Siempre son del vehículo (aunque no se diga cuál)
        private static readonly string[] DelVehiculo =
        {
            "soat", "tecnomecanica", "tecno", "llanta", "llantas", "rin", "rines", "repuesto", "repuestos", "aceite", "filtro", "filtros",
            "bateria", "freno", "frenos", "pastillas", "taller", "mecanico", "latoneria", "alineacion", "balanceo", "embrague",
            "clutch", "bujia", "bujias", "amortiguador", "amortiguadores", "suspension", "cadena", "kit", "correa", "radiador",
        };
        // Solo si se nombra el vehículo ("impuesto del carro", "seguro de la moto")
        private static readonly string[] DelVehiculoSiSeNombra =
            { "impuesto", "impuestos", "seguro", "multa", "comparendo", "mantenimiento", "revision", "pintura", "arreglo" };
        // Nunca van al vehículo (son gasto de transporte del día a día)
        private static readonly string[] NoDelVehiculo = { "gasolina", "tanqueo", "tanqueada", "tanquear", "peaje", "parqueadero", "parqueo", "lavada", "lavado" };

        /// <summary>
        /// Si la frase es un gasto directo de un vehículo, cuál. Se reconoce por el nombre, la placa o
        /// "carro"/"moto"; si no se nombra y solo hay un vehículo, ese. Devuelve (es gasto de vehículo, id o null si no se sabe cuál).
        /// </summary>
        public static (bool EsDeVehiculo, int? VehiculoId) DetectarVehiculo(string? frase, IEnumerable<Vehiculo> vehiculos)
        {
            var lista = vehiculos.ToList();
            var texto = SinTildes((frase ?? string.Empty).ToLowerInvariant());
            if (lista.Count == 0 || texto.Trim().Length == 0) return (false, null);
            var palabras = Regex.Split(texto, @"[^\p{L}\p{N}]+").Where(p => p.Length > 0).ToHashSet();
            if (NoDelVehiculo.Any(palabras.Contains)) return (false, null);

            // ¿Cuál vehículo nombra? (nombre, placa o tipo)
            var compacto = Regex.Replace(texto, @"\s", "");
            var nombrados = lista.Where(v =>
                (v.Nombre.Trim().Length > 0 && palabras.Contains(SinTildes(v.Nombre.Trim().ToLowerInvariant()))) ||
                (v.Placa.Trim().Length >= 5 && compacto.Contains(SinTildes(v.Placa.Trim().ToLowerInvariant())))).ToList();
            if (nombrados.Count == 0)
            {
                string? tipo = palabras.Contains("moto") ? "moto" : palabras.Overlaps(new[] { "carro", "auto", "camioneta", "vehiculo" }) ? "carro" : null;
                if (tipo != null) nombrados = lista.Where(v => SinTildes(v.Tipo.ToLowerInvariant()) == tipo || tipo == "carro" && palabras.Contains("vehiculo")).ToList();
            }
            bool seNombra = nombrados.Count > 0 || palabras.Overlaps(new[] { "carro", "moto", "auto", "camioneta", "vehiculo" });

            bool esDeVehiculo = DelVehiculo.Any(palabras.Contains) || (seNombra && DelVehiculoSiSeNombra.Any(palabras.Contains));
            if (!esDeVehiculo) return (false, null);
            if (nombrados.Count == 1) return (true, nombrados[0].Id);
            if (nombrados.Count == 0 && lista.Count == 1) return (true, lista[0].Id);
            return (true, null); // es de un vehículo, pero hay que elegir cuál
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
