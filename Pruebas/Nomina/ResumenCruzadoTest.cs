using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using MisFinanzas;
using MisFinanzas.Modulos;

/// <summary>
/// Datos variados → resumen que calcula el PC (ResumenCelular) para el mes anterior, el actual y el siguiente,
/// junto con esos datos tal como quedan en la nube. Celular/app/tests/calculo.cjs calcula con el código del
/// celular y debe dar exactamente lo mismo.
/// </summary>
static class ResumenCruzadoTest
{
    public static void Run()
    {
        AppDbContext.RutaBD = Path.Combine(Path.GetTempPath(), "cruzado-" + Guid.NewGuid() + ".db");
        var hoy = DateTime.Today;
        var mes = Periodo.InicioMes(hoy);
        DateTime Dia(DateTime m, int d) => m.AddDays(Math.Min(d, DateTime.DaysInMonth(m.Year, m.Month)) - 1);

        using (var db = new AppDbContext())
        {
            EsquemaBD.Actualizar(db);
            // El registro empezó hace 3 meses (la base nueva lo creó con la fecha de hoy)
            var inicioRegistro = db.IniciosRegistro.FirstOrDefault() ?? db.IniciosRegistro.Add(new InicioRegistro()).Entity;
            inicioRegistro.Fecha = mes.AddMonths(-3).AddDays(4);
            Movimiento Cuenta(string nombre, decimal valor, int dia, string frecuencia = Frecuencias.Mensual, DateTime? desde = null, int intervalo = 1, string mesFin = "", string categoria = "Servicios") =>
                new() { Tipo = CuentasMensuales.TipoGasto, Descripcion = nombre, Venta = valor, DiaPago = dia, Frecuencia = frecuencia, Fecha = desde ?? mes.AddMonths(-3),
                        IntervaloMeses = intervalo, MesFin = mesFin, Categoria = categoria };
            var arriendo = Cuenta("Arriendo", 1200000, 5, categoria: "Hogar");
            var agua = Cuenta("Agua", 40000, 18);
            var buses = Cuenta("Buses", 50000, 15, Frecuencias.Quincenal, categoria: "Transporte");
            var predial = Cuenta("Impuesto predial", 900000, 10, Frecuencias.Anual, desde: mes.AddMonths(1), categoria: "Impuestos");
            var seguro = Cuenta("Seguro", 210000, Math.Max(1, hoy.Day - 1), desde: mes.AddMonths(-2), intervalo: 2);
            var tecno = Cuenta("Revisión técnico", 250000, 28, Frecuencias.Unica, desde: mes);
            var gym = Cuenta("Gimnasio", 90000, 3, mesFin: Periodo.Clave(mes.AddMonths(-1)));
            var celular = Cuenta("Plan celular", 65000, Math.Min(28, hoy.Day + 3), categoria: "");
            db.Movimientos.AddRange(arriendo, agua, buses, predial, seguro, tecno, gym, celular,
                new Movimiento { Tipo = "Ingreso", Descripcion = "Salario Mensual", Venta = 6395000, Fecha = mes.AddMonths(-3) },
                new Movimiento { Tipo = CuentasMensuales.TipoIngresoExtra, Descripcion = "Freelance", Venta = 300000, Fecha = mes.AddMonths(-1), Frecuencia = Frecuencias.Mensual },
                new Movimiento { Tipo = "Inversion", Descripcion = "CDT", Costo = 5000000, Fecha = mes.AddMonths(-2) },
                new Movimiento { Tipo = "Inversion", Descripcion = "Acciones", Costo = 1200000, Fecha = mes.AddMonths(-1) });

            var hipoteca = new Credito { NombreCustom = "Hipotecario", Total = 100000000, Disponible = 72000000, CuotaMensual = 1526000, TasaEA = 12.75, DiaPago = Math.Max(1, hoy.Day - 2), PlazoMeses = 120 };
            var libre = new Credito { NombreCustom = "Libre inversión", Total = 8000000, Disponible = 3100000, CuotaMensual = 420000, TasaEA = 24.5, DiaPago = 0 };
            var visa = new Credito { NombreCustom = "visa", Modalidad = TiposCredito.Tarjeta, Total = 20000000, Disponible = 1482000, TasaEA = 28.9, DiaPago = 12 };
            db.Creditos.AddRange(hipoteca, libre, visa);
            var meta = new MetaAhorro { Nombre = "Viaje", NombreCustom = "Viaje", MontoObjetivo = 6000000, MontoActual = 2000000, PlazoMeses = 12 };
            var kia = new Vehiculo { Nombre = "Kia", Tipo = "Carro", Placa = "ABC123" };
            db.Metas.Add(meta); db.Vehiculos.Add(kia);
            db.SaveChanges();

            db.PagosCuenta.AddRange(
                new PagoCuenta { MovimientoId = arriendo.Id, Mes = Periodo.Clave(mes), Monto = 1250000 },
                new PagoCuenta { MovimientoId = buses.Id, Mes = Periodo.Clave(mes), Monto = 50000, Quincena = 0 },
                new PagoCuenta { MovimientoId = agua.Id, Mes = Periodo.Clave(mes.AddMonths(-1)), Monto = 38000 },
                new PagoCuenta { MovimientoId = arriendo.Id, Mes = Periodo.Clave(mes.AddMonths(-1)), Monto = 1200000 });
            db.PagosCredito.AddRange(
                new PagoCredito { CreditoId = hipoteca.Id, Mes = Periodo.Clave(mes.AddMonths(-1)), Fecha = Dia(mes.AddMonths(-1), 20), Tipo = Amortizacion.TipoCuota, Monto = 1526000, Interes = 720000, Capital = 806000 },
                new PagoCredito { CreditoId = libre.Id, Mes = Periodo.Clave(mes), Fecha = Dia(mes, 2), Tipo = Amortizacion.TipoCuota, Monto = 420000, Interes = 58000, Capital = 362000 });
            db.AbonosMeta.AddRange(new AbonoMeta { MetaId = meta.Id, Fecha = Dia(mes, 1).AddHours(9), Monto = 500000 },
                                   new AbonoMeta { MetaId = meta.Id, Fecha = Dia(mes.AddMonths(-1), 15), Monto = 1500000 });
            var repuestos = new GastoDiario { Fecha = Dia(mes, 1).AddHours(18), Monto = 30000, Categoria = "Vehículos", Descripcion = "Repuestos", VehiculoId = kia.Id, Origen = GastoDiario.OrigenPC };
            db.GastosDiarios.AddRange(repuestos,
                new GastoDiario { Fecha = Dia(mes, 1).AddHours(13), Monto = 20000, Categoria = "Alimentación", Descripcion = "Almuerzo", Origen = GastoDiario.OrigenPC },
                new GastoDiario { Fecha = Dia(mes.AddMonths(-1), 25), Monto = 120000, Categoria = "Alimentación", Descripcion = "Mercado", Origen = GastoDiario.OrigenPC },
                new GastoDiario { Fecha = Dia(mes.AddMonths(-1), 8), Monto = 82000, Categoria = "Alimentación", Descripcion = "Cena", TarjetaId = visa.Id, Cuotas = 1, Origen = GastoDiario.OrigenPC },
                new GastoDiario { Fecha = Dia(mes.AddMonths(-2), 3), Monto = 1400000, Categoria = "Hogar", Descripcion = "Nevera", TarjetaId = visa.Id, Cuotas = 6, Origen = GastoDiario.OrigenPC },
                new GastoDiario { Fecha = Dia(mes, 1).AddHours(20), Monto = 62000, Categoria = "Alimentación", Descripcion = "Hamburguesa", TarjetaId = visa.Id, Cuotas = 1, Origen = GastoDiario.OrigenPC });
            db.SaveChanges();
            db.GastosVehiculo.AddRange(new GastoVehiculo { VehiculoId = kia.Id, Concepto = "SOAT", Valor = 230000, Fecha = Dia(mes, 1) },
                                       new GastoVehiculo { VehiculoId = kia.Id, Concepto = "Repuestos", Valor = 30000, Fecha = repuestos.Fecha, GastoDiarioUid = repuestos.Uid },
                                       new GastoVehiculo { VehiculoId = kia.Id, Concepto = "Compra", Valor = 0, Fecha = mes.AddMonths(-3) });
            db.SaveChanges();
        }

        // Lo que hace el PC al arrancar (listas en memoria que usan el Dashboard y el resumen)
        CreditosView.CargarDatosGlobal();
        using (var db = new AppDbContext())
        {
            VehiculosView.listaVehiculos.Clear();
            foreach (var v in db.Vehiculos.ToList())
            {
                v.Gastos = new System.Collections.ObjectModel.ObservableCollection<GastoVehiculo>(db.GastosVehiculo.Where(g => g.VehiculoId == v.Id));
                VehiculosView.listaVehiculos.Add(v);
            }
            IngresosView.ListaMetas.Clear();
            foreach (var m in db.Metas.ToList()) IngresosView.ListaMetas.Add(m);
        }

        var salida = new JsonObject { ["hoy"] = hoy.ToString("yyyy-MM-dd") };
        var colecciones = new JsonObject();
        using (var db = new AppDbContext())
        {
            var uidDe = ReplicaNube.TraductorAUid(db);
            foreach (var t in ReplicaNube.Tablas)
                colecciones[t.Coleccion] = new JsonArray(t.Consulta(db).AsEnumerable()
                    .Select(r => (JsonNode)new JsonObject { ["id"] = r.Uid, ["fields"] = ReplicaNube.ADocumento(db, r, uidDe) }).ToArray());
            salida["gastos"] = new JsonArray(db.GastosDiarios.AsEnumerable().Select(g => (JsonNode)new JsonObject
            {
                ["id"] = g.Uid, ["fecha"] = new DateTimeOffset(g.Fecha).ToString("o"), ["monto"] = g.Monto, ["categoria"] = g.Categoria,
                ["descripcion"] = g.Descripcion, ["tarjetaId"] = g.TarjetaId, ["cuotas"] = Math.Max(1, g.Cuotas)
            }).ToArray());
        }
        salida["colecciones"] = colecciones;
        var resumenes = new JsonObject();
        foreach (var m in new[] { mes.AddMonths(-1), mes, mes.AddMonths(1) })
            resumenes[Periodo.Clave(m)] = JsonSerializer.SerializeToNode(ResumenCelular.Construir(m));
        salida["resumenes"] = resumenes;

        // Pago de la cuota del mes de cada préstamo (lo que hace Amortizacion.PagarCuota), para el botón Pagado del celular
        var pagos = new JsonObject();
        using (var db = new AppDbContext())
            foreach (var c in db.Creditos.ToList())
            {
                bool pagada = db.PagosCredito.Any(p => p.CreditoId == c.Id && p.Tipo == Amortizacion.TipoCuota && p.Mes == Periodo.MesActual);
                if (c.EsTarjeta || c.Disponible <= 0 || c.CuotaMensual <= 0 || pagada) { pagos[c.Uid] = null; continue; }
                var r = Amortizacion.RepartirCuota(c.Disponible, c.TasaEA, c.CuotaMensual);
                pagos[c.Uid] = new JsonObject
                {
                    ["monto"] = r.Cuota, ["interes"] = r.Interes, ["capital"] = r.Capital, ["saldoNuevo"] = c.Disponible - r.Capital,
                    ["plazoMeses"] = c.PlazoMeses > 0 ? c.PlazoMeses - 1 : c.PlazoMeses
                };
            }
        salida["pagosCuota"] = pagos;

        // Junto a la prueba del celular (Celular/app/tests/fixtures)
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Celular"))) dir = dir.Parent;
        if (dir == null) throw new Exception("No se encontró la carpeta Celular para dejar el resultado de la prueba cruzada");
        var destino = Path.Combine(dir.FullName, "Celular", "app", "tests", "fixtures", "resumen-pc.json");
        Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
        File.WriteAllText(destino, salida.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"OK Resumen del PC para la prueba cruzada del celular ({resumenes.Count} meses)");
    }
}
