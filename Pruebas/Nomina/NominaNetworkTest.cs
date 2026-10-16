using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using MisFinanzas.Modulos;
class NominaNetworkTest {
 public static async Task Run() {
  using var handler=new NubeSimulada();using var cliente=new HttpClient(handler);
  AppDbContext.RutaBD=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"nube-nomina-"+Guid.NewGuid()+".db");
  using(var db=new AppDbContext()) {
   EsquemaBD.Actualizar(db);db.ConfiguracionesNomina.Add(new ConfiguracionNomina { Frecuencia="Quincenal",Dias="5,20",Confirmada=true,MontoPorPago=3200000,ActualizadaUtc=DateTime.UtcNow,Pendiente=true });db.SaveChanges();
   Nomina.GuardarCobro(db,"nomina-pago",DateTime.Today,3200000,"Quincena",false);
  }
  async Task Sync() => await (Task)typeof(SincronizacionCelular).GetMethod("SincronizarNominaAsync",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,new object[]{"prueba","sin-token-real",cliente})!;
  void Check(bool ok,string texto){if(!ok)throw new Exception(texto);Console.WriteLine("OK "+texto);}
  await Sync();
  using(var db=new AppDbContext()) Check(!db.ConfiguracionesNomina.Find(1)!.Pendiente && !db.IngresosRecibidos.Find("nomina-pago")!.Pendiente,"PC sube calendario y cobro; confirma ambos solo después del envío");
  var plan=handler.Docs.Values.Single(d=>d["name"]!.GetValue<string>().EndsWith("/nomina"));
  plan["fields"]!["dias"]!["stringValue"]="10,25";
  plan["fields"]!["actualizada"]!["timestampValue"]=DateTime.UtcNow.AddMinutes(1).ToString("O");
  await Sync();
  using(var db=new AppDbContext()) Check(db.ConfiguracionesNomina.Find(1)!.Dias=="10,25","Editar días en celular actualiza el PC");
  var pago=handler.Docs.Values.Single(d=>d["name"]!.GetValue<string>().EndsWith("/nomina-pago"));
  pago["fields"]!["borrado"]!["booleanValue"]=true;
  pago["fields"]!["actualizada"]!["timestampValue"]=DateTime.UtcNow.AddMinutes(1).ToString("O");
  await Sync();

  using(var db=new AppDbContext()) Check(db.IngresosRecibidos.Find("nomina-pago")!.Borrado && Nomina.Saldo(db,DateTime.Today)==0,"Eliminar en celular se refleja en el PC sin reingresar el cobro");
  using(var db=new AppDbContext()) Nomina.GuardarCobro(db,"otro",DateTime.Today,100,"Extra",false);
  handler.Fallar=true; bool fallo=false;try{await Sync();}catch(HttpRequestException){fallo=true;}
  using(var db=new AppDbContext()) Check(fallo && db.IngresosRecibidos.Find("otro")!.Pendiente,"Fallo de red conserva el cobro pendiente para reintentar");
 }
 class NubeSimulada:HttpMessageHandler {
  public Dictionary<string,JsonNode> Docs=new();public bool Fallar;
  protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct){
   if(Fallar)return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
   var path=r.RequestUri!.AbsolutePath;
   if(r.Method==HttpMethod.Patch){var d=JsonNode.Parse(await r.Content!.ReadAsStringAsync())!;d["name"]=path;d["updateTime"]=DateTime.UtcNow.ToString("O");Docs[path]=d;return Respuesta(d);}
   if(path.EndsWith("/ingresosRecibidos"))return Respuesta(new JsonObject{["documents"]=new JsonArray(Docs.Where(x=>x.Key.Contains("/ingresosRecibidos/")).Select(x=>x.Value.DeepClone()).ToArray())});
   return Docs.TryGetValue(path,out var doc)?Respuesta(doc):new HttpResponseMessage(HttpStatusCode.NotFound);
  }
  static HttpResponseMessage Respuesta(JsonNode d)=>new(HttpStatusCode.OK){Content=new StringContent(d.ToJsonString(),Encoding.UTF8,"application/json")};
 }
}
