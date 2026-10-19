import { ChangeDetectionStrategy, Component, inject, resource, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { BillingService, type BillingCycle } from '../../../core/billing/billing.service';
import {
  commitmentLabel,
  planConditions,
  promoLabel,
  trialLabel,
} from '../../../core/billing/plan-conditions';
import { formatMoney } from '../../../core/format/format';

/** `/tarifs` — public plans and prices for professionals (PRD BIL-01). */
@Component({
  selector: 'app-pricing',
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
      background: var(--kb-card);
    }
    .plans {
      display: grid;
      gap: 1rem;
      grid-template-columns: repeat(auto-fit, minmax(min(100%, 16rem), 1fr));
    }
    .plan {
      display: grid;
      gap: 0.75rem;
      align-content: start;
      padding: 1.25rem;
      border: 1px solid var(--kb-border);
      border-radius: var(--radius-lg);
      background: var(--kb-card);
      box-shadow: var(--shadow-soft);
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
  `,
  template: `
    <div class="container page">
      <div>
        <p class="eyebrow">Professionnels</p>
        <h1>Tarifs</h1>
        <p class="auth-lead">
          Votre fiche est gratuite. Choisissez la formule qui correspond à votre activité.
        </p>
      </div>
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
      @if (plans.hasValue()) {
        <div class="plans">
          @for (plan of plans.value(); track plan.id) {
            <article class="plan">
              @if (plan.badge) {
                <span class="badge">{{ plan.badge }}</span>
              }
              <h2>{{ plan.name }}</h2>
              @if (plan.description) {
                <p>{{ plan.description }}</p>
              }
              <p class="price">
                {{
                  money(
                    cycle() === 'Annual' ? plan.annualPrice : plan.monthlyPrice,
                    plan.currencyCode
                  )
                }}
                @if (plan.monthlyPrice > 0) {
                  <small>/ {{ cycle() === 'Annual' ? 'an' : 'mois' }}</small>
                }
              </p>
              <ul class="features">
                @for (c of conditions(plan); track c.label) {
                  <li [attr.data-off]="!c.on">{{ c.label }}</li>
                }
              </ul>
              @if (plan.monthlyPrice > 0 || plan.annualPrice > 0) {
                <p class="hint">{{ commitment(plan.commitmentMonths) }}</p>
                @if (plan.trialDays > 0) {
                  <p class="hint">
                    <strong>{{ trial(plan.trialDays) }}</strong>
                  </p>
                }
                @if (plan.promoActive) {
                  <p class="hint">
                    <strong>{{ promo(plan) }}</strong>
                  </p>
                }
              }
            </article>
          }
        </div>
        <p>Résiliable à l'échéance de l'engagement. Paiement par Mobile Money ou carte.</p>
        <a class="btn btn-primary" routerLink="/pro">Gérer mon abonnement</a>
      } @else if (plans.error()) {
        <p class="form-error" role="alert">Impossible de charger les tarifs.</p>
      } @else {
        <p role="status">Chargement…</p>
      }
    </div>
  `,
})
export class Pricing {
  private readonly billing = inject(BillingService);
  protected readonly cycle = signal<BillingCycle>('Monthly');
  protected readonly plans = resource({ loader: () => this.billing.plans() });

  protected readonly conditions = planConditions;
  protected readonly commitment = commitmentLabel;
  protected readonly trial = trialLabel;
  protected readonly promo = promoLabel;

  protected money(amount: number, currency: string): string {
    return amount === 0 ? 'Gratuit' : formatMoney(amount, currency);
  }
}
