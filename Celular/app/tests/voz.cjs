// Ejercita los manejadores reales con reconocimiento y eventos simulados; no usa el micrófono.
const ts = require('typescript');
const fs = require('node:fs');
const assert = require('node:assert/strict');
const codigo = ts.transpileModule(fs.readFileSync('src/app/paginas/registrar/registrar.ts','utf8'), {
  compilerOptions: { module:ts.ModuleKind.CommonJS, target:ts.ScriptTarget.ES2022, experimentalDecorators:true }
}).outputText;
let destruir, avisos=[];
const core = {
  DestroyRef: class {}, ChangeDetectionStrategy:{OnPush:0}, Component:()=>c=>c,
  signal:(v)=>{ const f=()=>v; f.set=n=>{v=n}; f.update=fn=>{v=fn(v)};return f; },
  computed:fn=>fn, effect:()=>{}, viewChild:()=>()=>undefined,
  inject:token=>token===core.DestroyRef ? destruir : {mostrar:t=>avisos.push(t),registrar:()=>()=>{}},
};
global.document = Object.assign(new EventTarget(),{hidden:false});
global.window = new EventTarget();
class Reconocimiento {
  static instancias=[]; static falla=false;
  constructor(){ this.stops=0; this.aborts=0; Reconocimiento.instancias.push(this); }
  start(){ if(Reconocimiento.falla) throw new Error('No disponible'); }
  stop(){ this.stops++; }
  abort(){ this.aborts++; }
}
window.SpeechRecognition = Reconocimiento;
const modulo={exports:{}};
new Function('require','exports','module',codigo)(nombre=> {
  if(nombre==='@angular/core') return core;
  if(nombre.endsWith('/categorias')) return {CATEGORIAS:[]};
  return new Proxy({}, {get:(_,k)=>k==='__esModule' ? true : class {}});
},modulo.exports,modulo);
const {Registrar}=modulo.exports;
let ahora=1000;Date.now=()=>ahora;const sostener=()=>{ahora+=600;};
const evento=(id=7)=>({isPrimary:true,button:0,pointerId:id,currentTarget:{setPointerCapture(){}},preventDefault(){}});
const tecla=(key=' ',repeat=false)=>({key,repeat,preventDefault(){}});
function crear(){ const callbacks=[]; destruir={destroyed:false,onDestroy:cb=>callbacks.push(cb)}; const d=destruir;
  const c=new Registrar();return {c,fin(){d.destroyed=true;callbacks.forEach(cb=>cb());}}; }
let x=crear();let c=x.c;
c.sostenerVoz(evento());let r=Reconocimiento.instancias.at(-1);
assert.ok(c.escuchando() && c.sosteniendo());
c.soltarVoz(evento(8));assert.equal(r.stops,0,'Otro dedo no detiene la sesión');
sostener();c.soltarVoz(evento());assert.equal(r.stops,1);assert.equal(c.sosteniendo(),false);
let texto='';c.alEscribirFrase=t=>texto=t;r.onresult({results:[[{transcript:'gasté 25 mil en almuerzo'}]]});
assert.equal(texto,'gasté 25 mil en almuerzo','Conserva el último texto al soltar');
r.onend();assert.equal(c.escuchando(),false);x.fin();
x=crear();c=x.c;c.dictar();r=Reconocimiento.instancias.at(-1);c.dictar();assert.equal(r.stops,1,'Conserva iniciar/detener por toque');r.onend();x.fin();
x=crear();c=x.c;c.sostenerVoz({...evento(),isPrimary:false});assert.equal(c.escuchando(),false);x.fin();
x=crear();c=x.c;c.sostenerVoz(evento());r=Reconocimiento.instancias.at(-1);sostener();c.soltarVoz(evento());r.onstart();
assert.equal(r.stops,2,'Soltar antes del permiso detiene también un inicio tardío');r.onend();x.fin();
x=crear();c=x.c;c.sostenerTecla(tecla());r=Reconocimiento.instancias.at(-1);const n=Reconocimiento.instancias.length;c.sostenerTecla(tecla(' ',true));
assert.equal(Reconocimiento.instancias.length,n,'Repetir la tecla no duplica sesiones');sostener();c.soltarTecla(tecla());assert.equal(r.stops,1);r.onend();x.fin();
x=crear();c=x.c;c.sostenerVoz(evento());r=Reconocimiento.instancias.at(-1);document.hidden=true;document.dispatchEvent(new Event('visibilitychange'));
assert.equal(r.stops,1,'Salir de la app detiene la grabación');document.hidden=false;x.fin();assert.equal(r.aborts,1,'Cambiar de pantalla libera el micrófono');assert.equal(r.onresult,null);
x=crear();c=x.c;Reconocimiento.falla=true;c.sostenerVoz(evento());assert.equal(c.escuchando(),false);assert.equal(c.sosteniendo(),false);Reconocimiento.falla=false;x.fin();
x=crear();c=x.c;window.SpeechRecognition=undefined;c.sostenerVoz(evento());assert.equal(c.sosteniendo(),false);assert.ok(avisos.at(-1).includes('teclado'),'Conserva dictado con teclado si no hay API de voz');x.fin();
// Toque corto en el mismo botón: sigue escuchando (pausa visible) y el siguiente toque termina
window.SpeechRecognition=Reconocimiento;x=crear();c=x.c;c.sostenerVoz(evento());r=Reconocimiento.instancias.at(-1);c.soltarVoz(evento());
assert.ok(c.escuchando() && !c.sosteniendo(),'Un toque deja escuchando con la pausa');assert.equal(r.stops,0);
c.terminarPulsacion();assert.equal(r.stops,0,'Perder el foco sin pulsar no corta el dictado');
const nTap=Reconocimiento.instancias.length;c.sostenerVoz(evento(9));assert.equal(Reconocimiento.instancias.length,nTap,'Tocar la pausa no abre otra sesión');
c.soltarVoz(evento(9));assert.equal(r.stops,1,'Tocar la pausa termina el dictado');r.onend();assert.equal(c.escuchando(),false);x.fin();
x=crear();c=x.c;c.sostenerTecla(tecla('Enter'));r=Reconocimiento.instancias.at(-1);c.soltarTecla(tecla('Enter'));assert.equal(r.stops,0,'Enter corto también deja escuchando');
c.sostenerTecla(tecla('Enter'));c.soltarTecla(tecla('Enter'));assert.equal(r.stops,1,'Otro Enter termina');r.onend();x.fin();
console.log('OK Voz: sostener/soltar, toque con pausa, multitoque, último texto, permiso tardío, teclado, salida, limpieza y errores.');

// La corrección de Safari usa un listener no pasivo sobre el botón, sin bloquear el resto de la página.
let elemento = new EventTarget(); let opciones, limpiar;
const agregar = elemento.addEventListener.bind(elemento);
elemento.addEventListener=(tipo,handler,opts)=>{if(tipo==='touchstart') opciones=opts;agregar(tipo,handler,opts);};
const coreDirectiva={Directive:()=>c=>c,ElementRef:class{},DestroyRef:class{},inject:token=>token===coreDirectiva.ElementRef ? {nativeElement:elemento} : {onDestroy:fn=>limpiar=fn}};
const codigoDirectiva=ts.transpileModule(fs.readFileSync('src/app/compartido/sin-seleccion-tactil.ts','utf8'),{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022,experimentalDecorators:true}}).outputText;
const modDirectiva={exports:{}};new Function('require','exports','module',codigoDirectiva)(()=>coreDirectiva,modDirectiva.exports,modDirectiva);
new modDirectiva.exports.SinSeleccionTactil();
assert.equal(opciones.passive,false,'El listener táctil puede cancelar el gesto nativo de iPhone');
const contacto=new Event('touchstart',{cancelable:true});elemento.dispatchEvent(contacto);assert.equal(contacto.defaultPrevented,true);
const seleccion=new Event('selectstart',{cancelable:true});elemento.dispatchEvent(seleccion);assert.equal(seleccion.defaultPrevented,true);
limpiar();const despues=new Event('touchstart',{cancelable:true});elemento.dispatchEvent(despues);assert.equal(despues.defaultPrevented,false,'Retira los listeners al salir de la pantalla');
window.SpeechRecognition=Reconocimiento;x=crear();c=x.c;c.sostenerVoz(evento());r=Reconocimiento.instancias.at(-1);
c.soltarTacto({touches:[{}]});assert.equal(r.stops,0,'Otro dedo aún en contacto no termina el dictado');
sostener();c.soltarTacto({touches:[]});assert.equal(r.stops,1,'touchend también detiene el dictado en Safari');r.onend();x.fin();
console.log('OK iPhone: evita selección con listener activo, limpia eventos y conserva detener al soltar.');
