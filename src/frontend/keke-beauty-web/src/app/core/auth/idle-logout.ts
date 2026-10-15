import { DOCUMENT } from '@angular/common';
import { Injectable, effect, inject } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from './auth.service';

/** How often the inactivity clock is checked; the sign-out can therefore be this late. */
const CHECK_EVERY_MS = 15_000;
const ACTIVITY_EVENTS = ['pointerdown', 'keydown', 'touchstart', 'scroll', 'wheel'] as const;
const MINUTE_MS = 60_000;

/**
 * Signs the user out after the inactivity period set by the admins (the API sends it with every session: 60 minutes by default).
 * The server enforces the same delay on the refresh token; this makes the web app react on its own instead of waiting for a 401.
 */
@Injectable({ providedIn: 'root' })
export class IdleLogout {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly document = inject(DOCUMENT);

  private lastActivity = Date.now();
  private timer: ReturnType<typeof setInterval> | null = null;
  private readonly remember = (): void => {
    this.lastActivity = Date.now();
  };

  constructor() {
    effect((onCleanup) => {
      const minutes = this.auth.idleMinutes();
      if (minutes === null) {
        return;
      }
      this.start(minutes * MINUTE_MS);
      onCleanup(() => this.stop());
    });
  }

  private start(limitMs: number): void {
    this.lastActivity = Date.now();
    const target = this.document.defaultView;
    if (!target) {
      return;
    }
    for (const name of ACTIVITY_EVENTS) {
      target.addEventListener(name, this.remember, { passive: true });
    }
    this.timer = setInterval(() => {
      if (Date.now() - this.lastActivity >= limitMs) {
        this.stop();
        void this.auth
          .logout()
          .then(() => this.router.navigate(['/connexion'], { queryParams: { inactif: 1 } }));
      }
    }, CHECK_EVERY_MS);
  }

  private stop(): void {
    if (this.timer !== null) {
      clearInterval(this.timer);
      this.timer = null;
    }
    const target = this.document.defaultView;
    for (const name of ACTIVITY_EVENTS) {
      target?.removeEventListener(name, this.remember);
    }
  }
}
