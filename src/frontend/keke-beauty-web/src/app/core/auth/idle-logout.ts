import { DOCUMENT } from '@angular/common';
import { Injectable, effect, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from './auth.service';

/** How often the inactivity clock is checked; the sign-out can therefore be this late. */
const CHECK_EVERY_MS = 15_000;
const WARNING_THRESHOLD_MS = 60_000;
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

  /** Non-null during the final 60 seconds before idle sign-out. */
  readonly warningSecondsLeft = signal<number | null>(null);

  private readonly remember = (): void => {
    this.lastActivity = Date.now();
    if (this.warningSecondsLeft() !== null) {
      this.warningSecondsLeft.set(null);
    }
  };

  constructor() {
    effect((onCleanup) => {
      const minutes = this.auth.idleMinutes();
      if (minutes === null) {
        this.warningSecondsLeft.set(null);
        return;
      }
      this.start(minutes * MINUTE_MS);
      onCleanup(() => this.stop());
    });
  }

  /** Resets the inactivity timer and renews the session when the user clicks "Rester connecté(e)". */
  staySignedIn(): void {
    this.lastActivity = Date.now();
    this.warningSecondsLeft.set(null);
    void this.auth.refresh();
  }

  private start(limitMs: number): void {
    this.lastActivity = Date.now();
    this.warningSecondsLeft.set(null);
    const target = this.document.defaultView;
    if (!target) {
      return;
    }
    for (const name of ACTIVITY_EVENTS) {
      target.addEventListener(name, this.remember, { passive: true });
    }
    this.timer = setInterval(() => {
      const elapsed = Date.now() - this.lastActivity;
      if (elapsed >= limitMs) {
        this.stop();
        void this.auth
          .logout()
          .then(() => this.router.navigate(['/connexion'], { queryParams: { inactif: 1 } }));
        return;
      }

      const remainingMs = limitMs - elapsed;
      if (remainingMs <= WARNING_THRESHOLD_MS) {
        this.warningSecondsLeft.set(Math.max(1, Math.ceil(remainingMs / 1000)));
      } else if (this.warningSecondsLeft() !== null) {
        this.warningSecondsLeft.set(null);
      }
    }, CHECK_EVERY_MS);
  }

  private stop(): void {
    this.warningSecondsLeft.set(null);
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
