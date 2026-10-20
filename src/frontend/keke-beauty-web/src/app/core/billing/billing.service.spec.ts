import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import { BillingService } from './billing.service';
import { planConditions } from './plan-conditions';

describe('BillingService', () => {
  let service: BillingService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(BillingService);
    http = TestBed.inject(HttpTestingController);
  });

  it('subscribes with the plan code and the billing cycle', async () => {
    const promise = service.subscribe('e1', 'premium', 'Annual');

    const request = http.expectOne('/api/v1/pro/establishments/e1/subscription');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ planCode: 'premium', cycle: 'Annual' });
    request.flush({
      subscriptionId: 's',
      invoiceId: 'i',
      transactionId: 'KB1',
      paymentUrl: 'http://pay',
    });

    expect((await promise).paymentUrl).toBe('http://pay');
  });

  it('asks the server to re-read the pending payments of an establishment', async () => {
    const promise = service.refreshPending('e1');

    const request = http.expectOne('/api/v1/pro/establishments/e1/subscription/refresh');
    expect(request.request.method).toBe('POST');
    request.flush(1);

    expect(await promise).toBe(1);
  });

  it('cancels a subscription and starts the payment of an invoice', async () => {
    const cancel = service.cancel('s1');
    const deleted = http.expectOne('/api/v1/pro/subscriptions/s1');
    expect(deleted.request.method).toBe('DELETE');
    deleted.flush(null, { status: 204, statusText: 'No Content' });
    await cancel;

    const pay = service.payInvoice('i1');
    const post = http.expectOne('/api/v1/pro/invoices/i1/pay');
    expect(post.request.method).toBe('POST');
    post.flush({
      subscriptionId: 's1',
      invoiceId: 'i1',
      transactionId: 'KB2',
      paymentUrl: 'http://pay2',
    });
    expect((await pay).transactionId).toBe('KB2');
  });

  it('simulates a payment outcome with an encoded transaction id', async () => {
    const promise = service.simulate('KB 1', 'refused');

    const request = http.expectOne('/api/v1/dev/payments/KB%201/simulate');
    expect(request.request.body).toEqual({ status: 'refused' });
    request.flush(null, { status: 204, statusText: 'No Content' });
    await promise;
  });

  it('includes maxServices and custom perks in planConditions', () => {
    const conditions = planConditions({
      id: 'p1',
      code: 'gold',
      name: 'Or',
      monthlyPrice: 10000,
      annualPrice: 100000,
      currencyCode: 'XOF',
      isActive: true,
      sortOrder: 1,
      description: 'Formule Or',
      commitmentMonths: 12,
      trialDays: 14,
      promoPercent: 0,
      promoPeriods: 1,
      promoActive: false,
      features: {
        booking: true,
        videos: true,
        maxMedia: 30,
        featured: true,
        stats: true,
        maxVideos: 3,
        maxResources: 5,
        maxAppointmentsPerMonth: 200,
        maxServices: 25,
        perks: ['Support VIP WhatsApp', 'Formation équipe'],
      },
    });
    expect(conditions.map((c) => c.label)).toContain("Jusqu'à 25 prestations au catalogue");
    expect(conditions.map((c) => c.label)).toContain('Support VIP WhatsApp');
    expect(conditions.map((c) => c.label)).toContain('Formation équipe');
  });
});
