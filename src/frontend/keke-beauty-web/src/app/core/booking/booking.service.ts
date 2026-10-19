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
export type AppointmentPass = Schemas['PassDto'];

export interface TipResult {
  readonly amount: number;
  readonly status: string;
  readonly paymentUrl: string | null;
}

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

  /** The signed token shown as a QR code at the establishment. */
  pass(appointmentId: string): Promise<AppointmentPass> {
    return firstValueFrom(
      this.http.get<AppointmentPass>(`/api/v1/appointments/${appointmentId}/pass`),
    );
  }

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

  /**
   * A tip for the collaborator who served me, once the treatment is finished (whole amount, one per appointment).
   * With online payments the answer carries the payment page to go to; otherwise the tip is simply recorded.
   */
  tip(id: string, amount: number): Promise<TipResult> {
    return firstValueFrom(this.http.post<TipResult>(`/api/v1/appointments/${id}/tip`, { amount }));
  }

  /** After the cancellation limit, a cancellation only goes through when the client accepts the fee shown on the appointment. */
  cancel(id: string, acceptLateFee = false): Promise<Appointment> {
    return firstValueFrom(
      this.http.post<Appointment>(`/api/v1/appointments/${id}/cancel`, { acceptLateFee }),
    );
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

  /** Downloads the appointment as an iCalendar (.ics) file for Apple/Google/Outlook calendars. */
  downloadIcs(a: Appointment): void {
    const pad = (n: number) => `${n}`.padStart(2, '0');
    const toIcsUtc = (iso: string) => {
      const d = new Date(iso);
      return `${d.getUTCFullYear()}${pad(d.getUTCMonth() + 1)}${pad(d.getUTCDate())}T${pad(d.getUTCHours())}${pad(d.getUTCMinutes())}${pad(d.getUTCSeconds())}Z`;
    };
    const esc = (v: string) =>
      v.replace(/\\/g, '\\\\').replace(/;/g, '\\;').replace(/,/g, '\\,').replace(/\r?\n/g, '\\n');
    const services = a.lines.map((l) => l.name).join(', ');
    const desc = [
      `Prestations : ${services}`,
      `Total : ${a.totalPrice} ${a.currencyCode}`,
      ...(a.resourceName ? [`Intervenant(e) : ${a.resourceName}`] : []),
    ].join('\n');
    const ics = [
      'BEGIN:VCALENDAR',
      'VERSION:2.0',
      'PRODID:-//Keke Beauty//RDV//FR',
      'CALSCALE:GREGORIAN',
      'METHOD:PUBLISH',
      'BEGIN:VEVENT',
      `UID:rdv-${a.id}@kekebeauty.ci`,
      `DTSTAMP:${toIcsUtc(new Date().toISOString())}`,
      `DTSTART:${toIcsUtc(a.startsAt)}`,
      `DTEND:${toIcsUtc(a.endsAt)}`,
      `SUMMARY:${esc(`RDV Keke Beauty — ${a.establishmentName}`)}`,
      `LOCATION:${esc(a.establishmentName)}`,
      `DESCRIPTION:${esc(desc)}`,
      'BEGIN:VALARM',
      'TRIGGER:-PT2H',
      'ACTION:DISPLAY',
      `DESCRIPTION:${esc(`Rappel RDV chez ${a.establishmentName}`)}`,
      'END:VALARM',
      'END:VEVENT',
      'END:VCALENDAR',
      '',
    ].join('\r\n');

    const blob = new Blob([ics], { type: 'text/calendar;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = `keke-beauty-${a.establishmentSlug}.ics`;
    document.body.appendChild(link);
    link.click();
    link.remove();
    URL.revokeObjectURL(url);
  }
}

/** Query parameters to pre-fill the booking page from a previous appointment ("Réserver à nouveau"). */
export function rebookQueryParams(a: Appointment): Record<string, string> {
  const params: Record<string, string> = {};
  const names = a.lines.map((l) => l.name).filter(Boolean);
  if (names.length > 0) {
    params['reprendre'] = names.join('|');
  }
  if (a.resourceName) {
    params['intervenant'] = a.resourceName;
  }
  return params;
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
