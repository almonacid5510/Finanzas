using System;
using Velopack;
namespace MisFinanzas
{
    internal static class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            // Los hooks del instalador deben salir antes de crear WPF o abrir la base.
            VelopackApp.Build().SetAutoApplyOnStartup(false).Run();
            var app = new App();
            app.InitializeComponent();
            app.Run();
        }
    }
}
