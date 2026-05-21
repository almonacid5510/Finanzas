using Microsoft.Data.Sqlite;
using System.IO;

public static class DatabaseConfig
{
    private static string dbPath = Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "MisFinanzas.db");

    public static void InitializeDatabase()
    {
        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            connection.Open();
            var command = connection.CreateCommand();

            // 1. Tabla de Movimientos (Ingresos/Gastos)
            command.CommandText = @"
            CREATE TABLE IF NOT EXISTS Movimientos (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Monto DECIMAL(18, 2) NOT NULL,
                Descripcion TEXT,
                Fecha DATETIME,
                Tipo TEXT
            );";
            command.ExecuteNonQuery();

            // 2. Tabla de Créditos (Ajustada a tu XAML actual)
            command.CommandText = @"
CREATE TABLE IF NOT EXISTS Creditos (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    NombreCustom TEXT,
    MontoTotal DECIMAL,
    SaldoPendiente DECIMAL,
    Tasa DECIMAL,
    PlazoMeses INTEGER,
    CuotaMensual DECIMAL -- Verifica que esta línea exista aquí
);";
            command.ExecuteNonQuery();
        }
    }

    public static SqliteConnection GetConnection()
    {
        return new SqliteConnection($"Data Source={dbPath}");
    }
}