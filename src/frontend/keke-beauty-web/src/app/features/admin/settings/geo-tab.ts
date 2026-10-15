import {
  ChangeDetectionStrategy,
  Component,
  inject,
  input,
  model,
  resource,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AdminApiService, type GeoLevel } from '../../../core/admin/admin-api.service';
import { problemMessage } from '../../../core/auth/auth.models';

/** One level of the zone hierarchy: list, select, switch on or off, add (PRD ADM-05). */
@Component({
  selector: 'app-geo-column',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  styles: `
    ul {
      display: grid;
      gap: 0.25rem;
      margin: 0;
      padding: 0;
      list-style: none;
    }
    li {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 0.5rem;
    }
    .pick {
      flex: 1;
      text-align: left;
      border: 0;
      background: none;
      font: inherit;
      cursor: pointer;
      padding: 0.375rem 0.5rem;
      border-radius: var(--radius-sm, 0.5rem);

      &[aria-pressed='true'] {
        background: var(--kb-primary-50);
        font-weight: 700;
      }
    }
    .off {
      color: var(--kb-ink-muted);
      text-decoration: line-through;
    }
  `,
  template: `
    <section class="admin-card" [attr.aria-label]="title()">
      <h3>{{ title() }}</h3>
      @if (!ready()) {
        <p class="auth-lead">Sélectionnez d'abord l'élément parent.</p>
      } @else {
        @if (error(); as text) {
          <p class="form-error" role="alert">{{ text }}</p>
        }
        <ul>
          @for (item of items.value() ?? []; track item.id) {
            <li>
              <button
                type="button"
                class="pick"
                [class.off]="!item.isActive"
                [attr.aria-pressed]="selected() === item.id"
                (click)="selected.set(item.id)"
              >
                {{ item.name }}{{ item.code ? ' (' + item.code + ')' : '' }}
              </button>
              <button
                type="button"
                class="btn btn-ghost btn-sm"
                [disabled]="busy()"
                (click)="toggle(item.id, item.name, item.isActive)"
              >
                {{ item.isActive ? 'Désactiver' : 'Activer' }}
              </button>
            </li>
          } @empty {
            <li>Aucune zone.</li>
          }
        </ul>
        <form (ngSubmit)="add()" class="admin-filters">
          <label>
            Ajouter
            <input class="admin-input" name="name" [(ngModel)]="name" placeholder="Nom" required />
          </label>
          @if (level() === 'countries') {
            <label>
              Code ISO
              <input
                class="admin-input"
                name="iso"
                [(ngModel)]="iso2"
                maxlength="2"
                placeholder="CI"
                required
              />
            </label>
            <label>
              Indicatif
              <input
                class="admin-input"
                name="prefix"
                [(ngModel)]="prefix"
                placeholder="+225"
                required
              />
            </label>
            <label>
              Devise
              <input
                class="admin-input"
                name="currency"
                [(ngModel)]="currency"
                maxlength="3"
                placeholder="XOF"
                required
              />
            </label>
          }
          <button type="submit" class="btn btn-primary btn-sm" [disabled]="busy()">Ajouter</button>
        </form>
      }
    </section>
  `,
})
export class GeoColumn {
  private readonly api = inject(AdminApiService);

  readonly title = input.required<string>();
  readonly level = input.required<GeoLevel>();
  readonly parentId = input<string | null>(null);
  readonly selected = model<string | null>(null);

  protected name = '';
  protected iso2 = '';
  protected prefix = '';
  protected currency = '';
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly items = resource({
    params: () => ({ level: this.level(), parent: this.parentId() }),
    loader: ({ params }) =>
      params.level === 'countries' || params.parent
        ? this.api.geo(params.level, params.parent ?? undefined)
        : Promise.resolve([]),
  });

  protected ready(): boolean {
    return this.level() === 'countries' || this.parentId() !== null;
  }

  protected async add(): Promise<void> {
    await this.run(async () => {
      await this.api.createGeo(this.level(), {
        parentId: this.parentId(),
        name: this.name,
        iso2: this.iso2,
        phonePrefix: this.prefix,
        currencyCode: this.currency,
      });
      this.name = this.iso2 = this.prefix = this.currency = '';
    });
  }

  protected async toggle(id: string, name: string, isActive: boolean): Promise<void> {
    await this.run(() => this.api.updateGeo(this.level(), id, name, !isActive));
  }

  private async run(action: () => Promise<unknown>): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await action();
      this.items.reload();
    } catch (e) {
      this.error.set(problemMessage(e));
    } finally {
      this.busy.set(false);
    }
  }
}

/** Countries → regions → cities → communes. */
@Component({
  selector: 'app-geo-tab',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [GeoColumn],
  styles: `
    .columns {
      display: grid;
      gap: 1rem;
      grid-template-columns: repeat(auto-fit, minmax(min(100%, 16rem), 1fr));
    }
  `,
  template: `
    <div class="columns">
      <app-geo-column
        title="Pays"
        level="countries"
        [(selected)]="country"
        (selectedChange)="resetBelow(1)"
      />
      <app-geo-column
        title="Régions"
        level="regions"
        [parentId]="country()"
        [(selected)]="region"
        (selectedChange)="resetBelow(2)"
      />
      <app-geo-column
        title="Villes"
        level="cities"
        [parentId]="region()"
        [(selected)]="city"
        (selectedChange)="resetBelow(3)"
      />
      <app-geo-column
        title="Communes"
        level="communes"
        [parentId]="city()"
        [(selected)]="commune"
      />
    </div>
  `,
})
export class GeoTab {
  protected readonly country = signal<string | null>(null);
  protected readonly region = signal<string | null>(null);
  protected readonly city = signal<string | null>(null);
  protected readonly commune = signal<string | null>(null);

  /** Choosing an element clears the selections of the levels below it. */
  protected resetBelow(level: 1 | 2 | 3): void {
    if (level <= 1) {
      this.region.set(null);
    }

    if (level <= 2) {
      this.city.set(null);
    }

    this.commune.set(null);
  }
}
