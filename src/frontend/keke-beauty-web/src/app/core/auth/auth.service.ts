import { HttpClient, httpResource } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import {
  AuthResponse,
  ChallengeResponse,
  ChallengeStatus,
  ProvidersResponse,
  RegisterPayload,
  TwoFactorSetup,
  UserProfile,
} from './auth.models';

/** Non-sensitive hint that a refresh cookie probably exists, so visitors do not trigger a pointless 401. */
const SESSION_HINT_KEY = 'kb_session';

interface PendingChallenge {
  readonly status: ChallengeStatus;
  readonly token: string;
}

/**
 * Client-side session. The access token lives in memory only; the refresh token is an httpOnly cookie
 * the browser sends by itself. Passwords never open a session: they yield a two-factor challenge.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);

  private readonly session = signal<AuthResponse | null>(null);
  private refreshInFlight: Promise<boolean> | null = null;

  readonly user = computed(() => this.session()?.user ?? null);
  readonly accessToken = computed(() => this.session()?.accessToken ?? null);
  readonly isAuthenticated = computed(() => this.session() !== null);
  /** Minutes of inactivity before sign-out; null while signed out. */
  readonly idleMinutes = computed(() => {
    const session = this.session();
    return session ? (session.idleMinutes ?? 60) : null;
  });

  readonly challenge = signal<PendingChallenge | null>(null);
  readonly recoveryCodes = signal<readonly string[] | null>(null);

  readonly providers = httpResource<ProvidersResponse>(() => '/api/v1/auth/providers');

  /** Restores a previous session at startup, if the browser remembers one. */
  async restore(): Promise<void> {
    if (readHint()) {
      await this.refresh();
    }
  }

  /** Single-flight refresh: concurrent callers share one request (rotation would revoke duplicates). */
  refresh(): Promise<boolean> {
    this.refreshInFlight ??= this.doRefresh().finally(() => {
      this.refreshInFlight = null;
    });
    return this.refreshInFlight;
  }

  async register(payload: RegisterPayload): Promise<void> {
    await firstValueFrom(this.http.post<void>('/api/v1/auth/register', payload));
  }

  async confirmEmail(token: string): Promise<void> {
    await firstValueFrom(this.http.post<void>('/api/v1/auth/confirm-email', { token }));
  }

  async forgotPassword(email: string): Promise<void> {
    await firstValueFrom(this.http.post<void>('/api/v1/auth/forgot-password', { email }));
  }

  async resetPassword(token: string, newPassword: string): Promise<void> {
    await firstValueFrom(
      this.http.post<void>('/api/v1/auth/reset-password', { token, newPassword }),
    );
  }

  async login(email: string, password: string): Promise<ChallengeStatus> {
    const response = await firstValueFrom(
      this.http.post<ChallengeResponse>('/api/v1/auth/login', { email, password }),
    );
    return this.keepChallenge(response);
  }

  async loginWithGoogle(idToken: string): Promise<ChallengeStatus> {
    const response = await firstValueFrom(
      this.http.post<ChallengeResponse>('/api/v1/auth/google', { idToken }),
    );
    return this.keepChallenge(response);
  }

  async requestWhatsAppOtp(phone: string): Promise<void> {
    await firstValueFrom(this.http.post<void>('/api/v1/auth/whatsapp/request', { phone }));
  }

  async verifyWhatsAppOtp(phone: string, code: string): Promise<ChallengeStatus> {
    const response = await firstValueFrom(
      this.http.post<ChallengeResponse>('/api/v1/auth/whatsapp/verify', { phone, code }),
    );
    return this.keepChallenge(response);
  }

  beginTwoFactorSetup(): Promise<TwoFactorSetup> {
    return firstValueFrom(
      this.http.post<TwoFactorSetup>('/api/v1/auth/2fa/setup', {
        challengeToken: this.requireChallenge().token,
      }),
    );
  }

  async enableTwoFactor(code: string, rememberDevice = false): Promise<void> {
    const challengeToken = this.requireChallenge().token;
    const body = rememberDevice
      ? { challengeToken, code, rememberDevice: true }
      : { challengeToken, code };
    const response = await firstValueFrom(
      this.http.post<AuthResponse>('/api/v1/auth/2fa/enable', body),
    );
    this.accept(response);
    this.recoveryCodes.set(response.recoveryCodes);
  }

  async verifyTwoFactor(code: string, rememberDevice = false): Promise<void> {
    const challengeToken = this.requireChallenge().token;
    const body = rememberDevice
      ? { challengeToken, code, rememberDevice: true }
      : { challengeToken, code };
    const response = await firstValueFrom(
      this.http.post<AuthResponse>('/api/v1/auth/2fa/verify', body),
    );
    this.accept(response);
  }

  async completeTrustedDevice(): Promise<void> {
    const response = await firstValueFrom(
      this.http.post<AuthResponse>('/api/v1/auth/2fa/trusted', {
        challengeToken: this.requireChallenge().token,
      }),
    );
    this.accept(response);
  }

  async logout(): Promise<void> {
    try {
      await firstValueFrom(this.http.post<void>('/api/v1/auth/logout', {}));
    } catch {
      // The server session may already be gone; local state is cleared either way.
    }

    this.clear();
    await this.router.navigateByUrl('/');
  }

  updateUser(user: UserProfile): void {
    this.session.update((current) => (current ? { ...current, user } : current));
  }

  clearChallenge(): void {
    this.challenge.set(null);
    this.recoveryCodes.set(null);
  }

  private async doRefresh(): Promise<boolean> {
    try {
      this.accept(await firstValueFrom(this.http.post<AuthResponse>('/api/v1/auth/refresh', {})));
      return true;
    } catch {
      this.clear();
      return false;
    }
  }

  private keepChallenge(response: ChallengeResponse): ChallengeStatus {
    this.challenge.set({ status: response.status, token: response.challengeToken });
    return response.status;
  }

  private requireChallenge(): PendingChallenge {
    const challenge = this.challenge();
    if (!challenge) {
      throw new Error('No pending sign-in challenge.');
    }

    return challenge;
  }

  private accept(response: AuthResponse): void {
    this.session.set(response);
    this.challenge.set(null);
    writeHint(true);
  }

  private clear(): void {
    this.session.set(null);
    this.challenge.set(null);
    this.recoveryCodes.set(null);
    writeHint(false);
  }
}

function readHint(): boolean {
  try {
    return localStorage.getItem(SESSION_HINT_KEY) === '1';
  } catch {
    return false;
  }
}

function writeHint(present: boolean): void {
  try {
    if (present) {
      localStorage.setItem(SESSION_HINT_KEY, '1');
    } else {
      localStorage.removeItem(SESSION_HINT_KEY);
    }
  } catch {
    // Storage can be unavailable (private mode); the hint is only an optimisation.
  }
}
