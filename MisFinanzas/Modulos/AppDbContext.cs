// ============================================================
//  Proyecto: Mis Finanzas
//  Autoria: Juan Almonacid
//  Copyright (c) 2026 Juan Almonacid. Todos los derechos reservados.
// ============================================================
using Microsoft.EntityFrameworkCore;
using System;
using System.IO;
using System.Linq;

namespace MisFinanzas.Modulos
{
    public class AppDbContext : DbContext
    {
        public DbSet<ConfiguracionNomina> ConfiguracionesNomina { get; set; }
        public DbSet<IngresoRecibido> IngresosRecibidos { get; set; }
        public DbSet<InicioRegistro> IniciosRegistro { get; set; }
        public DbSet<MetaAhorro> Metas { get; set; }
        public DbSet<AbonoMeta> AbonosMeta { get; set; }
        public DbSet<GastoDiario> GastosDiarios { get; set; }
        public DbSet<CambioNube> CambiosNube { get; set; }
        public DbSet<Movimiento> Movimientos { get; set; }
        public DbSet<Vehiculo> Vehiculos { get; set; }
        public DbSet<GastoVehiculo> GastosVehiculo { get; set; }
        public DbSet<Credito> Creditos { get; set; }
        public DbSet<PagoCuenta> PagosCuenta { get; set; }
        public DbSet<PagoCredito> PagosCredito { get; set; }
        public DbSet<Etiqueta> Etiquetas { get; set; }
        public DbSet<CambioReplica> CambiosReplica { get; set; }
        public DbSet<EstadoReplica> EstadosReplica { get; set; }

        /// <summary>Lo que se guarda aquí viene de la nube: no se vuelve a anotar para subir.</summary>
        public bool AplicandoNube { get; set; }

        /// <summary>Se lanza (en el hilo que guardó) cuando quedan cambios de este PC por subir a la nube.</summary>
        public static event Action? CambiosLocales;

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            bool hayCambios = !AplicandoNube && AnotarCambiosParaLaNube();
            var filas = base.SaveChanges(acceptAllChangesOnSuccess);
            if (hayCambios) CambiosLocales?.Invoke();
            return filas;
        }

        /// <summary>
        /// Cada registro que vive en la nube (IReplicable) y se agregó, cambió o borró aquí queda en la cola
        /// CambiosReplica con su identidad; la sincronización sube su versión actual (o lo marca borrado).
        /// </summary>
        private bool AnotarCambiosParaLaNube()
        {
            var ahora = DateTime.UtcNow;
            var cambios = ChangeTracker.Entries<IReplicable>()
                .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToList();
            foreach (var e in cambios)
            {
                if (string.IsNullOrEmpty(e.Entity.Uid)) e.Entity.Uid = Guid.NewGuid().ToString("N");
                if (e.State != EntityState.Deleted) e.Entity.ActualizadaUtc = ahora;
                CambiosReplica.Add(new CambioReplica
                {
                    Coleccion = ReplicaNube.Coleccion(e.Entity.GetType()),
                    Uid = e.Entity.Uid,
                    Borrado = e.State == EntityState.Deleted,
                    FechaUtc = ahora
                });
            }
            return cambios.Count > 0;
        }

        // La ruta en Documentos es correcta y persistente
        public static string RutaBD { get; set; } =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MisFinanzas_Real.db");

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlite($"Data Source={RutaBD}");
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Configuración simplificada y sin duplicados
            // Solo es necesario si NO usas el atributo [Key] en tus clases de modelo
            modelBuilder.Entity<MetaAhorro>().HasKey(m => m.Id);
            modelBuilder.Entity<Movimiento>().HasKey(m => m.Id);
            modelBuilder.Entity<Vehiculo>().HasKey(v => v.Id);
            modelBuilder.Entity<GastoVehiculo>().HasKey(g => g.Id);
            modelBuilder.Entity<Credito>().HasKey(c => c.Id);
        }
    }
}