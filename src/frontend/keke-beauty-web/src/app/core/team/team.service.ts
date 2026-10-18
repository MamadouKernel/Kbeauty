import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

// Shapes of `TeamDtos.cs`. Written by hand until `npm run api:types` is next run against a live API.

export interface TeamMembership {
  readonly resourceId: string;
  readonly resourceName: string;
  readonly establishmentId: string;
  readonly establishmentName: string;
  readonly commissionPercent: number;
}

export interface TeamAppointment {
  readonly id: string;
  readonly establishmentId: string;
  readonly establishmentName: string;
  readonly resourceName: string;
  readonly clientName: string;
  readonly clientUserId: string | null;
  readonly startsAt: string;
  readonly endsAt: string;
  readonly status: string;
  readonly lines: readonly { name: string; price: number; durationMinutes: number }[];
  readonly startedAt: string | null;
  readonly finishedAt: string | null;
  readonly tipAmount: number;
  readonly allergies: string | null;
  readonly canStart: boolean;
  readonly canFinish: boolean;
}

export interface TeamPlanning {
  readonly date: string;
  readonly timezone: string;
  readonly appointments: readonly TeamAppointment[];
}

export interface TechnicalNote {
  readonly allergies: string | null;
  readonly notes: string | null;
  readonly updatedAt: string | null;
}

export interface CommissionLine {
  readonly appointmentId: string;
  readonly finishedAt: string;
  readonly clientName: string;
  readonly servicePrice: number;
  readonly commission: number;
  readonly tip: number;
}

export interface CommissionSummary {
  readonly resourceId: string;
  readonly resourceName: string;
  readonly commissionPercent: number;
  readonly services: number;
  readonly revenue: number;
  readonly commission: number;
  readonly tips: number;
  readonly total: number;
  readonly currencyCode: string;
  readonly lines: readonly CommissionLine[];
}

export interface TeamMember {
  readonly resourceId: string;
  readonly name: string;
  readonly isActive: boolean;
  readonly userId: string | null;
  readonly email: string | null;
  readonly collaboratorName: string | null;
  readonly commissionPercent: number;
}

export interface TeamAssignment {
  readonly email: string | null;
  readonly commissionPercent: number;
}

/** Collaborator portal (`/api/v1/team`) and the owner's team management (`/api/v1/pro/establishments/:id/team`). */
@Injectable({ providedIn: 'root' })
export class TeamService {
  private readonly http = inject(HttpClient);

  // ───────── Collaborator ─────────

  memberships(): Promise<TeamMembership[]> {
    return firstValueFrom(this.http.get<TeamMembership[]>('/api/v1/team/me'));
  }

  planning(date?: string): Promise<TeamPlanning> {
    const params = date ? new HttpParams().set('date', date) : new HttpParams();
    return firstValueFrom(this.http.get<TeamPlanning>('/api/v1/team/planning', { params }));
  }

  start(appointmentId: string): Promise<TeamAppointment> {
    return firstValueFrom(
      this.http.post<TeamAppointment>(`/api/v1/team/appointments/${appointmentId}/start`, {}),
    );
  }

  finish(appointmentId: string): Promise<TeamAppointment> {
    return firstValueFrom(
      this.http.post<TeamAppointment>(`/api/v1/team/appointments/${appointmentId}/finish`, {}),
    );
  }

  note(establishmentId: string, clientId: string): Promise<TechnicalNote> {
    return firstValueFrom(this.http.get<TechnicalNote>(this.noteUrl(establishmentId, clientId)));
  }

  saveNote(
    establishmentId: string,
    clientId: string,
    input: { allergies: string | null; notes: string | null },
  ): Promise<TechnicalNote> {
    return firstValueFrom(
      this.http.put<TechnicalNote>(this.noteUrl(establishmentId, clientId), input),
    );
  }

  commissions(from?: string, to?: string): Promise<CommissionSummary[]> {
    return firstValueFrom(
      this.http.get<CommissionSummary[]>('/api/v1/team/commissions', {
        params: this.period(from, to),
      }),
    );
  }

  // ───────── Owner ─────────

  members(establishmentId: string): Promise<TeamMember[]> {
    return firstValueFrom(
      this.http.get<TeamMember[]>(`/api/v1/pro/establishments/${establishmentId}/team`),
    );
  }

  assign(establishmentId: string, resourceId: string, input: TeamAssignment): Promise<TeamMember> {
    return firstValueFrom(
      this.http.put<TeamMember>(
        `/api/v1/pro/establishments/${establishmentId}/team/${resourceId}`,
        input,
      ),
    );
  }

  unassign(establishmentId: string, resourceId: string): Promise<void> {
    return firstValueFrom(
      this.http.delete<void>(`/api/v1/pro/establishments/${establishmentId}/team/${resourceId}`),
    );
  }

  ownerCommissions(
    establishmentId: string,
    from?: string,
    to?: string,
  ): Promise<CommissionSummary[]> {
    return firstValueFrom(
      this.http.get<CommissionSummary[]>(
        `/api/v1/pro/establishments/${establishmentId}/commissions`,
        { params: this.period(from, to) },
      ),
    );
  }

  private noteUrl(establishmentId: string, clientId: string): string {
    return `/api/v1/team/establishments/${establishmentId}/clients/${clientId}/note`;
  }

  private period(from?: string, to?: string): HttpParams {
    let params = new HttpParams();
    if (from) {
      params = params.set('from', from);
    }

    if (to) {
      params = params.set('to', to);
    }

    return params;
  }
}

/** `00:12:05` style elapsed time between two instants (the running timer of a treatment). */
export function elapsed(from: string, until: number): string {
  const seconds = Math.max(0, Math.floor((until - Date.parse(from)) / 1000));
  const h = Math.floor(seconds / 3600);
  const m = Math.floor((seconds % 3600) / 60);
  const s = seconds % 60;
  return [h, m, s].map((n) => `${n}`.padStart(2, '0')).join(':');
}
