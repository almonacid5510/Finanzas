// CreditoRepository.cs
public class CreditoRepository
{
    public void InsertarCredito(string nombre, double monto, int cuotas)
    {
        using (var db = DatabaseConfig.GetConnection())
        {
            db.Open();
            var cmd = db.CreateCommand();
            cmd.CommandText = "INSERT INTO Creditos (NombreCustom, MontoTotal, SaldoPendiente, CuotasTotales) VALUES (@nom, @monto, @monto, @cuotas)";
            cmd.Parameters.AddWithValue("@nom", nombre);
            cmd.Parameters.AddWithValue("@monto", monto);
            cmd.Parameters.AddWithValue("@cuotas", cuotas);
            cmd.ExecuteNonQuery();
        }
    }
}