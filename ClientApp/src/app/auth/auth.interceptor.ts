import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { AuthService } from './auth.service';

/** Auth endpoints handle their own 401s (wrong password, anonymous /me, logout of a dead session). */
const SELF_HANDLED = ['/api/auth/login', '/api/auth/register', '/api/auth/me', '/api/auth/logout'];

/**
 * A 401 from any other API call means the session ended (expired, logged out in another tab):
 * drop the cached user and send the visitor to login, returning to the current page afterwards.
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  return next(req).pipe(
    catchError((err: unknown) => {
      if (
        err instanceof HttpErrorResponse &&
        err.status === 401 &&
        req.url.startsWith('/api/') &&
        !SELF_HANDLED.some((url) => req.url.startsWith(url))
      ) {
        auth.clearSession();
        if (!router.url.startsWith('/login')) {
          void router.navigateByUrl(router.createUrlTree(['/login'], { queryParams: { returnUrl: router.url } }));
        }
      }
      return throwError(() => err);
    }),
  );
};
