import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  input,
  resource,
  signal,
} from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import {
  BillingService,
  INVOICE_STATUS,
  SUBSCRIPTION_STATUS,
  type BillingCycle,
  type ChangeQuote,
  type Invoice,
  type Plan,
  type PromoCheck,
  type SubscriptionOverview,
} from '../../../core/billing/billing.service';
import {
  commitmentLabel,
  planConditions,
  promoPrice,
  trialLabel,
} from '../../../core/billing/plan-conditions';
import { formatMoney } from '../../../core/format/format';

const POLL_MS = 2000;
const POLL_MAX = 15;

/** A change of plan the partner is looking at, with what it would do, before confirming. */
interface PendingChange {
  readonly subscriptionId: string;
  readonly planCode: string;
  readonly cycle: BillingCycle;
  readonly quote: ChangeQuote;
}

/** `/pro/etablissements/:id/abonnement` — plans, current subscription and invoices (PRD BIL-01 to BIL-07). */
@Component({
  selector: 'app-subscription-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  styles: `
    .page {
      display: grid;
      gap: 1.5rem;
      padding-block: clamp(1.5rem, 4vw, 3rem);
    }
    .cycle {
      display: inline-flex;
      gap: 0.25rem;
      padding: 0.25rem;
      border: 1px solid var(--kb-border);
      border-radius: var(--radius-pill);
      background: #fff;
    }
    .promo {
      display: flex;
      flex-wrap: wrap;
      gap: 0.5rem;
      align-items: end;
    }
    .promo label {
      font-weight: 600;
      font-size: var(--fs-sm);
    }
    .was {
      font-size: 1rem;
      font-weight: 400;
      color: var(--kb-ink-muted);
    }
    .plans {
      display: grid;
      gap: 1rem;
      grid-template-columns: repeat(auto-fit, minmax(min(100%, 16rem), 1fr));
    }
    .plan,
    .current,
    .invoices {
      display: grid;
      gap: 0.75rem;
      align-content: start;
      padding: 1.25rem;
      border: 1px solid var(--kb-border);
      border-radius: var(--radius-lg);
      background: #fff;
      box-shadow: var(--shadow-soft);
    }
    .plan[data-current='true'] {
      border-color: var(--kb-primary-400);
    }
    .price {
      font-family: var(--font-display);
      font-size: 1.75rem;
      font-weight: 700;
    }
    .price small {
      font-size: var(--fs-sm);
      font-weight: 400;
      color: var(--kb-ink-muted);
    }
    .features {
      display: grid;
      gap: 0.375rem;
      padding: 0;
      list-style: none;
    }
    .features li::before {
      content: '✓ ';
      color: var(--kb-success);
      font-weight: 700;
    }
    .features li[data-off='true'] {
      color: var(--kb-ink-muted);
    }
    .features li[data-off='true']::before {
      content: '— ';
      color: var(--kb-ink-muted);
    }
    .badge {
      display: inline-block;
      width: fit-content;
      padding: 0.125rem 0.75rem;
      border-radius: var(--radius-pill);
      background: var(--kb-primary-50);
      font-size: var(--fs-xs);
      font-weight: 700;
      color: var(--kb-primary-700);
    }
    .badge[data-tone='ok'] {
      background: color-mix(in srgb, var(--kb-success) 12%, white);
      color: var(--kb-success);
    }
    .badge[data-tone='bad'] {
      background: color-mix(in srgb, var(--kb-danger) 10%, white);
      color: var(--kb-danger);
    }
    table {
      width: 100%;
      border-collapse: collapse;
      font-size: var(--fs-md);
    }
    th,
    td {
      padding: 0.5rem;
      text-align: left;
      border-bottom: 1px solid var(--kb-border);
    }
    .scroll {
      position: relative;
      overflow-x: auto;
    }
    .row-actions {
      display: flex;
      flex-wrap: wrap;
      gap: 0.5rem;
    }
  `,
  template: `
    <div class="container page">
      <a class="link" routerLink="/pro">← Mes établissements</a>
      <div>
        <p class="eyebrow">Abonnement</p>
        <h1>Formule et <em>facturation</em></h1>
      </div>

      @if (polling()) {
        <p role="status" class="badge">Confirmation du paiement en cours…</p>
      }
      @if (error(); as message) {
        <p class="form-error" role="alert">{{ message }}</p>
      }

      @if (overview.hasValue()) {
        @let o = overview.value();
        @if (o.subscription; as s) {
          <section class="current" aria-labelledby="current-title">
            <h2 id="current-title">Votre abonnement</h2>
            <p>
              <strong>{{ s.planName }}</strong> ·
              {{ s.cycle === 'Annual' ? 'annuel' : 'mensuel' }} ·
              {{ money(s.unitPrice, s.currencyCode) }}
              <span
                class="badge"
                [attr.data-tone]="
                  s.status === 'Active' || s.status === 'Trialing'
                    ? 'ok'
                    : s.status === 'PastDue'
                      ? 'bad'
                      : ''
                "
                >{{ statusLabel(s.status) }}</span
              >
            </p>
            @if (s.commitmentEndDate) {
              <p>
                Engagement jusqu'au {{ day(s.commitmentEndDate) }}
                @if (s.currentPeriodEnd) {
                  · période en cours jusqu'au {{ day(s.currentPeriodEnd) }}
                }
              </p>
            }
            @if (s.status === 'Trialing' && s.trialEndsAt) {
              <p role="status">
                Essai gratuit jusqu'au {{ day(s.trialEndsAt) }}. Sans paiement de votre part, votre
                fiche repasse alors à la formule Gratuite.
              </p>
              <div class="row-actions">
                <button
                  type="button"
                  class="btn btn-primary btn-sm"
                  [disabled]="busy()"
                  (click)="payTrial(s.id)"
                >
                  Souscrire maintenant
                </button>
                <button
                  type="button"
                  class="btn btn-ghost btn-sm"
                  [disabled]="busy()"
                  (click)="cancel(s.id)"
                >
                  Arrêter l'essai
                </button>
              </div>
            }
            @if (s.pendingPlanName && s.pendingChangeAt && s.pendingUnitPrice) {
              <p role="status">
                Changement programmé : à partir du {{ day(s.pendingChangeAt) }}, formule
                <strong>{{ s.pendingPlanName }}</strong>
                ({{ s.pendingCycle === 'Annual' ? 'annuel' : 'mensuel' }},
                {{ money(s.pendingUnitPrice, s.currencyCode) }}).
              </p>
              <div>
                <button
                  type="button"
                  class="btn btn-ghost btn-sm"
                  [disabled]="busy()"
                  (click)="cancelChange(s.id)"
                >
                  Annuler le changement
                </button>
              </div>
            }
            @if (s.status === 'PastDue') {
              <p class="form-error" role="status">
                Une facture est impayée : la réservation en ligne, les statistiques et la mise en
                avant sont suspendues. Réglez-la ci-dessous pour les rétablir.
              </p>
            }
            @if (s.cancelAtCommitmentEnd) {
              <p role="status">
                Résiliation programmée : l'abonnement s'arrêtera à la fin de l'engagement.
              </p>
            } @else if (s.status === 'Active' || s.status === 'PastDue') {
              <div>
                <button type="button" class="btn btn-ghost btn-sm" (click)="cancel(s.id)">
                  Résilier à la fin de l'engagement
                </button>
              </div>
            } @else if (s.status === 'PendingPayment') {
              <div>
                <button type="button" class="btn btn-ghost btn-sm" (click)="cancel(s.id)">
                  Abandonner cette souscription
                </button>
              </div>
            }
            @if (s.status === 'Active' && !changing()) {
              <div>
                <button type="button" class="btn btn-primary btn-sm" (click)="changing.set(true)">
                  Changer de formule
                </button>
              </div>
            }
          </section>
        }

        @if (pendingChange(); as change) {
          <section class="current" aria-labelledby="quote-title">
            <h2 id="quote-title">Confirmer le changement</h2>
            @if (change.quote.kind === 'Upgrade') {
              <p>
                Passage <strong>immédiat</strong> à <strong>{{ change.quote.planName }}</strong> ({{
                  change.quote.cycle === 'Annual' ? 'annuel' : 'mensuel'
                }}, {{ money(change.quote.newPrice, change.quote.currencyCode) }}).
              </p>
              <ul>
                <li>
                  Crédit de la période déjà payée : −{{
                    money(change.quote.credit, change.quote.currencyCode)
                  }}
                </li>
                <li>
                  @if (change.quote.amountDue > 0) {
                    À payer maintenant :
                    <strong>{{ money(change.quote.amountDue, change.quote.currencyCode) }}</strong>
                  } @else {
                    Rien à payer : votre crédit couvre la première période.
                  }
                </li>
                <li>{{ commitment(change.quote.commitmentMonths) }} à compter d'aujourd'hui.</li>
              </ul>
            } @else {
              <p>
                Votre formule actuelle reste valable, avec toutes ses fonctionnalités, jusqu'au
                <strong>{{ day(change.quote.effectiveAt) }}</strong> (fin de votre engagement). À
                cette date, vous passerez à <strong>{{ change.quote.planName }}</strong> ({{
                  change.quote.cycle === 'Annual' ? 'annuel' : 'mensuel'
                }}, {{ money(change.quote.newPrice, change.quote.currencyCode) }}). Rien à payer
                maintenant.
              </p>
            }
            <div class="row-actions">
              <button
                type="button"
                class="btn btn-primary btn-sm"
                [disabled]="busy()"
                (click)="confirmChange()"
              >
                Confirmer le changement
              </button>
              <button
                type="button"
                class="btn btn-ghost btn-sm"
                [disabled]="busy()"
                (click)="pendingChange.set(null)"
              >
                Annuler
              </button>
            </div>
          </section>
        }

        @if (
          !o.subscription || o.subscription.status === 'PendingPayment' || activeSubscription()
        ) {
          <section aria-labelledby="plans-title">
            <h2 id="plans-title">
              {{ activeSubscription() ? 'Changer de formule' : 'Choisir une formule' }}
            </h2>
            <div class="cycle" role="group" aria-label="Périodicité">
              <button
                type="button"
                class="btn btn-sm"
                [class.btn-primary]="cycle() === 'Monthly'"
                [class.btn-ghost]="cycle() !== 'Monthly'"
                (click)="cycle.set('Monthly')"
              >
                Mensuel
              </button>
              <button
                type="button"
                class="btn btn-sm"
                [class.btn-primary]="cycle() === 'Annual'"
                [class.btn-ghost]="cycle() !== 'Annual'"
                (click)="cycle.set('Annual')"
              >
                Annuel
              </button>
            </div>
            <p class="auth-lead">
              L'engagement dépend de la formule choisie ; résiliable à son terme.
            </p>
            @if (!activeSubscription()) {
              <form class="promo" (submit)="applyPromo($event)">
                <div>
                  <label for="promo-code">Code promo</label>
                  <input
                    #code
                    id="promo-code"
                    class="admin-input"
                    maxlength="30"
                    autocomplete="off"
                    [value]="promoInput()"
                    (input)="promoInput.set(code.value)"
                  />
                </div>
                <button
                  type="submit"
                  class="btn btn-ghost btn-sm"
                  [disabled]="busy() || !promoInput().trim()"
                >
                  Appliquer
                </button>
              </form>
              @if (promoNote(); as note) {
                <p role="status" class="hint">{{ note }}</p>
              }
            }
            <div class="plans">
              @for (plan of plans.value() ?? []; track plan.id) {
                <article class="plan" [attr.data-current]="plan.code === o.effectivePlanCode">
                  @if (plan.badge) {
                    <span class="badge">{{ plan.badge }}</span>
                  }
                  <h3>{{ plan.name }}</h3>
                  @if (plan.description) {
                    <p>{{ plan.description }}</p>
                  }
                  <p class="price">
                    @if (discounted(plan); as d) {
                      <s class="was">{{ money(price(plan), plan.currencyCode) }}</s>
                      {{ money(d.price, plan.currencyCode) }}
                    } @else {
                      {{ money(price(plan), plan.currencyCode) }}
                    }
                    <small>/ {{ cycle() === 'Annual' ? 'an' : 'mois' }}</small>
                  </p>
                  @if (discounted(plan); as d) {
                    <p class="hint">
                      <strong
                        >−{{ d.percent }} %
                        {{
                          d.periods > 1
                            ? 'sur vos ' + d.periods + ' premières factures'
                            : 'sur votre première facture'
                        }}</strong
                      >
                    </p>
                  }
                  <ul class="features">
                    @for (c of conditions(plan); track c.label) {
                      <li [attr.data-off]="!c.on">{{ c.label }}</li>
                    }
                  </ul>
                  @if (price(plan) > 0) {
                    <p class="hint">{{ commitment(plan.commitmentMonths) }}</p>
                  }
                  @if (activeSubscription(); as active) {
                    @if (price(plan) > 0) {
                      @if (active.planCode === plan.code && active.cycle === cycle()) {
                        <span class="badge">Formule actuelle</span>
                      } @else {
                        <button
                          type="button"
                          class="btn btn-primary"
                          [disabled]="busy()"
                          (click)="quote(active.id, plan)"
                        >
                          Choisir cette formule
                        </button>
                      }
                    }
                  } @else if (price(plan) > 0) {
                    @if (trialOffered(o, plan)) {
                      <p class="hint">
                        <strong>{{ trial(plan.trialDays) }}</strong>
                      </p>
                    }
                    <button
                      type="button"
                      class="btn btn-primary"
                      [disabled]="busy()"
                      (click)="subscribe(plan)"
                    >
                      Souscrire et payer
                    </button>
                    @if (trialOffered(o, plan)) {
                      <button
                        type="button"
                        class="btn btn-ghost"
                        [disabled]="busy()"
                        (click)="startTrial(plan)"
                      >
                        Essayer gratuitement
                      </button>
                    }
                  } @else if (plan.code === o.effectivePlanCode) {
                    <span class="badge">Formule actuelle</span>
                  } @else if (plan.monthlyPrice > 0 || plan.annualPrice > 0) {
                    <span class="hint"
                      >Non proposée en paiement
                      {{ cycle() === 'Annual' ? 'annuel' : 'mensuel' }}.</span
                    >
                  }
                </article>
              }
            </div>
          </section>
        }
      } @else if (overview.error()) {
        <p class="form-error" role="alert">Impossible de charger votre abonnement.</p>
      } @else {
        <p role="status">Chargement…</p>
      }

      <section class="invoices" aria-labelledby="inv-title">
        <h2 id="inv-title">Factures</h2>
        @if (forEstablishment().length > 0) {
          <div class="scroll">
            <table>
              <thead>
                <tr>
                  <th scope="col">N°</th>
                  <th scope="col">Période</th>
                  <th scope="col">Montant</th>
                  <th scope="col">Statut</th>
                  <th scope="col"><span class="visually-hidden">Actions</span></th>
                </tr>
              </thead>
              <tbody>
                @for (i of forEstablishment(); track i.id) {
                  <tr>
                    <td>{{ i.number }}</td>
                    <td>{{ day(i.periodStart) }} → {{ day(i.periodEnd) }}</td>
                    <td>{{ money(i.amount, i.currencyCode) }}</td>
                    <td>
                      <span class="badge" [attr.data-tone]="tone(i)">{{
                        invoiceLabel(i.status)
                      }}</span>
                    </td>
                    <td>
                      <div class="row-actions">
                        <button type="button" class="btn btn-ghost btn-sm" (click)="pdf(i.id)">
                          PDF
                        </button>
                        @if (i.status === 'Open' || i.status === 'Overdue') {
                          <button
                            type="button"
                            class="btn btn-primary btn-sm"
                            [disabled]="busy()"
                            (click)="pay(i.id)"
                          >
                            Payer
                          </button>
                        }
                      </div>
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        } @else {
          <p>Aucune facture pour le moment.</p>
        }
      </section>
    </div>
  `,
})
export class SubscriptionPage {
  /** Bound from the `:id` route parameter. */
  readonly id = input.required<string>();
  /** Transaction id the payment provider sends the customer back with (`?tx=`). */
  readonly tx = input<string>();

  private readonly billing = inject(BillingService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly conditions = planConditions;
  protected readonly commitment = commitmentLabel;
  protected readonly trial = trialLabel;
  protected readonly promoInput = signal('');
  protected readonly promoChecks = signal<Record<string, PromoCheck>>({});
  protected readonly promoNote = signal<string | null>(null);
  protected readonly changing = signal(false);
  protected readonly pendingChange = signal<PendingChange | null>(null);
  protected readonly cycle = signal<BillingCycle>('Monthly');
  protected readonly busy = signal(false);
  protected readonly polling = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly plans = resource({ loader: () => this.billing.plans() });
  protected readonly overview = resource({
    params: () => this.id(),
    loader: ({ params }) => this.billing.overview(params),
  });
  /** The running subscription while the partner is choosing another plan (active and paid up). */
  protected readonly activeSubscription = computed(() => {
    const subscription = this.overview.value()?.subscription;
    return this.changing() && subscription?.status === 'Active' ? subscription : null;
  });
  protected readonly invoices = resource({ loader: () => this.billing.invoices() });
  protected readonly forEstablishment = computed<Invoice[]>(() =>
    (this.invoices.value() ?? []).filter((i) => i.establishmentId === this.id()),
  );

  constructor() {
    // Back from the payment page: wait for the notification to land (a few seconds at most).
    effect(() => {
      if (this.tx() && this.overview.hasValue() && !this.polling()) {
        if (this.overview.value().subscription?.status === 'PendingPayment') {
          this.startPolling();
        }
      }
    });
  }

  protected price(plan: Plan): number {
    return this.cycle() === 'Annual' ? plan.annualPrice : plan.monthlyPrice;
  }

  /** The discount shown on a plan: the code checked by the server, or the plan's own promotion. */
  protected discounted(plan: Plan): { price: number; percent: number; periods: number } | null {
    const base = this.price(plan);
    const check = this.promoChecks()[plan.code];
    if (base > 0 && check?.valid) {
      return {
        price: promoPrice(base, check.percent),
        percent: check.percent,
        periods: check.periods,
      };
    }

    if (base > 0 && plan.promoActive) {
      return {
        price: promoPrice(base, plan.promoPercent),
        percent: plan.promoPercent,
        periods: plan.promoPeriods,
      };
    }

    return null;
  }

  /** The code is sent along only for the plans it was checked valid on. */
  private codeFor(plan: Plan): string | undefined {
    return this.promoChecks()[plan.code]?.valid ? this.promoInput().trim() : undefined;
  }

  protected async applyPromo(event: Event): Promise<void> {
    event.preventDefault();
    const code = this.promoInput().trim();
    await this.run(async () => {
      const paid = (this.plans.value() ?? []).filter(
        (p) => p.monthlyPrice > 0 || p.annualPrice > 0,
      );
      const checks = await Promise.all(
        paid.map(
          async (p) => [p.code, await this.billing.checkPromo(p.code, this.cycle(), code)] as const,
        ),
      );
      const valid = checks.filter(([, c]) => c.valid);
      this.promoChecks.set(Object.fromEntries(valid));
      this.promoNote.set(
        valid.length > 0
          ? `Code ${code.toUpperCase()} appliqué.`
          : (checks[0]?.[1].message ?? 'Ce code ne s’applique à aucune formule.'),
      );
    });
  }

  protected trialOffered(overview: SubscriptionOverview, plan: Plan): boolean {
    return (
      plan.trialDays > 0 &&
      this.price(plan) > 0 &&
      (overview.trialEligiblePlanCodes ?? []).includes(plan.code)
    );
  }

  protected money(amount: number, currency: string): string {
    return formatMoney(amount, currency);
  }

  protected day(iso: string): string {
    return new Date(iso).toLocaleDateString('fr-FR');
  }

  protected statusLabel(status: string): string {
    return SUBSCRIPTION_STATUS[status] ?? status;
  }

  protected invoiceLabel(status: string): string {
    return INVOICE_STATUS[status] ?? status;
  }

  protected tone(invoice: Invoice): string {
    return invoice.status === 'Paid' ? 'ok' : invoice.status === 'Overdue' ? 'bad' : '';
  }

  protected async subscribe(plan: Plan): Promise<void> {
    await this.run(async () => {
      const checkout = await this.billing.subscribe(
        this.id(),
        plan.code,
        this.cycle(),
        this.codeFor(plan),
      );
      window.location.assign(checkout.paymentUrl);
    });
  }

  protected async startTrial(plan: Plan): Promise<void> {
    await this.run(async () => {
      await this.billing.startTrial(this.id(), plan.code, this.cycle(), this.codeFor(plan));
      this.refresh();
    });
  }

  protected async payTrial(subscriptionId: string): Promise<void> {
    await this.run(async () => {
      const checkout = await this.billing.payTrial(subscriptionId);
      window.location.assign(checkout.paymentUrl);
    });
  }

  protected async quote(subscriptionId: string, plan: Plan): Promise<void> {
    await this.run(async () => {
      const cycle = this.cycle();
      const quote = await this.billing.quoteChange(subscriptionId, plan.code, cycle);
      this.pendingChange.set({ subscriptionId, planCode: plan.code, cycle, quote });
    });
  }

  protected async confirmChange(): Promise<void> {
    const change = this.pendingChange();
    if (!change) {
      return;
    }

    await this.run(async () => {
      const result = await this.billing.changePlan(
        change.subscriptionId,
        change.planCode,
        change.cycle,
      );
      this.pendingChange.set(null);
      this.changing.set(false);
      if (result.outcome === 'PaymentRequired' && result.checkout) {
        window.location.assign(result.checkout.paymentUrl);
        return;
      }

      this.refresh();
    });
  }

  protected async cancelChange(subscriptionId: string): Promise<void> {
    await this.run(async () => {
      await this.billing.cancelScheduledChange(subscriptionId);
      this.refresh();
    });
  }

  protected async pay(invoiceId: string): Promise<void> {
    await this.run(async () => {
      const checkout = await this.billing.payInvoice(invoiceId);
      window.location.assign(checkout.paymentUrl);
    });
  }

  protected async cancel(subscriptionId: string): Promise<void> {
    await this.run(async () => {
      await this.billing.cancel(subscriptionId);
      this.refresh();
    });
  }

  protected async pdf(invoiceId: string): Promise<void> {
    await this.run(() => this.billing.openInvoicePdf(invoiceId));
  }

  private refresh(): void {
    this.overview.reload();
    this.invoices.reload();
  }

  private startPolling(): void {
    this.polling.set(true);
    let attempts = 0;
    const timer = setInterval(() => {
      attempts++;
      this.refresh();
      const status = this.overview.value()?.subscription?.status;
      if ((status && status !== 'PendingPayment') || attempts >= POLL_MAX) {
        clearInterval(timer);
        this.polling.set(false);
      }
    }, POLL_MS);
    this.destroyRef.onDestroy(() => clearInterval(timer));
  }

  private async run(action: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await action();
    } catch (e) {
      this.error.set(this.message(e));
    } finally {
      this.busy.set(false);
    }
  }

  private message(e: unknown): string {
    if (e instanceof HttpErrorResponse) {
      const title = (e.error as { title?: string } | null)?.title;
      if (title) {
        return title;
      }
    }

    return 'Une erreur est survenue. Réessayez dans un instant.';
  }
}
