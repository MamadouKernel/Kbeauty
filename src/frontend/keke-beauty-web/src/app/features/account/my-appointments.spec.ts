import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { Appointment } from '../../core/booking/booking.service';
import { NotificationService } from '../../core/notifications/notification.service';
import { MyAppointments } from './my-appointments';

const base: Appointment = {
  id: 'a1',
  establishmentId: 'e1',
  establishmentName: 'Salon Un',
  establishmentSlug: 'salon-un',
  resourceName: 'Principal',
  startsAt: '2026-10-14T10:00:00Z',
  endsAt: '2026-10-14T11:00:00Z',
  status: 'Confirmed',
  totalPrice: 10000,
  currencyCode: 'XOF',
  clientNote: null,
  rejectionReason: null,
  proposedStartsAt: null,
  proposedEndsAt: null,
  expiresAt: null,
  canCancel: true,
  lines: [{ name: 'Coupe longue', price: 10000, durationMinutes: 60 }],
  createdAt: '2026-10-11T10:00:00Z',
  tipAmount: 0,
  canTip: false,
  lateCancelFee: 0,
  canReschedule: false,
  canSignalLate: false,
};

describe('MyAppointments', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: NotificationService, useValue: { latest: signal(null) } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  async function open(list: Appointment[]) {
    const fixture = TestBed.createComponent(MyAppointments);
    fixture.detectChanges();
    (await vi.waitFor(() => http.expectOne('/api/v1/me/appointments'))).flush(list);
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect((fixture.nativeElement as HTMLElement).querySelector('h2')).toBeTruthy();
    });
    return fixture.nativeElement as HTMLElement;
  }

  it('separates upcoming from past appointments and shows their status', async () => {
    const root = await open([
      base,
      { ...base, id: 'a2', status: 'Rejected', rejectionReason: 'Fermé', canCancel: false },
    ]);

    expect(root.textContent).toContain('Confirmé');
    expect(root.textContent).toContain('Refusé');
    expect(root.textContent).toContain('Motif : Fermé');
    expect(root.querySelector('#upcoming-title')?.parentElement?.textContent).toContain('Salon Un');
    expect(root.querySelector('#past-title')).toBeTruthy();
  });

  it('only offers cancellation while it is still allowed', async () => {
    const root = await open([base, { ...base, id: 'a3', canCancel: false }]);

    const cancels = [...root.querySelectorAll('button')].filter((b) =>
      b.textContent?.includes('Annuler'),
    );
    expect(cancels).toHaveLength(1);
    expect(root.textContent).toContain("délai d'annulation");
  });

  it('lets the client accept a proposed slot', async () => {
    const root = await open([
      {
        ...base,
        status: 'RescheduleProposed',
        proposedStartsAt: '2026-10-15T14:00:00Z',
        proposedEndsAt: '2026-10-15T15:00:00Z',
      },
    ]);

    expect(root.textContent).toContain('vous propose');
    [...root.querySelectorAll('button')].find((b) => b.textContent?.includes('Accepter'))?.click();

    const accept = await vi.waitFor(() =>
      http.expectOne('/api/v1/appointments/a1/reschedule/accept'),
    );
    accept.flush({ ...base, startsAt: '2026-10-15T14:00:00Z' });
    (await vi.waitFor(() => http.expectOne('/api/v1/me/appointments'))).flush([]);
  });

  it('offers to move or to signal a delay only when the server allows it', async () => {
    const none = await open([base]);
    expect(none.textContent).not.toContain('Déplacer le rendez-vous');
    expect(none.textContent).not.toContain('Un retard ?');
  });

  it('lets the client signal a delay in one tap', async () => {
    const root = await open([{ ...base, canSignalLate: true }]);

    expect(root.textContent).toContain('Un retard ?');
    [...root.querySelectorAll('button')].find((b) => b.textContent?.trim() === '15 min')?.click();

    const request = await vi.waitFor(() => http.expectOne('/api/v1/appointments/a1/late'));
    expect(request.request.body).toEqual({ minutes: 15 });
    request.flush(base);
    (await vi.waitFor(() => http.expectOne('/api/v1/me/appointments'))).flush([]);
  });

  it('opens the slot picker to move an appointment and asks for slots of the same services', async () => {
    const root = await open([
      {
        ...base,
        canReschedule: true,
        lines: [{ name: 'Coupe longue', price: 10000, durationMinutes: 60, serviceId: 's1' }],
      },
    ]);

    [...root.querySelectorAll('button')]
      .find((b) => b.textContent?.includes('Déplacer le rendez-vous'))
      ?.click();
    const input = await vi.waitFor(() => {
      const found = root.querySelector<HTMLInputElement>('input[type="date"]');
      expect(found).toBeTruthy();
      return found!;
    });
    input.value = '2026-10-20';
    input.dispatchEvent(new Event('change'));

    const slots = await vi.waitFor(() =>
      http.expectOne((r) => r.url === '/api/v1/establishments/e1/availability'),
    );
    expect(slots.request.params.getAll('serviceIds')).toEqual(['s1']);
    expect(slots.request.params.get('from')).toBe('2026-10-20');
    slots.flush({ timezone: 'Africa/Abidjan', durationMinutes: 60, note: null, days: [] });
  });

  it('tells a visitor without appointments where to look', async () => {
    const fixture = TestBed.createComponent(MyAppointments);
    fixture.detectChanges();
    (await vi.waitFor(() => http.expectOne('/api/v1/me/appointments'))).flush([]);

    await vi.waitFor(() => {
      fixture.detectChanges();
      expect((fixture.nativeElement as HTMLElement).textContent).toContain(
        'Aucun rendez-vous à venir',
      );
    });
  });
});
