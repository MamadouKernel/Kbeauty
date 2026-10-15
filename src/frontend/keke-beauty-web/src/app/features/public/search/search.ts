import { httpResource } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  linkedSignal,
  signal,
  untracked,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { LucideCrosshair, LucideList, LucideMap, LucideSearch, LucideX } from '@lucide/angular';
import { InputText } from 'primeng/inputtext';
import { Select } from 'primeng/select';
import type { components } from '../../../core/api/api.generated';
import { CatalogService } from '../../../core/catalog/catalog.service';
import {
  EstablishmentService,
  EstablishmentSummary,
  SearchCriteria,
} from '../../../core/establishments/establishment.service';
import { GeolocationFailure, GeolocationService } from '../../../core/geo/geolocation.service';
import { EstablishmentCard } from '../../../shared/establishment-card/establishment-card';
import { MapExplorer } from '../../../shared/map-explorer/map-explorer';

type GeoItem = components['schemas']['GeoItemDto'];
type CountryItem = components['schemas']['CountryDto'];
type Status = 'loading' | 'ready' | 'error';

const LIST_PAGE_SIZE = 12;
const MAP_PAGE_SIZE = 50;
const DEFAULT_RADIUS_KM = 5;
const RADII = [1, 3, 5, 10, 25].map((km) => ({ km, label: `${km} km` }));
const SORT_TO_API: Record<string, string> = {
  distance: 'distance',
  nom: 'name',
  recent: 'recent',
  note: 'rating',
};

/** Directory search. Every filter lives in the URL (shareable, back-button friendly). */
@Component({
  selector: 'app-search',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    FormsModule,
    InputText,
    Select,
    EstablishmentCard,
    MapExplorer,
    LucideCrosshair,
    LucideList,
    LucideMap,
    LucideSearch,
    LucideX,
  ],
  styles: `
    .page {
      display: grid;
      gap: 1.5rem;
      padding-block: clamp(1.5rem, 4vw, 3rem);
    }
    .filters {
      display: grid;
      gap: 1rem;
      padding: clamp(1rem, 2.5vw, 1.5rem);
      border: 1px solid var(--kb-border);
      border-radius: var(--radius-lg);
      background: linear-gradient(160deg, #fff, var(--kb-primary-50));
      box-shadow: var(--shadow-luxe);
    }
    .filters[hidden] {
      display: none;
    }
    .row {
      display: grid;
      gap: 0.875rem;

      @media (min-width: 720px) {
        grid-template-columns: repeat(2, 1fr);
      }
      @media (min-width: 1100px) {
        grid-template-columns: repeat(4, 1fr);
      }
    }
    .field {
      display: grid;
      gap: 0.25rem;
    }
    .field label,
    legend {
      padding: 0;
      font-size: 0.75rem;
      font-weight: 600;
      letter-spacing: 0.06em;
      text-transform: uppercase;
      color: var(--kb-ink-muted);
    }
    .field p-select,
    .field input {
      width: 100%;
      min-height: 3rem;
    }
    .near {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 0.75rem;
    }
    .near .hint {
      font-size: 0.875rem;
      color: var(--kb-ink-muted);
    }
    .check {
      display: inline-flex;
      align-items: center;
      gap: 0.5rem;
      min-height: 2.75rem;
      font-weight: 500;
      cursor: pointer;
    }
    .check input {
      width: 1.25rem;
      height: 1.25rem;
      accent-color: var(--kb-primary-600);
    }
    .toolbar {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: space-between;
      gap: 0.75rem;
    }
    .count {
      font-weight: 700;
      color: var(--kb-primary-800);
    }
    .views {
      display: inline-flex;
      padding: 0.25rem;
      border: 1px solid var(--kb-border);
      border-radius: var(--radius-pill);
      background: #fff;
    }
    .views button {
      display: inline-flex;
      align-items: center;
      gap: 0.375rem;
      min-height: 2.5rem;
      padding: 0.25rem 1rem;
      border: 0;
      border-radius: var(--radius-pill);
      background: transparent;
      color: var(--kb-ink-muted);
      font: inherit;
      font-weight: 600;
      cursor: pointer;
    }
    .views button[aria-pressed='true'] {
      background: var(--kb-primary-600);
      color: #fff;
    }
    .views svg {
      width: 1.125rem;
      height: 1.125rem;
    }
    .grid {
      display: grid;
      gap: 1.25rem;
      grid-template-columns: repeat(auto-fill, minmax(min(100%, 17rem), 1fr));
    }
    .skeleton {
      aspect-ratio: 4 / 5;
      border-radius: var(--radius-lg);
      background: linear-gradient(90deg, var(--kb-primary-50), #fff, var(--kb-primary-50)) 0 0 /
        200% 100%;
      animation: shimmer 1.4s linear infinite;
    }
    @keyframes shimmer {
      to {
        background-position: -200% 0;
      }
    }
    .state {
      display: grid;
      justify-items: start;
      gap: 0.75rem;
      padding: 1.5rem;
      border: 1px dashed var(--kb-primary-300);
      border-radius: var(--radius-lg);
      background: #fff;
    }
    .more {
      justify-self: center;
    }
  `,
  template: `
    <div class="container page">
      @if (showFilters()) {
        <header>
          <p class="eyebrow">Annuaire</p>
          <h1>Trouvez votre <em>établissement</em></h1>
        </header>
      } @else {
        <h1 class="visually-hidden">Établissements sur la carte</h1>
      }

      <form
        class="filters"
        (ngSubmit)="submitText()"
        aria-label="Filtres de recherche"
        [hidden]="!showFilters()"
      >
        <div class="row">
          <div class="field">
            <label for="f-category">Catégorie</label>
            <p-select
              inputId="f-category"
              name="category"
              [options]="categoryOptions()"
              optionLabel="name"
              optionValue="slug"
              placeholder="Toutes les catégories"
              [showClear]="true"
              [ngModel]="filters().category ?? null"
              (ngModelChange)="update({ categorie: $event || null })"
            />
          </div>

          <div class="field">
            <label for="f-region">Région</label>
            <p-select
              inputId="f-region"
              name="region"
              [options]="regions.hasValue() ? regions.value() : []"
              optionLabel="name"
              optionValue="id"
              placeholder="Toutes les régions"
              [showClear]="true"
              [ngModel]="filters().region"
              (ngModelChange)="update({ region: $event || null, ville: null, commune: null })"
            />
          </div>

          <div class="field">
            <label for="f-city">Ville</label>
            <p-select
              inputId="f-city"
              name="city"
              [options]="cities.hasValue() ? cities.value() : []"
              optionLabel="name"
              optionValue="id"
              placeholder="Toutes les villes"
              [showClear]="true"
              [disabled]="!filters().region"
              [ngModel]="filters().city"
              (ngModelChange)="update({ ville: $event || null, commune: null })"
            />
          </div>

          <div class="field">
            <label for="f-commune">Commune</label>
            <p-select
              inputId="f-commune"
              name="commune"
              [options]="communes.hasValue() ? communes.value() : []"
              optionLabel="name"
              optionValue="id"
              placeholder="Toutes les communes"
              [showClear]="true"
              [disabled]="!filters().city"
              [ngModel]="filters().commune"
              (ngModelChange)="update({ commune: $event || null })"
            />
          </div>
        </div>

        <div class="row">
          <div class="field">
            <label for="f-text">Nom ou prestation</label>
            <input
              pInputText
              id="f-text"
              name="q"
              type="search"
              autocomplete="off"
              placeholder="Tresses, manucure, massage…"
              [ngModel]="text()"
              (ngModelChange)="text.set($event)"
            />
          </div>

          <div class="field">
            <label for="f-sort">Trier par</label>
            <p-select
              inputId="f-sort"
              name="sort"
              [options]="sortOptions()"
              optionLabel="label"
              optionValue="value"
              [ngModel]="filters().sort ?? ''"
              (ngModelChange)="update({ tri: $event || null })"
            />
          </div>

          <div class="field">
            <label for="f-radius">Rayon</label>
            <p-select
              inputId="f-radius"
              ariaLabel="Rayon de recherche"
              name="radius"
              [options]="radii"
              optionLabel="label"
              optionValue="km"
              [disabled]="!hasPosition()"
              [ngModel]="filters().radius ?? null"
              (ngModelChange)="update({ rayon: $event })"
            />
          </div>

          <div class="field">
            <span class="visually-hidden">Actions</span>
            <button type="submit" class="btn btn-primary">
              <svg lucideSearch aria-hidden="true"></svg>
              Rechercher
            </button>
          </div>
        </div>

        <div class="near">
          @if (hasPosition()) {
            <button type="button" class="btn btn-ghost btn-sm" (click)="clearLocation()">
              <svg lucideX aria-hidden="true"></svg>
              Ne plus utiliser ma position
            </button>
          } @else {
            <button
              type="button"
              class="btn btn-ghost btn-sm"
              [disabled]="locating()"
              (click)="locate()"
            >
              <svg lucideCrosshair aria-hidden="true"></svg>
              {{ locating() ? 'Localisation…' : 'Autour de moi' }}
            </button>
          }
          <label class="check">
            <input
              type="checkbox"
              [checked]="filters().bookable"
              (change)="update({ reservation: filters().bookable ? null : '1' })"
            />
            Réservation en ligne disponible
          </label>
          @if (hasFilters()) {
            <button type="button" class="btn btn-ghost btn-sm" (click)="reset()">
              Réinitialiser
            </button>
          }
          <p class="hint" role="status">{{ locationError() }}</p>
        </div>
      </form>

      <h2 class="visually-hidden">Résultats</h2>
      <div class="toolbar">
        <p class="count" role="status" aria-live="polite">{{ summary() }}</p>
        @if (filters().view === 'map') {
          <button
            type="button"
            class="btn btn-ghost btn-sm"
            [attr.aria-expanded]="filtersOpen()"
            (click)="filtersOpen.set(!filtersOpen())"
          >
            {{ filtersOpen() ? 'Masquer les filtres' : 'Filtres' }}
          </button>
        }
        <div class="views" role="group" aria-label="Mode d'affichage">
          <button
            type="button"
            [attr.aria-pressed]="filters().view === 'list'"
            (click)="update({ vue: null })"
          >
            <svg lucideList aria-hidden="true"></svg>Liste
          </button>
          <button
            type="button"
            [attr.aria-pressed]="filters().view === 'map'"
            (click)="update({ vue: 'carte' })"
          >
            <svg lucideMap aria-hidden="true"></svg>Carte
          </button>
        </div>
      </div>

      @switch (status()) {
        @case ('loading') {
          <div class="grid" aria-busy="true" aria-label="Chargement des établissements">
            @for (n of placeholders; track n) {
              <div class="skeleton"></div>
            }
          </div>
        }
        @case ('error') {
          <div class="state" role="alert">
            <p>Impossible de charger les établissements pour le moment.</p>
            <button type="button" class="btn btn-ghost" (click)="retry()">Réessayer</button>
          </div>
        }
        @default {
          @if (items().length === 0) {
            <div class="state">
              <h2>Aucun établissement trouvé</h2>
              <p>Essayez d'élargir la zone, de retirer un filtre ou de changer de mot-clé.</p>
              <button type="button" class="btn btn-primary" (click)="reset()">
                Réinitialiser les filtres
              </button>
            </div>
          } @else if (filters().view === 'map') {
            <app-map-explorer [items]="items()" />
          } @else {
            <div class="grid">
              @for (item of items(); track item.id) {
                <app-establishment-card [establishment]="item" />
              }
            </div>
            @if (items().length < total()) {
              <button
                type="button"
                class="btn btn-ghost more"
                [disabled]="loadingMore()"
                (click)="loadMore()"
              >
                {{ loadingMore() ? 'Chargement…' : 'Voir plus' }}
              </button>
            }
          }
        }
      }
    </div>
  `,
})
export class Search {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly establishments = inject(EstablishmentService);
  private readonly geolocation = inject(GeolocationService);
  private readonly catalog = inject(CatalogService);

  private readonly params = toSignal(this.route.queryParamMap, {
    initialValue: this.route.snapshot.queryParamMap,
  });

  protected readonly radii = RADII;
  protected readonly placeholders = [1, 2, 3, 4, 5, 6, 7, 8];

  protected readonly filters = computed(() => {
    const p = this.params();
    const number = (key: string): number | undefined => {
      const value = Number(p.get(key));
      return p.get(key) !== null && Number.isFinite(value) ? value : undefined;
    };
    return {
      category: p.get('categorie') ?? undefined,
      region: p.get('region'),
      city: p.get('ville'),
      commune: p.get('commune'),
      q: p.get('q') ?? '',
      lat: number('lat'),
      lng: number('lng'),
      radius: number('rayon'),
      bookable: p.get('reservation') === '1',
      sort: p.get('tri') ?? undefined,
      view: p.get('vue') === 'carte' ? ('map' as const) : ('list' as const),
    };
  });

  protected readonly hasPosition = computed(
    () => this.filters().lat !== undefined && this.filters().lng !== undefined,
  );
  protected readonly categoryOptions = computed(() =>
    this.catalog.categories.hasValue() ? this.catalog.categories.value() : [],
  );
  protected readonly sortOptions = computed(() => [
    { label: this.hasPosition() ? 'Pertinence et distance' : 'Pertinence', value: '' },
    ...(this.hasPosition() ? [{ label: 'Distance', value: 'distance' }] : []),
    { label: 'Mieux notés', value: 'note' },
    { label: 'Nom (A → Z)', value: 'nom' },
    { label: 'Plus récents', value: 'recent' },
  ]);
  protected readonly hasFilters = computed(() => this.params().keys.length > 0);

  /** On the map the screen belongs to the map: the filters fold away behind a "Filtres" button. */
  protected readonly filtersOpen = signal(false);
  protected readonly showFilters = computed(
    () => this.filters().view !== 'map' || this.filtersOpen(),
  );

  protected readonly text = linkedSignal(() => this.filters().q);

  // Region > City > Commune cascade (PRD SRCH-02): each list loads only once its parent is chosen.
  private readonly countries = httpResource<CountryItem[]>(() => '/api/v1/geo/countries');
  private readonly countryId = computed(() =>
    this.countries.hasValue() ? this.countries.value()[0]?.id : undefined,
  );
  protected readonly regions = httpResource<GeoItem[]>(() =>
    this.countryId() ? `/api/v1/geo/countries/${this.countryId()}/regions` : undefined,
  );
  protected readonly cities = httpResource<GeoItem[]>(() =>
    this.filters().region ? `/api/v1/geo/regions/${this.filters().region}/cities` : undefined,
  );
  protected readonly communes = httpResource<GeoItem[]>(() =>
    this.filters().city ? `/api/v1/geo/cities/${this.filters().city}/communes` : undefined,
  );

  protected readonly items = signal<readonly EstablishmentSummary[]>([]);
  protected readonly total = signal(0);
  protected readonly status = signal<Status>('loading');
  protected readonly loadingMore = signal(false);
  protected readonly locating = signal(false);
  protected readonly locationError = signal('');
  protected readonly summary = computed(() => {
    if (this.status() === 'loading') {
      return 'Recherche en cours…';
    }
    const total = this.total();
    return total === 0 ? 'Aucun résultat' : `${total} établissement${total > 1 ? 's' : ''}`;
  });

  private page = 1;
  private requestId = 0;

  constructor() {
    effect(() => {
      const current = this.filters();
      untracked(() => void this.reload(current));
    });
  }

  protected update(patch: Record<string, string | number | null>): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: patch,
      queryParamsHandling: 'merge',
    });
  }

  protected submitText(): void {
    this.update({ q: this.text().trim() || null });
  }

  protected reset(): void {
    void this.router.navigate([], { relativeTo: this.route, queryParams: {} });
  }

  protected clearLocation(): void {
    this.locationError.set('');
    this.update({
      lat: null,
      lng: null,
      rayon: null,
      tri: this.filters().sort === 'distance' ? null : (this.filters().sort ?? null),
    });
  }

  protected async locate(): Promise<void> {
    this.locating.set(true);
    this.locationError.set('');
    try {
      const { lat, lng } = await this.geolocation.locate();
      this.update({ lat, lng, rayon: this.filters().radius ?? DEFAULT_RADIUS_KM });
    } catch (error) {
      this.locationError.set(
        error instanceof GeolocationFailure ? error.userMessage : 'Position indisponible.',
      );
    } finally {
      this.locating.set(false);
    }
  }

  protected open(slug: string): void {
    void this.router.navigate(['/etablissements', slug]);
  }

  protected retry(): void {
    void this.reload(this.filters());
  }

  protected async loadMore(): Promise<void> {
    const id = this.requestId;
    this.loadingMore.set(true);
    try {
      const next = await this.establishments.search(this.criteria(this.filters(), this.page + 1));
      if (id !== this.requestId) {
        return;
      }
      this.page += 1;
      this.items.update((current) => [...current, ...next.items]);
      this.total.set(next.total);
    } catch {
      this.status.set('error');
    } finally {
      this.loadingMore.set(false);
    }
  }

  private async reload(filters: ReturnType<typeof this.filters>): Promise<void> {
    const id = ++this.requestId;
    this.status.set('loading');
    this.page = 1;
    try {
      const result = await this.establishments.search(this.criteria(filters, 1));
      if (id !== this.requestId) {
        return; // a newer search superseded this one
      }
      this.items.set(result.items);
      this.total.set(result.total);
      this.status.set('ready');
    } catch {
      if (id === this.requestId) {
        this.status.set('error');
      }
    }
  }

  private criteria(filters: ReturnType<typeof this.filters>, page: number): SearchCriteria {
    return {
      category: filters.category,
      cityId: filters.city ?? undefined,
      communeId: filters.commune ?? undefined,
      lat: filters.lat,
      lng: filters.lng,
      radiusKm: filters.lat === undefined ? undefined : filters.radius,
      q: filters.q.trim() || undefined,
      bookable: filters.bookable || undefined,
      sort:
        filters.sort &&
        SORT_TO_API[filters.sort] &&
        (filters.sort !== 'distance' || filters.lat !== undefined)
          ? SORT_TO_API[filters.sort]
          : undefined,
      page,
      pageSize: filters.view === 'map' ? MAP_PAGE_SIZE : LIST_PAGE_SIZE,
    };
  }
}
