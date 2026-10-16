using System;
using System.Collections.Generic;
using System.Linq;

namespace MisFinanzas.Modulos
{
    // Salidas efectivas: una cuenta pendiente o una compra con tarjeta no retira dinero todavía.
    public static class FlujoCaja
    {
        public static Dictionary<string, object?> Construir(AppDbContext db, DateTime hasta)
        {
            var limite = hasta.Date.AddDays(1);
            var ultimoMes = Periodo.Clave(hasta);
            var salidas = new Dictionary<string, decimal>();
            void Agregar(string mes, decimal monto)
            {
                if (string.CompareOrdinal(mes, ultimoMes) <= 0)
                    salidas[mes] = salidas.GetValueOrDefault(mes) + monto;
            }
            foreach (var p in db.PagosCuenta.ToList()) Agregar(p.Mes, p.Monto);
            foreach (var p in db.PagosCredito.Where(p => p.Fecha < limite).ToList()) Agregar(Periodo.Clave(p.Fecha), p.Monto);
            foreach (var a in db.AbonosMeta.Where(a => a.Fecha < limite).ToList()) Agregar(Periodo.Clave(a.Fecha), a.Monto);
            var diarios = db.GastosDiarios.Where(g => g.Fecha < limite).ToList();
            foreach (var g in diarios.Where(g => g.TarjetaId == null)) Agregar(Periodo.Clave(g.Fecha), g.Monto);
            foreach (var g in db.GastosVehiculo.Where(g => g.GastoDiarioUid == "" && g.Fecha < limite).ToList())
                Agregar(Periodo.Clave(g.Fecha), g.Valor);
            return new Dictionary<string, object?>
            {
                ["hasta"] = hasta.Date.ToString("yyyy-MM-dd"),
                ["egresosPorMes"] = salidas.ToDictionary(x => x.Key, x => (object?)x.Value),
                // Base para sumar/restar cambios del celular sin duplicarlos al sincronizar.
                ["gastosIncluidos"] = diarios.Where(g => Periodo.Clave(g.Fecha) == ultimoMes)
                    .Select(g => (object?)new Dictionary<string, object?> { ["id"] = g.Uid, ["monto"] = g.TarjetaId == null ? g.Monto : 0m }).ToList()
            };
        }
    }
}
