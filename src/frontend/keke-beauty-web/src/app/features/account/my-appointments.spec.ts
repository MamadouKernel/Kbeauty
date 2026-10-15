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
