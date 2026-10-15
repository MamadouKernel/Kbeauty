import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import type { components } from '../api/api.generated';

type Schemas = components['schemas'];

export type BookingInfo = Schemas['BookingInfo'];
export type BookableService = Schemas['BookableService'];
export type BookingResource = Schemas['ResourceDto'];
export type AvailabilityResponse = Schemas['AvailabilityResponse'];
export type AvailabilityDay = Schemas['AvailabilityDay'];
export type AvailabilitySlot = Schemas['AvailabilitySlot'];
export type Appointment = Schemas['AppointmentDto'];
export type BookingRequest = Schemas['BookingRequest'];

export const APPOINTMENT_STATUS: Readonly<
  Record<string, { label: string; tone: 'ok' | 'wait' | 'bad' | 'done' }>
> = {
  Pending: { label: 'En attente de confirmation', tone: 'wait' },
  Confirmed: { label: 'Confirmé', tone: 'ok' },
  Rejected: { label: 'Refusé', tone: 'bad' },
  Expired: { label: 'Demande expirée', tone: 'bad' },
  CancelledByClient: { label: 'Annulé par vous', tone: 'bad' },
  CancelledByEstablishment: { label: "Annulé par l'établissement", tone: 'bad' },
  RescheduleProposed: { label: 'Nouveau créneau proposé', tone: 'wait' },
  Completed: { label: 'Terminé', tone: 'done' },
  NoShow: { label: 'Absent', tone: 'bad' },
};

/** Client side of booking: public calendar, booking, and "my appointments" (PRD RDV-01 to RDV-06). */
@Injectable({ providedIn: 'root' })
export class BookingService {
  private readonly http = inject(HttpClient);

  info(slug: string): Promise<BookingInfo> {
    return firstValueFrom(
      this.http.get<BookingInfo>(`/api/v1/establishments/${encodeURIComponent(slug)}/booking`),
    );
  }

  availability(
    establishmentId: string,
    from: string,
    to: string,
    serviceIds: readonly string[],
    resourceId: string | null,
  ): Promise<AvailabilityResponse> {
    let params = new HttpParams().set('from', from).set('to', to);
    for (const id of serviceIds) {
      params = params.append('serviceIds', id);
    }

    if (resourceId) {
      params = params.set('resourceId', resourceId);
    }

    return firstValueFrom(
      this.http.get<AvailabilityResponse>(
        `/api/v1/establishments/${establishmentId}/availability`,
        {
          params,
        },
      ),
    );
  }

  book(request: BookingRequest): Promise<Appointment> {
    return firstValueFrom(this.http.post<Appointment>('/api/v1/appointments', request));
  }

  mine(): Promise<Appointment[]> {
    return firstValueFrom(this.http.get<Appointment[]>('/api/v1/me/appointments'));
  }

  /** A held appointment that has to be paid: returns it with a fresh payment link. */
  pay(id: string): Promise<Appointment> {
    return firstValueFrom(this.http.post<Appointment>(`/api/v1/appointments/${id}/pay`, {}));
  }

  cancel(id: string): Promise<Appointment> {
    return firstValueFrom(this.http.post<Appointment>(`/api/v1/appointments/${id}/cancel`, {}));
  }

  acceptReschedule(id: string): Promise<Appointment> {
    return firstValueFrom(
      this.http.post<Appointment>(`/api/v1/appointments/${id}/reschedule/accept`, {}),
    );
  }

  declineReschedule(id: string): Promise<Appointment> {
    return firstValueFrom(
      this.http.post<Appointment>(`/api/v1/appointments/${id}/reschedule/decline`, {}),
    );
  }
}

/** `2026-10-12` for a local calendar date (never through UTC: it would shift the day near midnight). */
export function isoDay(date: Date): string {
  const month = `${date.getMonth() + 1}`.padStart(2, '0');
  const day = `${date.getDate()}`.padStart(2, '0');
  return `${date.getFullYear()}-${month}-${day}`;
}

/** Wall-clock formatting in the establishment's own timezone, whatever the visitor's browser says. */
export function formatInZone(
  instant: string,
  timezone: string,
  options: Intl.DateTimeFormatOptions,
): string {
  try {
    return new Intl.DateTimeFormat('fr-FR', { ...options, timeZone: timezone }).format(
      new Date(instant),
    );
  } catch {
    return new Intl.DateTimeFormat('fr-FR', { ...options, timeZone: 'UTC' }).format(
      new Date(instant),
    );
  }
}
