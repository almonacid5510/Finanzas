// ============================================================
//  Proyecto: Mis Finanzas
//  Autoria: Juan Almonacid
//  Copyright (c) 2026 Juan Almonacid. Todos los derechos reservados.
// ============================================================
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace MisFinanzas.Modulos
{
    /// <summary>
    /// Mantiene el esquema de la BD alineado con el modelo de EF sin usar migraciones.
    /// EnsureCreated() solo crea la BD cuando no existe: en una BD ya creada no agrega
    /// tablas ni columnas nuevas. Aquí se compara el modelo con la BD real y se aplica
    /// lo que falte, conservando los datos. Antes de modificar nada se guarda un respaldo.
    /// Para agregar un campo nuevo basta con declararlo en la clase del modelo.
    /// </summary>
    public static class EsquemaBD
    {
        // Columnas que salieron del modelo. Si siguieran en la BD como NOT NULL sin
        // valor por defecto romperían los INSERT, por eso se eliminan.
        // SqlPrevio rescata los datos que haga falta antes de borrar la columna.
        private static readonly (string Tabla, string Columna, string? SqlPrevio)[] ColumnasObsoletas =
        {
            ("Creditos", "Editando", null),
            ("Creditos", "Tipo",
                "UPDATE \"Creditos\" SET \"NombreCustom\" = \"Tipo\" WHERE IFNULL(\"NombreCustom\", '') = '' AND \"Tipo\" IS NOT NULL;"),
        };

        public static void Actualizar(AppDbContext db)
        {
            db.Database.EnsureCreated();

            var conn = db.Database.GetDbConnection();
            conn.Open();
            try
            {
                var cambios = PlanificarCambios(db, conn);
                if (cambios.Count == 0) return;

                RespaldarBD(conn);
                using var tx = conn.BeginTransaction();
                foreach (var sql in cambios) Ejecutar(conn, sql, tx);
                tx.Commit();
            }
            finally
            {
                conn.Close();
            }
        }

        private static List<string> PlanificarCambios(AppDbContext db, DbConnection conn)
        {
            var cambios = new List<string>();
            string? scriptCreacion = null;

            foreach (var entidad in db.Model.GetEntityTypes())
            {
                var tabla = entidad.GetTableName();
                if (tabla == null) continue;

                var columnas = LeerColumnas(conn, tabla);
                if (columnas.Count == 0)
                {
                    // Tabla nueva: se toma su CREATE TABLE del script que generaría EF
                    scriptCreacion ??= db.Database.GenerateCreateScript();
                    var m = Regex.Match(scriptCreacion, $"CREATE TABLE \"{Regex.Escape(tabla)}\" \\(.*?\\n\\);",
                                        RegexOptions.Singleline);
                    if (m.Success)
                    {
                        cambios.Add(m.Value);
                        AgregarDatosIniciales(cambios, tabla, null);
                    }
                    continue;
                }

                var tablaId = StoreObjectIdentifier.Table(tabla, entidad.GetSchema());
                foreach (var prop in entidad.GetProperties())
                {
                    var columna = prop.GetColumnName(tablaId);
                    if (columna == null || columnas.Contains(columna)) continue;

                    var sql = $"ALTER TABLE \"{tabla}\" ADD COLUMN \"{columna}\" {prop.GetColumnType()}";
                    if (!prop.IsColumnNullable(tablaId))
                        sql += $" NOT NULL DEFAULT {ValorPorDefecto(prop.ClrType)}";
                    cambios.Add(sql + ";");
                    AgregarDatosIniciales(cambios, tabla, columna);
                }
            }

            foreach (var (tabla, columna, sqlPrevio) in ColumnasObsoletas)
            {
                if (!LeerColumnas(conn, tabla).Contains(columna)) continue;
                if (sqlPrevio != null) cambios.Add(sqlPrevio);
                cambios.Add($"ALTER TABLE \"{tabla}\" DROP COLUMN \"{columna}\";");
            }

            // Registros que apuntan a algo que ya no existe (versiones anteriores no los borraban)
            foreach (var (tabla, condicion) in Huerfanos)
            {
                if (LeerColumnas(conn, tabla).Count == 0) continue; // la tabla aún no existe
                var desde = $"FROM \"{tabla}\" WHERE {condicion}";
                if (Convert.ToInt64(Escalar(conn, $"SELECT COUNT(*) {desde};")) > 0)
                    cambios.Add($"DELETE {desde};");
            }

            return cambios;
        }

        private static readonly (string Tabla, string Condicion)[] Huerfanos =
        {
            ("GastosVehiculo", "\"VehiculoId\" NOT IN (SELECT \"Id\" FROM \"Vehiculos\")"),
            ("PagosCuenta", "\"MovimientoId\" NOT IN (SELECT \"Id\" FROM \"MovimientosV2\")"),
        };

        // Completa los datos de una tabla (columna = null) o columna recién agregada a una BD existente
        private static void AgregarDatosIniciales(List<string> cambios, string tabla, string? columna)
        {
            var sql = (tabla, columna) switch
            {
                // Los créditos que ya estaban marcados como pagados se toman como pagados este mes
                ("Creditos", nameof(Credito.MesPagado)) =>
                    $"UPDATE \"Creditos\" SET \"MesPagado\" = '{Periodo.MesActual}' WHERE \"Pagado\" = 1;",

                // Las cuentas existentes se repiten cada mes el mismo día de su fecha
                ("MovimientosV2", nameof(Movimiento.DiaPago)) =>
                    "UPDATE \"MovimientosV2\" SET \"DiaPago\" = CAST(strftime('%d', \"Fecha\") AS INTEGER) WHERE \"Tipo\" = 'Cuenta';",

                // Las cuentas que estaban pagadas quedan pagadas en el mes de su fecha, por su valor
                ("PagosCuenta", null) =>
                    "INSERT INTO \"PagosCuenta\" (\"MovimientoId\", \"Mes\", \"Monto\") " +
                    "SELECT \"Id\", strftime('%Y-%m', \"Fecha\"), \"Venta\" FROM \"MovimientosV2\" WHERE \"Tipo\" = 'Cuenta' AND \"EstaPagado\" = 1;",

                // Los pagos ya registrados se toman por el valor de su cuenta
                ("PagosCuenta", nameof(PagoCuenta.Monto)) =>
                    "UPDATE \"PagosCuenta\" SET \"Monto\" = IFNULL((SELECT \"Venta\" FROM \"MovimientosV2\" m WHERE m.\"Id\" = \"PagosCuenta\".\"MovimientoId\"), '0.0');",

                // Las cuentas existentes se repiten cada mes y quedan como "Otros" hasta que se les ponga categoría
                ("MovimientosV2", nameof(Movimiento.Frecuencia)) =>
                    $"UPDATE \"MovimientosV2\" SET \"Frecuencia\" = '{Frecuencias.Mensual}';",
                ("MovimientosV2", nameof(Movimiento.Categoria)) =>
                    "UPDATE \"MovimientosV2\" SET \"Categoria\" = 'Otros' WHERE \"Tipo\" = 'Cuenta';",

                _ => null
            };
            if (sql != null) cambios.Add(sql);
        }

        private static HashSet<string> LeerColumnas(DbConnection conn, string tabla)
        {
            var columnas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info(\"{tabla}\");";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                // La columna 1 de table_info es el nombre
                columnas.Add(reader.GetString(1));
            }
            return columnas;
        }

        // Valor para rellenar filas existentes al agregar una columna NOT NULL
        private static string ValorPorDefecto(Type tipo)
        {
            tipo = Nullable.GetUnderlyingType(tipo) ?? tipo;
            if (tipo == typeof(DateTime)) return "'0001-01-01 00:00:00'";
            if (tipo == typeof(decimal)) return "'0.0'"; // EF guarda decimal como TEXT en SQLite
            if (tipo.IsPrimitive || tipo.IsEnum) return "0";
            return "''";
        }

        // Copia consistente de la BD antes de tocar el esquema
        private static void RespaldarBD(DbConnection conn)
        {
            var baseRuta = Path.ChangeExtension(AppDbContext.RutaBD, null) + $".respaldo-{DateTime.Now:yyyyMMdd-HHmmss}";
            var ruta = baseRuta + ".db";
            for (int n = 2; File.Exists(ruta); n++) ruta = $"{baseRuta}-{n}.db"; // VACUUM INTO no sobrescribe
            Ejecutar(conn, $"VACUUM INTO '{ruta.Replace("'", "''")}';");
        }

        private static void Ejecutar(DbConnection conn, string sql, DbTransaction? tx = null)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }

        private static object? Escalar(DbConnection conn, string sql)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            return cmd.ExecuteScalar();
        }
    }
}
