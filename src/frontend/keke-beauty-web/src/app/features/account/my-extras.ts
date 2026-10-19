import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, resource, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { problemMessage } from '../../core/auth/auth.models';
import { formatMoney } from '../../core/format/format';
import {
  GIFT_CARD_STATUSES,
  GROUP_EVENTS,
  GROUP_STATUSES,
  GrowthService,
  WAITLIST_STATUSES,
  progressToReward,
  type GroupBooking,
  type WaitlistEntry,
} from '../../core/growth/growth.service';

/**
 * `/compte/fidelite` — what the client keeps with her establishments beyond appointments: loyalty cards, gift cards,
 * the days she waits for and her group requests.
 */
@Component({
  selector: 'app-my-extras',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, FormsModule, RouterLink],
  styles: `
    .page {
      display: grid;
      gap: 2rem;
      max-width: 52rem;
      padding-block: clamp(1.5rem, 4vw, 3rem);
    }
    section {
      display: grid;
      gap: 0.85rem;
    }
    .cards {
      display: grid;
      gap: 1rem;
      grid-template-columns: repeat(auto-fill, minmax(min(100%, 16rem), 1fr));
    }
    .card {
      display: grid;
      gap: 0.5rem;
      align-content: start;
      padding: 1.1rem 1.25rem;
      border: 1px solid var(--kb-border);
      border-radius: var(--radius-lg);
      background: var(--kb-card);
      box-shadow: var(--shadow-soft);
    }
    .card h3 a {
      color: inherit;
      text-decoration: none;
    }
    .points {
      font-size: var(--fs-h2);
      font-weight: 700;
      color: var(--kb-primary-600);
    }
    .bar {
      height: 0.5rem;
      border-radius: var(--radius-pill);
      background: var(--kb-primary-100);
      overflow: hidden;
    }
    .bar span {
      display: block;
      height: 100%;
      background: var(--kb-plum-gradient);
    }
    .stamps {
      display: flex;
      flex-wrap: wrap;
      gap: 0.35rem;
      margin-block: 0.15rem;
    }
    .stamp {
      display: inline-grid;
      place-items: center;
      width: 1.5rem;
      height: 1.5rem;
      border-radius: 50%;
      border: 1.5px dashed var(--kb-primary-300);
      font-size: 0.7rem;
      font-weight: 700;
      color: var(--kb-primary-400);
      background: var(--kb-primary-50);
    }
    .stamp[data-filled='true'] {
      border-style: solid;
      border-color: var(--kb-primary-600);
      background: linear-gradient(135deg, var(--kb-primary-500), var(--kb-primary-700));
      color: #fff;
    }
    .gift {
      background: linear-gradient(135deg, var(--kb-gold-soft), var(--kb-card));
    }
    .code {
      font-family: ui-monospace, 'SFMono-Regular', Menlo, monospace;
      font-size: var(--fs-lg);
      letter-spacing: 0.08em;
    }
    .meta {
      color: var(--kb-ink-muted);
      font-size: var(--fs-sm);
    }
    .claim {
      display: flex;
      flex-wrap: wrap;
      gap: 0.5rem;
      align-items: end;
    }
    .claim label {
      display: grid;
      gap: 0.25rem;
      font-weight: 600;
    }
    .claim input {
      min-height: 2.75rem;
      padding: 0.5rem 0.75rem;
      border: 1.5px solid var(--kb-border);
      border-radius: var(--radius-md);
      background: var(--kb-card);
      color: var(--kb-ink);
      font: inherit;
      text-transform: uppercase;
    }
    ul.list {
      display: grid;
      gap: 0.75rem;
      margin: 0;
      padding: 0;
      list-style: none;
    }
    .row {
      display: flex;
      flex-wrap: wrap;
      justify-content: space-between;
      align-items: center;
      gap: 0.5rem;
    }
  `,
  template: `
    <div class="container page">
      <div>
        <p class="eyebrow">Mon compte</p>
        <h1>Fidélité <em>et demandes</em></h1>
        <p class="hint">
          Vos points, vos cartes cadeaux, les journées que vous attendez et vos demandes de groupe.
        </p>
      </div>

      @if (error()) {
        <p class="form-error" role="alert">{{ error() }}</p>
      }
      @if (success()) {
        <p class="form-success" role="status">{{ success() }}</p>
      }

      <section aria-labelledby="loyalty-title">
        <h2 id="loyalty-title">Cartes de fidélité</h2>
        @if (loyalty.hasValue()) {
          <div class="cards">
            @for (card of loyalty.value(); track card.establishmentId) {
              <article class="card">
                <h3>
                  <a [routerLink]="['/etablissements', card.establishmentSlug]">{{
                    card.establishmentName
                  }}</a>
                </h3>
                <p class="points">{{ card.points }} pts</p>
                @if (card.programActive) {
                  <div
                    class="bar"
                    role="progressbar"
                    aria-label="Progression vers la prochaine récompense"
                    [attr.aria-valuenow]="progress(card.points, card.rewardThreshold)"
                    aria-valuemin="0"
                    aria-valuemax="100"
                  >
                    <span [style.width.%]="progress(card.points, card.rewardThreshold)"></span>
                  </div>
                  @if (stamps(card.points, card.rewardThreshold); as dots) {
                    <div class="stamps" aria-hidden="true">
                      @for (filled of dots; track $index) {
                        <span class="stamp" [attr.data-filled]="filled">{{
                          filled ? '✓' : $index + 1
                        }}</span>
                      }
                    </div>
                  }
                  @if (card.rewardsAvailable > 0) {
                    <p>
                      <span class="badge" data-tone="ok"
                        >{{ card.rewardsAvailable }} récompense(s) disponible(s)</span
                      >
                    </p>
                    <p class="meta">
                      {{ card.rewardDescription }} — demandez-la à l’établissement lors de votre
                      prochaine visite.
                    </p>
                  } @else {
                    <p class="meta">
                      Encore
                      {{ card.rewardThreshold - (card.points % card.rewardThreshold) }} points pour
                      : {{ card.rewardDescription }}
                    </p>
                  }
                  <div>
                    <a
                      class="btn btn-light btn-sm"
                      [routerLink]="['/etablissements', card.establishmentSlug, 'reserver']"
                    >
                      Réserver dans ce salon
                    </a>
                  </div>
                } @else {
                  <p class="meta">Le programme de fidélité de cet établissement est en pause.</p>
                }
              </article>
            } @empty {
              <p class="hint">
                Vous gagnez des points à chaque visite terminée dans un établissement qui propose
                une carte de fidélité.
              </p>
            }
          </div>
        } @else if (loyalty.error()) {
          <p class="form-error" role="alert">Impossible de charger vos cartes de fidélité.</p>
        } @else {
          <p role="status">Chargement…</p>
        }
      </section>

      <section aria-labelledby="gift-title">
        <h2 id="gift-title">Cartes cadeaux</h2>
        <form class="claim" (submit)="$event.preventDefault(); claim()">
          <label>
            Code reçu
            <input
              name="code"
              autocomplete="off"
              placeholder="KB-XXXX-XXXX"
              maxlength="14"
              [(ngModel)]="code"
            />
          </label>
          <button
            class="btn btn-primary btn-sm"
            type="submit"
            [disabled]="busy() || code().trim().length < 8"
          >
            Ajouter à mon compte
          </button>
        </form>
        @if (giftCards.hasValue()) {
          <div class="cards">
            @for (card of giftCards.value(); track card.id) {
              <article class="card gift">
                <h3>
                  <a [routerLink]="['/etablissements', card.establishmentSlug]">{{
                    card.establishmentName
                  }}</a>
                </h3>
                <p class="code">{{ card.code }}</p>
                <p class="points">{{ money(card.balance) }}</p>
                <p class="meta">
                  sur {{ money(card.initialAmount) }} · {{ giftStatus(card.status) }} · valable
                  jusqu’au {{ card.expiresAt | date: 'dd/MM/yyyy' }}
                </p>
                @if (card.message) {
                  <p>« {{ card.message }} »</p>
                }
                @if (card.status === 'Active' && card.balance > 0) {
                  <div>
                    <a
                      class="btn btn-light btn-sm"
                      [routerLink]="['/etablissements', card.establishmentSlug, 'reserver']"
                    >
                      Utiliser pour réserver
                    </a>
                  </div>
                }
              </article>
            } @empty {
              <p class="hint">
                Vous avez reçu une carte cadeau ? Saisissez son code pour suivre son solde ici.
              </p>
            }
          </div>
        }
      </section>

      <section aria-labelledby="waitlist-title">
        <h2 id="waitlist-title">Listes d’attente</h2>
        @if (waitlist.hasValue()) {
          <ul class="list">
            @for (entry of waitlist.value(); track entry.id) {
              <li class="card">
                <div class="row">
                  <strong>{{ entry.establishmentName }}</strong>
                  <span
                    class="badge"
                    [attr.data-tone]="entry.status === 'Notified' ? 'ok' : null"
                    >{{ waitStatus(entry.status) }}</span
                  >
                </div>
                <p class="meta">
                  {{ dayLabel(entry.date) }}
                  @if (entry.serviceName) {
                    · {{ entry.serviceName }}
                  }
                </p>
                <div class="row">
                  <a
                    class="link"
                    [routerLink]="['/etablissements', entry.establishmentSlug, 'reserver']"
                    >Voir les créneaux</a
                  >
                  @if (entry.status === 'Waiting') {
                    <button
                      type="button"
                      class="btn btn-ghost btn-sm"
                      [disabled]="busy()"
                      (click)="leave(entry)"
                    >
                      Ne plus attendre
                    </button>
                  }
                </div>
              </li>
            } @empty {
              <p class="hint">
                Une journée est complète ? Choisissez-la dans le calendrier de réservation et
                demandez à être prévenu(e) si un créneau se libère.
              </p>
            }
          </ul>
        }
      </section>

      <section aria-labelledby="groups-title">
        <h2 id="groups-title">Demandes de groupe</h2>
        @if (groups.hasValue()) {
          <ul class="list">
            @for (request of groups.value(); track request.id) {
              <li class="card">
                <div class="row">
                  <strong
                    >{{ eventLabel(request.eventType) }} · {{ request.establishmentName }}</strong
                  >
                  <span
                    class="badge"
                    [attr.data-tone]="
                      request.status === 'Accepted'
                        ? 'ok'
                        : request.status === 'Declined'
                          ? 'bad'
                          : null
                    "
                    >{{ groupStatus(request.status) }}</span
                  >
                </div>
                <p class="meta">
                  {{ request.groupSize }} personnes ·
                  {{ request.desiredStartsAt | date: 'dd/MM/yyyy HH:mm' }}
                </p>
                <p>{{ request.services }}</p>
                @if (request.quotedPrice) {
                  <p><strong>Devis :</strong> {{ money(request.quotedPrice) }}</p>
                }
                @if (request.partnerMessage) {
                  <p><strong>Réponse :</strong> {{ request.partnerMessage }}</p>
                }
                @if (request.status === 'Pending' || request.status === 'Accepted') {
                  <div>
                    <button
                      type="button"
                      class="btn btn-ghost btn-sm"
                      [disabled]="busy()"
                      (click)="cancelGroup(request)"
                    >
                      Annuler la demande
                    </button>
                  </div>
                }
              </li>
            } @empty {
              <p class="hint">
                Un mariage, un anniversaire ? Faites une demande de groupe depuis la page d’un
                établissement.
              </p>
            }
          </ul>
        }
      </section>
    </div>
  `,
})
export class MyExtras {
  private readonly growth = inject(GrowthService);

  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly success = signal('');
  protected readonly code = signal('');

  protected readonly loyalty = resource({ loader: () => this.growth.myLoyalty() });
  protected readonly giftCards = resource({ loader: () => this.growth.myGiftCards() });
  protected readonly waitlist = resource({ loader: () => this.growth.myWaitlist() });
  protected readonly groups = resource({ loader: () => this.growth.myGroupBookings() });

  protected money(amount: number): string {
    return formatMoney(amount);
  }

  protected dayLabel(isoDate: string): string {
    return new Intl.DateTimeFormat('fr-FR', {
      weekday: 'long',
      day: 'numeric',
      month: 'long',
    }).format(new Date(`${isoDate}T12:00:00`));
  }

  protected progress(points: number, threshold: number): number {
    return progressToReward(points, threshold);
  }

  protected stamps(points: number, threshold: number): boolean[] | null {
    if (threshold <= 0 || threshold > 12) {
      return null;
    }
    const filled = points >= threshold ? threshold : points % threshold;
    return Array.from({ length: threshold }, (_, idx) => idx < filled);
  }

  protected giftStatus(status: string): string {
    return GIFT_CARD_STATUSES[status] ?? status;
  }

  protected waitStatus(status: string): string {
    return WAITLIST_STATUSES[status] ?? status;
  }

  protected groupStatus(status: string): string {
    return GROUP_STATUSES[status] ?? status;
  }

  protected eventLabel(type: string): string {
    return GROUP_EVENTS[type] ?? type;
  }

  protected claim(): Promise<void> {
    return this.run(async () => {
      const card = await this.growth.claimGiftCard(this.code().trim());
      this.code.set('');
      this.giftCards.reload();
      return `Carte ${card.code} ajoutée : solde ${formatMoney(card.balance)}.`;
    });
  }

  protected leave(entry: WaitlistEntry): Promise<void> {
    return this.run(async () => {
      await this.growth.leaveWaitlist(entry.id);
      this.waitlist.reload();
      return 'Vous ne serez plus prévenu(e) pour cette journée.';
    });
  }

  protected cancelGroup(request: GroupBooking): Promise<void> {
    return this.run(async () => {
      await this.growth.cancelGroupBooking(request.id);
      this.groups.reload();
      return 'Demande de groupe annulée ; l’établissement est prévenu.';
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
