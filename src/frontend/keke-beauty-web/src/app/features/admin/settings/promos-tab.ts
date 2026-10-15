import { ChangeDetectionStrategy, Component, inject, resource, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  AdminApiService,
  type PromoCode,
  type PromoCodeInput,
} from '../../../core/admin/admin-api.service';
import { problemMessage } from '../../../core/auth/auth.models';

/** A new code being typed: dates are plain `yyyy-MM-dd` strings until they are sent. */
interface Draft {
  code: string;
  percent: number;
  periods: number;
  validUntil: string;
  maxUses: number | null;
  planCodes: string[];
}

const EMPTY: Draft = {
  code: '',
  percent: 20,
  periods: 1,
  validUntil: '',
  maxUses: null,
  planCodes: [],
};

/**
 * Promotion codes (PRD BIL-01): a percent off the next invoices of the chosen plans, with a validity date and a limit of uses.
 * A plan can also carry its own promotion (Formules et tarifs); the better of the two applies, never both.
 */
@Component({
  selector: 'app-promos-tab',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  styles: `
    form {
      display: grid;
      gap: 0.75rem;
      min-width: 0;
    }
    fieldset {
      display: flex;
      flex-wrap: wrap;
      gap: 0.75rem 1rem;
      border: 0;
      padding: 0;
      margin: 0;
    }
    legend {
      font-weight: 700;
      margin-bottom: 0.25rem;
    }
    label {
      display: grid;
      gap: 0.25rem;
      font-size: var(--fs-sm);
      font-weight: 600;
      min-width: 0;
    }
    label.check {
      display: flex;
      align-items: center;
      gap: 0.5rem;
    }
    .hint {
      margin: 0;
      font-size: var(--fs-xs);
      color: var(--kb-ink-muted);
      font-weight: 400;
    }
    table {
      width: 100%;
      border-collapse: collapse;
      font-size: var(--fs-md);
    }
    th,
    td {
      padding: 0.5rem;
      text-align: left;
      border-bottom: 1px solid var(--kb-border);
    }
    .scroll {
      overflow-x: auto;
    }
  `,
  template: `
    @if (message(); as text) {
      <p role="status" class="badge" data-tone="ok">{{ text }}</p>
    }
    @if (error(); as text) {
      <p class="form-error" role="alert">{{ text }}</p>
    }

    <form class="admin-card" (ngSubmit)="create()" aria-label="Nouveau code promo">
      <h2>Nouveau code promo</h2>
      <fieldset>
        <label>
          Code
          <input
            class="admin-input"
            name="code"
            maxlength="30"
            placeholder="BIENVENUE20"
            [(ngModel)]="draft.code"
            required
          />
        </label>
        <label>
          Remise (%)
          <input
            class="admin-input"
            name="percent"
            type="number"
            min="1"
            max="90"
            [(ngModel)]="draft.percent"
          />
        </label>
        <label>
          Nombre de factures remisées
          <input
            class="admin-input"
            name="periods"
            type="number"
            min="1"
            max="36"
            [(ngModel)]="draft.periods"
          />
        </label>
        <label>
          Valable jusqu'au
          <input class="admin-input" name="until" type="date" [(ngModel)]="draft.validUntil" />
        </label>
        <label>
          Utilisations maximales
          <input
            class="admin-input"
            name="maxUses"
            type="number"
            min="1"
            placeholder="illimité"
            [(ngModel)]="draft.maxUses"
          />
        </label>
      </fieldset>
      <fieldset>
        <legend>Formules concernées</legend>
        @for (p of paidPlans.value() ?? []; track p.id) {
          <label class="check">
            <input
              type="checkbox"
              [name]="'plan-' + p.code"
              [checked]="draft.planCodes.includes(p.code)"
              (change)="togglePlan(p.code)"
            />
            {{ p.name }}
          </label>
        }
        <p class="hint">Aucune case cochée : le code vaut pour toutes les formules payantes.</p>
      </fieldset>
      <div>
        <button type="submit" class="btn btn-primary btn-sm" [disabled]="busy()">
          Créer le code
        </button>
      </div>
    </form>

    <section class="admin-card" aria-labelledby="codes-title">
      <h2 id="codes-title">Codes existants</h2>
      @if ((codes.value() ?? []).length > 0) {
        <div class="scroll">
          <table>
            <thead>
              <tr>
                <th scope="col">Code</th>
                <th scope="col">Remise</th>
                <th scope="col">Validité</th>
                <th scope="col">Utilisations</th>
                <th scope="col">Formules</th>
                <th scope="col"><span class="visually-hidden">Actions</span></th>
              </tr>
            </thead>
            <tbody>
              @for (c of codes.value() ?? []; track c.id) {
                <tr>
                  <td>
                    <strong>{{ c.code }}</strong>
                  </td>
                  <td>
                    {{ c.percent }} % sur {{ c.periods }}
                    {{ c.periods > 1 ? 'factures' : 'facture' }}
                  </td>
                  <td>{{ c.validUntil ? 'jusqu’au ' + day(c.validUntil) : 'sans limite' }}</td>
                  <td>{{ c.usedCount }} / {{ c.maxUses ?? '∞' }}</td>
                  <td>{{ c.planCodes.length > 0 ? c.planCodes.join(', ') : 'toutes' }}</td>
                  <td>
                    <button
                      type="button"
                      class="btn btn-ghost btn-sm"
                      [disabled]="busy()"
                      (click)="toggle(c)"
                    >
                      {{ c.isActive ? 'Désactiver' : 'Réactiver' }}
                    </button>
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      } @else {
        <p>Aucun code promo pour le moment.</p>
      }
    </section>
  `,
})
export class PromosTab {
  private readonly api = inject(AdminApiService);

  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly message = signal<string | null>(null);
  protected draft: Draft = { ...EMPTY, planCodes: [] };

  protected readonly codes = resource({ loader: () => this.api.promoCodes() });
  protected readonly paidPlans = resource({
    loader: async () =>
      (await this.api.plans()).filter((p) => p.monthlyPrice > 0 || p.annualPrice > 0),
  });

  protected day(iso: string): string {
    return new Date(iso).toLocaleDateString('fr-FR');
  }

  protected togglePlan(code: string): void {
    const chosen = this.draft.planCodes;
    this.draft = {
      ...this.draft,
      planCodes: chosen.includes(code) ? chosen.filter((c) => c !== code) : [...chosen, code],
    };
  }

  protected async create(): Promise<void> {
    await this.run('Code promo créé.', async () => {
      await this.api.createPromoCode({
        code: this.draft.code,
        percent: Number(this.draft.percent),
        periods: Number(this.draft.periods),
        // The code is valid through the whole last day.
        validUntil: this.draft.validUntil ? `${this.draft.validUntil}T23:59:59Z` : null,
        maxUses: this.draft.maxUses ? Number(this.draft.maxUses) : null,
        planCodes: this.draft.planCodes,
        isActive: true,
      });
      this.draft = { ...EMPTY, planCodes: [] };
    });
  }

  protected async toggle(code: PromoCode): Promise<void> {
    const input: PromoCodeInput = {
      code: code.code,
      percent: code.percent,
      periods: code.periods,
      validFrom: code.validFrom,
      validUntil: code.validUntil,
      maxUses: code.maxUses,
      planCodes: code.planCodes,
      isActive: !code.isActive,
    };
    await this.run(code.isActive ? 'Code désactivé.' : 'Code réactivé.', () =>
      this.api.updatePromoCode(code.id, input).then(() => undefined),
    );
  }

  private async run(done: string, action: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    this.message.set(null);
    try {
      await action();
      this.message.set(done);
      this.codes.reload();
    } catch (e) {
      this.error.set(problemMessage(e));
    } finally {
      this.busy.set(false);
    }
  }
}
