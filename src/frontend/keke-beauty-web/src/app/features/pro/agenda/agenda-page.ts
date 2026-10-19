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
import { NgTemplateOutlet } from '@angular/common';
import { RouterLink } from '@angular/router';
import { LucideBellRing } from '@lucide/angular';
import { problemMessage } from '../../../core/auth/auth.models';
import { APPOINTMENT_STATUS, formatInZone } from '../../../core/booking/booking.service';
import { formatMoney } from '../../../core/format/format';
import { MOBILE_MONEY_LABELS } from '../../../core/format/payment-methods';
import { AgendaService, PartnerAppointment } from '../../../core/partner/agenda.service';
import { PartnerService } from '../../../core/partner/partner.service';
import { RealtimeConnection, RealtimeService } from '../../../core/realtime/realtime.service';
import { ResourcesPanel } from './resources-panel';
import { SettingsPanel } from './settings-panel';
import { StatsPanel } from './stats-panel';
import { TimeOffPanel } from './time-off-panel';

/** Establishments are in Côte d'Ivoire (UTC+0, no daylight saving): their wall clock equals UTC. */
const ZONE = 'Africa/Abidjan';

type Tab = 'requests' | 'agenda' | 'resources' | 'timeoff' | 'settings' | 'stats';
type Span = 'day' | 'week' | 'month';

const TABS: readonly { readonly id: Tab; readonly label: string }[] = [
  { id: 'requests', label: 'Demandes' },
  { id: 'agenda', label: 'Agenda' },
  { id: 'resources', label: 'Ressources' },
  { id: 'timeoff', label: 'Indisponibilités' },
  { id: 'settings', label: 'Paramètres' },
  { id: 'stats', label: 'Statistiques' },
];

const SPANS: readonly { readonly id: Span; readonly label: string }[] = [
  { id: 'day', label: 'Jour' },
  { id: 'week', label: 'Semaine' },
  { id: 'month', label: 'Mois' },
];

const LIVE_STATUSES = new Set([
  'Pending',
  'Confirmed',
  'RescheduleProposed',
  'Completed',
  'NoShow',
]);

/** `datetime-local` value of an establishment in UTC+0 to an instant. */
export function toInstant(localValue: string): string {
  return `${localValue}:00.000Z`;
}

function startOfDay(date: Date): Date {
  return new Date(Date.UTC(date.getFullYear(), date.getMonth(), date.getDate()));
}

/**
 * `/pro/etablissements/:id/agenda` — incoming requests, day/week/month agenda, resources, closures, settings and
 * statistics (PRD AGD-01 to AGD-08). New requests and cancellations arrive live (SignalR) with a visual and sound alert.
 */
@Component({
  selector: 'app-agenda-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    RouterLink,
    ResourcesPanel,
    TimeOffPanel,
    SettingsPanel,
    StatsPanel,
    LucideBellRing,
    NgTemplateOutlet,
  ],
  styles: `
    .page {
      display: grid;
      gap: 1.25rem;
      padding-block: clamp(1.5rem, 4vw, 3rem);
    }
    .head {
      display: flex;
      flex-wrap: wrap;
      align-items: end;
      justify-content: space-between;
      gap: 1rem;
    }
    .tabs {
      display: flex;
      flex-wrap: wrap;
      gap: 0.5rem;
    }
    .tab {
      display: inline-flex;
      align-items: center;
      gap: 0.5rem;
      min-height: 2.75rem;
      padding: 0.5rem 1.125rem;
      border: 1.5px solid var(--kb-border);
      border-radius: var(--radius-pill);
      background: var(--kb-card);
      font: inherit;
      font-weight: 600;
      cursor: pointer;

      &[aria-selected='true'] {
        border-color: var(--kb-primary-600);
        background: var(--kb-primary-50);
        color: var(--kb-primary-800);
      }
    }
    .badge {
      min-width: 1.5rem;
      padding-inline: 0.375rem;
      border-radius: var(--radius-pill);
      background: var(--kb-danger);
      color: #fff;
      font-size: var(--fs-2xs);
      font-weight: 800;
      line-height: 1.5rem;
      text-align: center;
    }
    .alert {
      display: flex;
      align-items: center;
      gap: 0.75rem;
      padding: 0.75rem 1rem;
      border: 1px solid var(--kb-primary-300);
      border-radius: var(--radius-md);
      background: var(--kb-primary-50);
      font-weight: 600;
      animation: ring 600ms var(--ease-out);

      svg {
        width: 1.25rem;
        height: 1.25rem;
        color: var(--kb-primary-600);
      }
    }
    @keyframes ring {
      30% {
        transform: translateX(-4px);
      }
      60% {
        transform: translateX(4px);
      }
    }
    .toolbar {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 0.5rem;
    }
    .toolbar .range {
      flex: 1;
      min-width: 12rem;
      font-weight: 700;
      text-align: center;
      text-transform: capitalize;
    }
    .list {
      display: grid;
      gap: 0.75rem;
    }
    .day-title {
      margin-top: 0.5rem;
      font-family: var(--font-body);
      font-size: var(--fs-md);
      font-weight: 700;
      text-transform: capitalize;
      color: var(--kb-primary-700);
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
    .card[data-status='Pending'] {
      border-color: var(--kb-primary-300);
    }
    .top {
      display: flex;
      flex-wrap: wrap;
      align-items: baseline;
      justify-content: space-between;
      gap: 0.5rem;
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
    }
    .propose {
      display: flex;
      flex-wrap: wrap;
      align-items: end;
      gap: 0.75rem;
      padding: 0.75rem;
      border-radius: var(--radius-md);
      background: var(--kb-primary-50);

      label {
        display: grid;
        gap: 0.25rem;
        font-size: var(--fs-sm);
        font-weight: 600;
      }
      input {
        min-height: 2.75rem;
        padding: 0.5rem 0.75rem;
        border: 1px solid var(--kb-border);
        border-radius: var(--radius-sm);
        font: inherit;
      }
    }
  `,
  template: `
    <div class="container page">
      <a class="link" routerLink="/pro">← Mes établissements</a>
      <div class="head">
        <div>
          <p class="eyebrow">Agenda</p>
          <h1>{{ detail.hasValue() ? detail.value().name : 'Rendez-vous' }}</h1>
        </div>
        <span class="live" [hidden]="!live()">En direct</span>
      </div>

      @if (alert()) {
        <p class="alert" role="status" aria-live="assertive">
          <svg lucideBellRing aria-hidden="true"></svg>
          {{ alert() }}
        </p>
      }

      <div class="tabs" role="tablist" aria-label="Sections de l'agenda">
        @for (t of tabs; track t.id) {
          <button
            type="button"
            class="tab"
            role="tab"
            [id]="'tab-' + t.id"
            [attr.aria-selected]="tab() === t.id"
            [attr.aria-controls]="'panel-' + t.id"
            (click)="tab.set(t.id)"
          >
            {{ t.label }}
            @if (t.id === 'requests' && pending().length > 0) {
              <span class="badge" [attr.aria-label]="pending().length + ' demande(s) en attente'">{{
                pending().length
              }}</span>
            }
          </button>
        }
      </div>

      @if (error()) {
        <p class="form-error" role="alert">{{ error() }}</p>
      }

      <section [id]="'panel-' + tab()" role="tabpanel" [attr.aria-labelledby]="'tab-' + tab()">
        @switch (tab()) {
          @case ('requests') {
            <div class="list">
              @for (a of pending(); track a.id) {
                <ng-container *ngTemplateOutlet="card; context: { $implicit: a }" />
              } @empty {
                <p class="hint">
                  Aucune demande en attente. Les nouvelles demandes apparaissent ici en direct.
                </p>
              }
            </div>
          }
          @case ('agenda') {
            <div class="toolbar">
              @for (s of spans; track s.id) {
                <button
                  type="button"
                  class="btn btn-sm"
                  [class.btn-primary]="span() === s.id"
                  [class.btn-ghost]="span() !== s.id"
                  (click)="span.set(s.id)"
                >
                  {{ s.label }}
                </button>
              }
              <button
                type="button"
                class="btn btn-ghost btn-sm"
                aria-label="Période précédente"
                (click)="shift(-1)"
              >
                ←
              </button>
              <span class="range" aria-live="polite">{{ rangeLabel() }}</span>
              <button
                type="button"
                class="btn btn-ghost btn-sm"
                aria-label="Période suivante"
                (click)="shift(1)"
              >
                →
              </button>
              <button type="button" class="btn btn-ghost btn-sm" (click)="today()">
                Aujourd'hui
              </button>
            </div>
            <div class="list">
              @for (group of grouped(); track group.day) {
                <h2 class="day-title">{{ group.title }}</h2>
                @for (a of group.items; track a.id) {
                  <ng-container *ngTemplateOutlet="card; context: { $implicit: a }" />
                }
              } @empty {
                <p class="hint">Aucun rendez-vous sur cette période.</p>
              }
            </div>
          }
          @case ('resources') {
            @if (detail.hasValue()) {
              <app-resources-panel [establishmentId]="id()" [services]="detail.value().services" />
            }
          }
          @case ('timeoff') {
            <app-time-off-panel [establishmentId]="id()" />
          }
          @case ('settings') {
            <app-settings-panel [establishmentId]="id()" />
          }
          @case ('stats') {
            <app-stats-panel [establishmentId]="id()" />
          }
        }
      </section>
    </div>

    <ng-template #card let-a>
      <article class="card" [attr.data-status]="a.status">
        <div class="top">
          <h3>{{ a.clientName }}</h3>
          <span class="status" [attr.data-tone]="tone(a.status)">{{ label(a.status) }}</span>
        </div>
        <p>
          <strong>{{ when(a.startsAt) }}</strong> — {{ a.resourceName }}
        </p>
        <p class="meta">{{ summary(a) }}</p>
        @if (a.clientPhone) {
          <p class="meta">
            Téléphone : <a class="link" [href]="'tel:' + a.clientPhone">{{ a.clientPhone }}</a>
          </p>
        }
        @if (a.clientNote) {
          <p class="meta">« {{ a.clientNote }} »</p>
        }
        @if (a.status === 'RescheduleProposed' && a.proposedStartsAt) {
          <p class="meta">
            Nouveau créneau proposé : {{ when(a.proposedStartsAt) }} (en attente de la réponse du
            client)
          </p>
        }

        @if (proposing() === a.id) {
          <div class="propose">
            <label
              >Nouveau créneau
              <input
                type="datetime-local"
                step="900"
                [value]="proposalValue()"
                (input)="proposalValue.set($any($event.target).value)"
              />
            </label>
            <label
              >Message (facultatif)
              <input
                type="text"
                maxlength="200"
                [value]="proposalComment()"
                (input)="proposalComment.set($any($event.target).value)"
              />
            </label>
            <button
              type="button"
              class="btn btn-primary btn-sm"
              [disabled]="busy() || !proposalValue()"
              (click)="sendProposal(a)"
            >
              Envoyer la proposition
            </button>
            <button type="button" class="btn btn-ghost btn-sm" (click)="proposing.set(null)">
              Annuler
            </button>
          </div>
        }

        @if (a.servicePayment; as sp) {
          <p class="meta">
            Payé : {{ methodLabel(sp.method) }}{{ sp.reference ? ', réf. ' + sp.reference : '' }}
            <button
              type="button"
              class="btn btn-ghost btn-sm"
              [disabled]="busy()"
              (click)="markUnpaid(a)"
            >
              Annuler l'enregistrement
            </button>
          </p>
        } @else if (a.status === 'Confirmed' || a.status === 'Completed') {
          @if (payingId() === a.id) {
            <div class="proposal">
              <label
                >Réglé par
                <select [value]="payMethod()" (change)="payMethod.set($any($event.target).value)">
                  @for (m of payMethods(); track m.id) {
                    <option [value]="m.id">{{ m.label }}</option>
                  }
                </select>
              </label>
              @if (payMethod() !== 'Cash') {
                <label
                  >Référence de la transaction
                  <input
                    type="text"
                    maxlength="60"
                    [value]="payReference()"
                    (input)="payReference.set($any($event.target).value)"
                  />
                </label>
              }
              <button
                type="button"
                class="btn btn-primary btn-sm"
                [disabled]="busy() || (payMethod() !== 'Cash' && payReference().trim().length < 3)"
                (click)="markPaid(a)"
              >
                Enregistrer le paiement
              </button>
              <button type="button" class="btn btn-ghost btn-sm" (click)="payingId.set(null)">
                Annuler
              </button>
            </div>
          }
        }

        <div class="actions">
          @if (
            !a.servicePayment &&
            (a.status === 'Confirmed' || a.status === 'Completed') &&
            payingId() !== a.id
          ) {
            <button
              type="button"
              class="btn btn-ghost btn-sm"
              [disabled]="busy()"
              (click)="startPayment(a)"
            >
              Marquer payé
            </button>
          }
          @if (a.status === 'Pending') {
            <button
              type="button"
              class="btn btn-primary btn-sm"
              [disabled]="busy()"
              (click)="confirmRequest(a)"
            >
              Valider
            </button>
            <button
              type="button"
              class="btn btn-ghost btn-sm"
              [disabled]="busy()"
              (click)="reject(a)"
            >
              Refuser
            </button>
          }
          @if (a.status === 'Pending' || a.status === 'Confirmed') {
            <button
              type="button"
              class="btn btn-ghost btn-sm"
              [disabled]="busy()"
              (click)="startProposal(a)"
            >
              Proposer un autre créneau
            </button>
          }
          @if (a.status === 'Confirmed') {
            <button
              type="button"
              class="btn btn-ghost btn-sm"
              [disabled]="busy()"
              (click)="cancel(a)"
            >
              Annuler
            </button>
            @if (hasStarted(a)) {
              <button
                type="button"
                class="btn btn-ghost btn-sm"
                [disabled]="busy()"
                (click)="noShow(a)"
              >
                Client absent
              </button>
            }
          }
          @if (a.status === 'RescheduleProposed') {
            <button
              type="button"
              class="btn btn-ghost btn-sm"
              [disabled]="busy()"
              (click)="cancel(a)"
            >
              Annuler le rendez-vous
            </button>
          }
        </div>
      </article>
    </ng-template>
  `,
})
export class AgendaPage {
  /** Bound from the `:id` route parameter. */
  readonly id = input.required<string>();

  private readonly agenda = inject(AgendaService);
  private readonly partner = inject(PartnerService);
  private readonly realtime = inject(RealtimeService);

  protected readonly tabs = TABS;
  protected readonly spans = SPANS;
  protected readonly tab = signal<Tab>('requests');
  protected readonly span = signal<Span>('week');
  protected readonly anchor = signal(startOfDay(new Date()));
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly alert = signal('');
  protected readonly live = signal(false);
  protected readonly proposing = signal<string | null>(null);
  protected readonly payingId = signal<string | null>(null);
  protected readonly payMethod = signal('Cash');
  protected readonly payReference = signal('');
  private readonly accepted = resource({
    params: () => this.id(),
    loader: ({ params }) => this.agenda.acceptedPayments(params),
  });
  /** The ways this establishment accepts to be paid, for the "marquer payé" form. */
  protected readonly payMethods = computed(() => {
    const value = this.accepted.value();
    return [
      ...(value?.cash === false ? [] : [{ id: 'Cash', label: 'Espèces' }]),
      ...(value?.mobileMoney ?? []).map((m) => ({
        id: m.operator,
        label: MOBILE_MONEY_LABELS[m.operator] ?? m.operator,
      })),
    ];
  });
  protected readonly proposalValue = signal('');
  protected readonly proposalComment = signal('');

  protected readonly detail = resource({
    params: () => this.id(),
    loader: ({ params }) => this.partner.get(params),
  });

  private readonly range = computed(() => {
    const anchor = this.anchor();
    const day = anchor.getUTCDay();
    const mondayOffset = (day + 6) % 7;
    switch (this.span()) {
      case 'day':
        return { from: anchor, to: anchor };
      case 'week': {
        const from = new Date(anchor.getTime() - mondayOffset * 86_400_000);
        return { from, to: new Date(from.getTime() + 6 * 86_400_000) };
      }
      default:
        return {
          from: new Date(Date.UTC(anchor.getUTCFullYear(), anchor.getUTCMonth(), 1)),
          to: new Date(Date.UTC(anchor.getUTCFullYear(), anchor.getUTCMonth() + 1, 0)),
        };
    }
  });

  protected readonly pendingList = resource({
    params: () => this.id(),
    loader: ({ params }) => this.agenda.appointments(params, { status: 'Pending' }),
  });

  protected readonly periodList = resource({
    params: () => ({ id: this.id(), range: this.range() }),
    loader: ({ params }) =>
      this.agenda.appointments(params.id, {
        from: params.range.from.toISOString(),
        to: new Date(params.range.to.getTime() + 86_400_000 - 1).toISOString(),
      }),
  });

  protected readonly pending = computed(() =>
    this.pendingList.hasValue() ? this.pendingList.value() : [],
  );

  protected readonly grouped = computed(() => {
    const items = (this.periodList.hasValue() ? this.periodList.value() : []).filter((a) =>
      LIVE_STATUSES.has(a.status),
    );
    const days = new Map<string, PartnerAppointment[]>();
    for (const a of items) {
      const key = a.startsAt.slice(0, 10);
      days.set(key, [...(days.get(key) ?? []), a]);
    }

    return [...days.entries()]
      .sort(([a], [b]) => a.localeCompare(b))
      .map(([day, list]) => ({
        day,
        title: new Intl.DateTimeFormat('fr-FR', {
          weekday: 'long',
          day: 'numeric',
          month: 'long',
          timeZone: 'UTC',
        }).format(new Date(`${day}T12:00:00Z`)),
        items: list.sort((x, y) => x.startsAt.localeCompare(y.startsAt)),
      }));
  });

  protected readonly rangeLabel = computed(() => {
    const { from, to } = this.range();
    const format = new Intl.DateTimeFormat('fr-FR', {
      day: 'numeric',
      month: 'long',
      year: 'numeric',
      timeZone: 'UTC',
    });
    if (this.span() === 'month') {
      return new Intl.DateTimeFormat('fr-FR', {
        month: 'long',
        year: 'numeric',
        timeZone: 'UTC',
      }).format(from);
    }

    return this.span() === 'day'
      ? format.format(from)
      : `${format.format(from)} → ${format.format(to)}`;
  });

  private connection: RealtimeConnection | null = null;
  private watched: string | null = null;

  constructor() {
    effect(() => {
      const id = this.id();
      untracked(() => void this.watch(id));
    });
    inject(DestroyRef).onDestroy(() => void this.connection?.stop());
  }

  protected shift(direction: -1 | 1): void {
    const anchor = this.anchor();
    const step =
      this.span() === 'day'
        ? new Date(anchor.getTime() + direction * 86_400_000)
        : this.span() === 'week'
          ? new Date(anchor.getTime() + direction * 7 * 86_400_000)
          : new Date(Date.UTC(anchor.getUTCFullYear(), anchor.getUTCMonth() + direction, 1));
    this.anchor.set(step);
  }

  protected today(): void {
    this.anchor.set(startOfDay(new Date()));
  }

  protected label(status: string): string {
    return APPOINTMENT_STATUS[status]?.label ?? status;
  }

  protected tone(status: string): string {
    return APPOINTMENT_STATUS[status]?.tone ?? 'done';
  }

  protected when(instant: string): string {
    return formatInZone(instant, ZONE, {
      weekday: 'short',
      day: 'numeric',
      month: 'long',
      hour: '2-digit',
      minute: '2-digit',
    });
  }

  protected summary(a: PartnerAppointment): string {
    return `${a.lines.map((l) => l.name).join(', ')} · ${formatMoney(a.totalPrice, a.currencyCode)}`;
  }

  protected hasStarted(a: PartnerAppointment): boolean {
    return new Date(a.startsAt).getTime() <= Date.now();
  }

  protected confirmRequest(a: PartnerAppointment): Promise<void> {
    return this.run(() => this.agenda.confirm(a.id));
  }

  protected reject(a: PartnerAppointment): Promise<void> {
    const reason = prompt('Motif du refus (facultatif, transmis au client)');
    return reason === null ? Promise.resolve() : this.run(() => this.agenda.reject(a.id, reason));
  }

  protected cancel(a: PartnerAppointment): Promise<void> {
    const reason = prompt("Motif de l'annulation (facultatif, transmis au client)");
    return reason === null ? Promise.resolve() : this.run(() => this.agenda.cancel(a.id, reason));
  }

  protected noShow(a: PartnerAppointment): Promise<void> {
    return this.run(() => this.agenda.noShow(a.id));
  }

  protected methodLabel(method: string): string {
    return method === 'Cash' ? 'espèces' : (MOBILE_MONEY_LABELS[method] ?? method);
  }

  protected startPayment(a: PartnerAppointment): void {
    this.payingId.set(a.id);
    this.payMethod.set(this.payMethods()[0]?.id ?? 'Cash');
    this.payReference.set('');
  }

  protected async markPaid(a: PartnerAppointment): Promise<void> {
    await this.run(() =>
      this.agenda.markPaid(
        a.id,
        this.payMethod(),
        this.payMethod() === 'Cash' ? null : this.payReference(),
      ),
    );
    if (!this.error()) {
      this.payingId.set(null);
    }
  }

  protected markUnpaid(a: PartnerAppointment): Promise<void> {
    return this.run(() => this.agenda.markUnpaid(a.id));
  }

  protected startProposal(a: PartnerAppointment): void {
    this.proposing.set(a.id);
    this.proposalValue.set(a.startsAt.slice(0, 16));
    this.proposalComment.set('');
  }

  protected async sendProposal(a: PartnerAppointment): Promise<void> {
    await this.run(() =>
      this.agenda.proposeReschedule(a.id, toInstant(this.proposalValue()), this.proposalComment()),
    );
    if (!this.error()) {
      this.proposing.set(null);
    }
  }

  protected refreshLists(): void {
    this.pendingList.reload();
    this.periodList.reload();
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
      this.refreshLists();
    }
  }

  private async watch(id: string): Promise<void> {
    if (id === this.watched) {
      return;
    }

    this.watched = id;
    await this.connection?.stop();
    this.connection = null;
    const connection = await this.realtime.connect('/hubs/pro', {
      authenticated: true,
      handlers: {
        AppointmentRequested: () => this.announce('Nouvelle demande de rendez-vous !'),
        AppointmentCancelled: () => this.announce('Un rendez-vous a été annulé.'),
        AppointmentUpdated: () => this.refreshLists(),
        Reconnected: () => this.refreshLists(),
      },
      join: { method: 'Join', args: [id] },
    });
    if (this.watched === id) {
      this.connection = connection;
      this.live.set(connection.connected());
    } else {
      await connection.stop();
    }
  }

  private announce(message: string): void {
    this.refreshLists();
    this.alert.set(message);
    beep();
    setTimeout(() => this.alert.set(''), 8000);
  }
}

/** A short two-tone chime; silently skipped where audio is not allowed (autoplay policies, tests). */
function beep(): void {
  try {
    const context = new AudioContext();
    const oscillator = context.createOscillator();
    const gain = context.createGain();
    oscillator.connect(gain);
    gain.connect(context.destination);
    oscillator.type = 'sine';
    oscillator.frequency.setValueAtTime(880, context.currentTime);
    oscillator.frequency.setValueAtTime(1175, context.currentTime + 0.12);
    gain.gain.setValueAtTime(0.0001, context.currentTime);
    gain.gain.exponentialRampToValueAtTime(0.15, context.currentTime + 0.02);
    gain.gain.exponentialRampToValueAtTime(0.0001, context.currentTime + 0.4);
    oscillator.start();
    oscillator.stop(context.currentTime + 0.42);
    oscillator.onended = () => void context.close();
  } catch {
    // No audio available.
  }
}
