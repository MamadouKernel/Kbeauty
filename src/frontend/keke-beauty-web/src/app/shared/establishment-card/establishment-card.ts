import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { LucideCalendarCheck, LucideMapPin, LucideStar } from '@lucide/angular';
import type { EstablishmentSummary } from '../../core/establishments/establishment.service';
import { formatMoney } from '../../core/format/format';
import { FavoriteButton } from '../favorite-button/favorite-button';
import { StarRating } from '../star-rating/star-rating';

/** Visual result card: cover, name, categories, place (+ distance) and "from" price. The whole card is one link. */
@Component({
  selector: 'app-establishment-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, FavoriteButton, StarRating, LucideCalendarCheck, LucideMapPin, LucideStar],
  styles: `
    :host {
      position: relative;
      display: block;
    }
    /* Beside the link, not inside it (a button in a link is invalid), laid over the top right corner of the cover. */
    .heart {
      position: absolute;
      top: 0.625rem;
      right: 0.625rem;
      z-index: 1;
    }
    a {
      display: grid;
      height: 100%;
      overflow: hidden;
      border: 1px solid var(--kb-border);
      border-radius: var(--radius-lg);
      background: #fff;
      box-shadow: var(--shadow-soft);
      color: inherit;
      text-decoration: none;
      transition:
        transform 200ms var(--ease-out),
        box-shadow 200ms var(--ease-out),
        border-color 200ms ease;
      grid-template-rows: auto 1fr;
    }
    a:hover {
      transform: translateY(-3px);
      border-color: var(--kb-primary-300);
      box-shadow: var(--shadow-lift);
    }
    .cover {
      position: relative;
      aspect-ratio: 3 / 2;
      background: linear-gradient(135deg, var(--kb-primary-300), var(--kb-primary-700));
    }
    img {
      width: 100%;
      height: 100%;
      object-fit: cover;
    }
    .badges {
      position: absolute;
      top: 0.75rem;
      left: 0.75rem;
      display: flex;
      flex-wrap: wrap;
      gap: 0.375rem;
    }
    .badge {
      display: inline-flex;
      align-items: center;
      gap: 0.25rem;
      padding: 0.25rem 0.625rem;
      border-radius: var(--radius-pill);
      background: rgb(255 255 255 / 92%);
      color: var(--kb-primary-800);
      font-size: var(--fs-2xs);
      font-weight: 700;
      backdrop-filter: blur(6px);
    }
    .badge svg {
      width: 0.875rem;
      height: 0.875rem;
      color: var(--kb-primary-600);
    }
    .body {
      display: grid;
      align-content: start;
      gap: 0.375rem;
      padding: 1rem 1.125rem 1.125rem;
    }
    h3 {
      font-size: 1.1875rem;
    }
    .categories {
      font-size: var(--fs-xs);
      font-weight: 600;
      color: var(--kb-primary-600);
    }
    .place {
      display: flex;
      align-items: center;
      gap: 0.375rem;
      font-size: var(--fs-sm);
      color: var(--kb-ink-muted);
    }
    .place svg {
      width: 1rem;
      height: 1rem;
      flex: none;
    }
    .price {
      margin-top: 0.25rem;
      font-size: var(--fs-sm);
      font-weight: 600;
    }
    .cta {
      justify-self: start;
      margin-top: 0.5rem;
      padding: 0.4rem 0.9rem;
      border-radius: var(--radius-pill);
      background: var(--kb-primary-50);
      color: var(--kb-primary-700);
      font-size: var(--fs-xs);
      font-weight: 700;
      transition:
        background 200ms ease,
        color 200ms ease;
    }
    a:hover .cta {
      background: var(--kb-primary-700);
      color: #fff;
    }
  `,
  template: `
    <a [routerLink]="['/etablissements', establishment().slug]">
      <div class="cover">
        @if (establishment().coverUrl; as cover) {
          <img [src]="cover" width="600" height="400" loading="lazy" alt="" />
        }
        <div class="badges">
          @if (establishment().isFeatured) {
            <span class="badge"><svg lucideStar aria-hidden="true"></svg>À la une</span>
          }
          @if (establishment().bookingAvailable) {
            <span class="badge"><svg lucideCalendarCheck aria-hidden="true"></svg>Réservable</span>
          }
        </div>
      </div>
      <div class="body">
        <h3>{{ establishment().name }}</h3>
        <app-star-rating [average]="establishment().rating" [count]="establishment().reviewCount" />
        <p class="categories">{{ categories() }}</p>
        <p class="place">
          <svg lucideMapPin aria-hidden="true"></svg>
          <span>{{ place() }}</span>
        </p>
        @if (price(); as value) {
          <p class="price">{{ value }}</p>
        }
        <span class="cta" aria-hidden="true">Découvrir →</span>
      </div>
    </a>
    <app-favorite-button
      class="heart"
      [establishmentId]="establishment().id"
      [name]="establishment().name"
    />
  `,
})
export class EstablishmentCard {
  readonly establishment = input.required<EstablishmentSummary>();

  protected readonly categories = computed(() =>
    this.establishment()
      .categories.map((c) => c.name)
      .join(' · '),
  );

  protected readonly place = computed(() => {
    const { communeName, cityName, distanceKm } = this.establishment();
    const place = communeName === cityName ? communeName : `${communeName}, ${cityName}`;
    return distanceKm == null ? place : `${place} · ${String(distanceKm).replace('.', ',')} km`;
  });

  protected readonly price = computed(() => {
    const { startingPrice, currencyCode } = this.establishment();
    return startingPrice == null
      ? null
      : `À partir de ${formatMoney(startingPrice, currencyCode ?? 'XOF')}`;
  });
}
