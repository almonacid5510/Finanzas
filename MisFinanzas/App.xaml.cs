// ============================================================
//  Proyecto: Mis Finanzas
//  Autoria: Juan Almonacid
//  Copyright (c) 2026 Juan Almonacid. Todos los derechos reservados.
// ============================================================
using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Markup;

namespace MisFinanzas
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        // Formato colombiano en toda la app, sin depender de la configuración de Windows:
        // moneda "$ 1.526.000", fechas "02/10/2026" y meses en español.
        public static readonly CultureInfo Cultura = CrearCultura();

        private static CultureInfo CrearCultura()
        {
            var cultura = (CultureInfo)CultureInfo.GetCultureInfo("es-CO").Clone();
            cultura.DateTimeFormat.ShortDatePattern = "dd/MM/yyyy";
            cultura.NumberFormat.CurrencySymbol = "$";
            cultura.NumberFormat.CurrencyPositivePattern = 2; // "$ n"
            cultura.NumberFormat.CurrencyNegativePattern = 9; // "-$ n"
            return cultura;
        }

        public App()
        {
            CultureInfo.DefaultThreadCurrentCulture = Cultura;
            CultureInfo.DefaultThreadCurrentUICulture = Cultura;
            Thread.CurrentThread.CurrentCulture = Cultura;
            Thread.CurrentThread.CurrentUICulture = Cultura;

            // Los Binding con StringFormat usan el idioma del elemento (por defecto en-US: "$1,526,000")
            FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
                new FrameworkPropertyMetadata(XmlLanguage.GetLanguage("es-CO")));
            // También los fragmentos de texto (Run) dentro de un TextBlock
            FrameworkContentElement.LanguageProperty.OverrideMetadata(typeof(System.Windows.Documents.TextElement),
                new FrameworkPropertyMetadata(XmlLanguage.GetLanguage("es-CO")));
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            // El tema elegido en Cuenta, antes de crear la ventana (las pantallas toman sus colores al cargar)
            Tema.Aplicar(Resources, MisFinanzas.Modulos.Preferencias.Actual.Tema);
            base.OnStartup(e);
        }
    }
}
