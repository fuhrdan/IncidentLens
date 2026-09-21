import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AuthService } from './auth.service';

/** Never send the API bearer to an unrelated origin, including OIDC endpoints. */
export const bearerTokenInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  const target = new URL(request.url, window.location.origin);
  const base = new URL(environment.apiBaseUrl, window.location.origin);
  if (target.origin !== base.origin ||
      !target.pathname.startsWith(`${base.pathname.replace(/\/$/, '')}/`)) return next(request);
  const token = auth.token();
  const authorized = token ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : request;
  return next(authorized).pipe(catchError((error: unknown) => {
    if (error instanceof HttpErrorResponse && error.status === 401 && token) {
      auth.clearSession(); void router.navigateByUrl('/login');
    }
    return throwError(() => error);
  }));
};
