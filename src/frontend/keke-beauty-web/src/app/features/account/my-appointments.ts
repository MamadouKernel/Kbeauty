import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  resource,
  signal,
  untracked,
} from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { problemMessage } from '../../core/auth/auth.models';
import {
  APPOINTMENT_STATUS,
  Appointment,
  BookingService,
  formatInZone,
} from '../../core/booking/booking.service';
import { formatMoney } from '../../core/format/format';
import { MOBILE_MONEY_LABELS } from '../../core/format/payment-methods';
import { NotificationService } from '../../core/notifications/notification.service';
import { ReviewService } from '../../core/reviews/review.service';
import { ReviewForm } from '../../shared/review-form/review-form';
import { StarRating } from '../../shared/star-rating/star-rating';

/** Establishments are in Côte d'Ivoire; appointments carry instants, shown on the local wall clock. */
const ZONE = 'Africa/Abidjan';
const ACTIVE = new Set(['Pending', 'Confirmed', 'RescheduleProposed']);

/** `/compte/rendez-vous` — upcoming and past appointments, cancellation and reschedule answers (PRD RDV-05/06). */
@Component({
  selector: 'app-my-appointments',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, ReviewForm, StarRating],
  styles: `
    .page {
      display: grid;
      gap: 1.5rem;
      max-width: 52rem;
      padding-block: clamp(1.5rem, 4vw, 3rem);
    }
    .list {
      display: grid;
      gap: 0.75rem;
    }
    .card {
      display: grid;
      gap: 0.5rem;
      padding: 1rem 1.25rem;
      border: 1px solid var(--kb-border);
      border-radius: var(--radius-lg);
      background: #fff;
      box-shadow: var(--shadow-soft);
    }
    .top {
      display: flex;
      flex-wrap: wrap;
      align-items: baseline;
      justify-content: space-between;
      gap: 0.5rem;
    }
    .when {
      font-weight: 600;
      text-transform: capitalize;
    }
    .status {
      padding: 0.125rem 0.75rem;
      border-radius: var(--radius-pill);
      font-size: 0.8125rem;
      font-weight: 700;
      background: var(--kb-primary-50);
      color: var(--kb-primary-700);
    }
    .status[data-tone='ok'] {
      background: color-mix(in srgb, var(--kb-success) 12%, white);
      color: var(--kb-success);
    }
    .status[data-tone='bad'] {
      background: color-mix(in srgb, var(--kb-danger) 10%, white);
      color: var(--kb-danger);
    }
    .status[data-tone='wait'] {
      background: color-mix(in srgb, var(--kb-warning) 14%, white);
      color: var(--kb-warning);
    }
    .meta {
      color: var(--kb-ink-muted);
      font-size: 0.9375rem;
    }
    .actions {
      display: flex;
      flex-wrap: wrap;
      gap: 0.5rem;
      margin-top: 0.25rem;
    }
    .proposal {
      padding: 0.75rem 1rem;
      border-radius: var(--radius-md);
      background: var(--kb-primary-50);
    }
  `,
  template: `
    <div class="container page">
      <div>
        <p class="eyebrow">Mon compte</p>
        <h1>Mes rendez-vous</h1>
      </div>

      @if (returnedFromPayment) {
        <p class="badge" data-tone="ok" role="status">
          Merci ! Votre paiement est en cours de vérification : cette page se met à jour dès qu'il
          est reçu.
        </p>
      }
      @if (error()) {
        <p class="form-error" role="alert">{{ error() }}</p>
      }

      @if (appointments.hasValue()) {
        <section aria-labelledby="upcoming-title" class="list">
          <h2 id="upcoming-title">À venir</h2>
          @for (a of upcoming(); track a.id) {
            <article class="card">
              <div class="top">
                <h3>
                  <a class="link" [routerLink]="['/etablissements', a.establishmentSlug]">{{
                    a.establishmentName
                  }}</a>
                </h3>
                <span class="status" [attr.data-tone]="tone(a.status)">{{ label(a.status) }}</span>
              </div>
              <p class="when">{{ when(a.startsAt) }}</p>
              <p class="meta">{{ summary(a) }}</p>
              @if (a.servicePayment; as sp) {
                <p class="meta">
                  Réglé à l'établissement ({{ methodLabel(sp.method)
                  }}{{ sp.reference ? ', réf. ' + sp.reference : '' }}).
                </p>
              }
              @if (a.payment; as p) {
                <p class="meta">{{ paymentNote(p) }}</p>
                @if (p.status === 'Awaiting') {
                  <div class="actions">
                    <button
                      class="btn btn-primary btn-sm"
                      type="button"
                      [disabled]="busy()"
                      (click)="pay(a)"
                    >
                      Payer maintenant
                    </button>
                  </div>
                }
              }
              @if (a.status === 'RescheduleProposed' && a.proposedStartsAt) {
                <p class="proposal">
                  L'établissement vous propose : <strong>{{ when(a.proposedStartsAt) }}</strong
                  >.
                </p>
                <div class="actions">
                  <button
                    class="btn btn-primary btn-sm"
                    type="button"
                    [disabled]="busy()"
                    (click)="accept(a)"
                  >
                    Accepter ce créneau
                  </button>
                  <button
                    class="btn btn-ghost btn-sm"
                    type="button"
                    [disabled]="busy()"
                    (click)="decline(a)"
                  >
                    Refuser (annuler le rendez-vous)
                  </button>
                </div>
              } @else if (a.canCancel) {
                <div class="actions">
                  <button
                    class="btn btn-ghost btn-sm"
                    type="button"
                    [disabled]="busy()"
                    (click)="cancel(a)"
                  >
                    Annuler le rendez-vous
                  </button>
                </div>
              } @else if (a.status === 'Confirmed' || a.status === 'Pending') {
                <p class="meta">
                  Le délai d'annulation en ligne est dépassé : contactez directement
                  l'établissement.
                </p>
              }
            </article>
          } @empty {
            <p class="hint">
              Aucun rendez-vous à venir.
              <a class="link" routerLink="/etablissements">Trouver un établissement</a>
            </p>
          }
        </section>

        @if (past().length > 0) {
          <section aria-labelledby="past-title" class="list">
            <h2 id="past-title">Passés et annulés</h2>
            @for (a of past(); track a.id) {
              <article class="card">
                <div class="top">
                  <h3>
                    <a class="link" [routerLink]="['/etablissements', a.establishmentSlug]">{{
                      a.establishmentName
                    }}</a>
                  </h3>
                  <span class="status" [attr.data-tone]="tone(a.status)">{{
                    label(a.status)
                  }}</span>
                </div>
                <p class="when">{{ when(a.startsAt) }}</p>
                <p class="meta">{{ summary(a) }}</p>
                @if (a.rejectionReason) {
                  <p class="meta">Motif : {{ a.rejectionReason }}</p>
                }
                @if (a.status === 'Completed') {
                  @if (reviewOf(a.id); as review) {
                    <p class="meta">
                      Votre avis :
                      <app-star-rating [average]="review.rating" [count]="1" [showText]="false" />
                      <span class="visually-hidden">{{ review.rating }} sur 5</span>
                    </p>
                  } @else if (openReview() === a.id) {
                    <app-review-form
                      [appointmentId]="a.id"
                      (submitted)="reviewSent()"
                      (cancelled)="openReview.set(null)"
                    />
                  } @else {
                    <div class="actions">
                      <button
                        class="btn btn-primary btn-sm"
                        type="button"
                        (click)="openReview.set(a.id)"
                      >
                        Donner mon avis
                      </button>
                    </div>
                  }
                }
              </article>
            }
          </section>
        }
      } @else if (appointments.error()) {
        <p class="form-error" role="alert">Impossible de charger vos rendez-vous. Réessayez.</p>
      } @else {
        <p role="status">Chargement…</p>
      }
    </div>
  `,
})
export class MyAppointments {
  private readonly booking = inject(BookingService);
  private readonly notifications = inject(NotificationService);

  /** The payment provider sends the client back here with `?paiement=<transaction>`. */
  protected readonly returnedFromPayment =
    inject(ActivatedRoute).snapshot.queryParamMap.has('paiement');

  protected readonly appointments = resource({ loader: () => this.booking.mine() });
  private readonly reviewService = inject(ReviewService);
  /** What the client already reviewed: a completed visit without review offers "Donner mon avis". */
  protected readonly reviews = resource({ loader: () => this.reviewService.mine() });
  protected readonly openReview = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal('');

  protected readonly upcoming = computed(() =>
    this.list()
      .filter((a) => ACTIVE.has(a.status))
      .sort((a, b) => a.startsAt.localeCompare(b.startsAt)),
  );
  protected readonly past = computed(() => this.list().filter((a) => !ACTIVE.has(a.status)));

  constructor() {
    // A notification means something changed on one of my appointments: refresh the list.
    effect(() => {
      this.notifications.latest();
      untracked(() => this.appointments.reload());
    });
  }

  protected reviewOf(appointmentId: string) {
    return this.reviews.hasValue()
      ? this.reviews.value().find((r) => r.appointmentId === appointmentId)
      : undefined;
  }

  protected reviewSent(): void {
    this.openReview.set(null);
    this.reviews.reload();
  }

  protected label(status: string): string {
    return APPOINTMENT_STATUS[status]?.label ?? status;
  }

  protected tone(status: string): string {
    return APPOINTMENT_STATUS[status]?.tone ?? 'done';
  }

  protected when(instant: string): string {
    return formatInZone(instant, ZONE, {
      weekday: 'long',
      day: 'numeric',
      month: 'long',
      year: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
    });
  }

  protected summary(a: Appointment): string {
    const names = a.lines.map((l) => l.name).join(', ');
    return `${names} · ${formatMoney(a.totalPrice, a.currencyCode)}${a.resourceName ? ` · ${a.resourceName}` : ''}`;
  }

  protected methodLabel(method: string): string {
    return method === 'Cash' ? 'espèces' : (MOBILE_MONEY_LABELS[method] ?? method);
  }

  protected paymentNote(p: NonNullable<Appointment['payment']>): string {
    const amount = formatMoney(p.amount, p.currencyCode);
    const kind = p.kind === 'Deposit' ? 'Acompte' : 'Paiement';
    switch (p.status) {
      case 'Awaiting':
        return p.holdExpiresAt
          ? `${kind} de ${amount} à régler avant ${formatInZone(p.holdExpiresAt, ZONE, { hour: '2-digit', minute: '2-digit' })} pour garder le créneau.`
          : `${kind} de ${amount} à régler.`;
      case 'Paid':
        return `${kind} de ${amount} payé.`;
      case 'RefundPending':
        return `Remboursement de ${amount} en cours.`;
      case 'Refunded':
        return `${amount} remboursé.`;
      case 'RefundFailed':
        return `Remboursement de ${amount} en attente : l'équipe le traite.`;
      case 'Expired':
        return 'Le délai de paiement est dépassé : le créneau a été libéré.';
      case 'Failed':
        return 'Le paiement a été refusé.';
      default:
        return '';
    }
  }

  protected async pay(a: Appointment): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    try {
      const url = (await this.booking.pay(a.id)).payment?.paymentUrl;
      if (url) {
        window.location.assign(url);
        return;
      }
      this.error.set('Le lien de paiement est indisponible, réessayez dans un instant.');
    } catch (error) {
      this.error.set(problemMessage(error));
    }
    this.busy.set(false);
    this.appointments.reload();
  }

  protected cancel(a: Appointment): Promise<void> {
    return confirm('Annuler ce rendez-vous ?')
      ? this.run(() => this.booking.cancel(a.id))
      : Promise.resolve();
  }

  protected accept(a: Appointment): Promise<void> {
    return this.run(() => this.booking.acceptReschedule(a.id));
  }

  protected decline(a: Appointment): Promise<void> {
    return confirm('Refuser ce créneau annulera votre rendez-vous. Continuer ?')
      ? this.run(() => this.booking.declineReschedule(a.id))
      : Promise.resolve();
  }

  private list(): readonly Appointment[] {
    return this.appointments.hasValue() ? this.appointments.value() : [];
  }

  private async run(action: () => Promise<unknown>): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    try {
      await action();
    } catch (error) {
      this.error.set(problemMessage(error));
    } finally {
      this.busy.set(false);
      this.appointments.reload();
    }
  }
}
