using Microsoft.Data.Sqlite;
using MisFinanzas.Modulos;
using System.IO;

static class ActualizacionesTest
{
    static void Check(bool ok,string mensaje) { if(!ok) throw new Exception(mensaje); Console.WriteLine("OK " + mensaje); }
    sealed class Paquetes : IPaquetesActualizacion
    {
        public bool Instalado { get; set; }=true;
        public bool ErrorRed, ErrorDescarga, Aplicado;
        public int Descargas;
        public Func<Task>? Esperar;
        public OfertaActualizacion? Oferta=new("2.0.0","Novedades de prueba");
        public Task<OfertaActualizacion?> BuscarAsync() => ErrorRed ? Task.FromException<OfertaActualizacion?>(new IOException()) : Task.FromResult(Oferta);
        public async Task DescargarAsync(OfertaActualizacion oferta,Action<int> progreso) { Descargas++; if(ErrorDescarga) throw new IOException(); if(Esperar!=null) await Esperar(); progreso(100); }
        public void Aplicar(OfertaActualizacion oferta) { Aplicado=true; }
    }
    public static async Task ProbarPaquete(string fuente)
    {
        var carpeta=Path.Combine(Path.GetTempPath(),"paquete-"+Guid.NewGuid());Directory.CreateDirectory(carpeta);
        var locator=new Velopack.Locators.TestVelopackLocator("MisFinanzas","1.8.0",carpeta);
        var manager=new Velopack.UpdateManager(fuente,new Velopack.UpdateOptions{ExplicitChannel="win"},locator);
        var info=await manager.CheckForUpdatesAsync();
        Check(info?.TargetFullRelease.Version.ToString()=="1.9.0","Velopack detecta la actualización real desde una instalación de prueba 1.8.0");
        await manager.DownloadUpdatesAsync(info!);
        var descargado=Path.Combine(carpeta,info!.TargetFullRelease.FileName);
        Check(File.Exists(descargado) && new FileInfo(descargado).Length==info.TargetFullRelease.Size,"Velopack descarga y verifica el paquete generado");
        var actual=new Velopack.UpdateManager(fuente,new Velopack.UpdateOptions{ExplicitChannel="win"},new Velopack.Locators.TestVelopackLocator("MisFinanzas","1.9.0",carpeta));
        Check(await actual.CheckForUpdatesAsync()==null,"La misma versión no vuelve a ofrecerse como actualización");
        var corrupta=Path.Combine(carpeta,"canal-corrupto");Directory.CreateDirectory(corrupta);
        File.Copy(Path.Combine(fuente,"releases.win.json"),Path.Combine(corrupta,"releases.win.json"));
        File.WriteAllText(Path.Combine(corrupta,info.TargetFullRelease.FileName),"paquete incompleto");
        var destino=Path.Combine(carpeta,"descarga-corrupta");Directory.CreateDirectory(destino);
        var invalido=new Velopack.UpdateManager(corrupta,new Velopack.UpdateOptions{ExplicitChannel="win"},new Velopack.Locators.TestVelopackLocator("MisFinanzas","1.8.0",destino));
        bool rechazo=false;try{await invalido.DownloadUpdatesAsync((await invalido.CheckForUpdatesAsync())!);}catch{rechazo=true;}
        Check(rechazo,"El motor real rechaza un paquete incompleto o alterado");
    }
    public static async Task Run()
    {
        var motor=new Paquetes(); int copias=0;
        var servicio=new Actualizaciones(motor,()=>{copias++;return Task.CompletedTask;});
        await servicio.BuscarAsync();
        Check(servicio.Disponible && servicio.MostrarAviso && !motor.Aplicado,"Actualización encontrada espera la aceptación del usuario");
        servicio.MasTarde();Check(!servicio.MostrarAviso && servicio.Disponible,"Más tarde conserva la actualización sin instalar");
        await servicio.BuscarAsync();Check(servicio.MostrarAviso,"Buscar manualmente vuelve a ofrecer la actualización");
        await servicio.ActualizarAsync();Check(motor.Aplicado && copias==1 && motor.Descargas==1,"Solo instala después de descargar y respaldar");
        motor=new Paquetes{ErrorDescarga=true};servicio=new Actualizaciones(motor,()=>throw new Exception("No debía respaldar"));
        await servicio.BuscarAsync();await servicio.ActualizarAsync();Check(!motor.Aplicado && !servicio.Ocupado,"Descarga fallida no instala y permite reintentar");
        motor=new Paquetes();servicio=new Actualizaciones(motor,()=>throw new IOException("Sin espacio para copia"));
        await servicio.BuscarAsync();await servicio.ActualizarAsync();Check(!motor.Aplicado,"Respaldo fallido impide instalar y cerrar la app");
        motor=new Paquetes{ErrorRed=true};servicio=new Actualizaciones(motor,()=>Task.CompletedTask);
        await servicio.BuscarAsync();Check(!servicio.Disponible && !servicio.Ocupado && servicio.Mensaje.Contains("conexión"),"Fallo al consultar no anuncia que está actualizado");
        motor=new Paquetes{Instalado=false};servicio=new Actualizaciones(motor,()=>Task.CompletedTask);
        await servicio.BuscarAsync();Check(!servicio.Disponible && servicio.Mensaje.Contains("instalador"),"Instalación anterior o de desarrollo explica cómo habilitar el actualizador");
        var espera=new TaskCompletionSource();motor=new Paquetes{Esperar=()=>espera.Task};servicio=new Actualizaciones(motor,()=>Task.CompletedTask);
        await servicio.BuscarAsync();var primera=servicio.ActualizarAsync();await servicio.ActualizarAsync();Check(motor.Descargas==1,"Doble clic no inicia dos instalaciones");espera.SetResult();await primera;
        var carpeta=Path.Combine(Path.GetTempPath(),"actualizar-"+Guid.NewGuid());Directory.CreateDirectory(carpeta);
        var origen=Path.Combine(carpeta,"original.db");
        using(var conn=new SqliteConnection($"Data Source={origen}")) {
            conn.Open();using var cmd=conn.CreateCommand();cmd.CommandText="PRAGMA journal_mode=WAL; CREATE TABLE Prueba (Monto INTEGER); INSERT INTO Prueba VALUES (3200000);";cmd.ExecuteNonQuery();
            var copia=Actualizaciones.RespaldarBase(origen)!;
            using var respaldo=new SqliteConnection($"Data Source={copia}");respaldo.Open();using var leer=respaldo.CreateCommand();leer.CommandText="SELECT Monto FROM Prueba";
            Check((long)leer.ExecuteScalar()! == 3200000,"Copia SQLite incluye datos aún presentes en WAL");
            leer.CommandText="PRAGMA integrity_check";Check((string)leer.ExecuteScalar()! == "ok","Respaldo de actualización íntegro");
        }
        Check(Actualizaciones.RespaldarBase(Path.Combine(carpeta,"no-existe.db"))==null,"Primera instalación no inventa una base personal");
    }
}
