import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { providePrimeNG } from 'primeng/config';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { Invoice, Plan, SubscriptionOverview } from '../../../core/billing/billing.service';
import { SubscriptionPage } from './subscription-page';

const PLANS: Plan[] = [
  {
    id: 'p1',
    code: 'free',
    name: 'Gratuit',
    monthlyPrice: 0,
    annualPrice: 0,
    currencyCode: 'XOF',
    features: {
      booking: false,
      videos: false,
      maxMedia: 5,
      featured: false,
      stats: false,
      maxVideos: 0,
    },
    isActive: true,
    sortOrder: 1,
    description: '',
    commitmentMonths: 12,
    trialDays: 0,
    promoPercent: 0,
    promoPeriods: 1,
    promoActive: false,
  },
  {
    id: 'p2',
    code: 'premium',
    name: 'Premium',
    monthlyPrice: 15000,
    annualPrice: 150000,
    currencyCode: 'XOF',
    features: {
      booking: true,
      videos: true,
      maxMedia: 30,
      featured: true,
      stats: true,
      maxVideos: 3,
    },
    isActive: true,
    sortOrder: 2,
    description: '',
    commitmentMonths: 12,
    trialDays: 14,
    promoPercent: 0,
    promoPeriods: 1,
    promoActive: false,
  },
];

const FREE: SubscriptionOverview = {
  subscription: null,
  effectivePlanCode: 'free',
  features: PLANS[0].features,
};

const PAST_DUE: SubscriptionOverview = {
  subscription: {
    id: 's1',
    planCode: 'premium',
    planName: 'Premium',
    cycle: 'Monthly',
    unitPrice: 15000,
    currencyCode: 'XOF',
    status: 'PastDue',
    startDate: '2026-01-01T00:00:00Z',
    commitmentEndDate: '2027-01-01T00:00:00Z',
    currentPeriodEnd: '2026-02-01T00:00:00Z',
    cancelAtCommitmentEnd: false,
    discountPercent: 0,
    discountPeriodsLeft: 0,
  },
  effectivePlanCode: 'free',
  features: PLANS[0].features,
};

const OVERDUE: Invoice = {
  id: 'i1',
  number: 'KB-2026-000002',
  establishmentId: 'e1',
  establishmentName: 'Salon Un',
  planName: 'Premium',
  amount: 15000,
  currencyCode: 'XOF',
  periodStart: '2026-02-01T00:00:00Z',
  periodEnd: '2026-03-01T00:00:00Z',
  dueDate: '2026-02-01T00:00:00Z',
  status: 'Overdue',
  paidAt: null,
};

describe('SubscriptionPage', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter(
          [{ path: 'pro/etablissements/:id/abonnement', component: SubscriptionPage }],
          withComponentInputBinding(),
        ),
        provideHttpClient(),
        provideHttpClientTesting(),
        providePrimeNG(),
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  async function open(overview: SubscriptionOverview, invoices: Invoice[] = []) {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/pro/etablissements/e1/abonnement', SubscriptionPage);
    (await vi.waitFor(() => http.expectOne('/api/v1/plans'))).flush(PLANS);
    (await vi.waitFor(() => http.expectOne('/api/v1/pro/establishments/e1/subscription'))).flush(
      overview,
    );
    (await vi.waitFor(() => http.expectOne('/api/v1/pro/invoices'))).flush(invoices);
    return harness;
  }

  const root = (harness: RouterTestingHarness) => harness.routeNativeElement as HTMLElement;

  const button = (harness: RouterTestingHarness, text: string) =>
    Array.from(root(harness).querySelectorAll('button')).find((b) => b.textContent?.includes(text));

  it('offers the paid plan with its price and starts the checkout on click', async () => {
    const harness = await open(FREE);

    await vi.waitFor(() => {
      harness.detectChanges();
      expect(root(harness).textContent).toContain('Premium');
    });
    expect(root(harness).textContent).toContain('15 000');
    expect(root(harness).textContent).toContain('Aucune facture');

    expect(button(harness, 'Souscrire')).toBeTruthy();
    button(harness, 'Souscrire')?.click();

    const request = await vi.waitFor(() =>
      http.expectOne('/api/v1/pro/establishments/e1/subscription'),
    );
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ planCode: 'premium', cycle: 'Monthly' });
    request.flush({ subscriptionId: 's', invoiceId: 'i', transactionId: 'KB1', paymentUrl: '#' });
  });

  it('switches the displayed price to the annual tariff', async () => {
    const harness = await open(FREE);
    await vi.waitFor(() => {
      harness.detectChanges();
      expect(root(harness).textContent).toContain('15 000');
    });

    button(harness, 'Annuel')?.click();
    harness.detectChanges();

    expect(root(harness).textContent).toContain('150 000');
  });

  it('warns about a past-due subscription and lets the owner pay the overdue invoice', async () => {
    const harness = await open(PAST_DUE, [OVERDUE]);

    await vi.waitFor(() => {
      harness.detectChanges();
      expect(root(harness).textContent).toContain('suspendues');
      expect(root(harness).textContent).toContain('KB-2026-000002');
    });
    expect(root(harness).textContent).toContain('En retard');

    button(harness, 'Payer')?.click();
    const request = await vi.waitFor(() => http.expectOne('/api/v1/pro/invoices/i1/pay'));
    expect(request.request.method).toBe('POST');
    request.flush({ subscriptionId: 's1', invoiceId: 'i1', transactionId: 'KB9', paymentUrl: '#' });
  });

  it('shows the API error message when the checkout is refused', async () => {
    const harness = await open(FREE);
    await vi.waitFor(() => {
      harness.detectChanges();
      expect(button(harness, 'Souscrire')).toBeTruthy();
    });

    button(harness, 'Souscrire')?.click();
    (await vi.waitFor(() => http.expectOne('/api/v1/pro/establishments/e1/subscription'))).flush(
      { title: 'Cet établissement a déjà un abonnement en cours.' },
      { status: 409, statusText: 'Conflict' },
    );

    await vi.waitFor(() => {
      harness.detectChanges();
      expect(root(harness).querySelector('[role="alert"]')?.textContent).toContain(
        'déjà un abonnement',
      );
    });
  });

  it('offers the free trial only to an establishment that is eligible, and starts it', async () => {
    const harness = await open({ ...FREE, trialEligiblePlanCodes: ['premium'] });
    await vi.waitFor(() => {
      harness.detectChanges();
      expect(root(harness).textContent).toContain('Essai gratuit de 14 jours');
    });

    button(harness, 'Essayer gratuitement')?.click();
    const request = await vi.waitFor(() =>
      http.expectOne('/api/v1/pro/establishments/e1/subscription/trial'),
    );
    expect(request.request.body).toEqual({ planCode: 'premium', cycle: 'Monthly' });
    request.flush({});
  });

  it('does not offer the trial once it has been used', async () => {
    const harness = await open({ ...FREE, trialEligiblePlanCodes: [] });
    await vi.waitFor(() => {
      harness.detectChanges();
      expect(button(harness, 'Souscrire')).toBeTruthy();
    });

    expect(button(harness, 'Essayer gratuitement')).toBeUndefined();
    expect(root(harness).textContent).not.toContain('Essai gratuit de');
  });

  it('shows a running trial with its end date and lets the owner pay early', async () => {
    const trialing: SubscriptionOverview = {
      ...PAST_DUE,
      subscription: {
        ...PAST_DUE.subscription!,
        status: 'Trialing',
        trialEndsAt: '2026-03-01T00:00:00Z',
      },
      effectivePlanCode: 'premium',
    };
    const harness = await open(trialing);
    await vi.waitFor(() => {
      harness.detectChanges();
      expect(root(harness).textContent).toContain('Essai gratuit jusqu');
    });

    button(harness, 'Souscrire maintenant')?.click();
    const request = await vi.waitFor(() =>
      http.expectOne('/api/v1/pro/subscriptions/s1/trial/pay'),
    );
    expect(request.request.method).toBe('POST');
    request.flush({ subscriptionId: 's1', invoiceId: 'i2', transactionId: 'KB7', paymentUrl: '#' });
  });

  it('quotes a change of plan and applies it only after confirmation', async () => {
    const active: SubscriptionOverview = {
      ...PAST_DUE,
      subscription: {
        ...PAST_DUE.subscription!,
        status: 'Active',
        planCode: 'free',
        cycle: 'Annual',
      },
      effectivePlanCode: 'free',
    };
    const harness = await open(active);
    await vi.waitFor(() => {
      harness.detectChanges();
      expect(button(harness, 'Changer de formule')).toBeTruthy();
    });

    button(harness, 'Changer de formule')?.click();
    harness.detectChanges();
    button(harness, 'Choisir cette formule')?.click();
    const quote = await vi.waitFor(() =>
      http.expectOne('/api/v1/pro/subscriptions/s1/change-quote'),
    );
    quote.flush({
      kind: 'Upgrade',
      planCode: 'premium',
      planName: 'Premium',
      cycle: 'Monthly',
      effectiveAt: '2026-02-10T00:00:00Z',
      credit: 5000,
      newPrice: 15000,
      amountDue: 10000,
      currencyCode: 'XOF',
      commitmentMonths: 12,
    });

    await vi.waitFor(() => {
      harness.detectChanges();
      expect(root(harness).textContent).toContain('Crédit de la période déjà payée');
      expect(root(harness).textContent).toContain('10 000');
    });
    http.expectNone('/api/v1/pro/subscriptions/s1/change');

    button(harness, 'Confirmer le changement')?.click();
    const change = await vi.waitFor(() => http.expectOne('/api/v1/pro/subscriptions/s1/change'));
    expect(change.request.body).toEqual({ planCode: 'premium', cycle: 'Monthly' });
    change.flush({ outcome: 'Scheduled', effectiveAt: '2027-01-01T00:00:00Z' });
  });

  it('shows the promotion of a plan with the old price struck through', async () => {
    const promoted = PLANS.map((p) =>
      p.code === 'premium' ? { ...p, promoPercent: 20, promoActive: true, promoPeriods: 2 } : p,
    );
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/pro/etablissements/e1/abonnement', SubscriptionPage);
    (await vi.waitFor(() => http.expectOne('/api/v1/plans'))).flush(promoted);
    (await vi.waitFor(() => http.expectOne('/api/v1/pro/establishments/e1/subscription'))).flush(
      FREE,
    );
    (await vi.waitFor(() => http.expectOne('/api/v1/pro/invoices'))).flush([]);

    await vi.waitFor(() => {
      harness.detectChanges();
      expect(root(harness).querySelector('s.was')?.textContent).toContain('15 000');
    });
    expect(root(harness).textContent).toContain('12 000');
    expect(root(harness).textContent).toContain('sur vos 2 premières factures');
  });

  it('applies a promotion code, shows the discounted price and sends the code with the checkout', async () => {
    const harness = await open(FREE);
    await vi.waitFor(() => {
      harness.detectChanges();
      expect(root(harness).querySelector('#promo-code')).toBeTruthy();
    });

    const input = root(harness).querySelector<HTMLInputElement>('#promo-code')!;
    input.value = 'bienvenue20';
    input.dispatchEvent(new Event('input'));
    harness.detectChanges();
    button(harness, 'Appliquer')?.click();

    const check = await vi.waitFor(() => http.expectOne('/api/v1/pro/promo-codes/check'));
    expect(check.request.body).toEqual({
      planCode: 'premium',
      cycle: 'Monthly',
      promoCode: 'bienvenue20',
    });
    check.flush({
      valid: true,
      percent: 20,
      periods: 1,
      source: 'Code',
      originalPrice: 15000,
      discountedPrice: 12000,
    });

    await vi.waitFor(() => {
      harness.detectChanges();
      expect(root(harness).textContent).toContain('appliqué');
      expect(root(harness).textContent).toContain('12 000');
    });

    button(harness, 'Souscrire')?.click();
    const request = await vi.waitFor(() =>
      http.expectOne('/api/v1/pro/establishments/e1/subscription'),
    );
    expect(request.request.body).toEqual({
      planCode: 'premium',
      cycle: 'Monthly',
      promoCode: 'bienvenue20',
    });
    request.flush({ subscriptionId: 's', invoiceId: 'i', transactionId: 'KB1', paymentUrl: '#' });
  });

  it('shows a scheduled change and lets the owner cancel it', async () => {
    const scheduled: SubscriptionOverview = {
      ...PAST_DUE,
      subscription: {
        ...PAST_DUE.subscription!,
        status: 'Active',
        pendingPlanCode: 'free',
        pendingPlanName: 'Gratuite',
        pendingCycle: 'Monthly',
        pendingUnitPrice: 5000,
        pendingChangeAt: '2027-01-01T00:00:00Z',
      },
      effectivePlanCode: 'premium',
    };
    const harness = await open(scheduled);
    await vi.waitFor(() => {
      harness.detectChanges();
      expect(root(harness).textContent).toContain('Changement programmé');
    });

    button(harness, 'Annuler le changement')?.click();
    const request = await vi.waitFor(() => http.expectOne('/api/v1/pro/subscriptions/s1/change'));
    expect(request.request.method).toBe('DELETE');
    request.flush(null, { status: 204, statusText: 'No Content' });
  });

  it('asks the server to re-read the payment when the customer is back from the payment page', async () => {
    const pending: SubscriptionOverview = {
      ...PAST_DUE,
      subscription: { ...PAST_DUE.subscription!, status: 'PendingPayment' },
    };
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/pro/etablissements/e1/abonnement?tx=KB1', SubscriptionPage);
    (await vi.waitFor(() => http.expectOne('/api/v1/plans'))).flush(PLANS);
    (await vi.waitFor(() => http.expectOne('/api/v1/pro/establishments/e1/subscription'))).flush(
      pending,
    );
    (await vi.waitFor(() => http.expectOne('/api/v1/pro/invoices'))).flush([]);

    // The provider may never call us back: the poll must make the server read its verdict.
    const refresh = await vi.waitFor(
      () => http.expectOne('/api/v1/pro/establishments/e1/subscription/refresh'),
      { timeout: 5000, interval: 100 },
    );
    expect(refresh.request.method).toBe('POST');
    refresh.flush(1);

    // Then the page reloads the subscription, which is now active.
    (await vi.waitFor(() => http.expectOne('/api/v1/pro/establishments/e1/subscription'))).flush({
      ...pending,
      subscription: { ...pending.subscription!, status: 'Active' },
    });
    (await vi.waitFor(() => http.expectOne('/api/v1/pro/invoices'))).flush([]);
    await vi.waitFor(() => {
      harness.detectChanges();
      expect(root(harness).textContent).not.toContain('Confirmation du paiement en cours');
    });
  });

  it('does not poll the server when there is no payment to wait for', async () => {
    const harness = await open(FREE);
    await vi.waitFor(() => {
      harness.detectChanges();
      expect(root(harness).textContent).toContain('Premium');
    });
    await new Promise((resolve) => setTimeout(resolve, 2500));
    http.expectNone('/api/v1/pro/establishments/e1/subscription/refresh');
  });
});
