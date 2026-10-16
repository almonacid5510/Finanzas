using MisFinanzas.Modulos;
using MisFinanzas;
static class ColoresPagoTest
{
    static void Check(bool ok,string mensaje) { if(!ok) throw new Exception(mensaje);Console.WriteLine("OK "+mensaje); }
    public static void Run()
    {
        var hoy=new DateTime(2026,10,15);
        Check(EstadoPago.Evaluar(false,hoy.AddDays(8),hoy).FondoFila=="#FFFFFF","Más de siete días: fila blanca");
        Check(EstadoPago.Evaluar(false,hoy.AddDays(7),hoy).FondoFila=="#FEFCE8","Exactamente siete días: fila amarilla");
        Check(EstadoPago.Evaluar(false,hoy,hoy).Texto=="Vence hoy" && EstadoPago.Evaluar(false,hoy,hoy).FondoFila=="#FEFCE8","El día de vencimiento sigue amarillo");
        Check(EstadoPago.Evaluar(false,hoy.AddDays(-1),hoy).FondoFila=="#FEF3F2","Después del plazo: fila roja");
        Check(EstadoPago.Evaluar(true,hoy.AddDays(-10),hoy).FondoFila=="#E7F5EF","Pagado tiene prioridad verde aunque la fecha haya pasado");
        Check(EstadoPago.Evaluar(false,null,hoy).FondoFila=="#FFFFFF","Sin fecha: blanco sin inventar vencimientos");
        Check(EstadoPago.Evaluar(false,new DateTime(2027,1,3),new DateTime(2026,12,28)).FondoFila=="#FEFCE8","La semana de aviso cruza mes y año");
        var actual=Periodo.InicioMes(DateTime.Today);
        var cuenta=new Movimiento{Id=9001,Tipo="Cuenta",Descripcion="Prueba de colores",Fecha=actual,DiaPago=DateTime.Today.Day,Frecuencia=Frecuencias.Mensual,Venta=100};
        var pago=new PagoCuenta{MovimientoId=cuenta.Id,Mes=Periodo.Clave(actual),Monto=100};
        var este=CuentasMensuales.DelMes(new[]{cuenta},new[]{pago},actual).Single();
        var siguiente=CuentasMensuales.DelMes(new[]{cuenta},new[]{pago},actual.AddMonths(1)).Single();
        Check(este.Pagada && este.FondoFila=="#E7F5EF" && !siguiente.Pagada && siguiente.FondoFila=="#FFFFFF","El mes nuevo reinicia en blanco sin reutilizar el pago anterior");
        Check(CuentasMensuales.DelMes(new[]{cuenta},new[]{pago},actual).Single().Pagada,"El historial del mes pagado permanece verde");
        bool notifica=false;este.PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(CuentaMes.FondoFila))notifica=true;};este.Pagada=false;
        Check(notifica && este.EstadoTexto=="Vence hoy","Desmarcar un pago actualiza el color inmediatamente");
        var credito=new Credito{Total=1000,Disponible=500,CuotaMensual=100,DiaPago=DateTime.Today.Day,Pagado=true,MesPagado=Periodo.Clave(actual.AddMonths(-1))};
        Check(credito.FondoFila=="#FEFCE8","Una cuota del mes anterior no deja el crédito verde en el mes nuevo");
        credito.MesPagado=Periodo.MesActual;Check(credito.FondoFila=="#E7F5EF","Confirmar cuota del mes actual la pone verde");
    }
}
