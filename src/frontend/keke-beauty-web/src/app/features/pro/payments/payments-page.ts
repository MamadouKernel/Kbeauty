import {
  ChangeDetectionStrategy,
  Component,
  effect,
  inject,
  input,
  resource,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { problemMessage } from '../../../core/auth/auth.models';
import { AgendaService } from '../../../core/partner/agenda.service';

export const MOBILE_MONEY_OPERATORS = [
  { id: 'wave-cote-divoire', label: 'Wave' },
  { id: 'orange-money-cote-divoire', label: 'Orange Money' },
  { id: 'mtn-cote-divoire', label: 'MTN Mobile Money' },
  { id: 'moov-cote-divoire', label: 'Moov Money' },
];

interface AccountRow {
  operator: string;
  account: string;
  holderName: string;
}

/**
 * `/pro/etablissements/:id/paiements` — how clients pay the establishment for the service: cash, and/or mobile money sent straight
 * to its accounts. Keke Beauty is not involved in that money; only the subscription goes through the payment provider.
 */
@Component({
  selector: 'app-pro-payments',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, RouterLink],
  styles: `
    .page {
      display: grid;
      gap: 1.25rem;
      padding-block: clamp(1.5rem, 4vw, 3rem);
    }
    .card {
      display: grid;
      gap: 1rem;
      padding: 1.25rem;
      border: 1px solid var(--kb-border);
      border-radius: var(--radius-lg);
      background: #fff;
      box-shadow: var(--shadow-soft);
    }
    form {
      display: grid;
      gap: 1rem;
      max-width: 44rem;
    }
    label.check {
      display: flex;
      align-items: center;
      gap: 0.625rem;
      min-height: 2.75rem;
      font-weight: 600;
    }
    label.check input {
      width: 1.25rem;
      height: 1.25rem;
    }
    .row {
      display: grid;
      gap: 0.75rem;
      grid-template-columns: repeat(auto-fit, minmax(11rem, 1fr));
      align-items: end;
      padding: 0.875rem;
      border: 1px solid var(--kb-border);
      border-radius: var(--radius-md);
    }
    .row label {
      display: grid;
      gap: 0.25rem;
      font-weight: 600;
      min-width: 0;
    }
    .hint {
      margin: 0;
      font-size: 0.875rem;
      color: var(--kb-ink-muted);
    }
  `,
  template: `
    <div class="container page">
      <a class="link" routerLink="/pro">← Mes établissements</a>
      <div>
        <p class="eyebrow">Règlement des prestations</p>
        <h1>Moyens de <em>paiement</em></h1>
      </div>

      <section class="card" aria-labelledby="methods-title">
        <h2 id="methods-title">Comment vos clients vous paient</h2>
        <p class="hint">
          Vos clients règlent la prestation <strong>directement auprès de vous</strong>, en espèces
          ou par Mobile Money sur le numéro que vous indiquez. Keke Beauty n'encaisse pas cet argent
          et ne prélève aucune commission dessus. Ces moyens de paiement sont affichés sur votre
          fiche et au moment de la réservation.
        </p>
        @if (methods.hasValue()) {
          <form (ngSubmit)="save()">
            <label class="check">
              <input type="checkbox" name="cash" [(ngModel)]="cash" />
              Espèces, sur place
            </label>

            @for (row of rows; track $index; let i = $index) {
              <div class="row">
                <label>
                  Opérateur
                  <select class="admin-input" [name]="'operator' + i" [(ngModel)]="row.operator">
                    @for (o of operators; track o.id) {
                      <option [value]="o.id">{{ o.label }}</option>
                    }
                  </select>
                </label>
                <label>
                  Numéro Mobile Money
                  <input
                    class="admin-input"
                    [name]="'account' + i"
                    type="tel"
                    inputmode="tel"
                    autocomplete="off"
                    [(ngModel)]="row.account"
                  />
                </label>
                <label>
                  Nom du titulaire
                  <input
                    class="admin-input"
                    [name]="'holder' + i"
                    autocomplete="off"
                    [(ngModel)]="row.holderName"
                  />
                </label>
                <button
                  type="button"
                  class="btn btn-ghost btn-sm"
                  [attr.aria-label]="'Retirer le compte ' + (i + 1)"
                  (click)="remove(i)"
                >
                  Retirer
                </button>
              </div>
            }

            @if (rows.length < maxAccounts) {
              <div>
                <button type="button" class="btn btn-ghost btn-sm" (click)="add()">
                  Ajouter un compte Mobile Money
                </button>
              </div>
            }

            <div>
              <button type="submit" class="btn btn-primary" [disabled]="busy()">Enregistrer</button>
            </div>
          </form>
        } @else if (methods.error()) {
          <p class="form-error" role="alert">Impossible de charger vos moyens de paiement.</p>
        }
        @if (message(); as text) {
          <p role="status" class="badge" data-tone="ok">{{ text }}</p>
        }
        @if (error(); as text) {
          <p class="form-error" role="alert">{{ text }}</p>
        }
      </section>
    </div>
  `,
})
export class ProPayments {
  /** Bound from the `:id` route parameter. */
  readonly id = input.required<string>();

  private readonly agenda = inject(AgendaService);

  protected readonly operators = MOBILE_MONEY_OPERATORS;
  protected readonly maxAccounts = 4;
  protected cash = true;
  protected rows: AccountRow[] = [];
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly message = signal<string | null>(null);
  protected readonly methods = resource({
    params: () => this.id(),
    loader: ({ params }) => this.agenda.acceptedPayments(params),
  });

  constructor() {
    effect(() => {
      const loaded = this.methods.value();
      if (loaded) {
        this.cash = loaded.cash;
        this.rows = loaded.mobileMoney.map((m) => ({
          operator: m.operator,
          account: m.account,
          holderName: m.holderName,
        }));
      }
    });
  }

  protected add(): void {
    const used = new Set(this.rows.map((r) => r.operator));
    const free = this.operators.find((o) => !used.has(o.id)) ?? this.operators[0];
    this.rows = [...this.rows, { operator: free.id, account: '', holderName: '' }];
  }

  protected remove(index: number): void {
    this.rows = this.rows.filter((_, i) => i !== index);
  }

  protected async save(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    this.message.set(null);
    try {
      const saved = await this.agenda.saveAcceptedPayments(this.id(), {
        cash: this.cash,
        mobileMoney: this.rows.map((r) => ({
          operator: r.operator,
          account: r.account,
          holderName: r.holderName,
        })),
      });
      this.rows = saved.mobileMoney.map((m) => ({ ...m }));
      this.message.set('Moyens de paiement enregistrés.');
    } catch (e) {
      this.error.set(problemMessage(e));
    } finally {
      this.busy.set(false);
    }
  }
}
