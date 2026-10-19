import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  input,
  resource,
  signal,
  untracked,
} from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { LucideCalendarCheck, LucideCircleCheck } from '@lucide/angular';
import { InputText } from 'primeng/inputtext';
import { HttpErrorResponse } from '@angular/common/http';
import { problemMessage } from '../../core/auth/auth.models';
import {
  Appointment,
  BookingService,
  formatInZone,
  isoDay,
} from '../../core/booking/booking.service';
import { formatDuration, formatMoney } from '../../core/format/format';
import { GrowthService } from '../../core/growth/growth.service';
import { RealtimeConnection, RealtimeService } from '../../core/realtime/realtime.service';
import { AvailabilityCalendar } from './availability-calendar';

const GRID_DAYS = 42;

function startOfMonth(date: Date): Date {
  return new Date(date.getFullYear(), date.getMonth(), 1);
}

/** First displayed day of the calendar grid: the Monday on or before the 1st of the month. */
function gridStart(month: Date): Date {
  const offset = (month.getDay() + 6) % 7;
  return new Date(month.getFullYear(), month.getMonth(), 1 - offset);
}

/**
 * `/etablissements/:slug/reserver` — services, professional, day and time, then confirmation (PRD RDV-01 to RDV-03).
 * The calendar updates live: a slot taken by someone else turns red without reloading (SignalR).
 */
@Component({
  selector: 'app-booking-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    InputText,
    AvailabilityCalendar,
    LucideCalendarCheck,
    LucideCircleCheck,
  ],
  styles: `
    .page {
      display: grid;
      gap: 1.25rem;
      max-width: 52rem;
      padding-block: clamp(1.5rem, 4vw, 3rem);
    }
    .card {
      display: grid;
      gap: 1rem;
      padding: clamp(1rem, 3vw, 1.75rem);
      border: 1px solid var(--kb-border);
      border-radius: var(--radius-lg);
      background: var(--kb-card);
      box-shadow: var(--shadow-soft);
    }
    .card h2 {
      display: flex;
      align-items: center;
      gap: 0.75rem;
      font-size: 1.25rem;
    }
    .step {
      display: inline-grid;
      flex: none;
      place-items: center;
      width: 2rem;
      height: 2rem;
      border-radius: 50%;
      background: linear-gradient(135deg, var(--kb-primary-500), var(--kb-primary-700));
      color: #fff;
      font-family: var(--font-body);
      font-size: var(--fs-md);
      font-weight: 700;
    }
    .choices {
      display: grid;
      gap: 0.5rem;
      padding: 0;
      border: 0;
      margin: 0;
    }
    .choice {
      display: grid;
      grid-template-columns: auto 1fr auto;
      align-items: center;
      gap: 0.75rem;
      padding: 0.75rem 1rem;
      border: 1.5px solid var(--kb-border);
      border-radius: var(--radius-md);
      cursor: pointer;
      transition:
        border-color 160ms ease,
        background-color 160ms ease;

      &:has(input:checked) {
        border-color: var(--kb-primary-600);
        background: var(--kb-primary-50);
      }
      &:has(input:focus-visible) {
        outline: 3px solid var(--kb-primary-400);
        outline-offset: 2px;
      }
      input {
        width: 1.25rem;
        height: 1.25rem;
        accent-color: var(--kb-primary-600);
      }
      small {
        display: block;
        color: var(--kb-ink-muted);
      }
    }
    .price {
      font-weight: 700;
      white-space: nowrap;
    }
    @media (max-width: 399px) {
      .choice {
        grid-template-columns: auto minmax(0, 1fr);
      }
      .choice .price {
        grid-column: 2;
      }
    }
    .total {
      display: flex;
      flex-wrap: wrap;
      justify-content: space-between;
      gap: 0.5rem;
      padding: 0.75rem 1rem;
      border-radius: var(--radius-md);
      background: var(--kb-primary-50);
      font-weight: 600;
    }
    .when {
      display: grid;
      gap: 1.25rem;

      @media (min-width: 720px) {
        grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
        align-items: start;
      }
    }
    .waitlist {
      display: grid;
      gap: 0.5rem;
      margin-top: 0.75rem;
      padding: 0.9rem 1rem;
      border: 1px dashed var(--kb-primary-300);
      border-radius: var(--radius-md);
      background: var(--kb-primary-50);
    }
    .slots {
      display: grid;
      gap: 0.5rem;
      grid-template-columns: repeat(auto-fill, minmax(min(100%, 5rem), 1fr));
    }
    .slot {
      min-height: 2.75rem;
      border: 1.5px solid var(--kb-primary-200);
      border-radius: var(--radius-sm);
      background: var(--kb-card);
      font: inherit;
      font-weight: 700;
      color: var(--kb-primary-800);
      cursor: pointer;
      transition:
        background-color 160ms ease,
        transform 160ms var(--ease-out);

      &:hover:not(:disabled) {
        background: var(--kb-primary-50);
        transform: translateY(-1px);
      }
      &[aria-pressed='true'] {
        border-color: var(--kb-primary-700);
        color: #fff;
        background: linear-gradient(135deg, var(--kb-primary-500), var(--kb-primary-700));
      }
      &:disabled {
        border-color: color-mix(in srgb, var(--kb-danger) 30%, var(--kb-card));
        color: var(--kb-danger);
        background: repeating-linear-gradient(
          135deg,
          color-mix(in srgb, var(--kb-danger) 7%, var(--kb-card)),
          color-mix(in srgb, var(--kb-danger) 7%, var(--kb-card)) 4px,
          color-mix(in srgb, var(--kb-danger) 13%, var(--kb-card)) 4px,
          color-mix(in srgb, var(--kb-danger) 13%, var(--kb-card)) 8px
        );
        text-decoration: line-through;
        cursor: not-allowed;
      }
    }
    .field {
      display: grid;
      gap: 0.375rem;

      label {
        font-size: var(--fs-sm);
        font-weight: 600;
      }
      input.p-inputtext {
        width: 100%;
        min-height: 3rem;
      }
    }
    textarea {
      width: 100%;
      min-height: 5rem;
      padding: 0.75rem;
      border: 1px solid var(--kb-border);
      border-radius: var(--radius-sm);
      font: inherit;
      color: inherit;
    }
    .recap {
      display: grid;
      gap: 0.25rem;
      padding: 1rem;
      border: 1px dashed var(--kb-primary-300);
      border-radius: var(--radius-md);
      background: var(--kb-primary-50);
    }
    .done {
      display: grid;
      justify-items: start;
      gap: 1rem;
    }
    .done svg {
      width: 3rem;
      height: 3rem;
      color: var(--kb-success);
    }
    .actions {
      display: flex;
      flex-wrap: wrap;
      gap: 0.75rem;
    }
    .live {
      display: inline-flex;
      align-items: center;
      gap: 0.5rem;
      font-size: var(--fs-xs);
      color: var(--kb-ink-muted);
    }
    .live::before {
      content: '';
      width: 0.5rem;
      height: 0.5rem;
      border-radius: 50%;
      background: var(--kb-success);
      box-shadow: 0 0 0 3px color-mix(in srgb, var(--kb-success) 25%, transparent);
    }
  `,
  template: `
    <div class="container page">
      <a class="link" [routerLink]="['/etablissements', slug()]">← Retour à la fiche</a>

      @if (info.hasValue()) {
        @let i = info.value();
        <div>
          <p class="eyebrow">Réservation en ligne</p>
          <h1>{{ i.name }}</h1>
        </div>

        @if (confirmation(); as done) {
          <section class="card done" aria-live="polite">
            <svg lucideCircleCheck aria-hidden="true"></svg>
            <h2>
              {{
                done.payment?.status === 'Awaiting'
                  ? 'Paiement à effectuer'
                  : done.status === 'Confirmed'
                    ? 'Rendez-vous confirmé'
                    : 'Demande envoyée'
              }}
            </h2>
            <p>
              {{ whenText(done.startsAt) }}<br />
              {{ done.lines.length }} prestation(s) ·
              {{ money(done.totalPrice, done.currencyCode) }}
            </p>
            @if (done.payment?.status === 'Awaiting') {
              <p>
                Votre créneau est réservé pendant 30 minutes. Payez
                {{ money(done.payment!.amount, done.payment!.currencyCode) }} ({{
                  done.payment!.kind === 'Deposit' ? 'acompte' : 'prix total'
                }}) pour le garder ; vous êtes redirigé vers la page de paiement.
              </p>
              @if (done.payment!.paymentUrl; as payUrl) {
                <a class="btn btn-primary" [href]="payUrl">Payer maintenant</a>
              }
            }
            <p>
              @if (done.payment?.status === 'Awaiting') {
                Le rendez-vous sera ensuite confirmé automatiquement ou transmis à
                {{ i.name }} selon ses réglages.
              } @else if (done.status === 'Confirmed') {
                Votre rendez-vous est confirmé. Un SMS de confirmation vous est envoyé.
              } @else {
                {{ i.name }} doit valider votre demande (réponse sous 24 h). Vous serez prévenu(e)
                par SMS et dans vos notifications.
              }
            </p>
            <div class="actions">
              <a class="btn btn-primary" routerLink="/compte/rendez-vous">Voir mes rendez-vous</a>
              <a class="btn btn-ghost" [routerLink]="['/etablissements', slug()]"
                >Retour à la fiche</a
              >
            </div>
          </section>
        } @else {
          <section class="card" aria-labelledby="step-services">
            <h2 id="step-services">
              <span class="step" aria-hidden="true">1</span> Choisissez vos prestations
            </h2>
            <fieldset class="choices">
              <legend class="visually-hidden">Prestations</legend>
              @for (service of i.services; track service.id) {
                <label class="choice">
                  <input
                    type="checkbox"
                    [checked]="selectedServiceIds().includes(service.id)"
                    (change)="toggleService(service.id)"
                  />
                  <span>
                    <strong
                      >{{ service.name }}
                      @if (service.promoPercent && service.promoPercent > 0) {
                        (−{{ service.promoPercent }} %)
                      }
                    </strong>
                    <small>{{ duration(service.durationMinutes) }}</small>
                    @if (service.perks && service.perks.length > 0) {
                      <small>Inclus : {{ service.perks.join(' · ') }}</small>
                    }
                  </span>
                  <span class="price">{{
                    servicePrice(service.price, service.priceType, service.currencyCode)
                  }}</span>
                </label>
              }
            </fieldset>
            @if (chosenServices().length > 0) {
              <p class="total" aria-live="polite">
                <span>Durée totale : {{ duration(totalMinutes()) }}</span>
                <span>Total : {{ money(totalPrice(), i.services[0]?.currencyCode ?? 'XOF') }}</span>
              </p>
            }
          </section>

          @if (chosenServices().length > 0) {
            @if (i.resources.length > 1) {
              <section class="card" aria-labelledby="step-resource">
                <h2 id="step-resource">
                  <span class="step" aria-hidden="true">2</span> Avec qui ?
                </h2>
                <fieldset class="choices">
                  <legend class="visually-hidden">Professionnel</legend>
                  <label class="choice">
                    <input
                      type="radio"
                      name="resource"
                      [checked]="resourceId() === null"
                      (change)="chooseResource(null)"
                    />
                    <span
                      ><strong>Sans préférence</strong><small>Le premier créneau libre</small></span
                    >
                    <span></span>
                  </label>
                  @for (resource of candidates(); track resource.id) {
                    <label class="choice">
                      <input
                        type="radio"
                        name="resource"
                        [checked]="resourceId() === resource.id"
                        (change)="chooseResource(resource.id)"
                      />
                      <span
                        ><strong>{{ resource.name }}</strong></span
                      >
                      <span></span>
                    </label>
                  }
                </fieldset>
              </section>
            }

            <section class="card" aria-labelledby="step-when">
              <h2 id="step-when">
                <span class="step" aria-hidden="true">{{ i.resources.length > 1 ? 3 : 2 }}</span>
                Date et heure
              </h2>
              <div class="when">
                <app-availability-calendar
                  [month]="monthStart()"
                  [days]="calendarDays()"
                  [selected]="selectedDay()"
                  [minMonth]="minMonth"
                  [maxMonth]="maxMonth()"
                  (daySelected)="selectDay($event)"
                  (monthChange)="changeMonth($event)"
                />
                <div>
                  @if (selectedDay()) {
                    <h3>{{ dayTitle() }}</h3>
                    @if (daySlots().length === 0) {
                      <p class="hint">Aucun horaire ce jour-là.</p>
                    } @else {
                      @if (dayFull()) {
                        <div class="waitlist">
                          <p><strong>Cette journée est complète.</strong></p>
                          @if (waitlisted() === selectedDay()) {
                            <p role="status">
                              C’est noté : vous serez prévenu(e) dès qu’un créneau se libère ce
                              jour-là. Retrouvez vos attentes dans
                              <a class="link" routerLink="/compte/fidelite">Fidélité et demandes</a
                              >.
                            </p>
                          } @else {
                            <p class="hint">
                              Si un rendez-vous de ce jour est annulé, nous vous prévenons tout de
                              suite : le premier qui réserve obtient le créneau.
                            </p>
                            <div>
                              <button
                                type="button"
                                class="btn btn-ghost btn-sm"
                                [disabled]="busy()"
                                (click)="joinWaitlist()"
                              >
                                Me prévenir si un créneau se libère
                              </button>
                            </div>
                          }
                        </div>
                      }
                      <div class="slots" role="group" aria-label="Horaires du jour">
                        @for (slot of daySlots(); track slot.startsAt) {
                          <button
                            type="button"
                            class="slot"
                            [disabled]="!slot.available"
                            [attr.aria-pressed]="selectedStart() === slot.startsAt"
                            [attr.aria-label]="slot.time + (slot.available ? '' : ', indisponible')"
                            (click)="selectedStart.set(slot.startsAt)"
                          >
                            {{ slot.time }}
                          </button>
                        }
                      </div>
                    }
                  } @else {
                    <p class="hint">
                      Choisissez un jour dans le calendrier pour voir les horaires.
                    </p>
                  }
                  @if (availability.isLoading()) {
                    <p class="hint" role="status">Chargement des disponibilités…</p>
                  }
                  @if (notice()) {
                    <p class="form-error" role="alert">{{ notice() }}</p>
                  }
                  @if (live()) {
                    <p class="live">Disponibilités mises à jour en direct</p>
                  }
                </div>
              </div>
            </section>

            @if (selectedStart()) {
              <section class="card" aria-labelledby="step-confirm">
                <h2 id="step-confirm">
                  <span class="step" aria-hidden="true">{{ i.resources.length > 1 ? 4 : 3 }}</span>
                  Confirmez
                </h2>
                <p class="recap">
                  <strong>{{ whenText(selectedStart()!) }}</strong>
                  <span
                    >{{ chosenServices().length }} prestation(s) · {{ duration(totalMinutes()) }} ·
                    {{ money(totalPrice(), i.services[0]?.currencyCode ?? 'XOF') }}</span
                  >
                </p>
                <p class="hint">
                  Vous réglez la prestation directement auprès de {{ i.name }} (moyens de paiement
                  indiqués sur sa fiche).
                </p>
                <form [formGroup]="form" (ngSubmit)="confirm()" novalidate>
                  <div class="field">
                    <label for="b-phone">Votre téléphone (pour le SMS de confirmation)</label>
                    <input
                      pInputText
                      id="b-phone"
                      type="tel"
                      formControlName="phone"
                      autocomplete="tel"
                      inputmode="tel"
                      placeholder="+225 07 00 00 00 00"
                    />
                    @if (phoneInvalid()) {
                      <p class="field-error" role="alert">
                        Saisissez un numéro valide (8 à 15 chiffres).
                      </p>
                    }
                  </div>
                  <div class="field">
                    <label for="b-note">Un message pour l'établissement (facultatif)</label>
                    <textarea id="b-note" formControlName="note" maxlength="500"></textarea>
                  </div>
                  @if (error()) {
                    <p class="form-error" role="alert">{{ error() }}</p>
                  }
                  <div class="actions">
                    <button class="btn btn-primary" type="submit" [disabled]="busy()">
                      <svg lucideCalendarCheck aria-hidden="true"></svg>
                      {{ busy() ? 'Envoi…' : 'Envoyer ma demande' }}
                    </button>
                  </div>
                </form>
              </section>
            }
          }
        }
      } @else if (info.error()) {
        <section class="card">
          <h1>Réservation indisponible</h1>
          <p>
            La réservation en ligne n'est pas disponible pour cet établissement. Appelez-le
            directement depuis sa fiche.
          </p>
          <a class="btn btn-primary" [routerLink]="['/etablissements', slug()]"
            >Retour à la fiche</a
          >
        </section>
      } @else {
        <p role="status">Chargement…</p>
      }
    </div>
  `,
})
export class BookingPage {
  /** Bound from the `:slug` route parameter. */
  readonly slug = input.required<string>();
  /** A service chosen on the fiche (`?prestation=<id>`): it is already ticked when the page opens. */
  readonly prestation = input<string>();

  private readonly booking = inject(BookingService);
  private readonly realtime = inject(RealtimeService);

  protected readonly info = resource({
    params: () => this.slug(),
    loader: ({ params }) => this.booking.info(params),
  });

  protected readonly selectedServiceIds = signal<readonly string[]>([]);
  protected readonly resourceId = signal<string | null>(null);
  protected readonly monthStart = signal(startOfMonth(new Date()));
  protected readonly selectedDay = signal<string | null>(null);
  protected readonly selectedStart = signal<string | null>(null);
  /** The day the client asked to be told about, to confirm it in place. */
  protected readonly waitlisted = signal<string | null>(null);
  private readonly growth = inject(GrowthService);
  protected readonly confirmation = signal<Appointment | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly notice = signal('');
  protected readonly live = signal(false);
  protected readonly minMonth = startOfMonth(new Date());

  protected readonly form = inject(NonNullableFormBuilder).group({
    phone: ['', [Validators.required, Validators.pattern(/^\+?[0-9][0-9 .-]{6,18}[0-9]$/)]],
    note: [''],
  });

  protected readonly chosenServices = computed(() => {
    const ids = this.selectedServiceIds();
    return this.info.hasValue() ? this.info.value().services.filter((s) => ids.includes(s.id)) : [];
  });
  protected readonly totalMinutes = computed(() =>
    this.chosenServices().reduce((sum, s) => sum + s.durationMinutes, 0),
  );
  protected readonly totalPrice = computed(() =>
    this.chosenServices().reduce((sum, s) => sum + s.price, 0),
  );

  /** Professionals able to perform every chosen service (none listed = all services). */
  protected readonly candidates = computed(() => {
    const ids = this.selectedServiceIds();
    return this.info.hasValue()
      ? this.info
          .value()
          .resources.filter(
            (r) => r.serviceIds.length === 0 || ids.every((id) => r.serviceIds.includes(id)),
          )
      : [];
  });

  protected readonly maxMonth = computed(() => {
    const horizon = this.info.hasValue() ? this.info.value().maxHorizonDays : 60;
    const last = new Date();
    last.setDate(last.getDate() + horizon);
    return startOfMonth(last);
  });

  protected readonly availability = resource({
    params: () => {
      const info = this.info.hasValue() ? this.info.value() : null;
      const services = this.selectedServiceIds();
      if (!info || services.length === 0) {
        return undefined;
      }

      const start = gridStart(this.monthStart());
      const end = new Date(start.getFullYear(), start.getMonth(), start.getDate() + GRID_DAYS - 1);
      return {
        id: info.establishmentId,
        services,
        resourceId: this.resourceId(),
        from: isoDay(start),
        to: isoDay(end),
      };
    },
    loader: ({ params }) =>
      this.booking.availability(
        params.id,
        params.from,
        params.to,
        params.services,
        params.resourceId,
      ),
  });

  protected readonly calendarDays = computed(() =>
    this.availability.hasValue() ? this.availability.value().days : [],
  );
  protected readonly daySlots = computed(
    () => this.calendarDays().find((d) => d.date === this.selectedDay())?.slots ?? [],
  );
  /** Open that day, but every slot is taken. */
  protected readonly dayFull = computed(
    () => this.daySlots().length > 0 && !this.daySlots().some((s) => s.available),
  );
  protected readonly dayTitle = computed(() => {
    const day = this.selectedDay();
    return day
      ? new Intl.DateTimeFormat('fr-FR', { weekday: 'long', day: 'numeric', month: 'long' }).format(
          new Date(`${day}T12:00:00`),
        )
      : '';
  });
  protected readonly phoneInvalid = computed(() => {
    const control = this.form.controls.phone;
    return control.invalid && (control.touched || control.dirty);
  });

  private connection: RealtimeConnection | null = null;
  private joined: string | null = null;

  constructor() {
    effect(() => {
      const wanted = this.prestation();
      const services = this.info.hasValue() ? this.info.value().services : [];
      untracked(() => {
        if (
          wanted &&
          this.selectedServiceIds().length === 0 &&
          services.some((s) => s.id === wanted)
        ) {
          this.selectedServiceIds.set([wanted]);
        }
      });
    });

    // A slot that someone else just took must not stay selected.
    effect(() => {
      const days = this.calendarDays();
      untracked(() => {
        const start = this.selectedStart();
        if (
          start &&
          days.length > 0 &&
          !days.some((d) => d.slots.some((s) => s.startsAt === start && s.available))
        ) {
          this.selectedStart.set(null);
          this.notice.set(
            "Ce créneau vient d'être réservé par quelqu'un d'autre. Choisissez un autre horaire.",
          );
        }
      });
    });

    effect(() => {
      const id = this.info.hasValue() ? this.info.value().establishmentId : null;
      untracked(() => void this.watch(id));
    });

    inject(DestroyRef).onDestroy(() => void this.connection?.stop());
  }

  protected toggleService(id: string): void {
    this.selectedServiceIds.update((ids) =>
      ids.includes(id) ? ids.filter((x) => x !== id) : [...ids, id],
    );
    if (this.resourceId() && !this.candidates().some((r) => r.id === this.resourceId())) {
      this.resourceId.set(null);
    }

    this.resetSlot();
  }

  protected chooseResource(id: string | null): void {
    this.resourceId.set(id);
    this.resetSlot();
  }

  protected selectDay(iso: string): void {
    this.selectedDay.set(iso);
    this.selectedStart.set(null);
    this.notice.set('');
  }

  protected async joinWaitlist(): Promise<void> {
    const day = this.selectedDay();
    if (!day || !this.info.hasValue()) {
      return;
    }

    this.busy.set(true);
    this.notice.set('');
    try {
      await this.growth.joinWaitlist(
        this.info.value().establishmentId,
        day,
        this.selectedServiceIds()[0] ?? null,
      );
      this.waitlisted.set(day);
    } catch (error) {
      this.notice.set(problemMessage(error));
    } finally {
      this.busy.set(false);
    }
  }

  protected changeMonth(delta: -1 | 1): void {
    const current = this.monthStart();
    this.monthStart.set(new Date(current.getFullYear(), current.getMonth() + delta, 1));
    this.selectedDay.set(null);
    this.selectedStart.set(null);
  }

  protected async confirm(): Promise<void> {
    this.form.markAllAsTouched();
    const info = this.info.hasValue() ? this.info.value() : null;
    const start = this.selectedStart();
    if (this.form.invalid || !info || !start) {
      return;
    }

    this.busy.set(true);
    this.error.set('');
    try {
      const value = this.form.getRawValue();
      const booked = await this.booking.book({
        establishmentId: info.establishmentId,
        serviceIds: [...this.selectedServiceIds()],
        resourceId: this.resourceId(),
        startsAt: start,
        phone: value.phone.trim(),
        note: value.note.trim() || null,
      });
      this.confirmation.set(booked);
      // The slot is held for a while: take the client straight to the payment page.
      if (booked.payment?.status === 'Awaiting' && booked.payment.paymentUrl) {
        window.location.assign(booked.payment.paymentUrl);
      }
    } catch (error) {
      this.error.set(problemMessage(error));
      if (error instanceof HttpErrorResponse && error.status === 409) {
        this.selectedStart.set(null);
        this.availability.reload();
      }
    } finally {
      this.busy.set(false);
    }
  }

  protected duration(minutes: number): string {
    return formatDuration(minutes);
  }

  protected money(amount: number, currency: string): string {
    return formatMoney(amount, currency);
  }

  protected servicePrice(amount: number, type: string, currency: string): string {
    return `${type === 'From' ? 'À partir de ' : ''}${formatMoney(amount, currency)}`;
  }

  protected whenText(instant: string): string {
    const zone = this.info.hasValue() ? this.info.value().timezone : 'UTC';
    return formatInZone(instant, zone, {
      weekday: 'long',
      day: 'numeric',
      month: 'long',
      hour: '2-digit',
      minute: '2-digit',
    });
  }

  private resetSlot(): void {
    this.selectedStart.set(null);
    this.notice.set('');
  }

  private async watch(establishmentId: string | null): Promise<void> {
    if (establishmentId === this.joined) {
      return;
    }

    this.joined = establishmentId;
    await this.connection?.stop();
    this.connection = null;
    this.live.set(false);
    if (!establishmentId) {
      return;
    }

    const connection = await this.realtime.connect('/hubs/availability', {
      handlers: {
        SlotsChanged: () => this.availability.reload(),
        Reconnected: () => this.availability.reload(),
      },
      join: { method: 'Join', args: [establishmentId] },
    });
    if (this.joined === establishmentId) {
      this.connection = connection;
      this.live.set(connection.connected());
    } else {
      await connection.stop();
    }
  }
}
