import { ChangeDetectionStrategy, Component, inject, resource, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AdminApiService } from '../../core/admin/admin-api.service';
import { problemMessage } from '../../core/auth/auth.models';
import { formatMoney } from '../../core/format/format';

const PERIODS = [
  { days: 7, label: '7 jours' },
  { days: 30, label: '30 jours' },
  { days: 90, label: '90 jours' },
  { days: 365, label: '12 mois' },
];

const METHOD_LABELS: Record<string, string> = {
  Wave: 'Wave',
  OrangeMoney: 'Orange Money',
  Mtn: 'MTN',
  Moov: 'Moov',
  Card: 'Carte bancaire',
  Other: 'Autre',
};

/** `/admin/finances` — subscriptions per plan, recurring revenue, cash-in, unpaid list with manual reminder, CSV (PRD ADM-04). */
@Component({
  selector: 'app-finance-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, DatePipe],
  styles: `
    .figure {
      font-family: var(--font-display);
      font-size: 2rem;
      font-weight: 700;
    }
  `,
  template: `
    <div class="container admin-page">
      <div>
        <p class="eyebrow">Administration</p>
        <h1>Finances</h1>
      </div>

      <div class="admin-filters" role="group" aria-label="Période">
        @for (p of periods; track p.days) {
          <button
            type="button"
            class="btn btn-sm"
            [class.btn-primary]="days() === p.days"
            [class.btn-ghost]="days() !== p.days"
            (click)="days.set(p.days)"
          >
            {{ p.label }}
          </button>
        }
        <button
          type="button"
          class="btn btn-ghost btn-sm"
          [disabled]="busy()"
          (click)="exportCsv()"
        >
          Exporter les paiements (CSV)
        </button>
      </div>

      @if (message(); as text) {
        <p role="status" class="badge" data-tone="ok">{{ text }}</p>
      }
      @if (error(); as text) {
        <p class="form-error" role="alert">{{ text }}</p>
      }

      @if (summary.hasValue()) {
        @let s = summary.value();
        <div class="admin-cards">
          <section class="admin-card" aria-label="Revenu récurrent mensuel">
            <span class="figure">{{ money(s.monthlyRecurringRevenue) }}</span>
            Revenu récurrent mensuel
          </section>
          <section class="admin-card" aria-label="Encaissements">
            <span class="figure">{{ money(s.revenue) }}</span>
            Encaissé sur la période
          </section>
          @for (p of s.plans; track p.planCode) {
            <section class="admin-card" [attr.aria-label]="'Abonnements ' + p.planName">
              <span class="figure">{{ p.active }}</span>
              Abonnements {{ p.planName }} actifs
              @if (p.pastDue > 0) {
                <span class="badge" data-tone="bad">{{ p.pastDue }} impayé(s)</span>
              }
            </section>
          }
        </div>

        <section class="admin-card" aria-labelledby="methods-title">
          <h2 id="methods-title">Encaissements par moyen de paiement</h2>
          @if (s.revenueByMethod.length > 0) {
            <div class="table-scroll">
              <table class="admin-table">
                <thead>
                  <tr>
                    <th scope="col">Moyen</th>
                    <th scope="col">Paiements</th>
                    <th scope="col">Montant</th>
                  </tr>
                </thead>
                <tbody>
                  @for (m of s.revenueByMethod; track m.method) {
                    <tr>
                      <td>{{ methodLabel(m.method) }}</td>
                      <td>{{ m.payments }}</td>
                      <td>{{ money(m.amount) }}</td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          } @else {
            <p>Aucun paiement sur la période.</p>
          }
        </section>

        <section class="admin-card" aria-labelledby="pastdue-title">
          <h2 id="pastdue-title">Impayés</h2>
          @if (s.pastDue.length > 0) {
            <div class="table-scroll">
              <table class="admin-table">
                <thead>
                  <tr>
                    <th scope="col">Établissement</th>
                    <th scope="col">Propriétaire</th>
                    <th scope="col">Facture</th>
                    <th scope="col">Montant</th>
                    <th scope="col">Échéance</th>
                    <th scope="col">Retard</th>
                    <th scope="col"><span class="visually-hidden">Actions</span></th>
                  </tr>
                </thead>
                <tbody>
                  @for (d of s.pastDue; track d.subscriptionId) {
                    <tr>
                      <td>{{ d.establishmentName }}</td>
                      <td>{{ d.ownerEmail ?? '—' }}</td>
                      <td>{{ d.invoiceNumber }}</td>
                      <td>{{ money(d.amount) }}</td>
                      <td>{{ d.dueDate | date: 'dd/MM/yyyy' }}</td>
                      <td>{{ d.daysLate }} j</td>
                      <td>
                        <button
                          type="button"
                          class="btn btn-primary btn-sm"
                          [disabled]="busy()"
                          (click)="remind(d.subscriptionId)"
                        >
                          Relancer
                        </button>
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          } @else {
            <p>Aucun impayé.</p>
          }
        </section>
      } @else if (summary.error()) {
        <p class="form-error" role="alert">Impossible de charger le tableau de bord.</p>
      } @else {
        <p role="status">Chargement…</p>
      }
    </div>
  `,
})
export class FinancePage {
  private readonly api = inject(AdminApiService);

  protected readonly periods = PERIODS;
  protected readonly days = signal(30);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly message = signal<string | null>(null);

  protected readonly summary = resource({
    params: () => this.days(),
    loader: ({ params }) => {
      const range = this.range(params);
      return this.api.financeSummary(range.from, range.to);
    },
  });

  protected money(amount: number): string {
    return formatMoney(amount);
  }

  protected methodLabel(method: string): string {
    return METHOD_LABELS[method] ?? method;
  }

  protected async remind(subscriptionId: string): Promise<void> {
    await this.run(async () => {
      await this.api.remind(subscriptionId);
      this.message.set('Relance envoyée au propriétaire.');
    });
  }

  protected async exportCsv(): Promise<void> {
    const range = this.range(this.days());
    await this.run(() => this.api.downloadPaymentsCsv(range.from, range.to));
  }

  private range(days: number): { from: string; to: string } {
    const to = new Date();
    to.setMinutes(to.getMinutes() + 1);
    const from = new Date(to.getTime() - days * 86_400_000);
    return { from: from.toISOString(), to: to.toISOString() };
  }

  private async run(action: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    this.message.set(null);
    try {
      await action();
    } catch (e) {
      this.error.set(problemMessage(e));
    } finally {
      this.busy.set(false);
    }
  }
}
