// ============================================================
//  Proyecto: Mis Finanzas
//  Autoria: Juan Almonacid
//  Copyright (c) 2026 Juan Almonacid. Todos los derechos reservados.
// ============================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;

namespace MisFinanzas.Modulos
{
    /// <summary>
    /// Traducción entre los registros del PC y sus documentos en la nube (usuarios/{uid}/{colección}/{Uid}).
    /// Las columnas salen del modelo de la base, así una columna nueva viaja sola. Las referencias a otra
    /// tabla (el Id numérico de este PC) viajan como el Uid del registro referido.
    /// La parte de red está en SincronizacionNube.cs.
    /// </summary>
    public static class ReplicaNube
    {
        public sealed record Tabla(string Coleccion, Type Tipo, Func<AppDbContext, IQueryable<IReplicable>> Consulta);

        /// <summary>Colecciones replicadas; primero las que otras referencian (así existen al aplicar las demás).</summary>
        public static readonly Tabla[] Tablas =
        {
            new("movimientos", typeof(Movimiento), db => db.Movimientos),
            new("creditos", typeof(Credito), db => db.Creditos),
            new("metas", typeof(MetaAhorro), db => db.Metas),
            new("vehiculos", typeof(Vehiculo), db => db.Vehiculos),
            new("etiquetas", typeof(Etiqueta), db => db.Etiquetas),
            new("pagosCuenta", typeof(PagoCuenta), db => db.PagosCuenta),
            new("pagosCredito", typeof(PagoCredito), db => db.PagosCredito),
            new("abonosMeta", typeof(AbonoMeta), db => db.AbonosMeta),
            new("gastosVehiculo", typeof(GastoVehiculo), db => db.GastosVehiculo),
        };

        /// <summary>Columnas que apuntan a otra tabla: en la nube van como el Uid del registro referido.</summary>
        private static readonly Dictionary<(Type Tipo, string Propiedad), (string Campo, Type Destino)> Referencias = new()
        {
            [(typeof(PagoCuenta), nameof(PagoCuenta.MovimientoId))] = ("movimientoUid", typeof(Movimiento)),
            [(typeof(PagoCredito), nameof(PagoCredito.CreditoId))] = ("creditoUid", typeof(Credito)),
            [(typeof(AbonoMeta), nameof(AbonoMeta.MetaId))] = ("metaUid", typeof(MetaAhorro)),
            [(typeof(GastoVehiculo), nameof(GastoVehiculo.VehiculoId))] = ("vehiculoUid", typeof(Vehiculo)),
        };

        public static string Coleccion(Type tipo) => Tablas.First(t => t.Tipo.IsAssignableFrom(tipo)).Coleccion;
        public static Tabla? PorColeccion(string coleccion) => Tablas.FirstOrDefault(t => t.Coleccion == coleccion);

        private static string Campo(string propiedad) => char.ToLowerInvariant(propiedad[0]) + propiedad[1..];

        private static IEnumerable<Microsoft.EntityFrameworkCore.Metadata.IProperty> Columnas(AppDbContext db, Type tipo) =>
            db.Model.FindEntityType(tipo)!.GetProperties()
              .Where(p => !p.IsPrimaryKey() && p.Name != nameof(IReplicable.Uid) && p.Name != nameof(IReplicable.ActualizadaUtc) && p.PropertyInfo != null);

        // ---------- Identidades y cola ----------

        /// <summary>Da identidad a los registros creados antes de la nube (una sola vez por registro).</summary>
        public static int AsignarIdentidades(AppDbContext db)
        {
            int n = 0;
            var ahora = DateTime.UtcNow;
            foreach (var t in Tablas)
                foreach (var r in t.Consulta(db).AsEnumerable().Where(r => string.IsNullOrEmpty(r.Uid)))
                {
                    r.Uid = Guid.NewGuid().ToString("N");
                    r.ActualizadaUtc = ahora;
                    n++;
                }
            if (n > 0) { db.AplicandoNube = true; db.SaveChanges(); db.AplicandoNube = false; }
            return n;
        }

        /// <summary>Primera vez con la nube: todos los registros quedan en la cola para subirse.</summary>
        public static int EncolarTodo(AppDbContext db)
        {
            var pendientes = db.CambiosReplica.Select(c => c.Uid).ToHashSet();
            int n = 0;
            foreach (var t in Tablas)
                foreach (var r in t.Consulta(db).AsEnumerable().Where(r => !pendientes.Contains(r.Uid)))
                {
                    db.CambiosReplica.Add(new CambioReplica { Coleccion = t.Coleccion, Uid = r.Uid });
                    n++;
                }
            db.AplicandoNube = true; db.SaveChanges(); db.AplicandoNube = false;
            return n;
        }

        // Uid ↔ Id de una tabla (para traducir referencias)
        private static int IdDe(IReplicable r) => (int)r.GetType().GetProperty("Id")!.GetValue(r)!;

        public static Func<Type, int, string?> TraductorAUid(AppDbContext db)
        {
            var cache = new Dictionary<Type, Dictionary<int, string>>();
            return (tipo, id) =>
            {
                if (!cache.TryGetValue(tipo, out var mapa))
                    cache[tipo] = mapa = Tablas.First(t => t.Tipo == tipo).Consulta(db).AsEnumerable().ToDictionary(IdDe, r => r.Uid);
                return mapa.GetValueOrDefault(id);
            };
        }

        public static Func<Type, string, int?> TraductorAId(AppDbContext db)
        {
            var cache = new Dictionary<Type, Dictionary<string, int>>();
            return (tipo, uid) =>
            {
                if (!cache.TryGetValue(tipo, out var mapa))
                    cache[tipo] = mapa = Tablas.First(t => t.Tipo == tipo).Consulta(db).AsEnumerable()
                                              .Where(r => r.Uid.Length > 0).GroupBy(r => r.Uid).ToDictionary(g => g.Key, g => IdDe(g.First()));
                return mapa.TryGetValue(uid, out var id) ? id : null;
            };
        }

        // ---------- Registro → documento ----------

        /// <summary>Campos del documento en la nube (formato REST de Firestore), con "actualizada" y "borrado".</summary>
        public static JsonObject ADocumento(AppDbContext db, IReplicable r, Func<Type, int, string?> uidDe)
        {
            var tipo = r.GetType();
            var campos = new JsonObject();
            foreach (var p in Columnas(db, tipo))
            {
                var valor = p.PropertyInfo!.GetValue(r);
                if (Referencias.TryGetValue((tipo, p.Name), out var refe))
                    campos[refe.Campo] = ValorNube(valor is int id ? uidDe(refe.Destino, id) ?? string.Empty : string.Empty);
                else
                    campos[Campo(p.Name)] = ValorNube(valor);
            }
            campos["actualizada"] = ValorNube(DateTime.SpecifyKind(r.ActualizadaUtc, DateTimeKind.Utc));
            campos["borrado"] = ValorNube(false);
            return campos;
        }

        /// <summary>Valor de C# → valor de Firestore. Los decimales con centavos van como double (no se redondean).</summary>
        public static JsonObject ValorNube(object? v) => v switch
        {
            null => new JsonObject { ["nullValue"] = null },
            string s => new JsonObject { ["stringValue"] = s },
            bool b => new JsonObject { ["booleanValue"] = b },
            int i => new JsonObject { ["integerValue"] = i.ToString(CultureInfo.InvariantCulture) },
            long l => new JsonObject { ["integerValue"] = l.ToString(CultureInfo.InvariantCulture) },
            decimal d when d == decimal.Truncate(d) => new JsonObject { ["integerValue"] = d.ToString("0", CultureInfo.InvariantCulture) },
            decimal d => new JsonObject { ["doubleValue"] = (double)d },
            double x => new JsonObject { ["doubleValue"] = x },
            DateTime t => new JsonObject { ["timestampValue"] = (t.Kind == DateTimeKind.Utc ? t : t.ToUniversalTime()).ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture) },
            _ => new JsonObject { ["stringValue"] = v.ToString() }
        };

        // ---------- Documento → registro ----------

        /// <summary>Registros de una tabla por Uid, para aplicar un lote de documentos.</summary>
        public static Dictionary<string, IReplicable> PorUid(AppDbContext db, Tabla tabla) =>
            tabla.Consulta(db).AsEnumerable().Where(r => r.Uid.Length > 0).GroupBy(r => r.Uid).ToDictionary(g => g.Key, g => g.First());

        public static DateTime Actualizada(JsonNode? campos) =>
            campos?["actualizada"]?["timestampValue"]?.GetValue<string>() is string t
            && DateTimeOffset.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.UtcDateTime : DateTime.MinValue;

        public static bool Borrado(JsonNode? campos) => campos?["borrado"]?["booleanValue"]?.GetValue<bool>() == true;

        public enum Resultado { Aplicado, SinCambios, FaltaReferencia }

        /// <summary>
        /// Aplica un documento de la nube en la base del PC: crea, actualiza o borra el registro con ese Uid.
        /// Solo si es más reciente que lo que hay aquí (gana el cambio más reciente). No guarda: lo hace quien llama.
        /// </summary>
        /// <param name="locales">Registros de la tabla por Uid (se cargan una vez por lote; se actualiza al crear o borrar).</param>
        public static Resultado AplicarDocumento(AppDbContext db, Tabla tabla, string uid, JsonNode campos, Func<Type, string, int?> idDe,
                                                 Dictionary<string, IReplicable> locales)
        {
            var actualizada = Actualizada(campos);
            var local = locales.GetValueOrDefault(uid);
            if (local != null && local.ActualizadaUtc >= actualizada) return Resultado.SinCambios;

            if (Borrado(campos))
            {
                if (local == null) return Resultado.SinCambios;
                db.Remove(local);
                locales.Remove(uid);
                return Resultado.Aplicado;
            }

            // Las referencias deben existir aquí antes de crear el registro
            var valores = new Dictionary<Microsoft.EntityFrameworkCore.Metadata.IProperty, object?>();
            foreach (var p in Columnas(db, tabla.Tipo))
            {
                if (Referencias.TryGetValue((tabla.Tipo, p.Name), out var refe))
                {
                    var uidRef = campos[refe.Campo]?["stringValue"]?.GetValue<string>();
                    if (string.IsNullOrEmpty(uidRef)) continue;
                    var id = idDe(refe.Destino, uidRef);
                    if (id == null) return Resultado.FaltaReferencia;
                    valores[p] = id.Value;
                }
                else if (campos[Campo(p.Name)] is JsonNode valor && LeerValor(valor, p.ClrType, out var leido))
                    valores[p] = leido;
            }

            var registro = local ?? (IReplicable)Activator.CreateInstance(tabla.Tipo)!;
            foreach (var (p, v) in valores) p.PropertyInfo!.SetValue(registro, v);
            registro.Uid = uid;
            registro.ActualizadaUtc = actualizada;
            if (local == null) { db.Add(registro); locales[uid] = registro; }
            return Resultado.Aplicado;
        }

        /// <summary>Valor de Firestore → C# según el tipo de la columna. False si el valor no corresponde.</summary>
        public static bool LeerValor(JsonNode valor, Type tipo, out object? resultado)
        {
            resultado = null;
            var baseTipo = Nullable.GetUnderlyingType(tipo) ?? tipo;
            if (valor["nullValue"] != null || valor.AsObject().ContainsKey("nullValue"))
            {
                if (Nullable.GetUnderlyingType(tipo) != null || !tipo.IsValueType) { resultado = baseTipo == typeof(string) ? string.Empty : null; return true; }
                return false;
            }
            string? entero = valor["integerValue"]?.GetValue<string>();
            double? doble = valor["doubleValue"]?.GetValue<double>();
            if (baseTipo == typeof(string)) { resultado = valor["stringValue"]?.GetValue<string>() ?? string.Empty; return valor["stringValue"] != null; }
            if (baseTipo == typeof(bool)) { resultado = valor["booleanValue"]?.GetValue<bool>() ?? false; return valor["booleanValue"] != null; }
            if (baseTipo == typeof(int))
            {
                if (entero != null && int.TryParse(entero, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) { resultado = i; return true; }
                if (doble != null) { resultado = (int)Math.Round(doble.Value); return true; }
                return false;
            }
            if (baseTipo == typeof(decimal))
            {
                if (entero != null && decimal.TryParse(entero, NumberStyles.Integer, CultureInfo.InvariantCulture, out var d)) { resultado = d; return true; }
                if (doble != null) { resultado = (decimal)doble.Value; return true; }
                return false;
            }
            if (baseTipo == typeof(double))
            {
                if (doble != null) { resultado = doble.Value; return true; }
                if (entero != null && double.TryParse(entero, NumberStyles.Float, CultureInfo.InvariantCulture, out var x)) { resultado = x; return true; }
                return false;
            }
            if (baseTipo == typeof(DateTime))
            {
                if (valor["timestampValue"]?.GetValue<string>() is string t && DateTimeOffset.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.None, out var f))
                { resultado = f.LocalDateTime; return true; }
                return false;
            }
            return false;
        }
    }
}
