import { HttpErrorResponse, httpResource } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { Meta, Title } from '@angular/platform-browser';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import {
  LucideArrowLeft,
  LucideBadgeCheck,
  LucideCalendarCheck,
  LucideClock,
  LucideMapPin,
  LucideNavigation,
  LucideShare2,
  LucidePhone,
} from '@lucide/angular';
import { Dialog } from 'primeng/dialog';
import { map } from 'rxjs';
import { DirectionsLinkService } from '../../../core/directions/directions.service';
import {
  EstablishmentDetails,
  EstablishmentEvent,
  EstablishmentService,
} from '../../../core/establishments/establishment.service';
import { WEEK_ORDER, dayName, formatDuration, formatMoney } from '../../../core/format/format';
import { MOBILE_MONEY_LABELS } from '../../../core/format/payment-methods';
import { FavoriteButton } from '../../../shared/favorite-button/favorite-button';
import { MapView, type MapPoint } from '../../../shared/map-view/map-view';
import { MediaCarousel } from '../../../shared/media-carousel/media-carousel';
import { StarRating } from '../../../shared/star-rating/star-rating';
import { ReviewsSection } from './reviews-section';

const OG_TAGS = ['og:title', 'og:description', 'og:image', 'og:type'] as const;

type Tab = 'prestations' | 'realisations' | 'avis' | 'apropos';

const TABS: readonly { readonly id: Tab; readonly label: string }[] = [
  { id: 'prestations', label: 'Prestations et tarifs' },
  { id: 'realisations', label: 'Réalisations' },
  { id: 'avis', label: 'Avis' },
  { id: 'apropos', label: 'À propos' },
];

const WEEKDAY_INDEX: Record<string, number> = {
  Sun: 0,
  Mon: 1,
  Tue: 2,
  Wed: 3,
  Thu: 4,
  Fri: 5,
  Sat: 6,
};

/** "HH:mm" as minutes since midnight. */
function minutes(time: string): number {
  const [hours, mins] = time.split(':').map(Number);
  return hours * 60 + mins;
}

/**
 * Public establishment page (PRD EST-01 to EST-06): cover, quick actions, then the services first (price, duration and a
 * "Réserver" button on each line), the gallery, the clients' reviews and the practical information in tabs.
 */
@Component({
  selector: 'app-establishment',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    RouterLink,
    Dialog,
    FavoriteButton,
    MediaCarousel,
    MapView,
    ReviewsSection,
    StarRating,
    LucideArrowLeft,
    LucideBadgeCheck,
    LucideShare2,
    LucideCalendarCheck,
    LucideClock,
    LucideMapPin,
    LucideNavigation,
    LucidePhone,
  ],
  styles: `
    .verified {
      display: inline-flex;
      align-items: center;
      gap: 0.375rem;
      margin: 0;
      font-size: var(--fs-xs);
      font-weight: 700;
      color: var(--kb-primary-700);
    }
    .verified svg {
      width: 1.125rem;
      height: 1.125rem;
      color: var(--kb-gold);
    }
    @media (max-width: 767px) {
      .btn-book {
        position: fixed;
        inset: auto 1rem calc(4.75rem + env(safe-area-inset-bottom));
        z-index: 45;
        justify-content: center;
        box-shadow: var(--shadow-luxe);
      }
    }
    .page {
      display: grid;
      gap: 1.5rem;
      padding-block: clamp(1.25rem, 3vw, 2.5rem);
    }
    .back {
      justify-self: start;
      display: inline-flex;
      align-items: center;
      gap: 0.375rem;
      font-weight: 600;
      color: var(--kb-primary-600);
      text-decoration: none;
    }
    .back svg {
      width: 1.125rem;
      height: 1.125rem;
    }
    .cover {
      position: relative;
      /* A fixed height, not an aspect ratio with a minimum: that would make the box wider than a phone. */
      height: clamp(13rem, 34vw, 24rem);
      min-width: 0;
      overflow: hidden;
      border-radius: var(--radius-lg);
      background: linear-gradient(135deg, var(--kb-primary-300), var(--kb-primary-700));
      box-shadow: var(--shadow-soft);
    }
    .cover img {
      width: 100%;
      height: 100%;
      object-fit: cover;
    }
    .cover .heart {
      position: absolute;
      top: 0.875rem;
      right: 0.875rem;
    }
    h1 {
      font-size: clamp(2rem, 1.4rem + 2.4vw, 3rem);
    }
    .meta {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 0.5rem 1rem;
      margin-top: 0.5rem;
    }
    .rating-link {
      border: 0;
      padding: 0;
      background: none;
      font: inherit;
      cursor: pointer;
    }
    .open {
      display: inline-flex;
      align-items: center;
      gap: 0.375rem;
      padding: 0.25rem 0.75rem;
      border-radius: var(--radius-pill);
      font-size: var(--fs-xs);
      font-weight: 700;
    }
    .open[data-open='true'] {
      background: color-mix(in srgb, var(--kb-success) 12%, white);
      color: var(--kb-success);
    }
    .open[data-open='false'] {
      background: var(--kb-primary-50);
      color: var(--kb-ink-muted);
    }
    .chips {
      display: flex;
      flex-wrap: wrap;
      gap: 0.5rem;
      margin-top: 0.75rem;
    }
    .chip {
      padding: 0.25rem 0.75rem;
      border-radius: var(--radius-pill);
      background: var(--kb-primary-50);
      color: var(--kb-primary-800);
      font-size: var(--fs-xs);
      font-weight: 600;
    }
    .place {
      display: flex;
      align-items: center;
      gap: 0.375rem;
      margin-top: 0.75rem;
      color: var(--kb-ink-muted);
    }
    .place-link {
      color: inherit;
      text-decoration: underline;
      text-decoration-color: var(--kb-primary-300);
      text-underline-offset: 3px;
    }
    .place-link:hover {
      color: var(--kb-primary-700);
    }
    .place svg {
      width: 1.125rem;
      height: 1.125rem;
      flex: none;
    }
    .actions {
      display: flex;
      flex-wrap: wrap;
      gap: 0.75rem;
      margin-top: 1.25rem;

      /* On a phone the actions sit two by two, so that the tabs below stay in reach without scrolling far. */
      @media (max-width: 767px) {
        display: grid;
        grid-template-columns: repeat(2, 1fr);
        gap: 0.5rem;
      }
      @media (max-width: 767px) {
        .btn:not(.btn-book) {
          justify-content: center;
          padding-inline: 0.5rem;
          white-space: normal;
          text-align: center;
        }
      }
    }
    .tabs {
      display: flex;
      gap: 0.25rem;
      padding: 0.25rem;
      overflow-x: auto;
      scrollbar-width: none;
      border: 1px solid var(--kb-border);
      border-radius: var(--radius-pill);
      background: #fff;
    }
    .tabs button {
      flex: none;
      min-height: 2.75rem;
      padding: 0 1.125rem;
      border: 0;
      border-radius: var(--radius-pill);
      background: transparent;
      color: var(--kb-ink);
      font: inherit;
      font-weight: 600;
      cursor: pointer;
    }
    .tabs button[aria-selected='true'] {
      background: var(--kb-primary-600);
      color: #fff;
    }
    .panel {
      display: grid;
      gap: 1.5rem;
    }
    .two {
      display: grid;
      gap: 1.5rem;

      @media (min-width: 960px) {
        grid-template-columns: 1.4fr 1fr;
        align-items: start;
      }
    }
    .stack {
      display: grid;
      gap: 1.5rem;
    }
    .card {
      display: grid;
      gap: 0.875rem;
      padding: clamp(1.25rem, 3vw, 1.75rem);
      border: 1px solid var(--kb-border);
      border-radius: var(--radius-lg);
      background: #fff;
      box-shadow: var(--shadow-soft);
    }
    h2 {
      font-size: 1.5rem;
    }
    h3 {
      font-family: var(--font-body);
      font-size: var(--fs-xs);
      font-weight: 700;
      letter-spacing: 0.1em;
      text-transform: uppercase;
      color: var(--kb-primary-600);
    }
    .service {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: space-between;
      gap: 0.5rem 1rem;
      padding-block: 0.75rem;
      border-top: 1px solid var(--kb-border);
    }
    .service:first-of-type {
      border-top: 0;
    }
    .service strong {
      font-weight: 600;
    }
    .service small {
      display: block;
      color: var(--kb-ink-muted);
    }
    .service .end {
      display: flex;
      align-items: center;
      gap: 0.75rem;
    }
    .price {
      font-weight: 700;
      white-space: nowrap;
    }
    table {
      width: 100%;
      border-collapse: collapse;
    }
    caption {
      display: flex;
      align-items: center;
      gap: 0.5rem;
      margin-bottom: 0.5rem;
      font-family: var(--font-display);
      font-size: 1.5rem;
      font-weight: 600;
      text-align: left;
    }
    caption svg {
      width: 1.25rem;
      height: 1.25rem;
      color: var(--kb-primary-600);
    }
    th,
    td {
      padding: 0.5rem 0;
      border-top: 1px solid var(--kb-border);
      text-align: left;
    }
    th {
      font-weight: 600;
    }
    tr.today {
      color: var(--kb-primary-700);
      font-weight: 700;
    }
    .closed {
      color: var(--kb-ink-muted);
    }
    .directions {
      display: grid;
      gap: 0.75rem;
    }
    .directions .btn {
      width: 100%;
    }
    .state {
      display: grid;
      justify-items: start;
      gap: 0.75rem;
      padding: 2rem;
      border: 1px dashed var(--kb-primary-300);
      border-radius: var(--radius-lg);
      background: #fff;
    }
  `,
  template: `
    <div class="container page">
      <a class="back" routerLink="/etablissements"
        ><svg lucideArrowLeft aria-hidden="true"></svg>Retour aux résultats</a
      >

      @if (notFound()) {
        <div class="state">
          <h1>Établissement introuvable</h1>
          <p>Cette fiche n'existe pas ou n'est plus visible.</p>
          <a class="btn btn-primary" routerLink="/etablissements">Voir tous les établissements</a>
        </div>
      } @else if (details.error()) {
        <div class="state" role="alert">
          <p>Impossible de charger cette fiche pour le moment.</p>
          <button type="button" class="btn btn-ghost" (click)="details.reload()">Réessayer</button>
        </div>
      } @else if (details.hasValue()) {
        @let d = details.value();
        <div class="cover">
          @if (cover(); as url) {
            <img [src]="url" width="1200" height="525" alt="" fetchpriority="high" />
          }
          <app-favorite-button class="heart" [establishmentId]="d.id" [name]="d.name" />
        </div>

        <header>
          <h1>{{ d.name }}</h1>
          <div class="meta">
            <p class="verified">
              <svg lucideBadgeCheck aria-hidden="true"></svg>Établissement vérifié
            </p>
            <button
              type="button"
              class="rating-link"
              aria-label="Voir les avis"
              (click)="select('avis')"
            >
              <app-star-rating [average]="d.rating" [count]="d.reviewCount" />
            </button>
            @if (openStatus(); as status) {
              <span class="open" [attr.data-open]="status.open">{{ status.text }}</span>
            }
          </div>
          <div class="chips">
            @for (category of d.categories; track category.slug) {
              <span class="chip">{{ category.name }}</span>
            }
          </div>
          <p class="place">
            <svg lucideMapPin aria-hidden="true"></svg>
            @if (yangoUrl(); as yango) {
              <a
                class="place-link"
                [href]="yango"
                target="_blank"
                rel="noopener noreferrer"
                (click)="track('directions')"
              >
                {{ d.addressLine }} — {{ d.communeName }}, {{ d.cityName }}
                <span class="visually-hidden">(ouvre Yango avec l'itinéraire)</span>
              </a>
            } @else {
              <span>{{ d.addressLine }} — {{ d.communeName }}, {{ d.cityName }}</span>
            }
          </p>

          <div class="actions">
            <a class="btn btn-ghost" [href]="telHref()" (click)="track('call')">
              <svg lucidePhone aria-hidden="true"></svg>
              Appeler <span class="visually-hidden">{{ phoneDisplay() }}</span>
            </a>
            @if (yangoUrl(); as yango) {
              <!-- Straight to Yango: on a phone the link opens the app with the shop already set as the destination. -->
              <a
                class="btn btn-ghost"
                [href]="yango"
                target="_blank"
                rel="noopener noreferrer"
                (click)="track('directions')"
              >
                <svg lucideNavigation aria-hidden="true"></svg>
                S'y rendre avec Yango
              </a>
              <button type="button" class="btn btn-ghost" (click)="openDirections()">
                Autres cartes
              </button>
            } @else {
              <button type="button" class="btn btn-ghost" (click)="openDirections()">
                <svg lucideNavigation aria-hidden="true"></svg>
                S'y rendre
              </button>
            }
            <button type="button" class="btn btn-ghost" (click)="share(d.name)">
              <svg lucideShare2 aria-hidden="true"></svg>
              {{ shared() ? 'Lien copié' : 'Partager' }}
            </button>
            @if (d.bookingAvailable) {
              <a
                class="btn btn-primary btn-book"
                [routerLink]="['/etablissements', d.slug, 'reserver']"
              >
                <svg lucideCalendarCheck aria-hidden="true"></svg>
                Réserver
              </a>
            }
          </div>
        </header>

        <div class="tabs" role="tablist" aria-label="Informations sur l'établissement">
          @for (t of tabs; track t.id) {
            <button
              type="button"
              role="tab"
              [id]="'tab-' + t.id"
              [attr.aria-selected]="tab() === t.id"
              [attr.aria-controls]="'panel-' + t.id"
              [attr.tabindex]="tab() === t.id ? 0 : -1"
              (click)="select(t.id)"
              (keydown)="tabKey($event)"
            >
              {{ t.label }}
              @if (t.id === 'avis' && d.reviewCount > 0) {
                ({{ d.reviewCount }})
              }
            </button>
          }
        </div>

        <div
          class="panel"
          role="tabpanel"
          [id]="'panel-' + tab()"
          [attr.aria-labelledby]="'tab-' + tab()"
        >
          @switch (tab()) {
            @case ('prestations') {
              <section class="card" aria-labelledby="services-title">
                <h2 id="services-title">Prestations et tarifs</h2>
                @for (group of d.serviceGroups; track $index) {
                  @if (group.name) {
                    <h3>{{ group.name }}</h3>
                  }
                  @for (service of group.services; track service.id) {
                    <div class="service">
                      <div>
                        <strong>{{ service.name }}</strong>
                        <small>{{ duration(service.durationMinutes) }}</small>
                      </div>
                      <div class="end">
                        <span class="price">{{
                          price(service.price, service.priceType, service.currencyCode)
                        }}</span>
                        @if (d.bookingAvailable) {
                          <a
                            class="btn btn-primary btn-sm"
                            [routerLink]="['/etablissements', d.slug, 'reserver']"
                            [queryParams]="{ prestation: service.id }"
                          >
                            Réserver<span class="visually-hidden"> {{ service.name }}</span>
                          </a>
                        }
                      </div>
                    </div>
                  }
                } @empty {
                  <p>Les prestations seront bientôt détaillées.</p>
                }
              </section>
            }
            @case ('realisations') {
              @if (d.media.length > 0) {
                <app-media-carousel [media]="d.media" />
              } @else {
                <div class="state"><p>Ce salon n'a pas encore publié de réalisations.</p></div>
              }
            }
            @case ('avis') {
              <app-reviews-section [slug]="d.slug" />
            }
            @case ('apropos') {
              <div class="two">
                <div class="stack">
                  <section class="card" aria-labelledby="about-title">
                    <h2 id="about-title">À propos</h2>
                    <p>{{ d.description }}</p>
                    <p><strong>Téléphone :</strong> {{ phoneDisplay() }}</p>
                  </section>

                  <section aria-label="Localisation">
                    <app-map-view
                      [points]="[mapPoint(d)]"
                      label="Localisation de l'établissement"
                      style="--map-height: 18rem"
                    />
                  </section>
                </div>

                <div class="stack">
                  <section class="card">
                    <table>
                      <caption>
                        <svg lucideClock aria-hidden="true"></svg>
                        Horaires
                      </caption>
                      <tbody>
                        @for (row of hours(); track row.day) {
                          <tr [class.today]="row.day === today">
                            <th scope="row">{{ row.label }}</th>
                            <td [class.closed]="row.ranges.length === 0">
                              {{ row.ranges.length ? row.ranges.join(' · ') : 'Fermé' }}
                            </td>
                          </tr>
                        }
                      </tbody>
                    </table>
                  </section>

                  <section class="card" aria-labelledby="pay-title">
                    <h2 id="pay-title">Moyens de paiement</h2>
                    <p class="hint">Vous réglez directement auprès de l'établissement.</p>
                    <ul>
                      @if (d.acceptedPayments.cash) {
                        <li>Espèces, sur place</li>
                      }
                      @for (m of d.acceptedPayments.mobileMoney; track m.operator) {
                        <li>
                          <strong>{{ operatorLabel(m.operator) }}</strong> : {{ m.account }} ({{
                            m.holderName
                          }})
                        </li>
                      }
                    </ul>
                  </section>
                </div>
              </div>
            }
          }
        </div>

        <p-dialog
          header="S'y rendre"
          [visible]="directionsOpen()"
          (visibleChange)="directionsOpen.set($event)"
          [modal]="true"
          [dismissableMask]="true"
          [draggable]="false"
          [style]="{ width: 'min(92vw, 24rem)' }"
        >
          <div class="directions">
            <p>{{ d.addressLine }}, {{ d.communeName }}</p>
            @for (option of directionsOptions(); track option.id) {
              <a
                class="btn btn-ghost"
                [href]="option.url"
                target="_blank"
                rel="noopener noreferrer"
                (click)="track('directions')"
              >
                {{ option.label }}
              </a>
            }
          </div>
        </p-dialog>
      } @else {
        <div class="state" aria-busy="true"><p>Chargement de la fiche…</p></div>
      }
    </div>
  `,
})
export class Establishment {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly establishments = inject(EstablishmentService);
  private readonly directions = inject(DirectionsLinkService);
  private readonly title = inject(Title);
  private readonly meta = inject(Meta);

  private readonly slug = toSignal(this.route.paramMap.pipe(map((p) => p.get('slug') ?? '')), {
    initialValue: this.route.snapshot.paramMap.get('slug') ?? '',
  });

  protected readonly tabs = TABS;
  /** The tab can be opened from a link (`?onglet=avis`), e.g. from the rating on a card. */
  protected readonly tab = signal<Tab>(
    TABS.find((t) => t.id === this.route.snapshot.queryParamMap.get('onglet'))?.id ?? 'prestations',
  );

  protected readonly details = httpResource<EstablishmentDetails>(() =>
    this.slug() ? `/api/v1/establishments/${encodeURIComponent(this.slug())}` : undefined,
  );

  protected readonly notFound = computed(() => {
    const error = this.details.error();
    return error instanceof HttpErrorResponse && error.status === 404;
  });

  /** The first photo is the cover; the whole gallery is under "Réalisations". */
  protected readonly cover = computed(() => {
    const media = this.details.hasValue() ? this.details.value().media : [];
    const photo = media.find((m) => m.kind !== 'Video');
    return photo?.url ?? media[0]?.posterUrl ?? null;
  });

  /** The point shown on this page's map, with what its popup displays. */
  protected mapPoint(d: EstablishmentDetails): MapPoint {
    return {
      ...d,
      serviceCount: d.serviceGroups.reduce((total, group) => total + group.services.length, 0),
    };
  }

  protected readonly directionsOpen = signal(false);
  protected readonly shared = signal(false);
  protected readonly today = new Date().getDay();

  protected readonly telHref = computed(
    () => `tel:${this.details.hasValue() ? this.details.value().phone.replace(/[^+\d]/g, '') : ''}`,
  );
  protected readonly phoneDisplay = computed(() => {
    const phone = this.details.hasValue() ? this.details.value().phone : '';
    return phone.replace(/^(\+225)(\d{2})(\d{2})(\d{2})(\d{2})(\d{2})$/, '$1 $2 $3 $4 $5 $6');
  });

  /** The Yango link with the shop as destination, or null when the option is switched off. */
  protected readonly yangoUrl = computed(
    () => this.directionsOptions().find((option) => option.id === 'yango')?.url ?? null,
  );

  protected readonly directionsOptions = computed(() =>
    this.details.hasValue()
      ? this.directions.options(this.details.value().latitude, this.details.value().longitude)
      : [],
  );

  protected readonly hours = computed(() => {
    const ranges = this.details.hasValue() ? this.details.value().openingHours : [];
    return WEEK_ORDER.map((day) => ({
      day,
      label: dayName(day),
      ranges: ranges.filter((h) => h.dayOfWeek === day).map((h) => `${h.opensAt} – ${h.closesAt}`),
    }));
  });

  /** "Ouvert · ferme à 18:00", "Fermé · ouvre à 09:00" or "Fermé aujourd'hui", read on the establishment's own clock. */
  protected readonly openStatus = computed(() => {
    if (!this.details.hasValue()) {
      return null;
    }

    const d = this.details.value();
    if (d.openingHours.length === 0) {
      return null;
    }

    const parts = new Intl.DateTimeFormat('en-GB', {
      timeZone: d.timezone,
      weekday: 'short',
      hour: '2-digit',
      minute: '2-digit',
      hourCycle: 'h23',
    }).formatToParts(new Date());
    const value = (type: string) => parts.find((p) => p.type === type)?.value ?? '0';
    const day = WEEKDAY_INDEX[value('weekday')] ?? 0;
    const now = Number(value('hour')) * 60 + Number(value('minute'));
    const today = d.openingHours
      .filter((h) => h.dayOfWeek === day)
      .sort((a, b) => minutes(a.opensAt) - minutes(b.opensAt));

    const current = today.find((h) => minutes(h.opensAt) <= now && now < minutes(h.closesAt));
    if (current) {
      return { open: true, text: `Ouvert · ferme à ${current.closesAt}` };
    }

    const next = today.find((h) => minutes(h.opensAt) > now);
    return next
      ? { open: false, text: `Fermé · ouvre à ${next.opensAt}` }
      : { open: false, text: "Fermé aujourd'hui" };
  });

  constructor() {
    effect(() => {
      if (!this.details.hasValue()) {
        return;
      }

      const details = this.details.value();
      untracked(() => {
        this.applyShareTags(details);
        this.establishments.recordEvent(details.id, 'view');
      });
    });

    inject(DestroyRef).onDestroy(() =>
      OG_TAGS.forEach((tag) => this.meta.removeTag(`property='${tag}'`)),
    );
  }

  protected select(tab: Tab): void {
    this.tab.set(tab);
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { onglet: tab === 'prestations' ? null : tab },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  /** Arrow keys, Home and End move between the tabs, as on any tab list. */
  protected tabKey(event: KeyboardEvent): void {
    const steps: Record<string, number> = { ArrowRight: 1, ArrowLeft: -1 };
    const index = TABS.findIndex((t) => t.id === this.tab());
    let target = -1;
    if (event.key === 'Home') {
      target = 0;
    } else if (event.key === 'End') {
      target = TABS.length - 1;
    } else if (steps[event.key] !== undefined) {
      target = (index + steps[event.key] + TABS.length) % TABS.length;
    }

    if (target < 0) {
      return;
    }

    event.preventDefault();
    this.select(TABS[target].id);
    queueMicrotask(() => document.getElementById(`tab-${TABS[target].id}`)?.focus());
  }

  protected track(type: EstablishmentEvent): void {
    if (this.details.hasValue()) {
      this.establishments.recordEvent(this.details.value().id, type);
    }
  }

  /** Native share sheet where available (phones), otherwise the link is copied (PRD EST-06). */
  protected async share(name: string): Promise<void> {
    const data = { title: name, text: `${name} sur Keke Beauty`, url: window.location.href };
    try {
      if (navigator.share) {
        await navigator.share(data);
        return;
      }

      await navigator.clipboard.writeText(data.url);
      this.shared.set(true);
      setTimeout(() => this.shared.set(false), 3000);
    } catch {
      // Cancelling the share sheet or a denied clipboard is not an error worth showing.
    }
  }

  protected openDirections(): void {
    this.directionsOpen.set(true);
  }

  protected operatorLabel(id: string): string {
    return MOBILE_MONEY_LABELS[id] ?? id;
  }

  protected duration(minutes: number): string {
    return formatDuration(minutes);
  }

  protected price(amount: number, type: string, currency: string): string {
    return `${type === 'From' ? 'À partir de ' : ''}${formatMoney(amount, currency)}`;
  }

  /**
   * Open Graph tags set in the browser. Social crawlers do not run JavaScript, so reliable link previews need
   * prerendering (planned with the hardening lot).
   */
  private applyShareTags(details: EstablishmentDetails): void {
    const description =
      details.description.length > 160
        ? `${details.description.slice(0, 157)}…`
        : details.description;
    this.title.setTitle(`${details.name} — Keke Beauty`);
    this.meta.updateTag({ name: 'description', content: description });
    this.meta.updateTag({ property: 'og:title', content: details.name });
    this.meta.updateTag({ property: 'og:description', content: description });
    this.meta.updateTag({ property: 'og:type', content: 'business.business' });
    const cover = details.media[0]?.url;
    if (cover) {
      this.meta.updateTag({
        property: 'og:image',
        content: new URL(cover, document.baseURI).toString(),
      });
    }
  }
}
