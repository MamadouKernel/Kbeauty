import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, from, switchMap, throwError } from 'rxjs';
import { AuthService } from './auth.service';

/**
 * Adds the Bearer token to API calls (never to /auth/*, which uses the refresh cookie) and, on a 401,
 * refreshes the session once and replays the request.
 */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthService);
  const isApiCall = request.url.startsWith('/api/') && !request.url.startsWith('/api/v1/auth/');
  if (!isApiCall) {
    return next(request);
  }

  const withToken = (token: string | null) =>
    token ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : request;

  return next(withToken(auth.accessToken())).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse && error.status === 401 && auth.isAuthenticated()) {
        return from(auth.refresh()).pipe(
          switchMap((refreshed) =>
            refreshed ? next(withToken(auth.accessToken())) : throwError(() => error),
          ),
        );
      }

      return throwError(() => error);
    }),
  );
};
