import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  inject,
  resource,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { problemMessage } from '../../core/auth/auth.models';
import { formatInZone } from '../../core/booking/booking.service';
import { formatMoney } from '../../core/format/format';
import {
  TeamService,
  elapsed,
  type CommissionSummary,
  type TeamAppointment,
} from '../../core/team/team.service';

type Step = 'todo' | 'running' | 'done' | 'absent';

/**
 * `/equipe` — the collaborator's day: her appointments, a timer to start and finish each treatment,
 * the client's technical note (allergies first) and what she has earned.
 */
@Component({
  selector: 'app-team-portal',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  styles: `
    .page {
      display: grid;
      gap: 1.25rem;
      max-width: 46rem;
      padding-block: clamp(1.5rem, 4vw, 3rem);
    }
    .days {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 0.5rem;
    }
    .card {
      display: grid;
      gap: 0.6rem;
      padding: 1.1rem 1.25rem;
      border: 1px solid var(--kb-border);
      border-radius: var(--radius-lg);
      background: #fff;
      box-shadow: var(--shadow-soft);
    }
    .card[data-step='running'] {
      border-color: color-mix(in srgb, var(--kb-success) 40%, white);
      background: color-mix(in srgb, var(--kb-success) 6%, white);
    }
    .card[data-step='done'],
    .card[data-step='absent'] {
      opacity: 0.8;
    }
    .top {
      display: flex;
      flex-wrap: wrap;
      justify-content: space-between;
      gap: 0.5rem;
    }
    h2,
    h3,
    p {
      margin: 0;
    }
    .when {
      font-weight: 700;
    }
    .meta {
      color: var(--kb-ink-muted);
    }
    .alert {
      padding: 0.5rem 0.75rem;
      border-radius: var(--radius-md);
      background: color-mix(in srgb, var(--kb-danger, #b3261e) 10%, white);
      font-weight: 600;
    }
    .timer {
      font-variant-numeric: tabular-nums;
      font-size: 1.5rem;
      font-weight: 700;
    }
    .actions {
      display: flex;
      flex-wrap: wrap;
      gap: 0.5rem;
    }
    .stack {
      display: grid;
      gap: 0.25rem;
    }
    .totals {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(9rem, 1fr));
      gap: 0.75rem;
      margin: 0;
    }
    .totals dt {
      color: var(--kb-ink-muted);
    }
    .totals dd {
      margin: 0;
      font-weight: 700;
    }
    table {
      width: 100%;
      border-collapse: collapse;
    }
    th,
    td {
      padding: 0.35rem 0.5rem;
      text-align: left;
      border-bottom: 1px solid var(--kb-border);
    }
    td.num,
    th.num {
      text-align: right;
    }
  `,
  template: `
    <div class="container page">
      <div>
        <p class="eyebrow">Équipe</p>
        <h1>Mon <em>planning</em></h1>
        @if (memberships.hasValue()) {
          <p class="hint">
            {{ placesLabel() }}
          </p>
        }
      </div>

      <div class="days" role="group" aria-label="Choisir le jour">
        <button class="btn btn-light btn-sm" type="button" (click)="shift(-1)">← Veille</button>
        <input
          class="admin-input"
          type="date"
          aria-label="Jour affiché"
          [ngModel]="date()"
          (ngModelChange)="day.set($event)"
        />
        <button class="btn btn-light btn-sm" type="button" (click)="shift(1)">Lendemain →</button>
        <button class="btn btn-ghost btn-sm" type="button" (click)="day.set('')">
          Aujourd'hui
        </button>
      </div>

      @if (error()) {
        <p class="form-error" role="alert">{{ error() }}</p>
      }

      @if (planning.hasValue()) {
        <section aria-labelledby="day-title" class="stack">
          <h2 id="day-title">{{ planning.value().appointments.length }} rendez-vous</h2>
          @for (a of planning.value().appointments; track a.id) {
            <article class="card" [attr.data-step]="step(a)">
              <div class="top">
                <p class="when">
                  {{ time(a.startsAt) }} – {{ time(a.endsAt) }} · {{ a.clientName }}
                </p>
                <span class="meta">{{ stepLabel(a) }}</span>
              </div>
              <p class="meta">{{ summary(a) }} · {{ a.establishmentName }}</p>
              @if (a.allergies) {
                <p class="alert" role="alert">Allergies : {{ a.allergies }}</p>
              }
              @if (a.startedAt && !a.finishedAt) {
                <p class="timer" role="timer" aria-label="Durée du soin">
                  {{ chrono(a.startedAt) }}
                </p>
              }
              @if (a.tipAmount > 0) {
                <p class="meta">Pourboire : {{ money(a.tipAmount) }}</p>
              }
              <div class="actions">
                @if (a.canStart) {
                  <button
                    class="btn btn-primary btn-sm"
                    type="button"
                    [disabled]="busy()"
                    (click)="start(a)"
                  >
                    Démarrer le soin
                  </button>
                }
                @if (a.canFinish) {
                  <button
                    class="btn btn-primary btn-sm"
                    type="button"
                    [disabled]="busy()"
                    (click)="finish(a)"
                  >
                    Terminer le soin
                  </button>
                }
                @if (a.clientUserId) {
                  <button
                    class="btn btn-ghost btn-sm"
                    type="button"
                    [attr.aria-expanded]="openNote() === a.id"
                    (click)="toggleNote(a)"
                  >
                    Fiche technique
                  </button>
                }
              </div>
              @if (openNote() === a.id) {
                <form class="stack" (ngSubmit)="saveNote(a)">
                  <label class="stack">
                    Allergies et contre-indications
                    <textarea
                      class="admin-input"
                      name="allergies"
                      rows="2"
                      maxlength="500"
                      [(ngModel)]="allergies"
                    ></textarea>
                  </label>
                  <label class="stack">
                    Notes de soin (produits, teintes, préférences)
                    <textarea
                      class="admin-input"
                      name="notes"
                      rows="4"
                      maxlength="2000"
                      [(ngModel)]="notes"
                    ></textarea>
                  </label>
                  <div class="actions">
                    <button class="btn btn-primary btn-sm" type="submit" [disabled]="busy()">
                      Enregistrer
                    </button>
                    <button class="btn btn-ghost btn-sm" type="button" (click)="openNote.set(null)">
                      Fermer
                    </button>
                  </div>
                </form>
              }
            </article>
          } @empty {
            <p class="hint">Aucun rendez-vous ce jour-là.</p>
          }
        </section>
      } @else if (planning.error()) {
        <p class="form-error" role="alert">Impossible de charger votre planning. Réessayez.</p>
      } @else {
        <p role="status">Chargement…</p>
      }

      @if (totals(); as t) {
        <section aria-labelledby="gains-title" class="card">
          <h2 id="gains-title">Mes gains · 30 derniers jours</h2>
          <dl class="totals">
            <div>
              <dt>Soins terminés</dt>
              <dd>{{ t.services }}</dd>
            </div>
            <div>
              <dt>Chiffre réalisé</dt>
              <dd>{{ money(t.revenue) }}</dd>
            </div>
            <div>
              <dt>Commissions</dt>
              <dd>{{ money(t.commission) }}</dd>
            </div>
            <div>
              <dt>Pourboires</dt>
              <dd>{{ money(t.tips) }}</dd>
            </div>
            <div>
              <dt>Total</dt>
              <dd>{{ money(t.total) }}</dd>
            </div>
          </dl>
          @if (t.lines.length > 0) {
            <details>
              <summary>Détail des soins</summary>
              <table>
                <thead>
                  <tr>
                    <th>Cliente</th>
                    <th class="num">Soin</th>
                    <th class="num">Commission</th>
                    <th class="num">Pourboire</th>
                  </tr>
                </thead>
                <tbody>
                  @for (l of t.lines; track l.appointmentId) {
                    <tr>
                      <td>{{ l.clientName }}</td>
                      <td class="num">{{ money(l.servicePrice) }}</td>
                      <td class="num">{{ money(l.commission) }}</td>
                      <td class="num">{{ money(l.tip) }}</td>
                    </tr>
                  }
                </tbody>
              </table>
            </details>
          }
        </section>
      }
    </div>
  `,
})
export class TeamPortal {
  private readonly team = inject(TeamService);

  protected readonly day = signal('');
  protected readonly now = signal(Date.now());
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly openNote = signal<string | null>(null);
  protected allergies = '';
  protected notes = '';

  protected readonly memberships = resource({ loader: () => this.team.memberships() });
  protected readonly planning = resource({
    params: () => this.day(),
    loader: ({ params }) => this.team.planning(params || undefined),
  });
  protected readonly commissions = resource({ loader: () => this.team.commissions() });

  /** The planning's own date (yyyy-mm-dd), whatever the browser's timezone. */
  protected readonly date = computed(() =>
    this.planning.hasValue() ? this.planning.value().date : this.day(),
  );

  /** All of her resources added up (she may work for several establishments). */
  protected readonly totals = computed(() => {
    if (!this.commissions.hasValue()) {
      return null;
    }

    const all: readonly CommissionSummary[] = this.commissions.value();
    return {
      services: all.reduce((n, c) => n + c.services, 0),
      revenue: all.reduce((n, c) => n + c.revenue, 0),
      commission: all.reduce((n, c) => n + c.commission, 0),
      tips: all.reduce((n, c) => n + c.tips, 0),
      total: all.reduce((n, c) => n + c.total, 0),
      lines: all.flatMap((c) => c.lines),
    };
  });

  constructor() {
    const timer = setInterval(() => this.now.set(Date.now()), 1000);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
  }

  protected placesLabel(): string {
    const places = this.memberships.value() ?? [];
    return places.length === 0
      ? 'Aucun établissement ne vous a encore ajoutée à son équipe.'
      : places
          .map((m) => `${m.establishmentName} (${m.commissionPercent} % de commission)`)
          .join(' · ');
  }

  protected shift(days: number): void {
    const base = this.date() ? new Date(`${this.date()}T12:00:00`) : new Date();
    base.setDate(base.getDate() + days);
    const month = `${base.getMonth() + 1}`.padStart(2, '0');
    this.day.set(`${base.getFullYear()}-${month}-${`${base.getDate()}`.padStart(2, '0')}`);
  }

  protected time(instant: string): string {
    const zone = this.planning.hasValue() ? this.planning.value().timezone : 'Africa/Abidjan';
    return formatInZone(instant, zone, { hour: '2-digit', minute: '2-digit' });
  }

  protected summary(a: TeamAppointment): string {
    return a.lines.map((l) => l.name).join(', ');
  }

  protected money(amount: number): string {
    return formatMoney(amount);
  }

  protected chrono(startedAt: string): string {
    return elapsed(startedAt, this.now());
  }

  protected step(a: TeamAppointment): Step {
    if (a.status === 'NoShow') {
      return 'absent';
    }

    if (a.finishedAt || a.status === 'Completed') {
      return 'done';
    }

    return a.startedAt ? 'running' : 'todo';
  }

  protected stepLabel(a: TeamAppointment): string {
    return { todo: 'À faire', running: 'En cours', done: 'Terminé', absent: 'Absente' }[
      this.step(a)
    ];
  }

  protected start(a: TeamAppointment): Promise<void> {
    return this.run(() => this.team.start(a.id));
  }

  protected finish(a: TeamAppointment): Promise<void> {
    return this.run(() => this.team.finish(a.id));
  }

  protected async toggleNote(a: TeamAppointment): Promise<void> {
    if (this.openNote() === a.id || !a.clientUserId) {
      this.openNote.set(null);
      return;
    }

    this.error.set('');
    try {
      const note = await this.team.note(a.establishmentId, a.clientUserId);
      this.allergies = note.allergies ?? '';
      this.notes = note.notes ?? '';
      this.openNote.set(a.id);
    } catch (error) {
      this.error.set(problemMessage(error));
    }
  }

  protected saveNote(a: TeamAppointment): Promise<void> {
    return this.run(async () => {
      await this.team.saveNote(a.establishmentId, a.clientUserId!, {
        allergies: this.allergies.trim() || null,
        notes: this.notes.trim() || null,
      });
      this.openNote.set(null);
    });
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
      this.planning.reload();
      this.commissions.reload();
    }
  }
}
