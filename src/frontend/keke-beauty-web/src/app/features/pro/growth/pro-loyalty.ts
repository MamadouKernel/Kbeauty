import { DatePipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  effect,
  inject,
  input,
  resource,
  signal,
  untracked,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { problemMessage } from '../../../core/auth/auth.models';
import { formatMoney } from '../../../core/format/format';
import {
  GIFT_CARD_STATUSES,
  GrowthService,
  type GiftCard,
  type LoyaltyMember,
} from '../../../core/growth/growth.service';

/**
 * `/pro/etablissements/:id/fidelite` — the establishment's loyalty card (points per visit, reward threshold), the clients'
 * balances with the reward to grant, and the gift cards it sells (it collects the money itself; the platform keeps the
 * balance as the card is spent).
 */
@Component({
  selector: 'app-pro-loyalty',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, FormsModule, RouterLink],
  styles: `
    .page {
      display: grid;
      gap: 1.75rem;
      max-width: 56rem;
      padding-block: clamp(1.5rem, 4vw, 3rem);
    }
    .card {
      display: grid;
      gap: 0.75rem;
      padding: 1.25rem;
      border: 1px solid var(--kb-border);
      border-radius: var(--radius-lg);
      background: var(--kb-card);
      box-shadow: var(--shadow-soft);
    }
    .fields {
      display: grid;
      gap: 0.75rem;
      grid-template-columns: repeat(auto-fit, minmax(min(100%, 13rem), 1fr));
    }
    label {
      display: grid;
      gap: 0.25rem;
      font-weight: 600;
    }
    label.inline {
      display: flex;
      align-items: center;
      gap: 0.5rem;
    }
    input:not([type='checkbox']),
    textarea {
      min-height: 2.75rem;
      padding: 0.5rem 0.75rem;
      border: 1.5px solid var(--kb-border);
      border-radius: var(--radius-md);
      background: var(--kb-card);
      color: var(--kb-ink);
      font: inherit;
    }
    .row {
      display: flex;
      flex-wrap: wrap;
      justify-content: space-between;
      align-items: center;
      gap: 0.5rem;
    }
    .code {
      font-family: ui-monospace, 'SFMono-Regular', Menlo, monospace;
      letter-spacing: 0.06em;
      font-weight: 700;
    }
    .meta {
      color: var(--kb-ink-muted);
      font-size: var(--fs-sm);
    }
    .actions {
      display: flex;
      flex-wrap: wrap;
      gap: 0.5rem;
      align-items: end;
    }
    .actions input {
      width: 8rem;
    }
  `,
  template: `
    <div class="container page">
      <a class="link" routerLink="/pro">← Mes établissements</a>
      <div>
        <p class="eyebrow">Fidéliser</p>
        <h1>Fidélité <em>et cartes cadeaux</em></h1>
      </div>

      @if (error()) {
        <p class="form-error" role="alert">{{ error() }}</p>
      }
      @if (success()) {
        <p class="form-success" role="status">{{ success() }}</p>
      }

      <section class="card" aria-labelledby="program-title">
        <h2 id="program-title">Carte de fidélité</h2>
        <p class="hint">
          Vos clientes gagnent des points à chaque visite terminée, selon le prix. Au seuil, elles
          ont droit à la récompense, que vous accordez au comptoir.
        </p>
        <form class="fields" (submit)="$event.preventDefault(); saveProgram()">
          <label class="inline">
            <input type="checkbox" name="active" [(ngModel)]="active" />
            Carte active
          </label>
          <label>
            Points par tranche de 1 000 F
            <input type="number" name="rate" min="1" max="100" [(ngModel)]="rate" />
          </label>
          <label>
            Points pour une récompense
            <input type="number" name="threshold" min="10" max="100000" [(ngModel)]="threshold" />
          </label>
          <label>
            Récompense
            <input
              name="reward"
              maxlength="200"
              placeholder="-5 000 F sur la prochaine prestation"
              [(ngModel)]="reward"
            />
          </label>
          <div>
            <button class="btn btn-primary btn-sm" type="submit" [disabled]="busy()">
              Enregistrer
            </button>
          </div>
        </form>
        <p class="meta">
          Exemple : un soin à 15 000 F rapporte {{ example() }} points ;
          {{ visitsForReward() }} visite(s) de ce prix pour une récompense.
        </p>
      </section>

      <section class="card" aria-labelledby="members-title">
        <h2 id="members-title">Clientes fidèles</h2>
        @if (members.hasValue()) {
          <div class="table-scroll">
            <table class="admin-table">
              <thead>
                <tr>
                  <th scope="col">Cliente</th>
                  <th scope="col">Points</th>
                  <th scope="col">Récompenses</th>
                  <th scope="col"><span class="visually-hidden">Actions</span></th>
                </tr>
              </thead>
              <tbody>
                @for (m of members.value(); track m.clientUserId) {
                  <tr>
                    <td>{{ m.clientName }}</td>
                    <td>{{ m.points }}</td>
                    <td>{{ m.rewardsAvailable }}</td>
                    <td>
                      @if (m.rewardsAvailable > 0) {
                        <button
                          type="button"
                          class="btn btn-ghost btn-sm"
                          [disabled]="busy()"
                          (click)="redeem(m)"
                        >
                          Accorder la récompense
                        </button>
                      }
                    </td>
                  </tr>
                } @empty {
                  <tr>
                    <td colspan="4" class="hint">Aucune cliente n’a encore de points.</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        } @else if (members.error()) {
          <p class="form-error" role="alert">Impossible de charger les clientes.</p>
        }
      </section>

      <section class="card" aria-labelledby="gift-title">
        <h2 id="gift-title">Cartes cadeaux</h2>
        <p class="hint">
          Encaissez la carte comme une prestation (espèces ou Mobile Money), puis créez-la ici : un
          code unique est généré, à remettre à la personne. Elle peut l’utiliser en une ou plusieurs
          fois.
        </p>
        <form class="fields" (submit)="$event.preventDefault(); createCard()">
          <label>
            Montant (F)
            <input type="number" name="amount" min="1000" step="500" [(ngModel)]="amount" />
          </label>
          <label>
            Pour
            <input name="recipient" maxlength="80" [(ngModel)]="recipient" />
          </label>
          <label>
            De la part de (facultatif)
            <input name="buyer" maxlength="80" [(ngModel)]="buyer" />
          </label>
          <label>
            Validité (mois)
            <input type="number" name="validity" min="1" max="24" [(ngModel)]="validity" />
          </label>
          <label>
            Message (facultatif)
            <input name="message" maxlength="300" [(ngModel)]="message" />
          </label>
          <div>
            <button
              class="btn btn-primary btn-sm"
              type="submit"
              [disabled]="busy() || recipient().trim().length < 2"
            >
              Créer la carte
            </button>
          </div>
        </form>

        <form class="actions" (submit)="$event.preventDefault(); lookup()">
          <label>
            Retrouver une carte par son code
            <input
              name="lookup"
              placeholder="KB-XXXX-XXXX"
              maxlength="14"
              [(ngModel)]="lookupCode"
            />
          </label>
          <button
            class="btn btn-ghost btn-sm"
            type="submit"
            [disabled]="busy() || lookupCode().trim().length < 8"
          >
            Rechercher
          </button>
        </form>

        @if (cards.hasValue()) {
          <div class="table-scroll">
            <table class="admin-table">
              <thead>
                <tr>
                  <th scope="col">Code</th>
                  <th scope="col">Pour</th>
                  <th scope="col">Solde</th>
                  <th scope="col">État</th>
                  <th scope="col">Débiter</th>
                </tr>
              </thead>
              <tbody>
                @for (card of visibleCards(); track card.id) {
                  <tr [attr.data-highlight]="card.id === highlighted() ? 'true' : null">
                    <td>
                      <span class="code">{{ card.code }}</span>
                      <div class="meta">jusqu’au {{ card.expiresAt | date: 'dd/MM/yyyy' }}</div>
                    </td>
                    <td>
                      {{ card.recipientName }}
                      @if (card.buyerName) {
                        <div class="meta">de {{ card.buyerName }}</div>
                      }
                    </td>
                    <td>
                      {{ money(card.balance) }}
                      <span class="meta">/ {{ money(card.initialAmount) }}</span>
                    </td>
                    <td>
                      <span
                        class="badge"
                        [attr.data-tone]="card.status === 'Active' ? 'ok' : null"
                        >{{ status(card.status) }}</span
                      >
                    </td>
                    <td>
                      @if (card.status === 'Active') {
                        <div class="actions">
                          <label class="visually-hidden" [for]="'debit-' + card.id"
                            >Montant à débiter</label
                          >
                          <input
                            type="number"
                            min="1"
                            [max]="card.balance"
                            [id]="'debit-' + card.id"
                            [ngModel]="debit(card)"
                            (ngModelChange)="setDebit(card, $event)"
                          />
                          <button
                            type="button"
                            class="btn btn-ghost btn-sm"
                            [disabled]="busy() || !(debit(card) > 0)"
                            (click)="redeemCard(card)"
                          >
                            Débiter
                          </button>
                          <button
                            type="button"
                            class="btn btn-ghost btn-sm"
                            [disabled]="busy()"
                            (click)="cancelCard(card)"
                          >
                            Annuler
                          </button>
                        </div>
                      }
                    </td>
                  </tr>
                } @empty {
                  <tr>
                    <td colspan="5" class="hint">Aucune carte cadeau pour le moment.</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      </section>
    </div>
  `,
})
export class ProLoyalty {
  private readonly growth = inject(GrowthService);

  readonly id = input.required<string>();

  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly success = signal('');

  protected readonly active = signal(true);
  protected readonly rate = signal(10);
  protected readonly threshold = signal(500);
  protected readonly reward = signal('');

  protected readonly amount = signal(10000);
  protected readonly recipient = signal('');
  protected readonly buyer = signal('');
  protected readonly validity = signal(12);
  protected readonly message = signal('');
  protected readonly lookupCode = signal('');
  protected readonly highlighted = signal<string | null>(null);
  private readonly debits = signal<Readonly<Record<string, number>>>({});

  protected readonly program = resource({
    params: () => this.id(),
    loader: ({ params }) => this.growth.loyaltyProgram(params),
  });
  protected readonly members = resource({
    params: () => this.id(),
    loader: ({ params }) => this.growth.loyaltyMembers(params),
  });
  protected readonly cards = resource({
    params: () => this.id(),
    loader: ({ params }) => this.growth.giftCards(params),
  });

  constructor() {
    // Fill the form with the saved card once it is loaded.
    effect(() => {
      const saved = this.program.hasValue() ? this.program.value() : null;
      untracked(() => {
        if (saved) {
          this.active.set(saved.isActive);
          this.rate.set(saved.pointsPer1000);
          this.threshold.set(saved.rewardThreshold);
          this.reward.set(saved.rewardDescription);
        }
      });
    });
  }

  protected example(): number {
    return Math.floor(15000 / 1000) * (Number(this.rate()) || 0);
  }

  protected visitsForReward(): number {
    const perVisit = this.example();
    return perVisit > 0 ? Math.ceil((Number(this.threshold()) || 0) / perVisit) : 0;
  }

  /** The card found by code first, then the others. */
  protected visibleCards(): GiftCard[] {
    const all = this.cards.hasValue() ? this.cards.value() : [];
    const first = this.highlighted();
    return first
      ? [...all.filter((c) => c.id === first), ...all.filter((c) => c.id !== first)]
      : all;
  }

  protected money(value: number): string {
    return formatMoney(value);
  }

  protected status(value: string): string {
    return GIFT_CARD_STATUSES[value] ?? value;
  }

  protected debit(card: GiftCard): number {
    return this.debits()[card.id] ?? card.balance;
  }

  protected setDebit(card: GiftCard, value: number): void {
    this.debits.update((all) => ({ ...all, [card.id]: Number(value) }));
  }

  protected saveProgram(): Promise<void> {
    return this.run(async () => {
      await this.growth.saveLoyaltyProgram(this.id(), {
        isActive: this.active(),
        pointsPer1000: Number(this.rate()),
        rewardThreshold: Number(this.threshold()),
        rewardDescription: this.reward().trim(),
      });
      this.program.reload();
      return 'Carte de fidélité enregistrée.';
    });
  }

  protected redeem(member: LoyaltyMember): Promise<void> {
    return this.run(async () => {
      const left = await this.growth.redeemLoyalty(this.id(), member.clientUserId);
      this.members.reload();
      return `Récompense accordée à ${member.clientName} : il lui reste ${left.points} points.`;
    });
  }

  protected createCard(): Promise<void> {
    return this.run(async () => {
      const card = await this.growth.createGiftCard(this.id(), {
        amount: Number(this.amount()),
        recipientName: this.recipient().trim(),
        buyerName: this.buyer().trim() || null,
        message: this.message().trim() || null,
        validityMonths: Number(this.validity()),
      });
      this.recipient.set('');
      this.buyer.set('');
      this.message.set('');
      this.highlighted.set(card.id);
      this.cards.reload();
      return `Carte créée : code ${card.code}, à remettre à ${card.recipientName}.`;
    });
  }

  protected lookup(): Promise<void> {
    return this.run(async () => {
      const card = await this.growth.lookupGiftCard(this.id(), this.lookupCode().trim());
      this.highlighted.set(card.id);
      this.cards.reload();
      return `Carte ${card.code} : solde ${formatMoney(card.balance)} (${this.status(card.status)}).`;
    });
  }

  protected redeemCard(card: GiftCard): Promise<void> {
    return this.run(async () => {
      const updated = await this.growth.redeemGiftCard(card.id, this.debit(card));
      this.debits.update((all) =>
        Object.fromEntries(Object.entries(all).filter(([id]) => id !== card.id)),
      );
      this.cards.reload();
      return `${formatMoney(card.balance - updated.balance)} débités : solde ${formatMoney(updated.balance)}.`;
    });
  }

  protected cancelCard(card: GiftCard): Promise<void> {
    return this.run(async () => {
      await this.growth.cancelGiftCard(card.id);
      this.cards.reload();
      return `Carte ${card.code} annulée.`;
    });
  }

  private async run(action: () => Promise<string>): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    this.success.set('');
    try {
      this.success.set(await action());
    } catch (error) {
      this.error.set(problemMessage(error));
    } finally {
      this.busy.set(false);
    }
  }
}
