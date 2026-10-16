// ============================================================
//  Proyecto: Mis Finanzas
//  Autoria: Juan Almonacid
//  Copyright (c) 2026 Juan Almonacid. Todos los derechos reservados.
// ============================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace MisFinanzas.Modulos
{
    /// <summary>
    /// Trae los gastos registrados desde el celular (app web en Celular/).
    /// El celular los deja en el "buzón" de la nube (Firestore: usuarios/{uid}/gastos) y quedan ahí
    /// el tiempo que haga falta; aquí se descargan los que tienen descargado = false, se guardan en
    /// GastosDiarios (sin duplicar, por Uid = id del documento) y se marcan como descargados.
    /// Se usa la API REST de Firebase, así no hace falta ninguna librería extra.
    /// </summary>
    public static partial class SincronizacionCelular
    {
        // Proyecto de Firebase (los mismos valores de Celular/public/firebase-config.js; son públicos por diseño)
        private const string ApiKey = "AIzaSyD5nujQQBtcMwn-V34b1kyll9taIoq_v0k";
        private const string ProjectId = "mis-finanzas-4ea0b";

        public static bool Configurado => ApiKey.Length > 0 && ProjectId.Length > 0;

        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
        private static readonly SemaphoreSlim UnaALaVez = new(1, 1);

        // Sesión guardada en este PC (cifrada con la cuenta de Windows del usuario)
        private static readonly string RutaSesion =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MisFinanzas", "celular.dat");

        private class Sesion
        {
            public string Correo { get; set; } = string.Empty;
            public string Uid { get; set; } = string.Empty;
            public string RefreshToken { get; set; } = string.Empty;
            public bool GastosPCSubidos { get; set; }   // ya se subieron los gastos registrados en este PC
        }

        private static Sesion? _sesion = LeerSesion();

        public static bool Conectado => _sesion != null;
        public static string? Correo => _sesion?.Correo;
        public static DateTime? UltimaSincronizacion { get; private set; }
        public static string? UltimoError { get; private set; }
        public static string? UltimoErrorNomina { get; private set; }

        /// <summary>Se lanza (en cualquier hilo) cuando llegan gastos nuevos del celular.</summary>
        public static event Action<int>? GastosRecibidos;

        // ---------- Cuenta ----------

        /// <summary>Entra (o crea la cuenta) y guarda la sesión en este PC. Devuelve un mensaje de error o null.</summary>
        public static async Task<string?> ConectarAsync(string correo, string clave, bool crearCuenta)
        {
            if (!Configurado) return "La conexión con el celular aún no está configurada en esta versión de la app.";
            try
            {
                var url = $"https://identitytoolkit.googleapis.com/v1/accounts:{(crearCuenta ? "signUp" : "signInWithPassword")}?key={ApiKey}";
                var cuerpo = JsonSerializer.Serialize(new { email = correo, password = clave, returnSecureToken = true });
                using var resp = await Http.PostAsync(url, new StringContent(cuerpo, Encoding.UTF8, "application/json"));
                var json = JsonNode.Parse(await resp.Content.ReadAsStringAsync());
                if (!resp.IsSuccessStatusCode) return MensajeError(json?["error"]?["message"]?.GetValue<string>());

                _sesion = new Sesion
                {
                    Correo = json!["email"]!.GetValue<string>(),
                    Uid = json["localId"]!.GetValue<string>(),
                    RefreshToken = json["refreshToken"]!.GetValue<string>()
                };
                GuardarSesion(_sesion);
                return null;
            }
            catch (HttpRequestException) { return "Sin conexión a internet. Intenta de nuevo."; }
            catch (TaskCanceledException) { return "El servidor no respondió. Intenta de nuevo."; }
            catch (Exception ex) { return "No se pudo conectar: " + ex.Message; }
        }

        public static void Desconectar()
        {
            _sesion = null;
            UltimaSincronizacion = null;
            try { if (File.Exists(RutaSesion)) File.Delete(RutaSesion); } catch { /* sin permisos: se ignora */ }
        }

        private static string MensajeError(string? codigo) => (codigo ?? string.Empty) switch
        {
            var c when c.StartsWith("INVALID_LOGIN_CREDENTIALS") || c.StartsWith("INVALID_PASSWORD") || c.StartsWith("EMAIL_NOT_FOUND")
                => "Correo o contraseña incorrectos.",
            var c when c.StartsWith("EMAIL_EXISTS") => "Ese correo ya tiene cuenta. Entra con él.",
            var c when c.StartsWith("INVALID_EMAIL") => "El correo no es válido.",
            var c when c.StartsWith("WEAK_PASSWORD") => "La contraseña debe tener al menos 6 caracteres.",
            var c when c.StartsWith("TOO_MANY_ATTEMPTS") => "Demasiados intentos. Espera un momento.",
            _ => "No se pudo conectar. Intenta de nuevo."
        };

        // ---------- Sincronización ----------

        /// <summary>
        /// Sincroniza en las dos direcciones. Nunca lanza excepciones: el error queda en UltimoError.
        /// 1. Aplica lo que se hizo en el celular (marcar pagado, borrar un gasto ya enviado).
        /// 2. Envía lo que se hizo aquí (gastos nuevos, editados o borrados).
        /// 3. Descarga los gastos nuevos del celular.
        /// 4. Publica el resumen de los meses (solo si cambió) para la pestaña "Resumen" del celular.
        /// Devuelve cuántos gastos nuevos llegaron.
        /// </summary>
        public static async Task<int> SincronizarAsync()
        {
            var sesion = _sesion;
            if (!Configurado || sesion == null) return 0;
            if (!await UnaALaVez.WaitAsync(0)) return 0; // ya hay una sincronización en curso

            try
            {
                var token = await TokenAsync(sesion);
                if (token == null) return 0;

                // La primera vez en este PC se suben también los gastos que ya estaban registrados aquí
                if (!sesion.GastosPCSubidos)
                {
                    EncolarGastosDelPC();
                    sesion.GastosPCSubidos = true;
                    GuardarSesion(sesion);
                }

                try { await SincronizarNominaAsync(sesion.Uid, token); UltimoErrorNomina = null; }
                catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    // Una instalación puede actualizarse antes de publicar las reglas nuevas.
                    // Conservar los cambios locales y permitir que los gastos sigan sincronizando.
                    UltimoErrorNomina = "La nómina está guardada en este PC; falta actualizar las reglas del celular para sincronizarla.";
                }
                var acciones = await AplicarAccionesAsync(sesion.Uid, token);
                await EnviarCambiosAsync(sesion.Uid, token);

                var pendientes = await PendientesAsync(sesion.Uid, token);
                int nuevos = GuardarEnBD(pendientes);
                // Solo después de guardarlos aquí se marcan como descargados en la nube
                foreach (var p in pendientes) await MarcarDescargadoAsync(p.Nombre, token);

                // Que la nube tenga exactamente los gastos del PC (al abrir la app y luego cada hora)
                if (DateTime.Now - _ultimaConciliacion > TimeSpan.FromHours(1))
                {
                    await ConciliarAsync(sesion.Uid, token);
                    _ultimaConciliacion = DateTime.Now;
                }

                await PublicarResumenesAsync(sesion.Uid, token);

                UltimaSincronizacion = DateTime.Now;
                UltimoError = null;
                if (nuevos > 0) GastosRecibidos?.Invoke(nuevos);
                if (acciones.Count > 0) AccionesAplicadas?.Invoke(string.Join(" · ", acciones));
                return nuevos;
            }
            catch (HttpRequestException) { UltimoError = "Sin conexión a internet"; return 0; }
            catch (TaskCanceledException) { UltimoError = "El servidor no respondió"; return 0; }
            catch (Exception ex) { UltimoError = ex.Message; return 0; }
            finally { UnaALaVez.Release(); }
        }

        /// <summary>Se lanza cuando se aplicó algo hecho en el celular (texto para mostrar al usuario).</summary>
        public static event Action<string>? AccionesAplicadas;

        /// <summary>Pide una sincronización pronto (por ejemplo, después de marcar algo como pagado aquí).</summary>
        public static void SincronizarPronto() => _ = SincronizarAsync();

        // ---------- Cambios del PC hacia el celular ----------

        /// <summary>
        /// Anota que un gasto se registró, editó o borró en el PC, para reflejarlo en la nube (y así en el celular).
        /// Se guarda en la BD y se envía de inmediato si hay conexión; si no, en la siguiente sincronización.
        /// </summary>
        public static void RegistrarCambio(GastoDiario gasto, bool borrado)
        {
            if (!Conectado) return; // al conectar se suben todos los del PC (EncolarGastosDelPC)
            try
            {
                using (var db = new AppDbContext())
                {
                    Encolar(db, gasto, borrado);
                    db.SaveChanges();
                }
                _ = SincronizarAsync();
            }
            catch (Exception ex) { UltimoError = ex.Message; }
        }

        private static void Encolar(AppDbContext db, GastoDiario gasto, bool borrado)
        {
            // Solo importa el último cambio de cada gasto
            db.CambiosNube.RemoveRange(db.CambiosNube.Where(c => c.Uid == gasto.Uid));
            db.CambiosNube.Add(new CambioNube
            {
                Uid = gasto.Uid,
                Accion = borrado ? CambioNube.Borrar : CambioNube.Subir,
                Fecha = gasto.Fecha,
                Monto = gasto.Monto,
                Descripcion = gasto.Descripcion,
                Categoria = gasto.Categoria
            });
        }

        private static void EncolarGastosDelPC()
        {
            using var db = new AppDbContext();
            foreach (var g in db.GastosDiarios.Where(g => g.Origen == GastoDiario.OrigenPC).ToList()) Encolar(db, g, false);
            db.SaveChanges();
        }

        private static async Task EnviarCambiosAsync(string uid, string token)
        {
            List<CambioNube> cambios;
            using (var db = new AppDbContext()) cambios = db.CambiosNube.ToList();

            foreach (var c in cambios)
            {
                var url = $"{Documentos}/usuarios/{uid}/gastos/{c.Uid}";
                HttpRequestMessage? pet = null;
                if (c.Accion == CambioNube.Borrar)
                    pet = new HttpRequestMessage(HttpMethod.Delete, url);
                else
                {
                    // Se sube como está ahora en el PC; si ya no existe (se borró desde el celular), no se revive
                    GastoDiario? g;
                    string? nombreVehiculo = null, nombreTarjeta = null;
                    using (var db = new AppDbContext())
                    {
                        g = db.GastosDiarios.FirstOrDefault(x => x.Uid == c.Uid);
                        if (g?.VehiculoId is int idV) nombreVehiculo = db.Vehiculos.Where(v => v.Id == idV).Select(v => v.Nombre).FirstOrDefault();
                        if (g?.TarjetaId is int idT) nombreTarjeta = db.Creditos.Where(t => t.Id == idT).Select(t => t.NombreCustom).FirstOrDefault();
                    }
                    if (g != null)
                        pet = new HttpRequestMessage(HttpMethod.Patch, url)
                        {
                            Content = Cuerpo(new Dictionary<string, object?>
                            {
                                ["fecha"] = g.Fecha, ["monto"] = g.Monto, ["descripcion"] = g.Descripcion,
                                ["categoria"] = g.Categoria, ["textoOriginal"] = g.TextoOriginal,
                                ["origen"] = g.Origen, ["descargado"] = true,
                                ["vehiculoId"] = nombreVehiculo != null ? g.VehiculoId : null, ["vehiculo"] = nombreVehiculo ?? string.Empty,
                                ["tarjetaId"] = nombreTarjeta != null ? g.TarjetaId : null, ["tarjeta"] = nombreTarjeta ?? string.Empty,
                                ["cuotas"] = nombreTarjeta != null ? Math.Max(1, g.Cuotas) : 1
                            })
                        };
                }
                if (pet != null)
                {
                    pet.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    using (pet)
                    using (var resp = await Http.SendAsync(pet))
                    {
                        // Hecho, o ya no existe / no es de esta cuenta: se quita de la cola. Otro error: se reintenta luego.
                        var codigo = (int)resp.StatusCode;
                        if (!resp.IsSuccessStatusCode && codigo != 404 && codigo != 403 && codigo != 400) continue;
                    }
                }
                using var dbFin = new AppDbContext();
                var fila = dbFin.CambiosNube.Find(c.Id);
                if (fila != null) { dbFin.CambiosNube.Remove(fila); dbFin.SaveChanges(); }
            }
        }

        // ---------- Conciliación: el PC manda ----------

        private static DateTime _ultimaConciliacion = DateTime.MinValue;

        /// <summary>Hace que la próxima sincronización compare también los gastos del PC con los de la nube.</summary>
        public static void ConciliarPronto() => _ultimaConciliacion = DateTime.MinValue;

        /// <summary>
        /// Deja en la nube exactamente los gastos que tiene el PC (desde el mes anterior, lo que muestra el celular):
        /// - Si está en la nube, ya se había descargado y el PC no lo tiene → se borró aquí: se borra de la nube.
        /// - Si está en el PC y no en la nube → se sube.
        /// Los del celular que aún no llegan al PC (descargado = false) no se tocan.
        /// </summary>
        private static async Task ConciliarAsync(string uid, string token)
        {
            var desde = Periodo.InicioMes(DateTime.Today).AddMonths(-1);
            var consulta = new JsonObject
            {
                ["structuredQuery"] = new JsonObject
                {
                    ["from"] = new JsonArray(new JsonObject { ["collectionId"] = "gastos" }),
                    ["select"] = new JsonObject { ["fields"] = new JsonArray(new JsonObject { ["fieldPath"] = "descargado" }) },
                    ["where"] = new JsonObject
                    {
                        ["fieldFilter"] = new JsonObject
                        {
                            ["field"] = new JsonObject { ["fieldPath"] = "fecha" },
                            ["op"] = "GREATER_THAN_OR_EQUAL",
                            ["value"] = Valor(desde)
                        }
                    }
                }
            };
            using var pet = new HttpRequestMessage(HttpMethod.Post, $"{Documentos}/usuarios/{uid}:runQuery")
            {
                Content = new StringContent(consulta.ToJsonString(), Encoding.UTF8, "application/json")
            };
            pet.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var resp = await Http.SendAsync(pet);
            if (!resp.IsSuccessStatusCode) return;

            // Uid → (nombre del documento, ya descargado)
            var nube = new Dictionary<string, (string Nombre, bool Descargado)>();
            foreach (var fila in JsonNode.Parse(await resp.Content.ReadAsStringAsync())!.AsArray())
            {
                var docu = fila?["document"];
                if (docu == null) continue;
                var nombre = docu["name"]!.GetValue<string>();
                bool descargado = docu["fields"]?["descargado"]?["booleanValue"]?.GetValue<bool>() ?? false;
                nube[nombre[(nombre.LastIndexOf('/') + 1)..]] = (nombre, descargado);
            }

            HashSet<string> locales, enCola;
            using (var db = new AppDbContext())
            {
                locales = db.GastosDiarios.Where(g => g.Fecha >= desde).Select(g => g.Uid).ToHashSet();
                enCola = db.CambiosNube.Select(c => c.Uid).ToHashSet();

                // En el PC y no en la nube: se sube
                var faltantes = locales.Where(u => !nube.ContainsKey(u) && !enCola.Contains(u)).ToList();
                foreach (var g in db.GastosDiarios.Where(g => faltantes.Contains(g.Uid)).ToList()) Encolar(db, g, false);
                db.SaveChanges();
            }

            // En la nube, ya descargado, y el PC no lo tiene: se borró aquí
            foreach (var (u, d) in nube)
            {
                if (!d.Descargado || locales.Contains(u) || enCola.Contains(u)) continue;
                using var borrar = new HttpRequestMessage(HttpMethod.Delete, $"https://firestore.googleapis.com/v1/{d.Nombre}");
                borrar.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var r = await Http.SendAsync(borrar);
            }

            await EnviarCambiosAsync(uid, token);
        }

        // ---------- Acciones hechas en el celular ----------

        /// <summary>
        /// Aplica lo que se pidió desde el celular (usuarios/{uid}/acciones) y borra cada acción ya aplicada.
        /// Tipos: "pagarCuenta" (cuenta vencida), "pagarCuota" (cuota de crédito) y "borrarGasto".
        /// Devuelve textos cortos de lo que se hizo.
        /// </summary>
        private static async Task<List<string>> AplicarAccionesAsync(string uid, string token)
        {
            var hechas = new List<string>();
            using var pet = new HttpRequestMessage(HttpMethod.Get, $"{Documentos}/usuarios/{uid}/acciones?pageSize=100");
            pet.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var resp = await Http.SendAsync(pet);
            if (!resp.IsSuccessStatusCode) return hechas;

            var docs = JsonNode.Parse(await resp.Content.ReadAsStringAsync())?["documents"]?.AsArray();
            if (docs == null) return hechas;

            foreach (var d in docs)
            {
                var nombre = d!["name"]!.GetValue<string>();
                var f = d["fields"]!;
                var tipo = LeerTexto(f["tipo"]);
                try
                {
                    switch (tipo)
                    {
                        case "pagarCuenta":
                        {
                            int id = (int)LeerNumero(f["id"]);
                            var mes = DateTime.ParseExact(LeerTexto(f["mes"]) + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
                            using var db = new AppDbContext();
                            var cuenta = db.Movimientos.Find(id);
                            if (cuenta != null)
                            {
                                var monto = LeerNumero(f["monto"]);
                                CuentasMensuales.MarcarPagada(db, id, mes, true, monto > 0 ? monto : cuenta.Venta, (int)LeerNumero(f["quincena"]));
                                hechas.Add($"\"{cuenta.Descripcion}\" marcado como pagado desde el celular");
                            }
                            break;
                        }
                        case "pagarCuota":
                        {
                            int id = (int)LeerNumero(f["id"]);
                            var credito = CreditosView.ListaCreditosPersistente.FirstOrDefault(c => c.Id == id);
                            // Solo si sigue pendiente (evita pagarla dos veces)
                            if (credito?.EsTarjeta == true)
                            {
                                // Tarjeta: la cuota es la suma de las cuotas de sus compras
                                if (credito.PuedePagarCuotaTarjeta)
                                {
                                    using (var db = new AppDbContext())
                                        Amortizacion.PagarCuotaTarjeta(db, credito.Id, DateTime.Today, credito.InteresMesTarjeta, credito.CapitalMesTarjeta);
                                    CreditosView.Recargar(credito);
                                    hechas.Add($"Cuota de \"{credito.NombreCustom}\" pagada desde el celular");
                                }
                            }
                            else if (credito != null && credito.CuotaPagadaEsteMes == null && CreditosView.RegistrarCuota(credito, true) != null)
                                hechas.Add($"Cuota de \"{credito.NombreCustom}\" pagada desde el celular");
                            break;
                        }
                        case "borrarGasto":
                        {
                            var uidGasto = LeerTexto(f["gasto"]);
                            using var db = new AppDbContext();
                            var g = db.GastosDiarios.FirstOrDefault(x => x.Uid == uidGasto);
                            if (g != null)
                            {
                                db.GastosDiarios.Remove(g);
                                db.CambiosNube.RemoveRange(db.CambiosNube.Where(c => c.Uid == uidGasto));
                                VinculoVehiculo.Quitar(db, uidGasto);
                                VinculoTarjeta.Ajustar(db, g.TarjetaId, g.Monto, null, 0); // se devuelve el cupo
                                db.SaveChanges();
                                if (g.VehiculoId != null) VinculoVehiculo.RecargarVehiculos();
                                if (g.TarjetaId != null) VinculoTarjeta.RecargarCreditos();
                                hechas.Add($"\"{g.Descripcion}\" borrado desde el celular");
                            }
                            break;
                        }
                    }
                }
                catch (Exception ex) { UltimoError = $"No se pudo aplicar una acción del celular: {ex.Message}"; }

                // Aplicada (o inválida): se quita para no repetirla
                using var borrar = new HttpRequestMessage(HttpMethod.Delete, $"https://firestore.googleapis.com/v1/{nombre}");
                borrar.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var respBorrar = await Http.SendAsync(borrar);
            }
            return hechas;
        }

        // ---------- Resumen para la pestaña "Resumen" del celular ----------

        // Último resumen publicado de cada mes en esta sesión: solo se vuelve a escribir si cambió
        private static readonly Dictionary<string, string> UltimoResumen = new();

        private static async Task PublicarResumenesAsync(string uid, string token)
        {
            var actual = Periodo.InicioMes(DateTime.Today);
            DateTime inicio;
            using (var db = new AppDbContext()) inicio = InicioRegistro.Obtener(db);
            foreach (var mes in new[] { actual.AddMonths(-1), actual, actual.AddMonths(1) }.Where(m => m >= inicio))
            {
                var datos = ResumenCelular.Construir(mes);
                var clave = Periodo.Clave(mes);
                var huella = JsonSerializer.Serialize(datos);
                if (UltimoResumen.TryGetValue(clave, out var anterior) && anterior == huella) continue;

                datos["actualizado"] = DateTime.Now;
                using var pet = new HttpRequestMessage(HttpMethod.Patch, $"{Documentos}/usuarios/{uid}/resumen/{clave}") { Content = Cuerpo(datos) };
                pet.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var resp = await Http.SendAsync(pet);
                if (resp.IsSuccessStatusCode) UltimoResumen[clave] = huella;
            }
        }

        // ---------- Formato de Firestore ----------
        private static string Documentos => $"https://firestore.googleapis.com/v1/projects/{ProjectId}/databases/(default)/documents";

        private static StringContent Cuerpo(Dictionary<string, object?> campos) =>
            new(new JsonObject { ["fields"] = Campos(campos) }.ToJsonString(), Encoding.UTF8, "application/json");

        private static JsonObject Campos(Dictionary<string, object?> campos)
        {
            var o = new JsonObject();
            foreach (var (k, v) in campos) o[k] = Valor(v);
            return o;
        }

        // Valor de C# → valor de Firestore (los montos van como enteros: pesos sin centavos)
        private static JsonObject Valor(object? v) => v switch
        {
            null => new JsonObject { ["nullValue"] = null },
            string s => new JsonObject { ["stringValue"] = s },
            bool b => new JsonObject { ["booleanValue"] = b },
            int i => new JsonObject { ["integerValue"] = i.ToString(CultureInfo.InvariantCulture) },
            decimal d => new JsonObject { ["integerValue"] = decimal.Round(d).ToString(CultureInfo.InvariantCulture) },
            double x => new JsonObject { ["doubleValue"] = x },
            DateTime t => new JsonObject { ["timestampValue"] = t.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture) },
            Dictionary<string, object?> m => new JsonObject { ["mapValue"] = new JsonObject { ["fields"] = Campos(m) } },
            IEnumerable<object?> lista => new JsonObject { ["arrayValue"] = new JsonObject { ["values"] = new JsonArray(lista.Select(x => (JsonNode)Valor(x)).ToArray()) } },
            _ => new JsonObject { ["stringValue"] = v.ToString() }
        };

        // Token de acceso (dura 1 hora) a partir del refresh token guardado
        private static async Task<string?> TokenAsync(Sesion sesion)
        {
            var cuerpo = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = sesion.RefreshToken
            });
            using var resp = await Http.PostAsync($"https://securetoken.googleapis.com/v1/token?key={ApiKey}", cuerpo);
            var json = JsonNode.Parse(await resp.Content.ReadAsStringAsync());
            if (!resp.IsSuccessStatusCode)
            {
                // Contraseña cambiada o cuenta eliminada: hay que volver a conectar
                UltimoError = "La sesión del celular expiró. Vuelve a conectar tu cuenta.";
                Desconectar();
                return null;
            }
            var nuevoRefresh = json?["refresh_token"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(nuevoRefresh) && nuevoRefresh != sesion.RefreshToken)
            {
                sesion.RefreshToken = nuevoRefresh;
                GuardarSesion(sesion);
            }
            return json?["id_token"]?.GetValue<string>();
        }

        private record GastoNube(string Nombre, string Uid, DateTime Fecha, decimal Monto, string Descripcion, string Categoria, string Texto, int? VehiculoId, int? TarjetaId, int Cuotas);

        private static async Task<List<GastoNube>> PendientesAsync(string uid, string token)
        {
            var url = $"https://firestore.googleapis.com/v1/projects/{ProjectId}/databases/(default)/documents/usuarios/{uid}:runQuery";
            var consulta = new JsonObject
            {
                ["structuredQuery"] = new JsonObject
                {
                    ["from"] = new JsonArray(new JsonObject { ["collectionId"] = "gastos" }),
                    ["where"] = new JsonObject
                    {
                        ["fieldFilter"] = new JsonObject
                        {
                            ["field"] = new JsonObject { ["fieldPath"] = "descargado" },
                            ["op"] = "EQUAL",
                            ["value"] = new JsonObject { ["booleanValue"] = false }
                        }
                    },
                    ["limit"] = 500
                }
            };
            using var pet = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(consulta.ToJsonString(), Encoding.UTF8, "application/json")
            };
            pet.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var resp = await Http.SendAsync(pet);
            resp.EnsureSuccessStatusCode();

            var lista = new List<GastoNube>();
            foreach (var fila in JsonNode.Parse(await resp.Content.ReadAsStringAsync())!.AsArray())
            {
                var docu = fila?["document"];
                if (docu == null) continue; // respuesta sin resultados: solo trae readTime
                var nombre = docu["name"]!.GetValue<string>();
                var f = docu["fields"]!;
                lista.Add(new GastoNube(
                    nombre,
                    nombre[(nombre.LastIndexOf('/') + 1)..],
                    LeerFecha(f["fecha"]),
                    LeerNumero(f["monto"]),
                    LeerTexto(f["descripcion"]) is { Length: > 0 } d ? d : "Gasto",
                    LeerTexto(f["categoria"]),
                    LeerTexto(f["textoOriginal"]),
                    LeerNumero(f["vehiculoId"]) is > 0 and var v ? (int)v : null,
                    LeerNumero(f["tarjetaId"]) is > 0 and var t ? (int)t : null,
                    (int)Math.Clamp(LeerNumero(f["cuotas"]), 1, 48)));
            }
            return lista;
        }

        // Guarda los que aún no existen (por Uid); devuelve cuántos se agregaron
        private static int GuardarEnBD(List<GastoNube> pendientes)
        {
            if (pendientes.Count == 0) return 0;
            using var db = new AppDbContext();
            var uids = pendientes.Select(p => p.Uid).ToList();
            var existentes = db.GastosDiarios.Where(g => uids.Contains(g.Uid)).Select(g => g.Uid).ToHashSet();

            int nuevos = 0;
            bool conVehiculo = false, conTarjeta = false;
            foreach (var p in pendientes.Where(p => !existentes.Contains(p.Uid) && p.Monto > 0))
            {
                var g = new GastoDiario
                {
                    Uid = p.Uid,
                    Fecha = p.Fecha,
                    Monto = p.Monto,
                    Categoria = Etiquetas.Asegurar(db, p.Categoria, Etiquetas.Gasto),
                    Descripcion = p.Descripcion,
                    Origen = GastoDiario.OrigenCelular,
                    TextoOriginal = p.Texto,
                    VehiculoId = p.VehiculoId,
                    TarjetaId = p.TarjetaId,
                    Cuotas = p.TarjetaId != null ? p.Cuotas : 1
                };
                db.GastosDiarios.Add(g);
                // Pagado con tarjeta de crédito: sube su deuda (baja el cupo)
                if (VinculoTarjeta.Ajustar(db, null, 0, g.TarjetaId, g.Monto)) conTarjeta = true;
                // Gasto directo de un vehículo: también va a su historial
                if (g.VehiculoId != null) { VinculoVehiculo.Sincronizar(db, g); conVehiculo = true; }
                nuevos++;
            }
            db.SaveChanges();
            if (conVehiculo) VinculoVehiculo.RecargarVehiculos();
            if (conTarjeta) VinculoTarjeta.RecargarCreditos();
            return nuevos;
        }

        private static async Task MarcarDescargadoAsync(string nombre, string token)
        {
            var url = $"https://firestore.googleapis.com/v1/{nombre}?updateMask.fieldPaths=descargado";
            var cuerpo = new JsonObject { ["fields"] = new JsonObject { ["descargado"] = new JsonObject { ["booleanValue"] = true } } };
            using var pet = new HttpRequestMessage(HttpMethod.Patch, url)
            {
                Content = new StringContent(cuerpo.ToJsonString(), Encoding.UTF8, "application/json")
            };
            pet.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var resp = await Http.SendAsync(pet);
            // Si falla, se vuelve a intentar en la próxima sincronización (el Uid evita duplicarlo aquí)
        }

        // ---------- Lectura de campos de Firestore ----------
        private static string LeerTexto(JsonNode? campo) => campo?["stringValue"]?.GetValue<string>() ?? string.Empty;

        private static decimal LeerNumero(JsonNode? campo)
        {
            if (campo?["integerValue"]?.GetValue<string>() is string entero && decimal.TryParse(entero, NumberStyles.Number, CultureInfo.InvariantCulture, out var e)) return e;
            if (campo?["doubleValue"] is JsonNode doble) return (decimal)doble.GetValue<double>();
            return 0;
        }

        private static DateTime LeerFecha(JsonNode? campo) =>
            campo?["timestampValue"]?.GetValue<string>() is string t && DateTimeOffset.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
                ? d.LocalDateTime
                : DateTime.Now;

        // ---------- Sesión cifrada en disco ----------
        private static Sesion? LeerSesion()
        {
            try
            {
                if (!File.Exists(RutaSesion)) return null;
                var datos = ProtectedData.Unprotect(File.ReadAllBytes(RutaSesion), null, DataProtectionScope.CurrentUser);
                return JsonSerializer.Deserialize<Sesion>(datos);
            }
            catch { return null; }
        }

        private static void GuardarSesion(Sesion sesion)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RutaSesion)!);
            var datos = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(sesion), null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(RutaSesion, datos);
        }
    }
}
