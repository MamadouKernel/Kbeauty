import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { DisputeService, canDispute } from './dispute.service';

describe('DisputeService', () => {
  let service: DisputeService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(DisputeService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('files a claim about an appointment', async () => {
    const pending = service.open('a1', 'SalonClosed', 'Le salon était fermé.');

    const request = http.expectOne('/api/v1/disputes');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({
      appointmentId: 'a1',
      reason: 'SalonClosed',
      description: 'Le salon était fermé.',
    });
    request.flush({ id: 'd1', status: 'Open' });

    expect((await pending).status).toBe('Open');
  });

  it('lets the establishment answer', async () => {
    const pending = service.respond('d1', 'Nous étions ouverts.');

    const request = http.expectOne('/api/v1/pro/disputes/d1/respond');
    expect(request.request.body).toEqual({ message: 'Nous étions ouverts.' });
    request.flush({ id: 'd1', partnerResponse: 'Nous étions ouverts.' });

    expect((await pending).partnerResponse).toBe('Nous étions ouverts.');
  });

  it('filters the back-office list by status only when asked', async () => {
    const all = service.adminList();
    http.expectOne('/api/v1/admin/disputes').flush([]);
    expect(await all).toEqual([]);

    const open = service.adminList('Open');
    http.expectOne('/api/v1/admin/disputes?status=Open').flush([]);
    expect(await open).toEqual([]);
  });

  it('sends the decision and the note of the administrator', async () => {
    const pending = service.resolve('d1', 'RefundedClient', 'Salon fermé.');

    const request = http.expectOne('/api/v1/admin/disputes/d1/resolve');
    expect(request.request.body).toEqual({ resolution: 'RefundedClient', note: 'Salon fermé.' });
    request.flush({ id: 'd1', status: 'Resolved', resolution: 'RefundedClient' });

    expect((await pending).resolution).toBe('RefundedClient');
  });
});

describe('canDispute', () => {
  const now = Date.parse('2026-10-18T12:00:00Z');
  const hoursAgo = (hours: number) => new Date(now - hours * 3600 * 1000).toISOString();

  it('opens right after the appointment and closes after seven days', () => {
    expect(canDispute('Completed', hoursAgo(1), now)).toBe(true);
    expect(canDispute('NoShow', hoursAgo(24 * 6), now)).toBe(true);
    expect(canDispute('Completed', hoursAgo(24 * 8), now)).toBe(false);
  });

  it('is not possible before the appointment is over', () => {
    expect(canDispute('Confirmed', hoursAgo(-2), now)).toBe(false);
  });

  it('is not possible for cancelled or pending appointments', () => {
    expect(canDispute('CancelledByClient', hoursAgo(2), now)).toBe(false);
    expect(canDispute('Pending', hoursAgo(2), now)).toBe(false);
  });
});
