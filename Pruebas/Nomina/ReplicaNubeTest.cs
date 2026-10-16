using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using MisFinanzas.Modulos;

/// <summary>La nube como base principal: el PC sube todo, baja lo de otros dispositivos y gana el cambio más reciente.</summary>
static class ReplicaNubeTest
{
    static void Check(bool ok, string texto) { if (!ok) throw new Exception(texto); Console.WriteLine("OK " + texto); }

    public static async Task Run()
    {
        using var nube = new NubeSimulada();
        using var cliente = new HttpClient(nube);
        AppDbContext.RutaBD = Path.Combine(Path.GetTempPath(), "replica-" + Guid.NewGuid() + ".db");
        async Task<bool> Replicar(bool primeraVez) =>
            await (Task<bool>)typeof(SincronizacionCelular).GetMethod("ReplicarAsync", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, new object[] { "prueba", "sin-token-real", primeraVez, cliente })!;

        // Datos creados antes de la nube (sin identidad) y uno nuevo (la recibe al guardarse)
        int idCuenta, idCredito;
        using (var db = new AppDbContext())
        {
            EsquemaBD.Actualizar(db);
            var agua = new Movimiento { Tipo = CuentasMensuales.TipoGasto, Descripcion = "Agua", Venta = 40000, Fecha = new DateTime(2026, 10, 1), DiaPago = 18 };
            var hipoteca = new Credito { NombreCustom = "Hipotecario", Total = 100000000, Disponible = 72000000, CuotaMensual = 1526000, TasaEA = 12.75, DiaPago = 23 };
            db.Movimientos.Add(agua); db.Creditos.Add(hipoteca); db.SaveChanges();
            db.PagosCuenta.Add(new PagoCuenta { MovimientoId = agua.Id, Mes = "2026-10", Monto = 41000 });
            db.SaveChanges();
            idCuenta = agua.Id; idCredito = hipoteca.Id;
            Check(agua.Uid.Length > 0 && db.CambiosReplica.Count() == 3, "Guardar da identidad al registro y lo anota para subir a la nube");
        }

        await Replicar(primeraVez: true);
        string uidAgua;
        using (var db = new AppDbContext())
        {
            uidAgua = db.Movimientos.Find(idCuenta)!.Uid;
            var pago = nube.Doc("pagosCuenta", db.PagosCuenta.Single().Uid)!;
            Check(db.CambiosReplica.Count() == 0 && nube.Doc("movimientos", uidAgua)?["fields"]?["venta"]?["integerValue"]?.GetValue<string>() == "40000",
                  "Primera vez: todo sube a la nube y la cola queda vacía");
            Check(pago["fields"]!["movimientoUid"]!["stringValue"]!.GetValue<string>() == uidAgua && pago["fields"]!["monto"]!["integerValue"]!.GetValue<string>() == "41000",
                  "El pago viaja con la identidad de su cuenta (no con el número interno del PC)");
            var credito = nube.Doc("creditos", db.Creditos.Find(idCredito)!.Uid)!;
            Check(Math.Abs(credito["fields"]!["tasaEA"]!["doubleValue"]!.GetValue<double>() - 12.75) < 1e-9, "Los decimales con centavos no se redondean");
            Check(nube.Doc("estado", "cambios")?["fields"]?["actualizada"] != null, "La subida actualiza la marca de cambios para los demás dispositivos");
        }

        // Otro dispositivo (el celular) cambia el valor del Agua y registra un pago nuevo con el PC apagado
        var despues = DateTime.UtcNow.AddMinutes(1);
        nube.Poner("movimientos", uidAgua, f => { f["venta"] = ReplicaNube.ValorNube(45000m); f["actualizada"] = ReplicaNube.ValorNube(despues); });
        var uidNuevo = Guid.NewGuid().ToString("N");
        nube.Crear("pagosCuenta", uidNuevo, new JsonObject
        {
            ["movimientoUid"] = ReplicaNube.ValorNube(uidAgua), ["mes"] = ReplicaNube.ValorNube("2026-11"), ["monto"] = ReplicaNube.ValorNube(45000m),
            ["quincena"] = ReplicaNube.ValorNube(0), ["actualizada"] = ReplicaNube.ValorNube(despues), ["borrado"] = ReplicaNube.ValorNube(false)
        });
        Check(await Replicar(false), "Al abrir el PC llegan los cambios hechos en otro dispositivo");
        using (var db = new AppDbContext())
        {
            Check(db.Movimientos.Find(idCuenta)!.Venta == 45000 && db.PagosCuenta.Any(p => p.Uid == uidNuevo && p.MovimientoId == idCuenta && p.Mes == "2026-11"),
                  "El cambio y el registro nuevo quedan en el PC, unidos a la cuenta correcta");
            Check(db.CambiosReplica.Count() == 0, "Lo que llega de la nube no se vuelve a subir");
        }

        // Gana el cambio más reciente: lo editado en el PC después no lo pisa una versión vieja de la nube
        using (var db = new AppDbContext()) { db.Movimientos.Find(idCuenta)!.Descripcion = "Agua y alcantarillado"; db.SaveChanges(); }
        nube.Poner("movimientos", uidAgua, f => { f["descripcion"] = ReplicaNube.ValorNube("Agua vieja"); f["actualizada"] = ReplicaNube.ValorNube(despues.AddSeconds(1)); });
        using (var db = new AppDbContext()) { var m = db.Movimientos.Find(idCuenta)!; m.ActualizadaUtc = despues.AddMinutes(5); db.AplicandoNube = true; db.SaveChanges(); }
        await Replicar(false);
        Check(nube.Doc("movimientos", uidAgua)!["fields"]!["descripcion"]!["stringValue"]!.GetValue<string>() == "Agua y alcantarillado",
              "Gana el cambio más reciente aunque llegue otro más viejo de la nube");

        // Borrar en otro dispositivo borra aquí; borrar aquí deja la marca de borrado en la nube
        nube.Poner("pagosCuenta", uidNuevo, f => { f["borrado"] = ReplicaNube.ValorNube(true); f["actualizada"] = ReplicaNube.ValorNube(DateTime.UtcNow.AddMinutes(10)); });
        await Replicar(false);
        using (var db = new AppDbContext()) Check(!db.PagosCuenta.Any(p => p.Uid == uidNuevo), "Lo borrado en el celular se borra en el PC");
        string uidPago;
        using (var db = new AppDbContext()) { var p = db.PagosCuenta.Single(); uidPago = p.Uid; db.PagosCuenta.Remove(p); db.SaveChanges(); }
        await Replicar(false);
        Check(nube.Doc("pagosCuenta", uidPago)!["fields"]!["borrado"]!["booleanValue"]!.GetValue<bool>(), "Lo borrado en el PC queda borrado en la nube para los demás");

        // Un registro cuya cuenta aún no llegó se aplica cuando llega
        var uidCuentaNueva = Guid.NewGuid().ToString("N"); var uidHuerfano = Guid.NewGuid().ToString("N");
        var t1 = DateTime.UtcNow.AddMinutes(20);
        nube.Crear("pagosCuenta", uidHuerfano, new JsonObject
        {
            ["movimientoUid"] = ReplicaNube.ValorNube(uidCuentaNueva), ["mes"] = ReplicaNube.ValorNube("2026-10"), ["monto"] = ReplicaNube.ValorNube(9000m),
            ["actualizada"] = ReplicaNube.ValorNube(t1), ["borrado"] = ReplicaNube.ValorNube(false)
        });
        await Replicar(false);
        using (var db = new AppDbContext()) Check(!db.PagosCuenta.Any(p => p.Uid == uidHuerfano), "Sin su cuenta, el pago espera");
        nube.Crear("movimientos", uidCuentaNueva, new JsonObject
        {
            ["tipo"] = ReplicaNube.ValorNube(CuentasMensuales.TipoGasto), ["descripcion"] = ReplicaNube.ValorNube("Internet"), ["venta"] = ReplicaNube.ValorNube(9000m),
            ["fecha"] = ReplicaNube.ValorNube(new DateTime(2026, 10, 1)), ["actualizada"] = ReplicaNube.ValorNube(t1.AddSeconds(1)), ["borrado"] = ReplicaNube.ValorNube(false)
        });
        await Replicar(false);
        using (var db = new AppDbContext())
        {
            var internet = db.Movimientos.Single(m => m.Uid == uidCuentaNueva);
            Check(db.PagosCuenta.Any(p => p.Uid == uidHuerfano && p.MovimientoId == internet.Id) && internet.Fecha == new DateTime(2026, 10, 1),
                  "Cuando llega la cuenta, el pago que esperaba queda unido a ella (y las fechas no se corren)");
        }

        nube.Fallar = true; bool fallo = false;
        using (var db = new AppDbContext()) { db.Movimientos.Find(idCuenta)!.Venta = 50000; db.SaveChanges(); }
        try { await Replicar(false); } catch (HttpRequestException) { fallo = true; }
        using (var db = new AppDbContext()) Check(fallo && db.CambiosReplica.Count() == 1, "Sin conexión, el cambio espera en la cola para subirse después");
    }

    /// <summary>Firestore en memoria: GET de documentos, :commit con la marca REQUEST_TIME y :runQuery por "actualizada".</summary>
    class NubeSimulada : HttpMessageHandler
    {
        readonly Dictionary<string, JsonObject> docs = new(); // "coleccion/id" → { name, fields }
        public bool Fallar;
        const string Raiz = "projects/mis-finanzas-4ea0b/databases/(default)/documents/usuarios/prueba/";

        public JsonObject? Doc(string coleccion, string id) => docs.GetValueOrDefault($"{coleccion}/{id}");
        public void Poner(string coleccion, string id, Action<JsonObject> cambiar) => cambiar((JsonObject)docs[$"{coleccion}/{id}"]["fields"]!);
        public void Crear(string coleccion, string id, JsonObject campos) => docs[$"{coleccion}/{id}"] = new JsonObject { ["name"] = Raiz + coleccion + "/" + id, ["fields"] = campos };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            if (Fallar) return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            var ruta = Uri.UnescapeDataString(r.RequestUri!.AbsolutePath);
            var ahora = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ", CultureInfo.InvariantCulture);
            if (r.Method == HttpMethod.Post && ruta.EndsWith(":commit"))
            {
                foreach (var w in JsonNode.Parse(await r.Content!.ReadAsStringAsync())!["writes"]!.AsArray())
                {
                    var nombre = w!["update"]!["name"]!.GetValue<string>();
                    var campos = (JsonObject)w["update"]!["fields"]!.DeepClone();
                    if (w["updateTransforms"] != null) campos["actualizada"] = new JsonObject { ["timestampValue"] = ahora };
                    docs[nombre[Raiz.Length..]] = new JsonObject { ["name"] = nombre, ["fields"] = campos, ["updateTime"] = ahora };
                }
                return Json(new JsonObject { ["commitTime"] = ahora });
            }
            if (r.Method == HttpMethod.Post && ruta.EndsWith(":runQuery"))
            {
                var q = JsonNode.Parse(await r.Content!.ReadAsStringAsync())!["structuredQuery"]!;
                var coleccion = q["from"]![0]!["collectionId"]!.GetValue<string>();
                var desde = DateTimeOffset.Parse(q["where"]!["fieldFilter"]!["value"]!["timestampValue"]!.GetValue<string>(), CultureInfo.InvariantCulture).UtcDateTime;
                var filas = docs.Where(d => d.Key.StartsWith(coleccion + "/"))
                    .Select(d => d.Value).Where(d => ReplicaNube.Actualizada(d["fields"]) > desde)
                    .OrderBy(d => ReplicaNube.Actualizada(d["fields"]))
                    .Select(d => (JsonNode)new JsonObject { ["document"] = d.DeepClone() }).ToArray();
                return Json(new JsonArray(filas.Length == 0 ? new JsonNode[] { new JsonObject { ["readTime"] = ahora } } : filas));
            }
            if (r.Method == HttpMethod.Get)
            {
                var clave = ruta[(ruta.IndexOf("/usuarios/prueba/") + "/usuarios/prueba/".Length)..];
                return docs.TryGetValue(clave, out var d) ? Json(d) : new HttpResponseMessage(HttpStatusCode.NotFound);
            }
            return new HttpResponseMessage(HttpStatusCode.BadRequest);
        }
        static HttpResponseMessage Json(JsonNode d) => new(HttpStatusCode.OK) { Content = new StringContent(d.ToJsonString(), Encoding.UTF8, "application/json") };
    }
}
