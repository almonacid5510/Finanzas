// ============================================================
//  Proyecto: Mis Finanzas
//  Autoria: Juan Almonacid
//  Copyright (c) 2026 Juan Almonacid. Todos los derechos reservados.
// ============================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MisFinanzas.Modulos
{
    /// <summary>Avisos y reglas que el usuario activa en Cuenta. Se guardan en AppData, junto a la sesión del celular.</summary>
    public sealed class Preferencias
    {
        /// <summary>Días de anticipación del recordatorio de pagos.</summary>
        public const int DiasAviso = 2;

        public bool RecordarPagos { get; set; } = true;
        public bool MostrarVencidosAlAbrir { get; set; } = true;

        private static readonly string Ruta =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MisFinanzas", "preferencias.json");

        public static Preferencias Actual { get; } = Leer();

        private static Preferencias Leer()
        {
            try
            {
                if (File.Exists(Ruta)) return JsonSerializer.Deserialize<Preferencias>(File.ReadAllText(Ruta)) ?? new Preferencias();
            }
            catch { /* archivo dañado: se usan los valores por defecto */ }
            return new Preferencias();
        }

        public void Guardar()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Ruta)!);
            File.WriteAllText(Ruta, JsonSerializer.Serialize(this));
        }

        /// <summary>Cuentas sin pagar que vencen después de hoy y dentro de los próximos días (cruza de mes).</summary>
        public static List<CuentaMes> PagosProximos(AppDbContext db, DateTime hoy, int dias = DiasAviso) =>
            CuentasMensuales.DelMes(db, hoy).Concat(CuentasMensuales.DelMes(db, hoy.AddMonths(1)))
                .Where(c => !c.Pagada && c.Vence > hoy.Date && c.Vence <= hoy.Date.AddDays(dias))
                .OrderBy(c => c.Vence)
                .ToList();
    }
}
