import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import type { components } from '../api/api.generated';

type Schemas = components['schemas'];

export type Plan = Schemas['PlanDto'];
export type PlanFeatures = Schemas['PlanFeaturesDto'];
export type SubscriptionOverview = Schemas['SubscriptionOverview'];
export type Subscription = Schemas['SubscriptionDto'];
export type Checkout = Schemas['CheckoutDto'];
export type Invoice = Schemas['InvoiceDto'];
export type ChangeQuote = Schemas['ChangeQuoteDto'];
export type PromoCheck = Schemas['PromoCheckDto'];
export type ChangeResult = Schemas['ChangeResultDto'];
export type BillingCycle = 'Monthly' | 'Annual';

export const SUBSCRIPTION_STATUS: Record<string, string> = {
  PendingPayment: 'En attente de paiement',
  Trialing: 'Essai gratuit',
  Active: 'Actif',
  PastDue: 'Impayé — fonctionnalités suspendues',
  Suspended: 'Suspendu',
  Cancelled: 'Résilié',
  Expired: 'Expiré',
};

export const INVOICE_STATUS: Record<string, string> = {
  Open: 'À payer',
  Paid: 'Payée',
  Overdue: 'En retard',
  Void: 'Annulée',
};

/** Plans, subscription, invoices and the development payment simulator (PRD BIL-01 to BIL-08). */
@Injectable({ providedIn: 'root' })
export class BillingService {
  private readonly http = inject(HttpClient);

  plans(): Promise<Plan[]> {
    return firstValueFrom(this.http.get<Plan[]>('/api/v1/plans'));
  }

  overview(establishmentId: string): Promise<SubscriptionOverview> {
    return firstValueFrom(
      this.http.get<SubscriptionOverview>(
        `/api/v1/pro/establishments/${establishmentId}/subscription`,
      ),
    );
  }

  /**
   * Back from the payment page: asks the server to re-read the provider's verdict on the pending payments (a provider may never
   * call us back). Resolves with how many payments were settled.
   */
  refreshPending(establishmentId: string): Promise<number> {
    return firstValueFrom(
      this.http.post<number>(`/api/v1/pro/establishments/${establishmentId}/subscription/refresh`, {}),
    );
  }

  subscribe(
    establishmentId: string,
    planCode: string,
    cycle: BillingCycle,
    promoCode?: string,
  ): Promise<Checkout> {
    return firstValueFrom(
      this.http.post<Checkout>(`/api/v1/pro/establishments/${establishmentId}/subscription`, {
        planCode,
        cycle,
        ...(promoCode ? { promoCode } : {}),
      }),
    );
  }

  /** What a promotion code would give on a plan, before paying. */
  checkPromo(planCode: string, cycle: BillingCycle, promoCode: string): Promise<PromoCheck> {
    return firstValueFrom(
      this.http.post<PromoCheck>('/api/v1/pro/promo-codes/check', { planCode, cycle, promoCode }),
    );
  }

  /** Starts the free trial of a plan: its features are available at once, nothing is paid. */
  startTrial(
    establishmentId: string,
    planCode: string,
    cycle: BillingCycle,
    promoCode?: string,
  ): Promise<Subscription> {
    return firstValueFrom(
      this.http.post<Subscription>(
        `/api/v1/pro/establishments/${establishmentId}/subscription/trial`,
        { planCode, cycle, ...(promoCode ? { promoCode } : {}) },
      ),
    );
  }

  /** Pays the first invoice during the trial: the paid period still starts when the trial ends. */
  payTrial(subscriptionId: string): Promise<Checkout> {
    return firstValueFrom(
      this.http.post<Checkout>(`/api/v1/pro/subscriptions/${subscriptionId}/trial/pay`, {}),
    );
  }

  /** What changing to another plan would do (credit, amount due, date), before the partner confirms. */
  quoteChange(subscriptionId: string, planCode: string, cycle: BillingCycle): Promise<ChangeQuote> {
    return firstValueFrom(
      this.http.post<ChangeQuote>(`/api/v1/pro/subscriptions/${subscriptionId}/change-quote`, {
        planCode,
        cycle,
      }),
    );
  }

  changePlan(subscriptionId: string, planCode: string, cycle: BillingCycle): Promise<ChangeResult> {
    return firstValueFrom(
      this.http.post<ChangeResult>(`/api/v1/pro/subscriptions/${subscriptionId}/change`, {
        planCode,
        cycle,
      }),
    );
  }

  cancelScheduledChange(subscriptionId: string): Promise<void> {
    return firstValueFrom(
      this.http.delete<void>(`/api/v1/pro/subscriptions/${subscriptionId}/change`),
    );
  }

  cancel(subscriptionId: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`/api/v1/pro/subscriptions/${subscriptionId}`));
  }

  invoices(): Promise<Invoice[]> {
    return firstValueFrom(this.http.get<Invoice[]>('/api/v1/pro/invoices'));
  }

  payInvoice(invoiceId: string): Promise<Checkout> {
    return firstValueFrom(this.http.post<Checkout>(`/api/v1/pro/invoices/${invoiceId}/pay`, {}));
  }

  /** Opens the PDF in a new tab (the endpoint needs the bearer token, so the file is fetched first). */
  async openInvoicePdf(invoiceId: string): Promise<void> {
    const blob = await firstValueFrom(
      this.http.get(`/api/v1/pro/invoices/${invoiceId}/pdf`, { responseType: 'blob' }),
    );
    const url = URL.createObjectURL(blob);
    window.open(url, '_blank', 'noopener');
    setTimeout(() => URL.revokeObjectURL(url), 60_000);
  }

  /** Development gateway only: plays the part of the customer on the provider's payment page. */
  simulate(transactionId: string, status: 'accepted' | 'refused'): Promise<void> {
    return firstValueFrom(
      this.http.post<void>(`/api/v1/dev/payments/${encodeURIComponent(transactionId)}/simulate`, {
        status,
      }),
    );
  }
}
