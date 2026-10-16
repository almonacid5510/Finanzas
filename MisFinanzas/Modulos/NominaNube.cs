using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace MisFinanzas.Modulos
{
    public static partial class SincronizacionCelular
    {
        public static event Action? NominaActualizada;
        private static DateTime Revision(JsonNode d) => DateTime.TryParse(
            d["fields"]?["actualizada"]?["timestampValue"]?.GetValue<string>() ?? d["updateTime"]?.GetValue<string>(),
            CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var f) ? f : DateTime.MinValue;

        private static async Task<JsonNode?> LeerDocumentoNomina(string ruta, string token, HttpClient? cliente = null)
        {
            using var pet = new HttpRequestMessage(HttpMethod.Get, ruta);
            pet.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var resp = await (cliente ?? Http).SendAsync(pet);
            if (resp.StatusCode == HttpStatusCode.NotFound) return null;
            resp.EnsureSuccessStatusCode();
            return JsonNode.Parse(await resp.Content.ReadAsStringAsync());
        }
        private static async Task SubirNomina(string ruta, string token, Dictionary<string, object?> campos, HttpClient? cliente = null)
        {
            using var pet = new HttpRequestMessage(HttpMethod.Patch, ruta) { Content = Cuerpo(campos) };
            pet.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var resp = await (cliente ?? Http).SendAsync(pet);
            resp.EnsureSuccessStatusCode();
        }
        private static async Task SincronizarNominaAsync(string uid, string token, HttpClient? cliente = null)
        {
            bool cambio = false;
            var ruta = $"{Documentos}/usuarios/{uid}/configuracion/nomina";
            var remoto = await LeerDocumentoNomina(ruta, token, cliente);
            using (var db = new AppDbContext())
            {
                var c = db.ConfiguracionesNomina.Find(1);
                if (c?.Pendiente == true && (remoto == null || c.ActualizadaUtc >= Revision(remoto)))
                {
                    await SubirNomina(ruta, token, new()
                    {
                        ["frecuencia"] = c.Frecuencia, ["dias"] = c.Dias, ["diaSemana"] = c.DiaSemana,
                        ["fechaBase"] = c.FechaBase.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        ["montoPorPago"] = c.MontoPorPago, ["montoSegundoPago"] = c.MontoSegundoPago, ["confirmada"] = c.Confirmada, ["actualizada"] = DateTime.SpecifyKind(c.ActualizadaUtc, DateTimeKind.Utc)
                    }, cliente);
                    // No borrar un cambio que el usuario haya hecho mientras se enviaba el anterior.
                    using var confirmar = new AppDbContext();
                    var vigente = confirmar.ConfiguracionesNomina.Find(1);
                    if (vigente?.ActualizadaUtc == c.ActualizadaUtc) { vigente.Pendiente = false; confirmar.SaveChanges(); }
                }
                else if (remoto != null && (c == null || Revision(remoto) > c.ActualizadaUtc))
                {
                    var f = remoto["fields"]!;
                    c ??= new ConfiguracionNomina();
                    c.Frecuencia = LeerTexto(f["frecuencia"]); c.Dias = LeerTexto(f["dias"]);
                    c.DiaSemana = (int)LeerNumero(f["diaSemana"]); c.MontoPorPago = LeerNumero(f["montoPorPago"]);
                    c.MontoSegundoPago = LeerNumero(f["montoSegundoPago"]);
                    c.FechaBase = DateTime.ParseExact(LeerTexto(f["fechaBase"]), "yyyy-MM-dd", CultureInfo.InvariantCulture);
                    c.Confirmada = f["confirmada"]?["booleanValue"]?.GetValue<bool>() == true;
                    c.ActualizadaUtc = Revision(remoto); c.Pendiente = false;
                    Nomina.Validar(c);
                    if (db.ConfiguracionesNomina.Find(1) == null) db.ConfiguracionesNomina.Add(c);
                    Nomina.ActualizarSalario(db, c); // cambio hecho en el celular: también el salario de Ingresos
                    db.SaveChanges(); cambio = true;
                }
            }

            List<IngresoRecibido> pendientes;
            using (var db = new AppDbContext()) pendientes = db.IngresosRecibidos.Where(i => i.Pendiente).ToList();
            foreach (var i in pendientes)
            {
                var url = $"{Documentos}/usuarios/{uid}/ingresosRecibidos/{Uri.EscapeDataString(i.Uid)}";
                var existente = await LeerDocumentoNomina(url, token, cliente);
                if (existente == null || i.ActualizadaUtc >= Revision(existente))
                    await SubirNomina(url, token, new()
                    {
                        ["fecha"] = i.Fecha, ["monto"] = i.Monto, ["concepto"] = i.Concepto,
                        ["inicial"] = i.Inicial, ["borrado"] = i.Borrado, ["actualizada"] = DateTime.SpecifyKind(i.ActualizadaUtc, DateTimeKind.Utc)
                    }, cliente);
                using var confirmar = new AppDbContext();
                var vigente = confirmar.IngresosRecibidos.Find(i.Uid);
                if (vigente?.ActualizadaUtc == i.ActualizadaUtc) { vigente.Pendiente = false; confirmar.SaveChanges(); }
            }
            string pagina = "";
            do
            {
                var docs = await LeerDocumentoNomina($"{Documentos}/usuarios/{uid}/ingresosRecibidos?pageSize=100&pageToken={Uri.EscapeDataString(pagina)}", token, cliente);
                using var db = new AppDbContext();
                foreach (var d in docs?["documents"]?.AsArray() ?? new JsonArray())
                {
                    if (d == null) continue;
                    var id = d["name"]!.GetValue<string>().Split('/').Last();
                    var local = db.IngresosRecibidos.Find(id);
                    if (local?.Pendiente == true || (local != null && local.ActualizadaUtc >= Revision(d))) continue;
                    var f = d["fields"]!;
                    local ??= new IngresoRecibido { Uid = id };
                    local.Fecha = LeerFecha(f["fecha"]); local.Monto = LeerNumero(f["monto"]);
                    local.Concepto = LeerTexto(f["concepto"]); local.Inicial = f["inicial"]?["booleanValue"]?.GetValue<bool>() == true;
                    local.Borrado = f["borrado"]?["booleanValue"]?.GetValue<bool>() == true;
                    local.ActualizadaUtc = Revision(d); local.Pendiente = false;
                    if (db.IngresosRecibidos.Find(id) == null) db.IngresosRecibidos.Add(local);
                    cambio = true;
                }
                db.SaveChanges();
                pagina = docs?["nextPageToken"]?.GetValue<string>() ?? "";
            } while (pagina.Length > 0);
            if (cambio) NominaActualizada?.Invoke();
        }
    }
}
