// ============================================================
//  Proyecto: Mis Finanzas
//  Autoria: Juan Almonacid
//  Copyright (c) 2026 Juan Almonacid. Todos los derechos reservados.
// ============================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace MisFinanzas.Modulos
{
    /// <summary>
    /// La nube como base principal: cuentas, pagos, créditos, metas, vehículos y categorías viven en
    /// usuarios/{uid}/{colección}/{Uid}. El PC sube lo que cambió aquí (cola CambiosReplica) y baja solo lo
    /// que cambió allá desde la última vez (EstadoReplica). Cada escritura actualiza usuarios/{uid}/estado/cambios:
    /// el PC revisa esa marca cada pocos segundos (1 lectura) y solo sincroniza cuando cambió.
    /// </summary>
    public static partial class SincronizacionCelular
    {
        /// <summary>Se lanza (en cualquier hilo) cuando llegaron de la nube cambios que el PC aplicó.</summary>
        public static event Action? DatosNubeActualizados;
        public static string? UltimoErrorNube { get; private set; }

        private static string? _marcaVista; // updateTime de estado/cambios ya procesado
        private const int TamanoLote = 400;   // Firestore admite hasta 500 escrituras por commit

        private static string RaizDocumentos => $"projects/{ProjectId}/databases/(default)/documents";

        /// <summary>
        /// Revisa la marca de cambios de la nube (1 lectura) y sincroniza solo si alguien escribió desde la
        /// última vez. Se llama cada pocos segundos; nunca lanza excepciones.
        /// </summary>
        public static async Task RevisarMarcaAsync()
        {
            var sesion = _sesion;
            if (!Configurado || sesion == null || UnaALaVez.CurrentCount == 0) return;
            try
            {
                var token = await TokenAsync(sesion);
                if (token == null) return;
                var marca = await LeerMarcaAsync(sesion.Uid, token);
                if (marca == null || marca == _marcaVista) return;
                await SincronizarAsync();
            }
            catch { /* sin conexión: se revisa de nuevo en unos segundos */ }
        }

        private static async Task<string?> LeerMarcaAsync(string uid, string token, HttpClient? cliente = null)
        {
            using var pet = new HttpRequestMessage(HttpMethod.Get, $"{Documentos}/usuarios/{uid}/estado/cambios");
            pet.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var resp = await (cliente ?? Http).SendAsync(pet);
            if (resp.StatusCode == HttpStatusCode.NotFound) return string.Empty;
            if (!resp.IsSuccessStatusCode) return null;
            return JsonNode.Parse(await resp.Content.ReadAsStringAsync())?["updateTime"]?.GetValue<string>() ?? string.Empty;
        }

        /// <summary>
        /// Réplica completa con la nube: baja lo que cambió allá, la primera vez encola todo lo del PC,
        /// y sube la cola. Devuelve true si llegaron cambios de la nube.
        /// </summary>
        private static async Task<bool> ReplicarAsync(string uid, string token, bool primeraVez, HttpClient? cliente = null)
        {
            // Lo escrito hasta esta marca queda procesado al bajar; lo posterior la vuelve a cambiar
            var marca = await LeerMarcaAsync(uid, token, cliente);
            bool llegaron = await BajarCambiosAsync(uid, token, cliente);
            if (primeraVez)
                using (var db = new AppDbContext()) ReplicaNube.EncolarTodo(db);
            _marcaSubida = null;
            await SubirCambiosAsync(uid, token, cliente);
            // Si este PC subió algo, la marca nueva es la suya: no hay que volver a bajar por ella
            _marcaVista = _marcaSubida ?? marca ?? _marcaVista;
            return llegaron;
        }

        private static string? _marcaSubida; // commitTime de la última subida de este PC (= la marca que dejó)

        // ---------- Bajar ----------

        private static async Task<bool> BajarCambiosAsync(string uid, string token, HttpClient? cliente)
        {
            bool llegaron = false;
            using var db = new AppDbContext { AplicandoNube = true };
            var estados = db.EstadosReplica.ToDictionary(e => e.Coleccion);
            // Lo borrado aquí y aún sin subir no se revive con una versión vieja de la nube
            var borradosLocales = db.CambiosReplica.Where(c => c.Borrado).Select(c => c.Uid).ToHashSet();

            foreach (var tabla in ReplicaNube.Tablas)
            {
                var desde = estados.TryGetValue(tabla.Coleccion, out var e) ? e.HastaUtc : DateTime.MinValue;
                var documentos = await ConsultarDesdeAsync(uid, token, tabla.Coleccion, desde, cliente);
                if (documentos.Count == 0) continue;

                var locales = ReplicaNube.PorUid(db, tabla);
                var idDe = ReplicaNube.TraductorAId(db);
                DateTime hasta = desde;
                DateTime? primeraFallida = null;
                foreach (var (docUid, campos) in documentos)
                {
                    var actualizada = ReplicaNube.Actualizada(campos);
                    if (!borradosLocales.Contains(docUid))
                    {
                        var r = ReplicaNube.AplicarDocumento(db, tabla, docUid, campos, idDe, locales);
                        if (r == ReplicaNube.Resultado.Aplicado) llegaron = true;
                        if (r == ReplicaNube.Resultado.FaltaReferencia) primeraFallida ??= actualizada;
                    }
                    if (actualizada > hasta) hasta = actualizada;
                }
                // Si a un registro le faltó su referencia, se vuelve a intentar desde él la próxima vez
                if (primeraFallida is DateTime f && f.AddTicks(-1) < hasta) hasta = f.AddTicks(-1);
                if (estados.TryGetValue(tabla.Coleccion, out var estado)) estado.HastaUtc = hasta;
                else db.EstadosReplica.Add(estados[tabla.Coleccion] = new EstadoReplica { Coleccion = tabla.Coleccion, HastaUtc = hasta });
                db.SaveChanges(); // cada tabla antes que las que la referencian (sus Id ya existen)
            }
            return llegaron;
        }

        /// <summary>Documentos de una colección cambiados después de 'desde', en orden, de 500 en 500.</summary>
        private static async Task<List<(string Uid, JsonNode Campos)>> ConsultarDesdeAsync(string uid, string token, string coleccion, DateTime desde, HttpClient? cliente)
        {
            var lista = new List<(string, JsonNode)>();
            var cursor = desde;
            while (true)
            {
                var consulta = new JsonObject
                {
                    ["structuredQuery"] = new JsonObject
                    {
                        ["from"] = new JsonArray(new JsonObject { ["collectionId"] = coleccion }),
                        ["where"] = new JsonObject
                        {
                            ["fieldFilter"] = new JsonObject
                            {
                                ["field"] = new JsonObject { ["fieldPath"] = "actualizada" },
                                ["op"] = "GREATER_THAN",
                                ["value"] = ReplicaNube.ValorNube(DateTime.SpecifyKind(cursor == DateTime.MinValue ? new DateTime(2000, 1, 1) : cursor, DateTimeKind.Utc))
                            }
                        },
                        ["orderBy"] = new JsonArray(new JsonObject { ["field"] = new JsonObject { ["fieldPath"] = "actualizada" }, ["direction"] = "ASCENDING" }),
                        ["limit"] = 500
                    }
                };
                using var pet = new HttpRequestMessage(HttpMethod.Post, $"{Documentos}/usuarios/{uid}:runQuery")
                {
                    Content = new StringContent(consulta.ToJsonString(), Encoding.UTF8, "application/json")
                };
                pet.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var resp = await (cliente ?? Http).SendAsync(pet);
                resp.EnsureSuccessStatusCode();

                int enPagina = 0;
                foreach (var fila in JsonNode.Parse(await resp.Content.ReadAsStringAsync())!.AsArray())
                {
                    var docu = fila?["document"];
                    if (docu?["fields"] is not JsonNode campos) continue;
                    var nombre = docu["name"]!.GetValue<string>();
                    lista.Add((nombre[(nombre.LastIndexOf('/') + 1)..], campos));
                    var actualizada = ReplicaNube.Actualizada(campos);
                    if (actualizada > cursor) cursor = actualizada;
                    enPagina++;
                }
                if (enPagina < 500) return lista;
            }
        }

        // ---------- Subir ----------

        private static async Task SubirCambiosAsync(string uid, string token, HttpClient? cliente)
        {
            List<CambioReplica> cola;
            using (var db = new AppDbContext()) cola = db.CambiosReplica.OrderBy(c => c.Id).ToList();
            if (cola.Count == 0) return;

            // Un documento por registro: su versión actual (o borrado, si ya no existe aquí)
            var porRegistro = cola.GroupBy(c => (c.Coleccion, c.Uid)).Select(g => (g.Key.Coleccion, g.Key.Uid, UltimoId: g.Max(c => c.Id))).ToList();
            for (int i = 0; i < porRegistro.Count; i += TamanoLote)
            {
                var lote = porRegistro.Skip(i).Take(TamanoLote).ToList();
                var escrituras = new JsonArray();
                using (var db = new AppDbContext())
                {
                    var uidDe = ReplicaNube.TraductorAUid(db);
                    foreach (var grupo in lote.GroupBy(x => x.Coleccion))
                    {
                        var tabla = ReplicaNube.PorColeccion(grupo.Key);
                        if (tabla == null) continue;
                        var locales = ReplicaNube.PorUid(db, tabla);
                        foreach (var (_, docUid, _) in grupo)
                        {
                            var campos = locales.TryGetValue(docUid, out var r)
                                ? ReplicaNube.ADocumento(db, r, uidDe)
                                : new JsonObject { ["borrado"] = ReplicaNube.ValorNube(true), ["actualizada"] = ReplicaNube.ValorNube(DateTime.UtcNow) };
                            escrituras.Add(new JsonObject
                            {
                                ["update"] = new JsonObject { ["name"] = $"{RaizDocumentos}/usuarios/{uid}/{grupo.Key}/{docUid}", ["fields"] = campos }
                            });
                        }
                    }
                }
                // La marca de cambios avisa a los demás dispositivos en la misma escritura
                escrituras.Add(new JsonObject
                {
                    ["update"] = new JsonObject { ["name"] = $"{RaizDocumentos}/usuarios/{uid}/estado/cambios", ["fields"] = new JsonObject() },
                    ["updateTransforms"] = new JsonArray(new JsonObject { ["fieldPath"] = "actualizada", ["setToServerValue"] = "REQUEST_TIME" })
                });

                using var pet = new HttpRequestMessage(HttpMethod.Post, $"{Documentos}:commit")
                {
                    Content = new StringContent(new JsonObject { ["writes"] = escrituras }.ToJsonString(), Encoding.UTF8, "application/json")
                };
                pet.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var resp = await (cliente ?? Http).SendAsync(pet);
                resp.EnsureSuccessStatusCode(); // si falla, la cola se conserva y se reintenta
                _marcaSubida = JsonNode.Parse(await resp.Content.ReadAsStringAsync())?["commitTime"]?.GetValue<string>();

                // Subido: se quita de la cola (sin borrar un cambio hecho mientras se enviaba)
                using var dbFin = new AppDbContext();
                var claves = lote.ToDictionary(x => (x.Coleccion, x.Uid), x => x.UltimoId);
                var hechos = dbFin.CambiosReplica.AsEnumerable()
                                  .Where(c => claves.TryGetValue((c.Coleccion, c.Uid), out var ultimo) && c.Id <= ultimo).ToList();
                dbFin.CambiosReplica.RemoveRange(hechos);
                dbFin.AplicandoNube = true;
                dbFin.SaveChanges();
            }
        }
    }
}
