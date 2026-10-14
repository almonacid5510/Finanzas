// ============================================================
//  Proyecto: Mis Finanzas
//  Autoria: Juan Almonacid
//  Copyright (c) 2026 Juan Almonacid. Todos los derechos reservados.
// ============================================================
using Microsoft.EntityFrameworkCore;
using System;
using System.IO;

namespace MisFinanzas.Modulos
{
    public class AppDbContext : DbContext
    {
        public DbSet<MetaAhorro> Metas { get; set; }
        public DbSet<AbonoMeta> AbonosMeta { get; set; }
        public DbSet<GastoDiario> GastosDiarios { get; set; }
        public DbSet<Movimiento> Movimientos { get; set; }
        public DbSet<Vehiculo> Vehiculos { get; set; }
        public DbSet<GastoVehiculo> GastosVehiculo { get; set; }
        public DbSet<Credito> Creditos { get; set; }
        public DbSet<PagoCuenta> PagosCuenta { get; set; }
        public DbSet<PagoCredito> PagosCredito { get; set; }
        public DbSet<Etiqueta> Etiquetas { get; set; }

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