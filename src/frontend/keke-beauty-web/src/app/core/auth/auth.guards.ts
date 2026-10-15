import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

/** Routes that require a signed-in user; others are sent to the sign-in page, remembering where they wanted to go. */
export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  return (
    auth.isAuthenticated() ||
    inject(Router).createUrlTree(['/connexion'], { queryParams: { retour: state.url } })
  );
};

/** Sign-in/sign-up pages are pointless for someone already signed in. */
export const guestGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  return !auth.isAuthenticated() || inject(Router).createUrlTree(['/compte']);
};

/** Role-restricted areas (e.g. /pro, /admin later). Mirrors, never replaces, server-side authorization. */
export const roleGuard =
  (...roles: string[]): CanActivateFn =>
  () => {
    const user = inject(AuthService).user();
    return (
      (user !== null && user.roles.some((role) => roles.includes(role))) ||
      inject(Router).createUrlTree(['/'])
    );
  };

/** Only same-site absolute paths are accepted as post-login destinations (prevents open redirects). */
export function safeReturnUrl(candidate: string | null | undefined, fallback = '/compte'): string {
  return candidate && candidate.startsWith('/') && !candidate.startsWith('//')
    ? candidate
    : fallback;
}
