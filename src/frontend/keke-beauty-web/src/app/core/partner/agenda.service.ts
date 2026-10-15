import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, firstValueFrom } from 'rxjs';
import type { components } from '../api/api.generated';

type Schemas = components['schemas'];

export type PartnerAppointment = Schemas['PartnerAppointmentDto'];
export type BookingSettings = Schemas['BookingSettingsDto'];
export type AgendaResource = Schemas['ResourceDto'];
export type ResourceInput = Schemas['ResourceInput'];
export type TimeOff = Schemas['TimeOffDto'];
export type TimeOffInput = Schemas['TimeOffInput'];
export type Stats = Schemas['StatsDto'];
export type AcceptedPayments = Schemas['AcceptedPaymentsDto'];
export type PaymentSettings = Schemas['PaymentSettingsDto'];
export type PartnerPayments = Schemas['PartnerPaymentsView'];
export type CounterRequest = Schemas['CounterRequest'];
export type PassCheck = Schemas['PassCheckDto'];

/** Partner agenda API (`/api/v1/pro`): appointments, resources, closures, settings, statistics (PRD AGD-01 to 08). */
@Injectable({ providedIn: 'root' })
export class AgendaService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/pro';

  // ───────── Appointments ─────────

  appointments(
    establishmentId: string,
    filter: { status?: string; from?: string; to?: string } = {},
  ): Promise<PartnerAppointment[]> {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(filter)) {
      if (value) {
        params = params.set(key, value);
      }
    }

    return firstValueFrom(
      this.http.get<PartnerAppointment[]>(
        `${this.base}/establishments/${establishmentId}/appointments`,
        { params },
      ),
    );
  }

  /** Walk-in recorded at the counter: confirmed at once, the client has no account. */
  counter(establishmentId: string, input: CounterRequest): Promise<PartnerAppointment> {
    return firstValueFrom(
      this.http.post<PartnerAppointment>(
        `${this.base}/establishments/${establishmentId}/counter`,
        input,
      ),
    );
  }

  /** Checks a client's pass (the scanned token or a pasted link) against this establishment. */
  verifyPass(establishmentId: string, token: string): Promise<PassCheck> {
    return firstValueFrom(
      this.http.post<PassCheck>(`${this.base}/establishments/${establishmentId}/pass/verify`, {
        token,
      }),
    );
  }

  confirm(id: string): Promise<PartnerAppointment> {
    return this.act(id, 'confirm', {});
  }

  reject(id: string, reason: string): Promise<PartnerAppointment> {
    return this.act(id, 'reject', { reason: reason.trim() || null });
  }

  cancel(id: string, reason: string): Promise<PartnerAppointment> {
    return this.act(id, 'cancel', { reason: reason.trim() || null });
  }

  markPaid(id: string, method: string, reference: string | null): Promise<PartnerAppointment> {
    return this.act(id, 'mark-paid', { method, reference });
  }

  markUnpaid(id: string): Promise<PartnerAppointment> {
    return this.act(id, 'mark-unpaid', {});
  }

  noShow(id: string): Promise<PartnerAppointment> {
    return this.act(id, 'no-show', {});
  }

  proposeReschedule(id: string, startsAt: string, comment: string): Promise<PartnerAppointment> {
    return this.act(id, 'propose-reschedule', { startsAt, comment: comment.trim() || null });
  }

  // ───────── Settings and statistics ─────────

  settings(establishmentId: string): Promise<BookingSettings> {
    return firstValueFrom(
      this.http.get<BookingSettings>(
        `${this.base}/establishments/${establishmentId}/booking-settings`,
      ),
    );
  }

  saveSettings(establishmentId: string, settings: BookingSettings): Promise<BookingSettings> {
    return firstValueFrom(
      this.http.put<BookingSettings>(
        `${this.base}/establishments/${establishmentId}/booking-settings`,
        settings,
      ),
    );
  }

  // ───────── How clients pay the establishment (cash, mobile money straight to it) ─────────

  acceptedPayments(establishmentId: string): Promise<AcceptedPayments> {
    return firstValueFrom(
      this.http.get<AcceptedPayments>(
        `${this.base}/establishments/${establishmentId}/accepted-payments`,
      ),
    );
  }

  saveAcceptedPayments(
    establishmentId: string,
    value: AcceptedPayments,
  ): Promise<AcceptedPayments> {
    return firstValueFrom(
      this.http.put<AcceptedPayments>(
        `${this.base}/establishments/${establishmentId}/accepted-payments`,
        value,
      ),
    );
  }

  // ───────── Online payments of appointments (switched off: the clients pay the establishment directly) ─────────

  paymentSettings(establishmentId: string): Promise<PaymentSettings> {
    return firstValueFrom(
      this.http.get<PaymentSettings>(
        `${this.base}/establishments/${establishmentId}/payment-settings`,
      ),
    );
  }

  savePaymentSettings(
    establishmentId: string,
    settings: PaymentSettings,
  ): Promise<PaymentSettings> {
    return firstValueFrom(
      this.http.put<PaymentSettings>(
        `${this.base}/establishments/${establishmentId}/payment-settings`,
        settings,
      ),
    );
  }

  payments(establishmentId: string): Promise<PartnerPayments> {
    return firstValueFrom(
      this.http.get<PartnerPayments>(`${this.base}/establishments/${establishmentId}/payments`),
    );
  }

  stats(establishmentId: string, from: string, to: string): Promise<Stats> {
    return firstValueFrom(
      this.http.get<Stats>(`${this.base}/establishments/${establishmentId}/stats`, {
        params: { from, to },
      }),
    );
  }

  // ───────── Resources and closures ─────────

  resources(establishmentId: string): Promise<AgendaResource[]> {
    return firstValueFrom(
      this.http.get<AgendaResource[]>(`${this.base}/establishments/${establishmentId}/resources`),
    );
  }

  addResource(establishmentId: string, input: ResourceInput): Promise<void> {
    return this.done(
      this.http.post(`${this.base}/establishments/${establishmentId}/resources`, input),
    );
  }

  updateResource(establishmentId: string, id: string, input: ResourceInput): Promise<void> {
    return this.done(
      this.http.put(`${this.base}/establishments/${establishmentId}/resources/${id}`, input),
    );
  }

  deleteResource(establishmentId: string, id: string): Promise<void> {
    return this.done(
      this.http.delete(`${this.base}/establishments/${establishmentId}/resources/${id}`),
    );
  }

  timeOff(establishmentId: string): Promise<TimeOff[]> {
    return firstValueFrom(
      this.http.get<TimeOff[]>(`${this.base}/establishments/${establishmentId}/time-off`),
    );
  }

  addTimeOff(establishmentId: string, input: TimeOffInput): Promise<void> {
    return this.done(
      this.http.post(`${this.base}/establishments/${establishmentId}/time-off`, input),
    );
  }

  deleteTimeOff(establishmentId: string, id: string): Promise<void> {
    return this.done(
      this.http.delete(`${this.base}/establishments/${establishmentId}/time-off/${id}`),
    );
  }

  private act(id: string, action: string, body: object): Promise<PartnerAppointment> {
    return firstValueFrom(
      this.http.post<PartnerAppointment>(`${this.base}/appointments/${id}/${action}`, body),
    );
  }

  private done(request: Observable<unknown>): Promise<void> {
    return firstValueFrom(request).then(() => undefined);
  }
}
