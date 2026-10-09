import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', loadComponent: () => import('./pages/overview/overview').then((m) => m.Overview), title: 'Portfolio overview' },
  { path: 'assets', loadComponent: () => import('./pages/assets/assets').then((m) => m.Assets), title: 'Asset explorer' },
  { path: '**', redirectTo: '' },
];
