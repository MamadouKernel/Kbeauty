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
  rebookQueryParams,
} from '../../core/booking/booking.service';
import { formatMoney } from '../../core/format/format';
import { MOBILE_MONEY_LABELS } from '../../core/format/payment-methods';
import {
  DISPUTE_REASONS,
  DISPUTE_RESOLUTIONS,
  DisputeService,
  canDispute,
  type Dispute,
} from '../../core/disputes/dispute.service';
import { DisputeForm } from '../../shared/dispute-form/dispute-form';
import { PassQr } from '../../shared/pass-qr/pass-qr';
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
  imports: [RouterLink, ReviewForm, StarRating, PassQr, DisputeForm],
  styles: `
    .page {
      display: grid;
      gap: 1.5rem;
      max-width: 52rem;
      padding-block: clamp(1.5rem, 4vw, 3rem);
    }
    .rebook-banner {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: space-between;
      gap: 1rem;
      padding: 1.1rem 1.35rem;
      border: 1px solid var(--kb-primary-200);
      border-radius: var(--radius-lg);
      background: linear-gradient(135deg, var(--kb-primary-50), var(--kb-card));
      box-shadow: var(--shadow-soft);
    }
    .rebook-banner__info {
      display: grid;
      gap: 0.2rem;
    }
    .rebook-banner__eyebrow {
      margin: 0;
      font-size: var(--fs-xs);
      font-weight: 700;
      text-transform: uppercase;
      letter-spacing: 0.06em;
      color: var(--kb-primary-700);
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
      background: var(--kb-card);
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
      font-size: var(--fs-xs);
      font-weight: 700;
      background: var(--kb-primary-50);
      color: var(--kb-primary-700);
    }
    .status[data-tone='ok'] {
      background: color-mix(in srgb, var(--kb-success) 12%, var(--kb-card));
      color: var(--kb-success);
    }
    .status[data-tone='bad'] {
      background: color-mix(in srgb, var(--kb-danger) 10%, var(--kb-card));
      color: var(--kb-danger);
    }
    .status[data-tone='wait'] {
      background: color-mix(in srgb, var(--kb-warning) 14%, var(--kb-card));
      color: var(--kb-warning);
    }
    .meta {
      color: var(--kb-ink-muted);
      font-size: var(--fs-md);
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

      @if (returnedFromTip) {
        <p class="badge" data-tone="ok" role="status">
          Merci pour votre pourboire ! Il est en cours de vérification et sera ajouté dès réception
          du paiement.
        </p>
      }
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
        @if (lastRebookable(); as fav) {
          <aside class="rebook-banner" aria-label="Réserver à nouveau en 1 clic">
            <div class="rebook-banner__info">
              <p class="rebook-banner__eyebrow">Réservation express en 1 clic</p>
              <strong>Reprendre chez {{ fav.establishmentName }}</strong>
              <span class="meta">{{ summary(fav) }}</span>
            </div>
            <a
              class="btn btn-primary btn-sm"
              [routerLink]="['/etablissements', fav.establishmentSlug, 'reserver']"
              [queryParams]="rebookParams(fav)"
            >
              Réserver à nouveau
            </a>
          </aside>
        }

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
              <div class="actions">
                @if (a.status === 'Confirmed') {
                  <button
                    class="btn btn-light btn-sm"
                    type="button"
                    [attr.aria-expanded]="openPass() === a.id"
                    (click)="openPass.set(openPass() === a.id ? null : a.id)"
                  >
                    {{ openPass() === a.id ? 'Masquer mon pass' : 'Mon pass QR' }}
                  </button>
                }
                <button
                  class="btn btn-ghost btn-sm"
                  type="button"
                  (click)="downloadIcs(a)"
                >
                  Ajouter au calendrier (.ics)
                </button>
              </div>
              @if (a.status === 'Confirmed' && openPass() === a.id) {
                <app-pass-qr [appointmentId]="a.id" />
              }
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
                @if (a.lateCancelFee) {
                  <p class="meta">
                    Annulation tardive : {{ money(a.lateCancelFee) }} seront retenus sur votre
                    paiement, le reste vous est remboursé.
                  </p>
                }
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
                @if (a.canTip) {
                  <div class="actions" role="group" aria-label="Laisser un pourboire">
                    <span class="meta">Un petit merci ?</span>
                    @for (amount of tipAmounts; track amount) {
                      <button
                        class="btn btn-light btn-sm"
                        type="button"
                        [disabled]="busy()"
                        (click)="tip(a, amount)"
                      >
                        {{ money(amount) }}
                      </button>
                    }
                  </div>
                } @else if (a.tipAmount) {
                  <p class="meta">Pourboire laissé : {{ money(a.tipAmount) }}. Merci !</p>
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
                @if (disputeOf(a.id); as dispute) {
                  <p class="meta">Litige : {{ disputeLabel(dispute) }}</p>
                } @else if (openDispute() === a.id) {
                  <app-dispute-form
                    [appointmentId]="a.id"
                    (submitted)="disputeSent()"
                    (cancelled)="openDispute.set(null)"
                  />
                } @else if (canReport(a)) {
                  <div class="actions">
                    <button
                      class="btn btn-ghost btn-sm"
                      type="button"
                      (click)="openDispute.set(a.id)"
                    >
                      Signaler un problème
                    </button>
                  </div>
                }
                <div class="actions">
                  <a
                    class="btn btn-ghost btn-sm"
                    [routerLink]="['/etablissements', a.establishmentSlug, 'reserver']"
                    [queryParams]="rebookParams(a)"
                  >
                    Réserver à nouveau
                  </a>
                </div>
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

  /** The payment provider sends the client back here with `?pourboire=<transaction>` after a tip. */
  protected readonly returnedFromTip =
    inject(ActivatedRoute).snapshot.queryParamMap.has('pourboire');

  protected readonly appointments = resource({ loader: () => this.booking.mine() });
  private readonly reviewService = inject(ReviewService);
  /** What the client already reviewed: a completed visit without review offers "Donner mon avis". */
  protected readonly reviews = resource({ loader: () => this.reviewService.mine() });
  protected readonly openReview = signal<string | null>(null);
  private readonly disputeService = inject(DisputeService);
  /** Claims already filed (Keke Protect): one per appointment, shown instead of the "report" button. */
  protected readonly disputes = resource({ loader: () => this.disputeService.mine() });
  protected readonly openDispute = signal<string | null>(null);
  protected readonly openPass = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal('');

  protected readonly upcoming = computed(() =>
    this.list()
      .filter((a) => ACTIVE.has(a.status))
      .sort((a, b) => a.startsAt.localeCompare(b.startsAt)),
  );
  protected readonly past = computed(() => this.list().filter((a) => !ACTIVE.has(a.status)));
  protected readonly lastRebookable = computed(
    () => this.past().find((a) => a.status === 'Completed') ?? this.past()[0] ?? null,
  );

  protected rebookParams(a: Appointment): Record<string, string> {
    return rebookQueryParams(a);
  }

  protected downloadIcs(a: Appointment): void {
    this.booking.downloadIcs(a);
  }

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

  protected disputeOf(appointmentId: string): Dispute | undefined {
    return this.disputes.hasValue()
      ? this.disputes.value().find((d) => d.appointmentId === appointmentId)
      : undefined;
  }

  protected canReport(a: Appointment): boolean {
    return canDispute(a.status, a.endsAt);
  }

  protected disputeLabel(d: Dispute): string {
    const reason = DISPUTE_REASONS[d.reason] ?? d.reason;
    return d.status === 'Open'
      ? `${reason} — en cours d’examen`
      : `${reason} — ${DISPUTE_RESOLUTIONS[d.resolution ?? ''] ?? 'clos'}`;
  }

  protected disputeSent(): void {
    this.openDispute.set(null);
    this.disputes.reload();
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

  /** Quick amounts offered for a tip (XOF); the server accepts any whole amount from 100 to 100 000. */
  protected readonly tipAmounts = [500, 1000, 2000, 5000] as const;

  protected money(amount: number): string {
    return formatMoney(amount);
  }

  protected async tip(a: Appointment, amount: number): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    try {
      const result = await this.booking.tip(a.id, amount);
      if (result.paymentUrl) {
        window.location.assign(result.paymentUrl);
        return;
      }
    } catch (error) {
      this.error.set(problemMessage(error));
    }

    this.busy.set(false);
    this.appointments.reload();
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
      this.error.set('Le lien de paiement est indisponible : réessayez dans un instant.');
    } catch (error) {
      this.error.set(problemMessage(error));
    }
    this.busy.set(false);
    this.appointments.reload();
  }

  protected cancel(a: Appointment): Promise<void> {
    const fee = a.lateCancelFee ?? 0;
    const question =
      fee > 0
        ? `Annuler maintenant ? ${formatMoney(fee)} seront retenus sur votre paiement (frais d’annulation tardive).`
        : 'Annuler ce rendez-vous ?';
    return confirm(question)
      ? this.run(() => this.booking.cancel(a.id, fee > 0))
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
