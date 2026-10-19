import { ChangeDetectionStrategy, Component, inject, input, resource, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { problemMessage } from '../../../core/auth/auth.models';
import { APPOINTMENT_STATUS } from '../../../core/booking/booking.service';
import { formatMoney } from '../../../core/format/format';
import { PartnerService } from '../../../core/partner/partner.service';

function todayMinus(days: number): string {
  const d = new Date(Date.now() - days * 86_400_000);
  return d.toISOString().slice(0, 10);
}

/**
 * `/pro/etablissements/:id/finances` — Partner detailed finances, accounting CSV export,
 * and PDF receipts for appointments and payouts (Lot 5 #31 & #69).
 */
@Component({
  selector: 'app-pro-finances',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, RouterLink, DatePipe],
  styles: `
    .page {
      display: grid;
      gap: 1.5rem;
      padding-block: clamp(1.5rem, 4vw, 3rem);
    }
    .toolbar {
      display: flex;
      flex-wrap: wrap;
      gap: 0.75rem;
      align-items: end;
    }
    .toolbar label {
      display: grid;
      gap: 0.25rem;
      font-size: var(--fs-sm);
      font-weight: 600;
    }
    .kpis {
      display: grid;
      gap: 1rem;
      grid-template-columns: repeat(auto-fit, minmax(min(100%, 13rem), 1fr));
    }
    .kpi {
      display: grid;
      gap: 0.25rem;
      padding: 1rem 1.25rem;
      border: 1px solid var(--kb-border);
      border-radius: var(--radius-lg);
      background: var(--kb-card);
      box-shadow: var(--shadow-soft);
    }
    .kpi strong {
      font-family: var(--font-display);
      font-size: 1.625rem;
      color: var(--kb-primary-700);
    }
    .kpi small {
      color: var(--kb-ink-muted);
      font-size: var(--fs-xs);
    }
    .card {
      display: grid;
      gap: 1rem;
      padding: clamp(1rem, 3vw, 1.5rem);
      border: 1px solid var(--kb-border);
      border-radius: var(--radius-lg);
      background: var(--kb-card);
      box-shadow: var(--shadow-soft);
      overflow-x: auto;
    }
    .two {
      display: grid;
      gap: 1.5rem;
      @media (min-width: 860px) {
        grid-template-columns: 1fr 1fr;
      }
    }
    table {
      width: 100%;
      border-collapse: collapse;
      font-size: var(--fs-sm);
    }
    th,
    td {
      padding: 0.625rem 0.5rem;
      border-bottom: 1px solid var(--kb-border);
      text-align: left;
    }
    th {
      font-weight: 700;
      color: var(--kb-ink-muted);
    }
    .hint {
      margin: 0;
      font-size: var(--fs-sm);
      color: var(--kb-ink-muted);
    }
  `,
  template: `
    <div class="container page">
      <a class="link" routerLink="/pro">← Mes établissements</a>
      <div>
        <p class="eyebrow">Comptabilité et justificatifs</p>
        <h1>Finances et <em>reçus</em></h1>
      </div>

      <form class="toolbar" (ngSubmit)="applyRange()">
        <label>
          Du
          <input class="admin-input" type="date" name="from" [(ngModel)]="fromInput" />
        </label>
        <label>
          Au
          <input class="admin-input" type="date" name="to" [(ngModel)]="toInput" />
        </label>
        <button type="submit" class="btn btn-primary btn-sm">Actualiser</button>
        <button
          type="button"
          class="btn btn-ghost btn-sm"
          [disabled]="busy()"
          (click)="exportCsv()"
        >
          Exporter en CSV
        </button>
      </form>

      @if (error(); as err) {
        <p class="form-error" role="alert">{{ err }}</p>
      }

      @if (summary.hasValue()) {
        @let s = summary.value();
        <div class="kpis" role="region" aria-label="Indicateurs financiers">
          <div class="kpi">
            <span>Chiffre d'affaires brut</span>
            <strong>{{ money(s.grossRevenue, s.currencyCode) }}</strong>
            <small
              >{{ s.completedCount }} prestation(s) honorée(s) ·
              {{ s.noShowCount }} absence(s)</small
            >
          </div>
          <div class="kpi">
            <span>Encaissements en ligne (net)</span>
            <strong>{{ money(s.onlineNet, s.currencyCode) }}</strong>
            <small
              >Brut : {{ money(s.onlineGross, s.currencyCode) }} · Frais :
              {{ money(s.platformFees, s.currencyCode) }}</small
            >
          </div>
          <div class="kpi">
            <span>Encaissements sur place</span>
            <strong>{{ money(s.onSiteCash + s.onSiteMobileMoney, s.currencyCode) }}</strong>
            <small
              >Espèces : {{ money(s.onSiteCash, s.currencyCode) }} · Mobile Money :
              {{ money(s.onSiteMobileMoney, s.currencyCode) }}</small
            >
          </div>
          <div class="kpi">
            <span>Pourboires & commissions</span>
            <strong>{{ money(s.totalTips, s.currencyCode) }}</strong>
            <small>Commissions équipe : {{ money(s.totalCommissions, s.currencyCode) }}</small>
          </div>
          <div class="kpi">
            <span>Reversements reçus</span>
            <strong>{{ money(s.totalPaidOut, s.currencyCode) }}</strong>
            <small>{{ s.payouts.length }} virement(s) sur la période</small>
          </div>
        </div>

        <div class="two">
          <section class="card" aria-labelledby="by-service-title">
            <h2 id="by-service-title">Recettes par prestation</h2>
            @if (s.byService.length === 0) {
              <p class="hint">Aucune prestation honorée sur la période.</p>
            } @else {
              <table>
                <thead>
                  <tr>
                    <th scope="col">Prestation</th>
                    <th scope="col">Volume</th>
                    <th scope="col">Chiffre d'affaires</th>
                  </tr>
                </thead>
                <tbody>
                  @for (row of s.byService; track row.serviceName) {
                    <tr>
                      <td>{{ row.serviceName }}</td>
                      <td>{{ row.count }}</td>
                      <td>{{ money(row.totalAmount, s.currencyCode) }}</td>
                    </tr>
                  }
                </tbody>
              </table>
            }
          </section>

          <section class="card" aria-labelledby="by-method-title">
            <h2 id="by-method-title">Répartition par mode de règlement</h2>
            @if (s.byMethod.length === 0) {
              <p class="hint">Aucun règlement enregistré sur la période.</p>
            } @else {
              <table>
                <thead>
                  <tr>
                    <th scope="col">Moyen</th>
                    <th scope="col">Opérations</th>
                    <th scope="col">Montant</th>
                  </tr>
                </thead>
                <tbody>
                  @for (row of s.byMethod; track row.method) {
                    <tr>
                      <td>{{ row.method }}</td>
                      <td>{{ row.count }}</td>
                      <td>{{ money(row.totalAmount, s.currencyCode) }}</td>
                    </tr>
                  }
                </tbody>
              </table>
            }
          </section>
        </div>

        <section class="card" aria-labelledby="payouts-title">
          <h2 id="payouts-title">Bordereaux de reversement</h2>
          @if (s.payouts.length === 0) {
            <p class="hint">Aucun reversement sur la période.</p>
          } @else {
            <table>
              <thead>
                <tr>
                  <th scope="col">Date</th>
                  <th scope="col">Compte</th>
                  <th scope="col">Montant</th>
                  <th scope="col">Statut</th>
                  <th scope="col">Justificatif</th>
                </tr>
              </thead>
              <tbody>
                @for (p of s.payouts; track p.id) {
                  <tr>
                    <td>{{ p.createdAt | date: 'shortDate' }}</td>
                    <td>{{ p.operator }} · {{ p.accountMasked }}</td>
                    <td>{{ money(p.amount, p.currencyCode) }}</td>
                    <td>{{ p.status }}</td>
                    <td>
                      <button
                        type="button"
                        class="btn btn-ghost btn-sm"
                        [disabled]="busy()"
                        (click)="openPayoutPdf(p.id)"
                      >
                        Bordereau PDF
                      </button>
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          }
        </section>

        <section class="card" aria-labelledby="tx-title">
          <h2 id="tx-title">Journal des prestations et reçus</h2>
          @if (s.transactions.length === 0) {
            <p class="hint">Aucun rendez-vous sur la période sélectionnée.</p>
          } @else {
            <table>
              <thead>
                <tr>
                  <th scope="col">Date</th>
                  <th scope="col">Client(e)</th>
                  <th scope="col">Prestations</th>
                  <th scope="col">Statut</th>
                  <th scope="col">Prix</th>
                  <th scope="col">En ligne (net)</th>
                  <th scope="col">Sur place</th>
                  <th scope="col">Justificatif</th>
                </tr>
              </thead>
              <tbody>
                @for (t of s.transactions; track t.appointmentId) {
                  <tr>
                    <td>{{ t.startsAt | date: 'short' }}</td>
                    <td>{{ t.clientName }}</td>
                    <td>{{ t.servicesSummary }}</td>
                    <td>{{ statusLabel(t.status) }}</td>
                    <td>{{ money(t.totalPrice, t.currencyCode) }}</td>
                    <td>{{ money(t.netOnlineAmount, t.currencyCode) }}</td>
                    <td>{{ t.onSitePaidMethod ?? '—' }}</td>
                    <td>
                      <button
                        type="button"
                        class="btn btn-ghost btn-sm"
                        [disabled]="busy()"
                        (click)="openReceiptPdf(t.appointmentId)"
                      >
                        Reçu PDF
                      </button>
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          }
        </section>
      } @else if (summary.error()) {
        <p class="form-error" role="alert">Impossible de charger les données financières.</p>
      } @else {
        <p role="status">Chargement…</p>
      }
    </div>
  `,
})
export class ProFinances {
  readonly id = input.required<string>();

  private readonly partner = inject(PartnerService);

  protected fromInput = todayMinus(30);
  protected toInput = todayMinus(0);
  protected readonly range = signal({ from: this.fromInput, to: this.toInput });
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly summary = resource({
    params: () => ({ id: this.id(), ...this.range() }),
    loader: ({ params }) =>
      this.partner.finances(
        params.id,
        params.from ? `${params.from}T00:00:00Z` : undefined,
        params.to ? `${params.to}T23:59:59Z` : undefined,
      ),
  });

  protected applyRange(): void {
    this.range.set({ from: this.fromInput, to: this.toInput });
  }

  protected money(amount: number, currency: string): string {
    return formatMoney(amount, currency);
  }

  protected statusLabel(status: string): string {
    return APPOINTMENT_STATUS[status]?.label ?? status;
  }

  protected async exportCsv(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      const r = this.range();
      await this.partner.downloadFinancesCsv(
        this.id(),
        r.from ? `${r.from}T00:00:00Z` : undefined,
        r.to ? `${r.to}T23:59:59Z` : undefined,
      );
    } catch (e) {
      this.error.set(problemMessage(e));
    } finally {
      this.busy.set(false);
    }
  }

  protected async openReceiptPdf(appointmentId: string): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await this.partner.openAppointmentReceiptPdf(appointmentId);
    } catch (e) {
      this.error.set(problemMessage(e));
    } finally {
      this.busy.set(false);
    }
  }

  protected async openPayoutPdf(payoutId: string): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await this.partner.openPayoutReceiptPdf(payoutId);
    } catch (e) {
      this.error.set(problemMessage(e));
    } finally {
      this.busy.set(false);
    }
  }
}
