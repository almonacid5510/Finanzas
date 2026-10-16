using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MisFinanzas.Modulos;
using MisFinanzas.Controles;
class Program {
 static void Check(bool ok,string mensaje) { if(!ok) throw new Exception(mensaje); Console.WriteLine("OK " + mensaje); }
 [STAThread] static void Main() {
 var octubre=new DateTime(2026,10,15); var inicioOct=new DateTime(2026,10,1); var inicioAgo=new DateTime(2026,8,1);
 Check(MisFinanzas.Periodo.PrimerMesVisible(inicioOct,octubre)==inicioOct,"Inicio en octubre no permite septiembre");
 Check(MisFinanzas.Periodo.LimitarMes(new DateTime(2026,9,1),inicioOct,octubre)==inicioOct,"Restringe retroceso anterior al registro");
 Check(MisFinanzas.Periodo.LimitarMes(new DateTime(2026,8,1),inicioAgo,octubre)==new DateTime(2026,9,1),"Con historial solo permite un mes anterior");
 Check(MisFinanzas.Periodo.LimitarMes(new DateTime(2026,12,1),inicioOct,octubre)==new DateTime(2026,11,1),"Solo permite un mes adelante");
 Check(MisFinanzas.Periodo.PrimerMesVisible(inicioOct,new DateTime(2026,11,1))==inicioOct,"Noviembre habilita octubre como historial");
 Check(MisFinanzas.Periodo.LimitarMes(new DateTime(2027,3,1),inicioOct,new DateTime(2027,1,1))==new DateTime(2027,2,1),"Límites correctos al cambiar de año");
 var c = new ConfiguracionNomina { Confirmada=true, Frecuencia="Quincenal", Dias="5,20", MontoPorPago=3200000 };
 var oct = Nomina.DelMes(c,new DateTime(2026,10,1));
 Check(oct.Count==2 && oct[0].Fecha.Day==5 && oct[1].Fecha.Day==20,"Días quincenales elegidos por el usuario");
 Check(oct.All(p=>p.Monto==3200000) && Nomina.SalarioMensual(c)==6400000,"Sin segundo valor, las dos quincenas pagan lo mismo");
 var dosValores = new ConfiguracionNomina { Confirmada=true, Frecuencia="Quincenal", Dias="14,30", MontoPorPago=3000000, MontoSegundoPago=3400000 };
 var pagosDos = Nomina.DelMes(dosValores,new DateTime(2026,10,1));
 Check(pagosDos[0].Monto==3000000 && pagosDos[1].Monto==3400000 && Nomina.SalarioMensual(dosValores)==6400000,"Cada quincena con su valor y salario mensual igual a la suma");
 var buses = new Movimiento { Id=9100, Tipo=CuentasMensuales.TipoGasto, Descripcion="Buses", Fecha=new DateTime(2026,10,1), DiaPago=15, Frecuencia=Frecuencias.Quincenal, Venta=50000 };
 var quincenas = CuentasMensuales.DelMes(new[]{buses}, new[]{ new PagoCuenta{ MovimientoId=9100, Mes="2026-10", Monto=50000, Quincena=2 } }, new DateTime(2026,10,1));
 Check(quincenas.Count==2 && quincenas[0].Vence.Day==15 && quincenas[1].Vence.Day==30 && !quincenas[0].Pagada && quincenas[1].Pagada && quincenas.Sum(q=>q.Venta)==100000,"Gasto quincenal: dos pagos al mes (15 y 30), cada uno con su estado");
 buses.DiaPago=31; var febrero = CuentasMensuales.DelMes(new[]{buses}, Array.Empty<PagoCuenta>(), new DateTime(2027,2,1));
 Check(febrero[0].Vence.Day==16 && febrero[1].Vence.Day==28,"Quincenal con día 31: 16 y último día en meses cortos");
 c.Dias="15,31";
 Check(Nomina.DelMes(c,new DateTime(2027,2,1))[1].Fecha.Day==28,"Último día en febrero");
 c.Dias="30,31"; var feb=Nomina.DelMes(c,new DateTime(2027,2,1));
 Check(feb.Count==2 && feb[0].Uid!=feb[1].Uid,"Dos pagos en la misma fecha ajustada conservan identificadores diferentes");
 c.Frecuencia="Semanal"; c.DiaSemana=5;
 Check(Nomina.DelMes(c,new DateTime(2026,10,1)).All(p=>p.Fecha.DayOfWeek==DayOfWeek.Friday),"Pago semanal en viernes");
 c.Frecuencia="Cada 14 días"; c.FechaBase=new DateTime(2026,9,25);
 var intervalo=Nomina.DelMes(c,new DateTime(2026,10,1));
 Check(intervalo.Count==2 && intervalo[0].Fecha.Day==9 && intervalo[1].Fecha.Day==23,"Intervalo continuo de 14 días");
 c.Frecuencia="Variable"; Check(Nomina.DelMes(c,DateTime.Today).Count==0,"Variable no inventa fechas");
 c.Frecuencia="Quincenal";c.Dias="15,15";bool rechazo=false;try{Nomina.Validar(c);}catch(ArgumentException){rechazo=true;}Check(rechazo,"Rechaza días duplicados");
 AppDbContext.RutaBD=Path.Combine(Path.GetTempPath(),"nomina-"+Guid.NewGuid()+".db");
 using(var db=new AppDbContext()) {
 EsquemaBD.Actualizar(db); c.Dias="5,20"; db.ConfiguracionesNomina.Add(c);db.SaveChanges();
 Check(db.IngresosRecibidos.Count()==0,"Configurar fechas no ingresa dinero");
 Nomina.GuardarCobro(db,"nomina-test",DateTime.Today,3200000,"Primera quincena",false);
 Nomina.GuardarCobro(db,"nomina-test",DateTime.Today,3200000,"Primera quincena",false);
 Check(db.IngresosRecibidos.Count()==1,"Confirmar el mismo pago dos veces no duplica");
 Check(Nomina.Saldo(db,DateTime.Today)==3200000,"Saldo aumenta solo con la quincena recibida");
 c.Dias="10,25";db.SaveChanges();Check(db.IngresosRecibidos.Count()==1,"Editar el calendario conserva pagos recibidos");
 rechazo=false;try{Nomina.GuardarCobro(db,"futuro",DateTime.Today.AddDays(1),3200000,"Pago futuro",false);}catch(ArgumentException){rechazo=true;}Check(rechazo,"Rechaza cobros con fecha futura");
 }
 NominaNetworkTest.Run().GetAwaiter().GetResult();
 ReplicaNubeTest.Run().GetAwaiter().GetResult();
 ColoresPagoTest.Run();
 ActualizacionesTest.Run().GetAwaiter().GetResult();
 var feed=Path.GetFullPath("Instalador/Salida/Actualizaciones/1.9.0");
 if(File.Exists(Path.Combine(feed,"releases.win.json"))) ActualizacionesTest.ProbarPaquete(feed).GetAwaiter().GetResult();
 // Verificar carga del XAML real con sus estilos, sin usar la base personal.
 var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
 app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source=new Uri("pack://application:,,,/MisFinanzas;component/Estilos/AppTheme.xaml") });
 var vista=new NominaView();var win=new Window{Content=vista,Width=1000,Height=950,Left=-20000,ShowInTaskbar=false,ShowActivated=false};win.Show();
 var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,()=>frame.Continue=false);Dispatcher.PushFrame(frame);
 Check(vista.IsLoaded,"Formulario WPF carga y valida sus recursos");
 var bmp=new RenderTargetBitmap((int)vista.ActualWidth,(int)vista.ActualHeight,96,96,PixelFormats.Pbgra32);bmp.Render(vista);var enc=new PngBitmapEncoder();enc.Frames.Add(BitmapFrame.Create(bmp));using(var f=File.Create(Path.Combine(AppContext.BaseDirectory,"nomina.png")))enc.Save(f);
 Check(((FrameworkElement)vista.FindName("reglas")).Visibility==Visibility.Collapsed && ((FrameworkElement)vista.FindName("movimientos")).Visibility==Visibility.Visible,"Ingresos muestra cobros y oculta las reglas");
 var configuracion=new MisFinanzas.Modulos.ConfiguracionView(); win.Content=configuracion;
 frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,()=>frame.Continue=false);Dispatcher.PushFrame(frame);
 var reglas=(NominaView)configuracion.FindName("nomina");
 reglas.GetType().GetMethod("AbrirEditor",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.Invoke(reglas,null);
 var selector=(System.Windows.Controls.ComboBox)reglas.FindName("frecuencia");
 selector.SelectedItem="Quincenal";
 Check(selector.Items.Cast<string>().SequenceEqual(new[]{"Mensual","Quincenal","Semanal"}),"Solo se ofrecen las tres frecuencias solicitadas");
 Check(((System.Windows.Controls.ComboBox)reglas.FindName("dias")).Items.Count==31 && ((FrameworkElement)reglas.FindName("panelSegundoDia")).Visibility==Visibility.Visible,"Quincenal muestra dos casillas de día independientes");
 selector.SelectedItem="Mensual";
 Check(((FrameworkElement)reglas.FindName("panelDias")).Visibility==Visibility.Visible && ((FrameworkElement)reglas.FindName("panelSegundoDia")).Visibility==Visibility.Collapsed,"Mensual muestra solo un día");
 selector.SelectedItem="Semanal";
 Check(((FrameworkElement)reglas.FindName("panelDias")).Visibility==Visibility.Collapsed && ((FrameworkElement)reglas.FindName("panelSemana")).Visibility==Visibility.Visible,"Semanal muestra el día de la semana");
 Check(reglas.IsLoaded && ((FrameworkElement)reglas.FindName("reglas")).Visibility==Visibility.Visible && ((FrameworkElement)reglas.FindName("movimientos")).Visibility==Visibility.Collapsed,"Configuración muestra las reglas sin el formulario de cobros");
 var cuentasVista=new CuentasView();win.Width=1360;win.Height=820;win.Content=cuentasVista;
 frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,()=>frame.Continue=false);Dispatcher.PushFrame(frame);
 var ejemplos=new[]{("Pendiente a más de una semana",10,false),("Vence en una semana",7,false),("Vence hoy",0,false),("Cuenta vencida",-1,false),("Cuenta pagada",-5,true)};
 var filas=ejemplos.Select((x,i)=>{var vence=DateTime.Today.AddDays(x.Item2);var movimiento=new Movimiento{Id=9900+i,Descripcion=x.Item1,Fecha=vence,DiaPago=vence.Day,Venta=150000,Frecuencia=Frecuencias.Mensual,Categoria="Servicios"};return new CuentaMes(movimiento,vence,x.Item3?new PagoCuenta{Monto=150000}:null);}).ToList();
 ((System.Windows.Controls.ItemsControl)cuentasVista.FindName("icCuentas")).ItemsSource=filas;
 win.UpdateLayout();
 // Verificar las etiquetas y el cambio al pasar el cursor sobre el pago registrado.
 System.Collections.Generic.IEnumerable<DependencyObject> Visuales(DependencyObject nodo) { yield return nodo;for(int i=0;i<VisualTreeHelper.GetChildrenCount(nodo);i++)foreach(var hijo in Visuales(VisualTreeHelper.GetChild(nodo,i)))yield return hijo; }
 var botones=Visuales(cuentasVista).OfType<System.Windows.Controls.Button>().Where(b=>b.Tag is bool).ToList();
 Check(!Visuales((System.Windows.Controls.ItemsControl)cuentasVista.FindName("icCuentas")).OfType<System.Windows.Controls.CheckBox>().Any(),"La tabla ya no muestra casillas de selección");
 var pendienteBoton=botones.First(b=>b.DataContext is CuentaMes cm && !cm.Pagada);
 var pagadoBoton=botones.First(b=>b.DataContext is CuentaMes cm && cm.Pagada);
 string Etiqueta(System.Windows.Controls.Button b)=>((System.Windows.Controls.TextBlock)b.Template.FindName("textoPago",b)).Text;
 Check(Etiqueta(pendienteBoton)=="Marcar pagado" && Etiqueta(pagadoBoton)=="Pagado","Botón pendiente y cápsula pagada usan los textos de referencia");
 var llaveHover=(DependencyPropertyKey)typeof(UIElement).GetField("IsMouseOverPropertyKey",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!.GetValue(null)!;
 pagadoBoton.SetValue(llaveHover,true);win.UpdateLayout();
 Check(Etiqueta(pagadoBoton)=="Deshacer","Al pasar el cursor por Pagado se muestra Deshacer");
 pagadoBoton.SetValue(llaveHover,false);win.UpdateLayout();Check(Etiqueta(pagadoBoton)=="Pagado","Al retirar el cursor vuelve a Pagado");
 var cuentaPrueba=(CuentaMes)pendienteBoton.DataContext;
 using(var dbBoton=new AppDbContext()){dbBoton.Movimientos.Add(cuentaPrueba.Cuenta);dbBoton.SaveChanges();}
 pendienteBoton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
 using(var dbBoton=new AppDbContext()){Check(dbBoton.PagosCuenta.Any(p=>p.MovimientoId==cuentaPrueba.Cuenta.Id && p.Mes==MisFinanzas.Periodo.Clave(cuentaPrueba.Mes)) && cuentaPrueba.Pagada,"Marcar pagado guarda primero y actualiza el estado");}
 pendienteBoton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
 using(var dbBoton=new AppDbContext()){Check(!dbBoton.PagosCuenta.Any(p=>p.MovimientoId==cuentaPrueba.Cuenta.Id && p.Mes==MisFinanzas.Periodo.Clave(cuentaPrueba.Mes)) && !cuentaPrueba.Pagada,"Deshacer retira el pago del mes y vuelve a pendiente");}
 var coloresBmp=new RenderTargetBitmap((int)cuentasVista.ActualWidth,(int)cuentasVista.ActualHeight,96,96,PixelFormats.Pbgra32);coloresBmp.Render(cuentasVista);var coloresPng=new PngBitmapEncoder();coloresPng.Frames.Add(BitmapFrame.Create(coloresBmp));using(var f=File.Create(Path.Combine(AppContext.BaseDirectory,"colores-cuentas.png")))coloresPng.Save(f);
 win.Close(); app.Shutdown();
 }
}
