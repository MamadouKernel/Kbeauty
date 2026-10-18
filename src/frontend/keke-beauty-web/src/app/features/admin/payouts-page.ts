import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, resource, signal } from '@angular/core';
import { AdminApiService, Payout, Refund } from '../../core/admin/admin-api.service';
import { problemMessage } from '../../core/auth/auth.models';
import { formatMoney } from '../../core/format/format';

const PAYOUT_LABELS: Record<string, string> = {
  Pending: 'En cours',
  Succeeded: 'Versé',
  Failed: 'Échec',
};

const REFUND_LABELS: Record<string, string> = {
  RefundPending: 'En cours',
  Refunded: 'Remboursé',
  RefundFailed: 'Échec',
};

/** `/admin/reversements` — what the platform pays the partners and the refunds owed to clients (PRD PAY-04, PAY-05). */
@Component({
  selector: 'app-payouts-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe],
  template: `
    <div class="container admin-page">
      <div>
        <p class="eyebrow">Administration</p>
        <h1>Reversements et remboursements</h1>
      </div>

      @if (message(); as text) {
        <p role="status" class="badge" data-tone="ok">{{ text }}</p>
      }
      @if (error(); as text) {
        <p class="form-error" role="alert">{{ text }}</p>
      }

      <section class="admin-card" aria-labelledby="payouts-title">
        <h2 id="payouts-title">Reversements aux établissements</h2>
        <div class="admin-filters">
          <button type="button" class="btn btn-primary" [disabled]="busy()" (click)="runPayouts()">
            Lancer les reversements dus
          </button>
        </div>
        <p>
          Chaque établissement reçoit le montant des rendez-vous honorés et payés en ligne, diminué
          de la commission de la plateforme, sur le compte Mobile Money enregistré. Le reversement
          réel n'est possible qu'avec un compte WiniPayer de production.
        </p>
        @if (payouts.hasValue()) {
          @if (payouts.value().length === 0) {
            <p>Aucun reversement pour l'instant.</p>
          } @else {
            <div class="table-scroll">
              <table class="admin-table">
                <thead>
                  <tr>
                    <th scope="col">Date</th>
                    <th scope="col">Établissement</th>
                    <th scope="col">Montant</th>
                    <th scope="col">Compte</th>
                    <th scope="col">État</th>
                  </tr>
                </thead>
                <tbody>
                  @for (p of payouts.value(); track p.id) {
                    <tr>
                      <td>{{ p.createdAt | date: 'dd/MM/yyyy HH:mm' }}</td>
                      <td>{{ p.establishmentName }}</td>
                      <td>{{ money(p) }}</td>
                      <td>{{ p.operator }} {{ p.accountMasked }}</td>
                      <td>
                        <span class="badge" [attr.data-tone]="payoutTone(p)">{{
                          payoutLabel(p)
                        }}</span>
                        @if (p.error) {
                          <br />
                          <small>{{ p.error }}</small>
                        }
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          }
        } @else if (payouts.error()) {
          <p class="form-error" role="alert">Impossible de charger les reversements.</p>
        }
      </section>

      <section class="admin-card" aria-labelledby="refunds-title">
        <h2 id="refunds-title">Remboursements des clients</h2>
        <p>
          Un rendez-vous annulé à temps est remboursé automatiquement. Si le remboursement échoue,
          relancez-le ou, s'il a été fait à la main, saisissez sa référence.
        </p>
        @if (refunds.hasValue()) {
          @if (refunds.value().length === 0) {
            <p>Aucun remboursement.</p>
          } @else {
            <div class="table-scroll">
              <table class="admin-table">
                <thead>
                  <tr>
                    <th scope="col">Date</th>
                    <th scope="col">Établissement</th>
                    <th scope="col">Montant</th>
                    <th scope="col">État</th>
                    <th scope="col"><span class="visually-hidden">Actions</span></th>
                  </tr>
                </thead>
                <tbody>
                  @for (r of refunds.value(); track r.id) {
                    <tr>
                      <td>{{ r.createdAt | date: 'dd/MM/yyyy HH:mm' }}</td>
                      <td>{{ r.establishmentName }}</td>
                      <td>{{ money(r) }}</td>
                      <td>
                        <span class="badge" [attr.data-tone]="refundTone(r)">{{
                          refundLabel(r)
                        }}</span>
                        @if (r.error) {
                          <br />
                          <small>{{ r.error }}</small>
                        }
                      </td>
                      <td>
                        @if (r.status === 'RefundFailed') {
                          <button
                            type="button"
                            class="btn btn-ghost btn-sm"
                            [disabled]="busy()"
                            (click)="retry(r)"
                          >
                            Relancer
                          </button>
                        }
                        @if (r.status !== 'Refunded') {
                          <button
                            type="button"
                            class="btn btn-ghost btn-sm"
                            [disabled]="busy()"
                            (click)="markDone(r)"
                          >
                            Marquer remboursé
                          </button>
                        }
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          }
        } @else if (refunds.error()) {
          <p class="form-error" role="alert">Impossible de charger les remboursements.</p>
        }
      </section>

      <section class="admin-card" aria-labelledby="collab-title">
        <h2 id="collab-title">Versements aux collaboratrices</h2>
        <p>
          Les pourboires payés en ligne et les commissions avancées par la plateforme, versés par
          mobile money à la demande des établissements.
        </p>
        @if (collaboratorPayouts.hasValue()) {
          @if (collaboratorPayouts.value().length === 0) {
            <p>Aucun versement pour l'instant.</p>
          } @else {
            <div class="table-scroll">
              <table class="admin-table">
                <thead>
                  <tr>
                    <th scope="col">Date</th>
                    <th scope="col">Collaboratrice</th>
                    <th scope="col">Établissement</th>
                    <th scope="col">Commissions</th>
                    <th scope="col">Pourboires</th>
                    <th scope="col">Total</th>
                    <th scope="col">État</th>
                  </tr>
                </thead>
                <tbody>
                  @for (c of collaboratorPayouts.value(); track c.id) {
                    <tr>
                      <td>{{ c.createdAt | date: 'dd/MM/yyyy HH:mm' }}</td>
                      <td>{{ c.collaboratorName }}</td>
                      <td>{{ c.establishmentName }}</td>
                      <td>
                        {{ money({ amount: c.commissionAmount, currencyCode: c.currencyCode }) }}
                      </td>
                      <td>{{ money({ amount: c.tipAmount, currencyCode: c.currencyCode }) }}</td>
                      <td>{{ money(c) }}</td>
                      <td>{{ c.status }}</td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          }
        } @else if (collaboratorPayouts.error()) {
          <p class="form-error" role="alert">Impossible de charger les versements.</p>
        }
      </section>
    </div>
  `,
})
export class PayoutsPage {
  private readonly api = inject(AdminApiService);

  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly message = signal<string | null>(null);
  protected readonly payouts = resource({ loader: () => this.api.payouts() });
  protected readonly refunds = resource({ loader: () => this.api.refunds() });
  protected readonly collaboratorPayouts = resource({
    loader: () => this.api.collaboratorPayouts(),
  });

  protected money(item: { amount: number; currencyCode: string }): string {
    return formatMoney(item.amount, item.currencyCode);
  }

  protected payoutLabel(payout: Payout): string {
    return PAYOUT_LABELS[payout.status] ?? payout.status;
  }

  protected payoutTone(payout: Payout): string {
    return payout.status === 'Succeeded' ? 'ok' : payout.status === 'Failed' ? 'bad' : 'warn';
  }

  protected refundLabel(refund: Refund): string {
    return REFUND_LABELS[refund.status] ?? refund.status;
  }

  protected refundTone(refund: Refund): string {
    return refund.status === 'Refunded' ? 'ok' : refund.status === 'RefundFailed' ? 'bad' : 'warn';
  }

  protected runPayouts(): Promise<void> {
    return this.run(async () => {
      const report = await this.api.runPayouts();
      return report.payouts === 0
        ? 'Aucun reversement à effectuer.'
        : `${report.payouts} reversement(s) lancé(s), ${formatMoney(report.total)} au total.`;
    });
  }

  protected retry(refund: Refund): Promise<void> {
    return this.run(async () => {
      await this.api.retryRefund(refund.id);
      return 'Remboursement relancé.';
    });
  }

  protected markDone(refund: Refund): Promise<void> {
    const reference = prompt('Référence du remboursement effectué à la main :')?.trim();
    if (!reference) {
      return Promise.resolve();
    }
    return this.run(async () => {
      await this.api.markRefunded(refund.id, reference);
      return 'Remboursement marqué comme effectué.';
    });
  }

  private async run(action: () => Promise<string>): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    this.message.set(null);
    try {
      this.message.set(await action());
    } catch (e) {
      this.error.set(problemMessage(e));
    } finally {
      this.busy.set(false);
      this.payouts.reload();
      this.refunds.reload();
    }
  }
}
