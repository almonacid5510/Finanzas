using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Velopack;

namespace MisFinanzas.Modulos
{
    public record OfertaActualizacion(string Version, string Novedades, UpdateInfo? Paquete = null);
    public interface IPaquetesActualizacion
    {
        bool Instalado { get; }
        Task<OfertaActualizacion?> BuscarAsync();
        Task DescargarAsync(OfertaActualizacion oferta, Action<int> progreso);
        void Aplicar(OfertaActualizacion oferta);
    }
    public sealed class PaquetesVelopack : IPaquetesActualizacion
    {
        public const string Fuente = "https://mis-finanzas-4ea0b.web.app/actualizaciones/windows";
        private UpdateManager? gestorInterno;
        private UpdateManager gestor => gestorInterno ??= new UpdateManager(Fuente);
        public bool Instalado => gestor.IsInstalled;
        public async Task<OfertaActualizacion?> BuscarAsync()
        {
            var info = await gestor.CheckForUpdatesAsync();
            if (info == null || info.IsDowngrade) return null;
            return new(info.TargetFullRelease.Version.ToString(), TextoNovedades(info.TargetFullRelease.NotesMarkdown), info);
        }

        /// <summary>Novedades en texto plano: sin el título "# Mis Finanzas x.y.z" y con viñetas en vez de guiones.</summary>
        public static string TextoNovedades(string? markdown)
        {
            if (string.IsNullOrWhiteSpace(markdown)) return "Mejoras y correcciones.";
            var lineas = markdown.Split('\n')
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && !l.StartsWith("#"))
                .Select(l => l.StartsWith("- ") || l.StartsWith("* ") ? "• " + l[2..] : l)
                .Select(l => l.Replace("**", ""));
            var texto = string.Join(Environment.NewLine, lineas);
            return texto.Length > 0 ? texto : "Mejoras y correcciones.";
        }
        public Task DescargarAsync(OfertaActualizacion oferta, Action<int> progreso) => gestor.DownloadUpdatesAsync(oferta.Paquete!, progreso);
        public void Aplicar(OfertaActualizacion oferta) => gestor.ApplyUpdatesAndRestart(oferta.Paquete!);
    }

    public sealed class Actualizaciones : INotifyPropertyChanged
    {
        private static readonly Lazy<Actualizaciones> instancia = new(() => new Actualizaciones(new PaquetesVelopack(), () => Task.Run(() => RespaldarBase(AppDbContext.RutaBD))));
        public static Actualizaciones Instancia => instancia.Value;
        private readonly IPaquetesActualizacion paquetes;
        private readonly Func<Task> respaldar;
        private OfertaActualizacion? oferta;
        public event PropertyChangedEventHandler? PropertyChanged;
        private void Avisar() { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("")); }
        public Actualizaciones(IPaquetesActualizacion paquetes, Func<Task> respaldar) { this.paquetes=paquetes; this.respaldar=respaldar; }
        public string VersionInstalada => typeof(App).Assembly.GetName().Version?.ToString(3) ?? "1.9.0";
        public string VersionNueva => oferta?.Version ?? "";
        public string Novedades => oferta?.Novedades ?? "";
        public string Mensaje { get; private set; } = "Busca versiones nuevas de Mis Finanzas.";
        public bool Disponible => oferta != null;
        public bool MostrarAviso { get; private set; }
        public bool Ocupado { get; private set; }
        public int Progreso { get; private set; }

        public async Task BuscarAsync()
        {
            if (Ocupado) return;
            Ocupado=true; Mensaje="Buscando actualizaciones…"; Avisar();
            try
            {
                if (!paquetes.Instalado)
                {
                    Mensaje="Para habilitar las actualizaciones automáticas, instala Mis Finanzas con el nuevo instalador. Esta copia conserva tus datos.";
                    return;
                }
                oferta = await paquetes.BuscarAsync();
                MostrarAviso=Disponible;
                Mensaje=Disponible ? $"Hay una actualización disponible: {VersionNueva}." : "Tienes la versión más reciente.";
            }
            catch { Mensaje="No se pudo consultar la actualización. Revisa tu conexión e inténtalo de nuevo."; }
            finally { Ocupado=false; Avisar(); }
        }
        public void MasTarde() { MostrarAviso=false; Avisar(); }
        public async Task ActualizarAsync()
        {
            if (Ocupado || oferta == null) return;
            var elegida=oferta;
            Ocupado=true; Progreso=0; Mensaje="Descargando actualización…"; Avisar();
            try
            {
                // Progress devuelve el avance al hilo que abrió esta operación (WPF).
                var progreso = new Progress<int>(p => { Progreso=Math.Clamp(p,0,100); Avisar(); });
                await paquetes.DescargarAsync(elegida,p => ((IProgress<int>)progreso).Report(p));
                Progreso=100; Mensaje="Creando respaldo de tus datos…"; Avisar();
                await respaldar(); // Si falla, no se instala ni se cierra la aplicación.
                Mensaje="Instalando actualización y reiniciando…"; Avisar();
                paquetes.Aplicar(elegida);
            }
            catch { Mensaje="No se pudo completar la actualización. Tus datos y la versión actual se conservan. Puedes volver a intentarlo."; }
            finally { Ocupado=false; Avisar(); }
        }
        public static string? RespaldarBase(string ruta)
        {
            if (!File.Exists(ruta)) return null;
            var copia=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(ruta))!, $"MisFinanzas.antes-de-actualizar-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.db");
            using var origen=new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=ruta, Mode=SqliteOpenMode.ReadOnly }.ToString());
            using var destino=new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=copia }.ToString());
            origen.Open(); destino.Open(); origen.BackupDatabase(destino);
            return copia;
        }
    }
}
