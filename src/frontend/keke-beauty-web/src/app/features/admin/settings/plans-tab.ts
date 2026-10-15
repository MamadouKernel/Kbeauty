import {
  ChangeDetectionStrategy,
  Component,
  inject,
  linkedSignal,
  resource,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AdminApiService, type Plan, type PlanUpdate } from '../../../core/admin/admin-api.service';
import { problemMessage } from '../../../core/auth/auth.models';
import { commitmentLabel, planConditions } from '../../../core/billing/plan-conditions';

/** A plan being edited: either an existing one or a new one that has not been created yet (empty id). */
type Draft = Plan;

const NEW_PLAN: Omit<Draft, 'id' | 'code'> = {
  name: '',
  monthlyPrice: 5000,
  annualPrice: 50000,
  currencyCode: 'XOF',
  features: {
    booking: true,
    videos: false,
    maxMedia: 15,
    featured: false,
    stats: false,
    maxVideos: 0,
    maxResources: null,
    maxAppointmentsPerMonth: null,
  },
  isActive: true,
  sortOrder: 0,
  description: '',
  badge: null,
  commitmentMonths: 12,
  trialDays: 0,
  promoPercent: 0,
  promoEndsAt: null,
  promoPeriods: 1,
  promoActive: false,
};

/** A numeric field left empty means "no limit". */
function limit(value: unknown): number | null {
  return value === null || value === undefined || value === '' ? null : Number(value);
}

/**
 * Plans (PRD ADM-05, BIL-01, BIL-03): the admin creates as many plans as needed, each with its own prices, commitment and
 * conditions. Running subscriptions keep their price and their commitment; the limits apply to what the establishment does next.
 */
@Component({
  selector: 'app-plans-tab',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  styles: `
    .toolbar {
      display: flex;
      flex-wrap: wrap;
      gap: 0.75rem;
      align-items: center;
    }
    form {
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
    ul.preview {
      margin: 0;
      padding-left: 1.125rem;
      font-size: var(--fs-sm);
    }
    ul.preview li[data-off='true'] {
      color: var(--kb-ink-muted);
      text-decoration: line-through;
    }
  `,
  template: `
    <div class="toolbar">
      <button type="button" class="btn btn-primary" [disabled]="busy()" (click)="addNew()">
        Nouvelle formule
      </button>
      <span class="hint"
        >Les abonnements en cours gardent leur prix et leur durée d'engagement.</span
      >
    </div>
    @if (message(); as text) {
      <p role="status" class="badge" data-tone="ok">{{ text }}</p>
    }
    @if (error(); as text) {
      <p class="form-error" role="alert">{{ text }}</p>
    }
    <div class="admin-cards">
      @for (p of drafts(); track $index; let i = $index) {
        <form
          class="admin-card"
          (ngSubmit)="save(p)"
          [attr.aria-label]="'Formule ' + (p.name || 'nouvelle')"
        >
          <h2>{{ p.id ? p.code : 'Nouvelle formule' }}</h2>
          <label>
            Nom
            <input class="admin-input" [name]="'name' + i" [(ngModel)]="p.name" required />
          </label>
          <label>
            Étiquette (facultatif, ex. Populaire)
            <input class="admin-input" [name]="'badge' + i" maxlength="30" [(ngModel)]="p.badge" />
          </label>
          <label>
            Description courte
            <textarea
              class="admin-input"
              rows="2"
              maxlength="300"
              [name]="'desc' + i"
              [(ngModel)]="p.description"
            ></textarea>
          </label>

          <fieldset>
            <legend>Prix et engagement</legend>
            <label>
              Prix mensuel ({{ p.currencyCode }})
              <input
                class="admin-input"
                type="number"
                min="0"
                step="5"
                [name]="'m' + i"
                [(ngModel)]="p.monthlyPrice"
              />
            </label>
            <label>
              Prix annuel ({{ p.currencyCode }})
              <input
                class="admin-input"
                type="number"
                min="0"
                step="5"
                [name]="'a' + i"
                [(ngModel)]="p.annualPrice"
              />
            </label>
            <label>
              Engagement (mois)
              <input
                class="admin-input"
                type="number"
                min="1"
                max="36"
                [name]="'c' + i"
                [(ngModel)]="p.commitmentMonths"
              />
            </label>
            <label>
              Essai gratuit (jours)
              <input
                class="admin-input"
                type="number"
                min="0"
                max="90"
                [name]="'t' + i"
                [(ngModel)]="p.trialDays"
              />
            </label>
            <label>
              Promotion (%)
              <input
                class="admin-input"
                type="number"
                min="0"
                max="90"
                [name]="'pp' + i"
                [(ngModel)]="p.promoPercent"
              />
            </label>
            <label>
              Promotion jusqu'au
              <input
                class="admin-input"
                type="date"
                [name]="'pe' + i"
                [ngModel]="dateOf(p.promoEndsAt)"
                (ngModelChange)="p.promoEndsAt = endOf($event)"
              />
            </label>
            <label>
              Factures remisées
              <input
                class="admin-input"
                type="number"
                min="1"
                max="36"
                [name]="'pn' + i"
                [(ngModel)]="p.promoPeriods"
              />
            </label>
            <p class="hint">
              0 = ce rythme de paiement n'est pas proposé. Essai gratuit : 0 = pas d'essai ; offert
              une seule fois par établissement.
            </p>
          </fieldset>

          <fieldset>
            <legend>Limites</legend>
            <label>
              Photos
              <input
                class="admin-input"
                type="number"
                min="0"
                max="500"
                [name]="'x' + i"
                [(ngModel)]="p.features.maxMedia"
              />
            </label>
            <label>
              Vidéos
              <input
                class="admin-input"
                type="number"
                min="0"
                max="50"
                [name]="'v' + i"
                [(ngModel)]="p.features.maxVideos"
              />
            </label>
            <label>
              Employés / ressources
              <input
                class="admin-input"
                type="number"
                min="1"
                placeholder="illimité"
                [name]="'r' + i"
                [(ngModel)]="p.features.maxResources"
              />
            </label>
            <label>
              Rendez-vous par mois
              <input
                class="admin-input"
                type="number"
                min="1"
                placeholder="illimité"
                [name]="'q' + i"
                [(ngModel)]="p.features.maxAppointmentsPerMonth"
              />
            </label>
          </fieldset>

          <fieldset>
            <legend>Options</legend>
            <label class="check"
              ><input type="checkbox" [name]="'b' + i" [(ngModel)]="p.features.booking" />
              Réservation en ligne</label
            >
            <label class="check"
              ><input type="checkbox" [name]="'s' + i" [(ngModel)]="p.features.stats" />
              Statistiques</label
            >
            <label class="check"
              ><input type="checkbox" [name]="'f' + i" [(ngModel)]="p.features.featured" /> Mise en
              avant</label
            >
            <label class="check"
              ><input type="checkbox" [name]="'vd' + i" [(ngModel)]="p.features.videos" />
              Vidéos</label
            >
            <label class="check"
              ><input type="checkbox" [name]="'act' + i" [(ngModel)]="p.isActive" /> Proposée à la
              vente</label
            >
          </fieldset>
          <label>
            Ordre d'affichage
            <input
              class="admin-input"
              type="number"
              min="0"
              [name]="'o' + i"
              [(ngModel)]="p.sortOrder"
            />
          </label>

          <div>
            <strong>Aperçu pour les professionnels</strong>
            <ul class="preview">
              @for (c of conditions(p); track c.label) {
                <li [attr.data-off]="!c.on">{{ c.label }}</li>
              }
              <li>{{ commitment(p.commitmentMonths) }}</li>
              @if (p.trialDays > 0) {
                <li>Essai gratuit de {{ p.trialDays }} jours</li>
              }
            </ul>
          </div>

          <div class="toolbar">
            <button type="submit" class="btn btn-primary btn-sm" [disabled]="busy()">
              {{ p.id ? 'Enregistrer' : 'Créer la formule' }}
            </button>
            <button
              type="button"
              class="btn btn-ghost btn-sm"
              [disabled]="busy()"
              (click)="duplicate(p)"
            >
              Dupliquer
            </button>
            @if (!p.id) {
              <button type="button" class="btn btn-ghost btn-sm" (click)="discard(i)">
                Abandonner
              </button>
            }
          </div>
        </form>
      }
    </div>
  `,
})
export class PlansTab {
  private readonly api = inject(AdminApiService);

  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly message = signal<string | null>(null);
  protected readonly conditions = planConditions;
  protected readonly commitment = commitmentLabel;

  protected readonly plans = resource({ loader: () => this.api.plans() });
  protected readonly drafts = linkedSignal<Plan[] | undefined, Draft[]>({
    source: () => this.plans.value(),
    computation: (loaded) => (loaded ?? []).map(copy),
  });

  protected dateOf(iso: string | null | undefined): string {
    return iso ? iso.slice(0, 10) : '';
  }

  /** The promotion lasts through the whole last day. */
  protected endOf(value: string): string | null {
    return value ? `${value}T23:59:59Z` : null;
  }

  protected addNew(): void {
    const next = Math.max(0, ...this.drafts().map((p) => p.sortOrder)) + 1;
    this.drafts.update((list) => [
      ...list,
      { ...copy({ ...NEW_PLAN, id: '', code: '' }), sortOrder: next },
    ]);
  }

  protected duplicate(source: Draft): void {
    this.drafts.update((list) => [
      ...list,
      {
        ...copy(source),
        id: '',
        code: '',
        name: `${source.name} (copie)`,
        sortOrder: Math.max(0, ...list.map((p) => p.sortOrder)) + 1,
      },
    ]);
  }

  protected discard(index: number): void {
    this.drafts.update((list) => list.filter((_, i) => i !== index));
  }

  protected async save(plan: Draft): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    this.message.set(null);
    const body: PlanUpdate = {
      name: plan.name,
      monthlyPrice: Number(plan.monthlyPrice),
      annualPrice: Number(plan.annualPrice),
      features: {
        ...plan.features,
        maxMedia: Number(plan.features.maxMedia),
        maxVideos: Number(plan.features.maxVideos),
        maxResources: limit(plan.features.maxResources),
        maxAppointmentsPerMonth: limit(plan.features.maxAppointmentsPerMonth),
      },
      isActive: plan.isActive,
      description: plan.description,
      badge: plan.badge?.trim() ? plan.badge.trim() : null,
      commitmentMonths: Number(plan.commitmentMonths),
      trialDays: Number(plan.trialDays),
      promoPercent: Number(plan.promoPercent),
      promoEndsAt: plan.promoEndsAt ?? null,
      promoPeriods: Number(plan.promoPeriods),
      sortOrder: Number(plan.sortOrder),
    };
    try {
      if (plan.id) {
        await this.api.updatePlan(plan.id, body);
        this.message.set('Formule enregistrée. Les abonnements en cours gardent leur prix.');
      } else {
        await this.api.createPlan(body);
        this.message.set('Formule créée.');
      }
      this.plans.reload();
    } catch (e) {
      this.error.set(problemMessage(e));
    } finally {
      this.busy.set(false);
    }
  }
}

function copy(plan: Plan): Draft {
  return { ...plan, features: { ...plan.features } };
}
