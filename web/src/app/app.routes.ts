import { inject } from '@angular/core';
import { Router, Routes } from '@angular/router';
import { environment } from '../environments/environment';
import { AuthService } from './core/services/auth.service';
import { Dashboard } from './features/dashboard/dashboard';
import { CommandCenter } from './features/command-center/command-center';
import { Login } from './features/auth/login';
import { AuthCallback } from './features/auth/callback';

export const routes: Routes = [
  { path: 'login', component: Login, title: 'Sign in | IncidentLens' },
  { path: 'auth/callback', component: AuthCallback, title: 'Signing in | IncidentLens' },
  { path: '', component: CommandCenter, title: 'IncidentLens | Command center',
    canActivate: [() => environment.demoMode || inject(AuthService).token().length > 0 ||
      inject(Router).parseUrl('/login')] },
  { path: 'workspace', component: Dashboard, title: 'IncidentLens | Incident workspace',
    canActivate: [() => environment.demoMode || inject(AuthService).token().length > 0 ||
      inject(Router).parseUrl('/login')] },
  { path: '**', redirectTo: '' },
];
