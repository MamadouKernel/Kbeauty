import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  resource,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { problemMessage } from '../../../core/auth/auth.models';
import { formatMoney } from '../../../core/format/format';
import { TeamService, type TeamMember } from '../../../core/team/team.service';

/**
 * `/pro/etablissements/:id/equipe` — who works here: link each person to her account (by e-mail), set her commission,
 * and follow what every one of them has earned over the last 30 days.
 */
@Component({
  selector: 'app-pro-team',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, RouterLink],
  styles: `
    .page {
      display: grid;
      gap: 1.25rem;
      max-width: 48rem;
      padding-block: clamp(1.5rem, 4vw, 3rem);
    }
    .card {
      display: grid;
      gap: 0.75rem;
      padding: 1.1rem 1.25rem;
      border: 1px solid var(--kb-border);
      border-radius: var(--radius-lg);
      background: #fff;
      box-shadow: var(--shadow-soft);
    }
    h2,
    h3,
    p {
      margin: 0;
    }
    .meta {
      color: var(--kb-ink-muted);
    }
    .row {
      display: flex;
      flex-wrap: wrap;
      align-items: end;
      gap: 0.5rem;
    }
    .row label {
      display: grid;
      gap: 0.25rem;
      flex: 1 1 12rem;
    }
    .row label.short {
      flex: 0 1 8rem;
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
      <a class="link" routerLink="/pro">← Mes établissements</a>
      <div>
        <p class="eyebrow">Équipe</p>
        <h1>Équipe et <em>commissions</em></h1>
        <p class="hint">
          Une collaboratrice se connecte avec son propre compte Keke Beauty et voit uniquement ses
          rendez-vous. Elle doit d'abord créer un compte client.
        </p>
      </div>

      @if (error()) {
        <p class="form-error" role="alert">{{ error() }}</p>
      }

      @if (members.hasValue()) {
        @for (m of members.value(); track m.resourceId) {
          <section class="card" [attr.aria-label]="m.name">
            <h2>{{ m.name }}</h2>
            @if (m.userId) {
              <p class="meta">Compte lié : {{ m.collaboratorName }} ({{ m.email }})</p>
            } @else {
              <p class="meta">Aucun compte lié : cette ressource n'a pas de portail.</p>
            }
            <div class="row">
              <label>
                E-mail de la collaboratrice
                <input
                  class="admin-input"
                  type="email"
                  autocomplete="off"
                  [ngModel]="emailOf(m)"
                  (ngModelChange)="setEmail(m, $event)"
                  [name]="'email-' + m.resourceId"
                />
              </label>
              <label class="short">
                Commission (%)
                <input
                  class="admin-input"
                  type="number"
                  min="0"
                  max="100"
                  step="0.5"
                  [ngModel]="percentOf(m)"
                  (ngModelChange)="setPercent(m, $event)"
                  [name]="'percent-' + m.resourceId"
                />
              </label>
              <button
                class="btn btn-primary btn-sm"
                type="button"
                [disabled]="busy()"
                (click)="save(m)"
              >
                Enregistrer
              </button>
              @if (m.userId) {
                <button
                  class="btn btn-ghost btn-sm"
                  type="button"
                  [disabled]="busy()"
                  (click)="remove(m)"
                >
                  Retirer l'accès
                </button>
              }
            </div>
          </section>
        }
      } @else if (members.error()) {
        <p class="form-error" role="alert">Impossible de charger l'équipe. Réessayez.</p>
      } @else {
        <p role="status">Chargement…</p>
      }

      @if (commissions.hasValue()) {
        <section aria-labelledby="gains" class="card">
          <h2 id="gains">Commissions · 30 derniers jours</h2>
          <table>
            <thead>
              <tr>
                <th>Ressource</th>
                <th class="num">Soins</th>
                <th class="num">Chiffre</th>
                <th class="num">Commission</th>
                <th class="num">Pourboires</th>
                <th class="num">À verser</th>
              </tr>
            </thead>
            <tbody>
              @for (c of commissions.value(); track c.resourceId) {
                <tr>
                  <td>{{ c.resourceName }} ({{ c.commissionPercent }} %)</td>
                  <td class="num">{{ c.services }}</td>
                  <td class="num">{{ money(c.revenue) }}</td>
                  <td class="num">{{ money(c.commission) }}</td>
                  <td class="num">{{ money(c.tips) }}</td>
                  <td class="num">{{ money(c.total) }}</td>
                </tr>
              }
            </tbody>
            <tfoot>
              <tr>
                <th>Total</th>
                <th class="num">{{ totals().services }}</th>
                <th class="num">{{ money(totals().revenue) }}</th>
                <th class="num">{{ money(totals().commission) }}</th>
                <th class="num">{{ money(totals().tips) }}</th>
                <th class="num">{{ money(totals().total) }}</th>
              </tr>
            </tfoot>
          </table>
          <p class="meta">
            Keke Beauty calcule ces montants ; c'est l'établissement qui règle ses collaboratrices.
          </p>
        </section>
      }
    </div>
  `,
})
export class ProTeam {
  private readonly team = inject(TeamService);

  readonly id = input.required<string>();

  protected readonly busy = signal(false);
  protected readonly error = signal('');
  private readonly emails = signal<Readonly<Record<string, string>>>({});
  private readonly percents = signal<Readonly<Record<string, number>>>({});

  protected readonly members = resource({
    params: () => this.id(),
    loader: ({ params }) => this.team.members(params),
  });
  protected readonly commissions = resource({
    params: () => this.id(),
    loader: ({ params }) => this.team.ownerCommissions(params),
  });

  protected readonly totals = computed(() => {
    const all = this.commissions.hasValue() ? this.commissions.value() : [];
    return {
      services: all.reduce((n, c) => n + c.services, 0),
      revenue: all.reduce((n, c) => n + c.revenue, 0),
      commission: all.reduce((n, c) => n + c.commission, 0),
      tips: all.reduce((n, c) => n + c.tips, 0),
      total: all.reduce((n, c) => n + c.total, 0),
    };
  });

  protected emailOf(m: TeamMember): string {
    return this.emails()[m.resourceId] ?? m.email ?? '';
  }

  protected percentOf(m: TeamMember): number {
    return this.percents()[m.resourceId] ?? m.commissionPercent;
  }

  protected setEmail(m: TeamMember, value: string): void {
    this.emails.update((all) => ({ ...all, [m.resourceId]: value }));
  }

  protected setPercent(m: TeamMember, value: number): void {
    this.percents.update((all) => ({ ...all, [m.resourceId]: Number(value) }));
  }

  protected money(amount: number): string {
    return formatMoney(amount);
  }

  protected save(m: TeamMember): Promise<void> {
    const typed = this.emailOf(m).trim();
    // Unchanged e-mail: only the rate moves.
    const email = typed && typed !== (m.email ?? '') ? typed : null;
    return this.run(async () => {
      await this.team.assign(this.id(), m.resourceId, {
        email,
        commissionPercent: this.percentOf(m),
      });
      this.emails.update((all) => without(all, m.resourceId));
      this.percents.update((all) => without(all, m.resourceId));
    });
  }

  protected remove(m: TeamMember): Promise<void> {
    return confirm(`Retirer l'accès de ${m.collaboratorName ?? m.name} ?`)
      ? this.run(() => this.team.unassign(this.id(), m.resourceId))
      : Promise.resolve();
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
      this.members.reload();
      this.commissions.reload();
    }
  }
}

function without<T>(all: Readonly<Record<string, T>>, key: string): Record<string, T> {
  return Object.fromEntries(Object.entries(all).filter(([k]) => k !== key));
}
