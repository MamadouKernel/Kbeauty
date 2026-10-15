import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  computed,
  inject,
  input,
  signal,
  viewChild,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { LucideMapPin } from '@lucide/angular';
import type { EstablishmentSummary } from '../../core/establishments/establishment.service';
import { formatMoney } from '../../core/format/format';
import { FavoriteButton } from '../favorite-button/favorite-button';
import { MapView, type MapPoint } from '../map-view/map-view';
import { StarRating } from '../star-rating/star-rating';

/**
 * The map as the main screen: it fills the viewport and a row of cards sits at the bottom. Swiping the cards moves the map to
 * the salon; touching a pin brings its card forward. Each card has "Détails" and, when the salon takes online bookings, "Réserver".
 */
@Component({
  selector: 'app-map-explorer',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, FavoriteButton, MapView, StarRating, LucideMapPin],
  styles: `
    :host {
      display: block;
    }
    .explorer {
      position: relative;
      height: clamp(30rem, calc(100dvh - 11rem), 60rem);
      overflow: hidden;
      border: 1px solid var(--kb-border);
      border-radius: var(--radius-lg);
      background: var(--kb-primary-50);
    }
    app-map-view {
      position: absolute;
      inset: 0;
      --map-height: 100%;
    }
    .deck {
      position: absolute;
      inset: auto 0 0;
      z-index: 1100;
      display: flex;
      gap: 0.75rem;
      padding: 0.75rem;
      overflow-x: auto;
      scroll-snap-type: x mandatory;
      scrollbar-width: none;
      -webkit-overflow-scrolling: touch;
    }
    .tile {
      position: relative;
      display: grid;
      grid-template-columns: 5rem 1fr;
      gap: 0.25rem 0.75rem;
      flex: 0 0 min(86%, 21rem);
      padding: 0.75rem;
      scroll-snap-align: center;
      border: 2px solid transparent;
      border-radius: var(--radius-lg);
      background: rgb(255 255 255 / 97%);
      box-shadow: var(--shadow-lift);
      cursor: pointer;
    }
    .tile[data-active='true'] {
      border-color: var(--kb-primary-600);
    }
    .thumb {
      grid-row: span 4;
      width: 5rem;
      height: 5rem;
      border-radius: 0.75rem;
      object-fit: cover;
      background: linear-gradient(135deg, var(--kb-primary-300), var(--kb-primary-700));
    }
    h3 {
      margin: 0;
      padding-right: 2.75rem;
      font-size: 1rem;
      line-height: 1.25;
    }
    .place {
      display: flex;
      align-items: center;
      gap: 0.25rem;
      margin: 0;
      font-size: 0.8125rem;
      color: var(--kb-ink-muted);
    }
    .place svg {
      width: 0.875rem;
      height: 0.875rem;
      flex: none;
    }
    .heart {
      position: absolute;
      top: 0.375rem;
      right: 0.375rem;
      transform: scale(0.8);
      transform-origin: top right;
    }
    .actions {
      grid-column: 1 / -1;
      display: flex;
      align-items: center;
      gap: 0.5rem;
      margin-top: 0.25rem;
    }
    .actions .from {
      margin-left: auto;
    }
    .count {
      position: absolute;
      top: 0.75rem;
      left: 3.75rem;
      z-index: 1100;
      margin: 0;
      padding: 0.25rem 0.75rem;
      border-radius: var(--radius-pill);
      background: rgb(255 255 255 / 94%);
      font-size: 0.8125rem;
      font-weight: 700;
      box-shadow: var(--shadow-soft);
    }
  `,
  template: `
    <div class="explorer">
      <app-map-view
        [points]="points()"
        [popups]="false"
        [highlight]="active()"
        label="Carte des établissements"
        (picked)="pick($event)"
      />
      <p class="count" role="status">{{ items().length }} sur la carte</p>

      <div
        #deck
        class="deck"
        role="list"
        aria-label="Établissements sur la carte"
        (scroll)="onScroll()"
      >
        @for (item of items(); track item.id) {
          <article
            class="tile"
            role="listitem"
            [id]="'tile-' + item.slug"
            [attr.data-active]="item.slug === active()"
            (pointerup)="active.set(item.slug)"
            (focusin)="active.set(item.slug)"
          >
            @if (item.coverUrl) {
              <img
                class="thumb"
                [src]="item.coverUrl"
                width="80"
                height="80"
                loading="lazy"
                alt=""
              />
            } @else {
              <span class="thumb" aria-hidden="true"></span>
            }
            <h3>{{ item.name }}</h3>
            <p class="place">
              <svg lucideMapPin aria-hidden="true"></svg>
              {{ place(item) }}
            </p>
            <p class="place">
              @if (item.phone) {
                <a [href]="telHref(item)" (click)="$event.stopPropagation()">{{ item.phone }}</a>
                ·
              }
              {{ item.serviceCount }} {{ item.serviceCount > 1 ? 'prestations' : 'prestation' }}
            </p>
            <app-star-rating [average]="item.rating" [count]="item.reviewCount" />
            <app-favorite-button class="heart" [establishmentId]="item.id" [name]="item.name" />
            <div class="actions">
              <a class="btn btn-ghost btn-sm" [routerLink]="['/etablissements', item.slug]"
                >Détails<span class="visually-hidden"> de {{ item.name }}</span></a
              >
              @if (item.bookingAvailable) {
                <a
                  class="btn btn-primary btn-sm"
                  [routerLink]="['/etablissements', item.slug, 'reserver']"
                  >Réserver<span class="visually-hidden"> chez {{ item.name }}</span></a
                >
              }
              @if (price(item); as value) {
                <span class="place from">{{ value }}</span>
              }
            </div>
          </article>
        }
      </div>
    </div>
  `,
})
export class MapExplorer {
  readonly items = input.required<readonly EstablishmentSummary[]>();

  private readonly deck = viewChild.required<ElementRef<HTMLElement>>('deck');
  protected readonly active = signal<string | null>(null);

  protected readonly points = computed<readonly MapPoint[]>(() => this.items());

  private scrollTimer: ReturnType<typeof setTimeout> | undefined;
  /** While the deck is moved by code (a pin was touched), its scroll must not change the active card back. */
  private programmatic = false;

  constructor() {
    inject(DestroyRef).onDestroy(() => clearTimeout(this.scrollTimer));
  }

  /** A pin was touched or activated from the keyboard: its card comes to the front. */
  protected pick(slug: string): void {
    this.active.set(slug);
    this.programmatic = true;
    document
      .getElementById(`tile-${slug}`)
      ?.scrollIntoView({ behavior: 'smooth', inline: 'center', block: 'nearest' });
    clearTimeout(this.scrollTimer);
    this.scrollTimer = setTimeout(() => (this.programmatic = false), 700);
  }

  /** The card nearest to the middle of the row is the one the map follows. */
  protected onScroll(): void {
    if (this.programmatic) {
      return;
    }

    clearTimeout(this.scrollTimer);
    this.scrollTimer = setTimeout(() => {
      const deck = this.deck().nativeElement;
      const middle = deck.getBoundingClientRect().left + deck.clientWidth / 2;
      let best: { slug: string; distance: number } | null = null;
      for (const tile of Array.from(deck.querySelectorAll<HTMLElement>('.tile'))) {
        const rect = tile.getBoundingClientRect();
        const distance = Math.abs(rect.left + rect.width / 2 - middle);
        if (!best || distance < best.distance) {
          best = { slug: tile.id.replace('tile-', ''), distance };
        }
      }

      if (best && best.slug !== this.active()) {
        this.active.set(best.slug);
      }
    }, 120);
  }

  protected telHref(item: EstablishmentSummary): string {
    return `tel:${item.phone.replace(/[^+\d]/g, '')}`;
  }

  protected place(item: EstablishmentSummary): string {
    return item.addressLine ? `${item.communeName} · ${item.addressLine}` : item.communeName;
  }

  protected price(item: EstablishmentSummary): string | null {
    return item.startingPrice == null
      ? null
      : `Dès ${formatMoney(item.startingPrice, item.currencyCode ?? 'XOF')}`;
  }
}
