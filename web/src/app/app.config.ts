import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { routes } from './app.routes';
import { provideIncidentGateway } from './core/services/incident-gateway';
import { bearerTokenInterceptor } from './core/services/auth.interceptor';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideHttpClient(withFetch(), withInterceptors([bearerTokenInterceptor])),
    provideRouter(routes),
    provideIncidentGateway(),
  ]
};
