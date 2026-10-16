using System;
using System.Linq;

namespace MisFinanzas.Modulos
{
    public class InicioRegistro : IReplicable
    {
        public int Id { get; set; }
        /// <summary>Identidad en la nube (igual en el PC y el celular).</summary>
        public string Uid { get; set; } = string.Empty;
        /// <summary>Último cambio (UTC): en la nube gana el más reciente.</summary>
        public DateTime ActualizadaUtc { get; set; }
        public DateTime Fecha { get; set; }

        // Se guarda una vez por base, para que el inicio no avance al cambiar de mes.
        public static DateTime Obtener(AppDbContext db)
        {
            var registro = db.IniciosRegistro.FirstOrDefault();
            if (registro != null) return Periodo.InicioMes(registro.Fecha);

            // Compatibilidad con bases anteriores: conservar los meses con datos existentes.
            var fechas = db.Movimientos.Select(m => m.Fecha).AsEnumerable()
                .Concat(db.GastosDiarios.Select(g => g.Fecha).AsEnumerable())
                .Concat(db.GastosVehiculo.Select(g => g.Fecha).AsEnumerable())
                .Concat(db.AbonosMeta.Select(a => a.Fecha).AsEnumerable())
                .Concat(db.PagosCredito.Select(p => p.Fecha).AsEnumerable())
                .Where(f => f > DateTime.MinValue && f <= DateTime.Today.AddDays(1))
                .Append(DateTime.Today);
            var inicio = fechas.Min();
            db.IniciosRegistro.Add(new InicioRegistro { Fecha = inicio });
            db.SaveChanges();
            return Periodo.InicioMes(inicio);
        }
    }
}
