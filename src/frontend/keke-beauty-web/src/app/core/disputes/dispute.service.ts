import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

// Shapes of `DisputeService.cs`. Written by hand until `npm run api:types` is next run against a live API.

export interface Dispute {
  readonly id: string;
  readonly appointmentId: string;
  readonly establishmentId: string;
  readonly establishmentName: string;
  readonly clientName: string;
  readonly appointmentStartsAt: string;
  readonly reason: string;
  readonly description: string;
  readonly status: string;
  readonly resolution: string | null;
  readonly partnerResponse: string | null;
  readonly adminNote: string | null;
  readonly refundStatus: string | null;
  readonly createdAt: string;
  readonly resolvedAt: string | null;
}

export const DISPUTE_REASONS: Readonly<Record<string, string>> = {
  SalonClosed: 'Salon fermé ou introuvable',
  LateOrAbsent: 'Retard important ou absence de l’équipe',
  ServiceNotAsDescribed: 'Prestation non conforme à la description',
  Other: 'Autre problème',
};

export const DISPUTE_RESOLUTIONS: Readonly<Record<string, string>> = {
  RefundedClient: 'Cliente remboursée',
  RejectedClaim: 'Litige rejeté',
};

/** Days after the end of an appointment during which a client can still file a claim (mirrors the server rule). */
export const CLAIM_WINDOW_DAYS = 7;

/** Keke Protect: claims filed by clients, answered by establishments, decided by administrators. */
@Injectable({ providedIn: 'root' })
export class DisputeService {
  private readonly http = inject(HttpClient);

  open(appointmentId: string, reason: string, description: string): Promise<Dispute> {
    return firstValueFrom(
      this.http.post<Dispute>('/api/v1/disputes', { appointmentId, reason, description }),
    );
  }

  mine(): Promise<Dispute[]> {
    return firstValueFrom(this.http.get<Dispute[]>('/api/v1/me/disputes'));
  }

  forEstablishment(establishmentId: string): Promise<Dispute[]> {
    return firstValueFrom(
      this.http.get<Dispute[]>(`/api/v1/pro/establishments/${establishmentId}/disputes`),
    );
  }

  respond(disputeId: string, message: string): Promise<Dispute> {
    return firstValueFrom(
      this.http.post<Dispute>(`/api/v1/pro/disputes/${disputeId}/respond`, { message }),
    );
  }

  adminList(status?: string): Promise<Dispute[]> {
    const params = status ? new HttpParams().set('status', status) : new HttpParams();
    return firstValueFrom(this.http.get<Dispute[]>('/api/v1/admin/disputes', { params }));
  }

  resolve(disputeId: string, resolution: string, note: string): Promise<Dispute> {
    return firstValueFrom(
      this.http.post<Dispute>(`/api/v1/admin/disputes/${disputeId}/resolve`, { resolution, note }),
    );
  }
}

/** True while a client can still file a claim about an appointment that ended at `endsAt`. */
export function canDispute(status: string, endsAt: string, now = Date.now()): boolean {
  const ended = Date.parse(endsAt);
  const windowMs = CLAIM_WINDOW_DAYS * 24 * 3600 * 1000;
  const happened = status === 'Completed' || status === 'NoShow' || status === 'Confirmed';
  return happened && ended <= now && now - ended <= windowMs;
}
