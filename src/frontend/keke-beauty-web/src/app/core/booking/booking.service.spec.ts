import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { BookingService, formatInZone, isoDay } from './booking.service';

describe('BookingService', () => {
  let service: BookingService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(BookingService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('sends every chosen service as a repeated query parameter', async () => {
    const pending = service.availability('e1', '2026-10-12', '2026-10-18', ['a', 'b'], 'r1');

    const request = http.expectOne((r) => r.url === '/api/v1/establishments/e1/availability');
    expect(request.request.params.getAll('serviceIds')).toEqual(['a', 'b']);
    expect(request.request.params.get('resourceId')).toBe('r1');
    expect(request.request.params.get('from')).toBe('2026-10-12');
    request.flush({
      timezone: 'UTC',
      durationMinutes: 90,
      totalPrice: 0,
      currencyCode: 'XOF',
      days: [],
    });
    await pending;
  });

  it('asks the API for the pass of one appointment', async () => {
    const pending = service.pass('a1');

    const request = http.expectOne('/api/v1/appointments/a1/pass');
    expect(request.request.method).toBe('GET');
    request.flush({ token: 'abc', expiresAt: '2026-10-12T10:30:00Z' });

    expect((await pending).token).toBe('abc');
  });

  it('omits the resource when there is no preference', async () => {
    const pending = service.availability('e1', '2026-10-12', '2026-10-12', ['a'], null);

    const request = http.expectOne((r) => r.url === '/api/v1/establishments/e1/availability');
    expect(request.request.params.has('resourceId')).toBe(false);
    request.flush({
      timezone: 'UTC',
      durationMinutes: 30,
      totalPrice: 0,
      currencyCode: 'XOF',
      days: [],
    });
    await pending;
  });

  it('posts a booking and answers reschedule proposals on dedicated endpoints', async () => {
    const booked = service.book({
      establishmentId: 'e1',
      serviceIds: ['a'],
      resourceId: null,
      startsAt: '2026-10-13T10:00:00Z',
      phone: '0700001234',
      note: null,
    });
    const post = http.expectOne('/api/v1/appointments');
    expect(post.request.method).toBe('POST');
    expect(post.request.body.serviceIds).toEqual(['a']);
    post.flush({ id: 'x', status: 'Pending' });
    await expect(booked).resolves.toMatchObject({ id: 'x' });

    const accept = service.acceptReschedule('x');
    http
      .expectOne('/api/v1/appointments/x/reschedule/accept')
      .flush({ id: 'x', status: 'Confirmed' });
    await accept;
    const decline = service.declineReschedule('x');
    http
      .expectOne('/api/v1/appointments/x/reschedule/decline')
      .flush({ id: 'x', status: 'CancelledByClient' });
    await decline;
  });

  it('moves an appointment and signals a delay through their own routes', async () => {
    const move = service.reschedule('x', '2026-10-20T10:00:00Z');
    const moved = http.expectOne('/api/v1/appointments/x/reschedule');
    expect(moved.request.method).toBe('POST');
    expect(moved.request.body).toEqual({ startsAt: '2026-10-20T10:00:00Z' });
    moved.flush({ id: 'x', status: 'Confirmed' });
    await move;

    const late = service.signalLate('x', 15);
    const signalled = http.expectOne('/api/v1/appointments/x/late');
    expect(signalled.request.body).toEqual({ minutes: 15 });
    signalled.flush({ id: 'x', status: 'Confirmed' });
    await late;
  });

  it('surfaces a taken slot as an HTTP 409 for the page to handle', async () => {
    const pending = service.book({
      establishmentId: 'e1',
      serviceIds: ['a'],
      resourceId: null,
      startsAt: '2026-10-13T10:00:00Z',
      phone: '0700001234',
      note: null,
    });
    http
      .expectOne('/api/v1/appointments')
      .flush({ code: 'slot.taken' }, { status: 409, statusText: 'Conflict' });

    await expect(pending).rejects.toMatchObject({ status: 409 });
  });
});

describe('date helpers', () => {
  it('formats a local calendar day without going through UTC', () => {
    expect(isoDay(new Date(2026, 9, 5, 23, 59))).toBe('2026-10-05');
    expect(isoDay(new Date(2026, 0, 1, 0, 0))).toBe('2026-01-01');
  });

  it('renders instants on the establishment wall clock', () => {
    const text = formatInZone('2026-10-12T10:30:00Z', 'UTC', {
      hour: '2-digit',
      minute: '2-digit',
    });
    expect(text).toContain('10');
    expect(text).toContain('30');
  });

  it('falls back to UTC for an unknown timezone', () => {
    expect(() =>
      formatInZone('2026-10-12T10:30:00Z', 'Nowhere/Land', { hour: '2-digit' }),
    ).not.toThrow();
  });
});

describe('BookingService late cancellation and tips', () => {
  let service: BookingService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(BookingService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('cancels without accepting a fee unless asked to', async () => {
    const plain = service.cancel('a1');
    const first = http.expectOne('/api/v1/appointments/a1/cancel');
    expect(first.request.body).toEqual({ acceptLateFee: false });
    first.flush({ id: 'a1' });
    await plain;

    const withFee = service.cancel('a1', true);
    const second = http.expectOne('/api/v1/appointments/a1/cancel');
    expect(second.request.body).toEqual({ acceptLateFee: true });
    second.flush({ id: 'a1' });
    await withFee;
  });

  it('gets the payment page of a tip, or a plain confirmation when payments are off', async () => {
    const online = service.tip('a1', 500);
    const request = http.expectOne('/api/v1/appointments/a1/tip');
    expect(request.request.body).toEqual({ amount: 500 });
    request.flush({ amount: 500, status: 'Awaiting', paymentUrl: 'https://pay.example/x?tx=KT1' });
    expect((await online).paymentUrl).toBe('https://pay.example/x?tx=KT1');

    const direct = service.tip('a2', 1000);
    http
      .expectOne('/api/v1/appointments/a2/tip')
      .flush({ amount: 1000, status: 'Recorded', paymentUrl: null });
    expect((await direct).status).toBe('Recorded');
  });
});
