import { Configuracion } from './paginas/configuracion/configuracion';
import { Routes } from '@angular/router';
import { Gastos } from './paginas/gastos/gastos';
import { Registrar } from './paginas/registrar/registrar';
import { Resumen } from './paginas/resumen/resumen';

export const routes: Routes = [
  { path: '', component: Registrar, title: 'Mis Finanzas' },
  { path: 'gastos', component: Gastos, title: 'Mis gastos · Mis Finanzas' },
  { path: 'resumen', component: Resumen, title: 'Resumen · Mis Finanzas' },
  { path: 'configuracion', component: Configuracion, title: 'Cuenta · Mis Finanzas' },
  { path: '**', redirectTo: '' },
];
