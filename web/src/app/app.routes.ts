import { inject } from '@angular/core';
import { Router, Routes } from '@angular/router';
import { environment } from '../environments/environment';
import { AuthService } from './core/services/auth.service';
import { Dashboard } from './features/dashboard/dashboard';
import { Login } from './features/auth/login';
import { AuthCallback } from './features/auth/callback';

export const routes: Routes = [
  { path: 'login', component: Login, title: 'Sign in | IncidentLens' },
  { path: 'auth/callback', component: AuthCallback, title: 'Signing in | IncidentLens' },
  { path: '', component: Dashboard, title: 'IncidentLens | Operations overview',
    canActivate: [() => environment.demoMode || inject(AuthService).token().length > 0 ||
      inject(Router).parseUrl('/login')] },
  { path: '**', redirectTo: '' },
];
