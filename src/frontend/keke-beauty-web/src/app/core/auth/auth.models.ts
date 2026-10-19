import { HttpErrorResponse } from '@angular/common/http';

export interface UserProfile {
  readonly id: string;
  readonly email: string;
  readonly firstName: string;
  readonly lastName: string;
  readonly roles: readonly string[];
  readonly twoFactorEnabled: boolean;
}

export interface AuthResponse {
  readonly accessToken: string;
  readonly expiresAt: string;
  readonly user: UserProfile;
  /** Present only once, right after two-factor enrolment. */
  readonly recoveryCodes: readonly string[] | null;
  /** Inactivity, in minutes, after which the session ends (admin setting). */
  readonly idleMinutes?: number;
}

export type ChallengeStatus = 'TwoFactorRequired' | 'TwoFactorSetupRequired' | 'TrustedDevice';

export interface ChallengeResponse {
  readonly status: ChallengeStatus;
  readonly challengeToken: string;
}

export interface TwoFactorSetup {
  readonly secret: string;
  readonly otpauthUri: string;
}

export interface ProviderStatus {
  readonly enabled: boolean;
  readonly clientId: string | null;
}

export interface ProvidersResponse {
  readonly password: boolean;
  readonly google: ProviderStatus;
  readonly whatsApp: ProviderStatus;
}

export interface SessionInfo {
  readonly id: string;
  readonly startedAt: string;
  readonly deviceInfo: string | null;
  readonly ipAddress: string | null;
  readonly isCurrent: boolean;
}

export interface TrustedDeviceInfo {
  readonly id: string;
  readonly createdAt: string;
  readonly lastUsedAt: string;
  readonly expiresAt: string;
  readonly deviceInfo: string | null;
  readonly ipAddress: string | null;
  readonly isCurrent: boolean;
}

export type AccountRole = 'Client' | 'PartnerOwner';

export interface RegisterPayload {
  readonly email: string;
  readonly password: string;
  readonly firstName: string;
  readonly lastName: string;
  readonly role: AccountRole;
  readonly acceptTerms: boolean;
}

interface ApiProblem {
  readonly title?: string;
  readonly code?: string;
  readonly errors?: Record<string, string[]>;
}

/** Human-readable (French) message for a failed API call, never leaking technical details. */
export function problemMessage(
  error: unknown,
  fallback = 'Une erreur est survenue. Réessayez.',
): string {
  if (!(error instanceof HttpErrorResponse)) {
    return fallback;
  }

  if (error.status === 0) {
    return 'Impossible de joindre le serveur. Vérifiez votre connexion.';
  }

  if (error.status === 429) {
    return 'Trop de tentatives. Patientez une minute puis réessayez.';
  }

  const body = error.error as ApiProblem | null;
  const firstValidation = body?.errors ? Object.values(body.errors)[0]?.[0] : undefined;
  return firstValidation ?? body?.title ?? fallback;
}

export function problemCode(error: unknown): string | undefined {
  return error instanceof HttpErrorResponse ? (error.error as ApiProblem | null)?.code : undefined;
}
