// ============================================================
//  Proyecto: Mis Finanzas
//  Autoria: Juan Almonacid
//  Copyright (c) 2026 Juan Almonacid. Todos los derechos reservados.
// ============================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MisFinanzas.Modulos
{
    /// <summary>
    /// Resumen de un mes con los mismos números del Dashboard (disponible, vencidas, por pagar, ahorro,
    /// inversiones, deuda y vehículos). El celular lo calcula por su cuenta con los datos de la nube
    /// (Celular/app/src/app/nucleo/calculo.ts); este cálculo es la referencia de la prueba cruzada
    /// (Pruebas/Nomina/ResumenCruzadoTest.cs) que comprueba que los dos dan lo mismo.
    /// Debe llamarse desde el hilo de la interfaz (usa las listas globales de créditos y vehículos).
    /// </summary>
    public static class ResumenCelular
    {
        public static Dictionary<string, object?> Construir(DateTime mes)
        {
            mes = Periodo.InicioMes(mes);
            var hoy = DateTime.Today;
            bool esActual = Periodo.EsMesActual(mes);

            Analisis.Datos datos;
            List<Movimiento> inversiones;
            List<AbonoMeta> abonos;
            List<MetaAhorro> metas;
            List<GastoDiario> diarios;
            Dictionary<string, object?> caja;
            using (var db = new AppDbContext())
            {
                datos = Analisis.Cargar(db);
                caja = FlujoCaja.Construir(db, mes > Periodo.InicioMes(hoy) ? hoy : (esActual ? hoy : mes.AddMonths(1).AddDays(-1)));
                inversiones = db.Movimientos.Where(m => m.Tipo == "Inversion").ToList();
                abonos = db.AbonosMeta.ToList();
                metas = db.Metas.ToList();
                var hasta = mes.AddMonths(1);
                // Incluye lo registrado directamente en Vehículos (categoría "Vehículos")
                diarios = db.GastosDiarios.Where(g => g.Fecha >= mes && g.Fecha < hasta).ToList()
                            .Concat(Analisis.GastosDeVehiculos(db, mes, hasta))
                            .Concat(Analisis.InteresesTarjetas(db, mes)).ToList(); // de las tarjetas solo los intereses son gasto
            }

            if (CreditosView.ListaCreditosPersistente.Count == 0 && datos.Creditos.Count > 0) CreditosView.CargarDatosGlobal();
            var creditos = CreditosView.ListaCreditosPersistente.ToList();

            // --- Lo mismo que calcula el Dashboard ---
            decimal ingresos = Analisis.IngresosDelMes(datos, mes);
            var cuentasDelMes = CuentasMensuales.DelMes(datos.Cuentas, datos.Pagos, mes);
            var cuotas = DashboardView.CuotasDelMes(creditos, datos.PagosCredito, mes);
            decimal gastos = cuentasDelMes.Sum(c => c.Real) + cuotas.Sum(c => c.Cuota) + diarios.Sum(g => g.Monto);
            decimal balance = ingresos - gastos;
            decimal ahorroMes = DashboardView.AbonosDelMes(abonos, mes).Sum(f => f.Monto);

            // Vencidas (solo el mes en curso): se pueden marcar como pagadas desde el celular
            var vencidas = new List<object?>();
            if (esActual)
            {
                foreach (var c in cuentasDelMes.Where(c => !c.Pagada && c.Vence <= hoy).OrderBy(c => c.Vence))
                    vencidas.Add(new Dictionary<string, object?>
                    {
                        ["tipo"] = "cuenta", ["id"] = c.Cuenta.Id, ["mes"] = Periodo.Clave(mes), ["quincena"] = c.Quincena,
                        ["nombre"] = c.Descripcion, ["monto"] = c.Venta,
                        ["detalle"] = c.Vence == hoy ? "Vence hoy" : $"Vencida el {c.Vence:dd/MM}"
                    });
                foreach (var c in creditos.Where(c => c.CuotaVencidaHoy()))
                    vencidas.Add(new Dictionary<string, object?>
                    {
                        ["tipo"] = "credito", ["id"] = c.Id, ["mes"] = Periodo.Clave(mes),
                        ["nombre"] = $"Cuota crédito · {c.NombreCustom}", ["monto"] = c.CuotaEsteMes,
                        ["detalle"] = c.VencimientoEn(hoy) == hoy ? "Vence hoy" : $"Vencida el {c.VencimientoEn(hoy):dd/MM}"
                    });
            }

            var delSiguiente = CuentasMensuales.DelMes(datos.Cuentas, datos.Pagos, mes.AddMonths(1)).Where(c => !c.Pagada).ToList();
            var porPagar = DashboardView.FilasPorPagar(cuentasDelMes, cuotas, delSiguiente, mes)
                .Select(f => (object?)new Dictionary<string, object?>
                {
                    ["dia"] = f.Dia, ["mesCorto"] = f.MesCorto, ["titulo"] = f.Titulo.Replace("Próxima: ", ""),
                    ["detalle"] = f.Detalle, ["monto"] = f.Monto,
                    ["vencida"] = f.ColorDia == "#B42318", ["proxima"] = f.Titulo.StartsWith("Próxima:"), ["credito"] = f.EsCredito
                }).ToList();

            // --- Tarjetas de abajo ---
            decimal ahorro = metas.Sum(m => m.MontoActual);
            string ahorroDetalle = metas.Count switch
            {
                0 => "Sin metas de ahorro",
                1 => $"Meta al {metas[0].Progreso / 100:P0}",
                _ => $"Repartido en {metas.Count} metas"
            };
            decimal deuda = creditos.Sum(c => Math.Max(0, c.Disponible));
            var prestamos = creditos.Where(c => !c.EsTarjeta).ToList(); // el avance es solo de los préstamos
            decimal prestado = prestamos.Sum(c => c.Total);
            var vehiculos = VehiculosView.listaVehiculos.ToList();

            return new Dictionary<string, object?>
            {
                ["mes"] = Periodo.Clave(mes),
                ["inicioRegistro"] = Periodo.Clave(datos.Inicio),
                ["nombre"] = Periodo.Nombre(mes),
                ["ingresos"] = ingresos,
                ["gastos"] = gastos,
                ["ahorroMes"] = ahorroMes,
                ["disponible"] = balance - ahorroMes,
                ["caja"] = caja, // El celular combina estas salidas con los ingresos realmente recibidos.
                ["saludable"] = balance >= 0,
                ["vencidas"] = vencidas,
                ["porPagar"] = porPagar,
                ["ahorro"] = new Dictionary<string, object?> { ["total"] = ahorro, ["detalle"] = ahorroDetalle },
                ["inversiones"] = new Dictionary<string, object?>
                {
                    ["total"] = inversiones.Sum(i => i.Costo),
                    ["detalle"] = inversiones.Count == 0 ? "Sin inversiones" : string.Join(" · ", inversiones.Select(i => i.Descripcion).Take(2))
                },
                ["deuda"] = new Dictionary<string, object?>
                {
                    ["total"] = deuda,
                    ["progreso"] = prestado > 0 ? (double)(prestamos.Sum(c => c.CapitalPagado) / prestado) : 0d
                },
                ["vehiculos"] = new Dictionary<string, object?>
                {
                    ["total"] = vehiculos.Sum(v => v.TotalInvertido),
                    ["detalle"] = vehiculos.Count == 0 ? "Sin vehículos" : string.Join(" · ", vehiculos.Select(v => v.Nombre).Take(2))
                },
                // Tarjetas de crédito: para pagar con ellas desde el celular (y ver su cupo)
                ["listaTarjetas"] = creditos.Where(c => c.EsTarjeta).Select(c => (object?)new Dictionary<string, object?>
                {
                    ["id"] = c.Id, ["nombre"] = c.NombreCustom, ["cupo"] = c.Total, ["deuda"] = c.Disponible, ["disponible"] = c.CupoDisponible,
                    ["tasaEA"] = c.TasaEA // para estimar los intereses al comprar a cuotas
                }).ToList(),
                // Para elegir (o detectar en la frase) el vehículo de un gasto en el celular
                ["listaVehiculos"] = vehiculos.Select(v => (object?)new Dictionary<string, object?>
                {
                    ["id"] = v.Id, ["nombre"] = v.Nombre, ["tipo"] = v.Tipo, ["placa"] = v.Placa
                }).ToList()
            };
        }
    }
}
