import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, input, resource, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { problemMessage } from '../../../core/auth/auth.models';
import { formatMoney } from '../../../core/format/format';
import {
  GROUP_EVENTS,
  GROUP_STATUSES,
  GrowthService,
  type GroupBooking,
} from '../../../core/growth/growth.service';

/**
 * `/pro/etablissements/:id/demandes` — group requests to answer (a quote or a refusal) and the clients waiting for a
 * slot on a full day, whom the platform tells automatically when a slot frees up.
 */
@Component({
  selector: 'app-pro-requests',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, FormsModule, RouterLink],
  styles: `
    .page {
      display: grid;
      gap: 1.5rem;
      max-width: 50rem;
      padding-block: clamp(1.5rem, 4vw, 3rem);
    }
    section {
      display: grid;
      gap: 0.85rem;
    }
    .card {
      display: grid;
      gap: 0.6rem;
      padding: 1.1rem 1.25rem;
      border: 1px solid var(--kb-border);
      border-radius: var(--radius-lg);
      background: var(--kb-card);
      box-shadow: var(--shadow-soft);
    }
    .top {
      display: flex;
      flex-wrap: wrap;
      justify-content: space-between;
      gap: 0.5rem;
    }
    .meta {
      color: var(--kb-ink-muted);
    }
    textarea,
    input {
      width: 100%;
      padding: 0.6rem 0.75rem;
      border: 1px solid var(--kb-border);
      border-radius: 0.75rem;
      background: var(--kb-card);
      color: var(--kb-ink);
      font: inherit;
    }
    textarea {
      min-height: 4.5rem;
      resize: vertical;
    }
    .answer {
      display: grid;
      gap: 0.5rem;
      grid-template-columns: minmax(0, 10rem) minmax(0, 1fr);
    }
    @media (max-width: 30rem) {
      .answer {
        grid-template-columns: minmax(0, 1fr);
      }
    }
    .actions {
      display: flex;
      flex-wrap: wrap;
      gap: 0.5rem;
    }
  `,
  template: `
    <div class="container page">
      <a class="link" routerLink="/pro">← Mes établissements</a>
      <div>
        <p class="eyebrow">Demandes</p>
        <h1>Groupes <em>et liste d’attente</em></h1>
      </div>

      @if (error()) {
        <p class="form-error" role="alert">{{ error() }}</p>
      }

      <section aria-labelledby="groups-title">
        <h2 id="groups-title">Demandes de groupe</h2>
        @if (groups.hasValue()) {
          @for (g of groups.value(); track g.id) {
            <article class="card">
              <div class="top">
                <h3>{{ eventLabel(g.eventType) }} · {{ g.groupSize }} personnes</h3>
                <span
                  class="badge"
                  [attr.data-tone]="
                    g.status === 'Accepted' ? 'ok' : g.status === 'Declined' ? 'bad' : null
                  "
                  >{{ statusLabel(g.status) }}</span
                >
              </div>
              <p class="meta">
                {{ g.clientName }}
                @if (g.clientPhone) {
                  · <a class="link" [href]="'tel:' + g.clientPhone">{{ g.clientPhone }}</a>
                }
                · souhaité le {{ g.desiredStartsAt | date: 'dd/MM/yyyy HH:mm' }}
              </p>
              <p><strong>Prestations :</strong> {{ g.services }}</p>
              @if (g.message) {
                <p>« {{ g.message }} »</p>
              }
              @if (g.status === 'Pending') {
                <div class="answer">
                  <label>
                    <span class="visually-hidden">Prix total proposé (F)</span>
                    <input
                      type="number"
                      min="1"
                      placeholder="Prix total (F)"
                      [ngModel]="price(g)"
                      (ngModelChange)="setPrice(g, $event)"
                    />
                  </label>
                  <label>
                    <span class="visually-hidden">Message à la cliente</span>
                    <textarea
                      maxlength="1000"
                      placeholder="Votre message : déroulé, acompte, déplacement…"
                      [ngModel]="draft(g)"
                      (ngModelChange)="setDraft(g, $event)"
                    ></textarea>
                  </label>
                </div>
                <div class="actions">
                  <button
                    type="button"
                    class="btn btn-primary btn-sm"
                    [disabled]="busy() || draft(g).trim().length < 3 || !(price(g) > 0)"
                    (click)="answer(g, true)"
                  >
                    Accepter avec ce devis
                  </button>
                  <button
                    type="button"
                    class="btn btn-ghost btn-sm"
                    [disabled]="busy() || draft(g).trim().length < 3"
                    (click)="answer(g, false)"
                  >
                    Décliner
                  </button>
                </div>
              } @else {
                @if (g.quotedPrice) {
                  <p><strong>Devis :</strong> {{ money(g.quotedPrice) }}</p>
                }
                @if (g.partnerMessage) {
                  <p class="meta"><strong>Votre réponse :</strong> {{ g.partnerMessage }}</p>
                }
              }
            </article>
          } @empty {
            <p class="hint">Aucune demande de groupe pour le moment.</p>
          }
        } @else if (groups.error()) {
          <p class="form-error" role="alert">Impossible de charger les demandes. Réessayez.</p>
        } @else {
          <p role="status">Chargement…</p>
        }
      </section>

      <section aria-labelledby="waitlist-title">
        <h2 id="waitlist-title">Liste d’attente</h2>
        <p class="hint">
          Ces clientes attendent un créneau sur une journée complète. Dès qu’un rendez-vous de ce
          jour est annulé, elles sont prévenues automatiquement.
        </p>
        @if (waitlist.hasValue()) {
          <div class="table-scroll">
            <table class="admin-table">
              <thead>
                <tr>
                  <th scope="col">Jour</th>
                  <th scope="col">Cliente</th>
                  <th scope="col">Prestation</th>
                  <th scope="col">État</th>
                </tr>
              </thead>
              <tbody>
                @for (w of waitlist.value(); track w.id) {
                  <tr>
                    <td>{{ w.date | date: 'dd/MM/yyyy' }}</td>
                    <td>{{ w.clientName }}</td>
                    <td>{{ w.serviceName ?? '—' }}</td>
                    <td>{{ w.status === 'Notified' ? 'Prévenue' : 'En attente' }}</td>
                  </tr>
                } @empty {
                  <tr>
                    <td colspan="4" class="hint">Personne n’attend de créneau.</td>
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
export class ProRequests {
  private readonly growth = inject(GrowthService);

  readonly id = input.required<string>();

  protected readonly busy = signal(false);
  protected readonly error = signal('');
  private readonly drafts = signal<Readonly<Record<string, string>>>({});
  private readonly prices = signal<Readonly<Record<string, number>>>({});

  protected readonly groups = resource({
    params: () => this.id(),
    loader: ({ params }) => this.growth.establishmentGroupBookings(params),
  });
  protected readonly waitlist = resource({
    params: () => this.id(),
    loader: ({ params }) => this.growth.establishmentWaitlist(params),
  });

  protected eventLabel(type: string): string {
    return GROUP_EVENTS[type] ?? type;
  }

  protected statusLabel(status: string): string {
    return GROUP_STATUSES[status] ?? status;
  }

  protected money(value: number): string {
    return formatMoney(value);
  }

  protected draft(g: GroupBooking): string {
    return this.drafts()[g.id] ?? '';
  }

  protected setDraft(g: GroupBooking, value: string): void {
    this.drafts.update((all) => ({ ...all, [g.id]: value }));
  }

  protected price(g: GroupBooking): number {
    return this.prices()[g.id] ?? 0;
  }

  protected setPrice(g: GroupBooking, value: number): void {
    this.prices.update((all) => ({ ...all, [g.id]: Number(value) }));
  }

  protected async answer(g: GroupBooking, accept: boolean): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    try {
      await this.growth.answerGroupBooking(
        g.id,
        accept,
        accept ? this.price(g) : null,
        this.draft(g).trim(),
      );
    } catch (error) {
      this.error.set(problemMessage(error));
    } finally {
      this.busy.set(false);
      this.groups.reload();
    }
  }
}
