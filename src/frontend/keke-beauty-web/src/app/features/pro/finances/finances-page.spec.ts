import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { ProFinances } from './finances-page';

describe('ProFinances', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter(
          [{ path: 'pro/etablissements/:id/finances', component: ProFinances }],
          withComponentInputBinding(),
        ),
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  it('renders KPI cards, service breakdown, method breakdown and receipt buttons', async () => {
    const harness = await RouterTestingHarness.create();
    const navPromise = harness.navigateByUrl('/pro/etablissements/e1/finances', ProFinances);

    await vi.waitFor(() => {
      const finReq = http.expectOne((r) => r.url === '/api/v1/pro/establishments/e1/finances');
      finReq.flush({
        establishmentId: 'e1',
        establishmentName: 'Maison Keke',
        from: '2026-10-01T00:00:00Z',
        to: '2026-10-31T23:59:59Z',
        currencyCode: 'XOF',
        completedCount: 12,
        noShowCount: 1,
        grossRevenue: 180000,
        onlineGross: 120000,
        platformFees: 6000,
        onlineNet: 114000,
        onSiteCash: 40000,
        onSiteMobileMoney: 20000,
        totalTips: 5000,
        totalCommissions: 27000,
        totalPaidOut: 60000,
        byService: [{ serviceName: 'Tresses royales', count: 6, totalAmount: 90000 }],
        byMethod: [{ method: 'En ligne', count: 8, totalAmount: 120000 }],
        transactions: [
          {
            appointmentId: 'a1',
            startsAt: '2026-10-15T10:00:00Z',
            clientName: 'Fatou Bamba',
            resourceName: 'Awa Koné',
            servicesSummary: 'Tresses royales',
            status: 'Completed',
            totalPrice: 15000,
            onlinePaidAmount: 15000,
            platformFee: 750,
            netOnlineAmount: 14250,
            onSitePaidMethod: null,
            onSitePaidReference: null,
            tipAmount: 1000,
            commissionAmount: 2250,
            currencyCode: 'XOF',
          },
        ],
        payouts: [
          {
            id: 'p1',
            establishmentId: 'e1',
            establishmentName: 'Maison Keke',
            amount: 60000,
            currencyCode: 'XOF',
            operator: 'WAVE_CI',
            accountMasked: '+22507****89',
            status: 'Paid',
            error: null,
            createdAt: '2026-10-16T12:00:00Z',
            completedAt: '2026-10-16T12:01:00Z',
          },
        ],
      });
    });

    await navPromise;
    await vi.waitFor(() => {
      harness.detectChanges();
      const text = (harness.routeNativeElement as HTMLElement).textContent ?? '';
      expect(text).toContain('Finances et reçus');
      expect(text).toContain('180 000 F CFA');
      expect(text).toContain('114 000 F CFA');
      expect(text).toContain('Tresses royales');
      expect(text).toContain('Fatou Bamba');
      expect(text).toContain('Exporter en CSV');
      expect(text).toContain('Reçu PDF');
      expect(text).toContain('Bordereau PDF');
    });
  });
});
