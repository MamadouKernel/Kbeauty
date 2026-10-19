import { httpResource } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  input,
  signal,
  untracked,
} from '@angular/core';
import type { components } from '../../../core/api/api.generated';
import { ReviewPhotos } from '../../../shared/review-photos/review-photos';
import { StarRating } from '../../../shared/star-rating/star-rating';

type ReviewsPage = components['schemas']['ReviewsPage'];
type Review = components['schemas']['ReviewDto'];

const PAGE_SIZE = 10;

/** The "Avis" tab of an establishment: the average, how the stars are spread, and the reviews with the owner's answers. */
@Component({
  selector: 'app-reviews-section',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [StarRating, ReviewPhotos],
  styles: `
    :host {
      display: grid;
      gap: 1.25rem;
    }
    .summary {
      display: grid;
      gap: 1rem;
      padding: 1.25rem;
      border: 1px solid var(--kb-border);
      border-radius: var(--radius-lg);
      background: var(--kb-card);

      @media (min-width: 600px) {
        grid-template-columns: auto 1fr;
        align-items: center;
        gap: 2rem;
      }
    }
    .big {
      margin: 0;
      font-family: var(--font-display);
      font-size: 3rem;
      font-weight: 700;
      line-height: 1;
    }
    .bars {
      display: grid;
      gap: 0.375rem;
      margin: 0;
      padding: 0;
      list-style: none;
    }
    .bars li {
      display: grid;
      grid-template-columns: 2.25rem 1fr 2rem;
      align-items: center;
      gap: 0.5rem;
      font-size: var(--fs-xs);
    }
    .track {
      height: 0.5rem;
      border-radius: var(--radius-pill);
      background: var(--kb-primary-50);
      overflow: hidden;
    }
    .track span {
      display: block;
      height: 100%;
      background: var(--kb-gold);
    }
    .review {
      display: grid;
      gap: 0.5rem;
      padding: 1rem 1.25rem;
      border: 1px solid var(--kb-border);
      border-radius: var(--radius-lg);
      background: var(--kb-card);
    }
    .head {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 0.25rem 0.75rem;
    }
    .date {
      color: var(--kb-ink-muted);
      font-size: var(--fs-xs);
    }
    .comment {
      margin: 0;
      white-space: pre-line;
    }
    .reply {
      display: grid;
      gap: 0.25rem;
      margin: 0.25rem 0 0;
      padding: 0.75rem 1rem;
      border-left: 3px solid var(--kb-primary-300);
      border-radius: 0 0.5rem 0.5rem 0;
      background: var(--kb-primary-50);
      font-size: var(--fs-md);
    }
    .reply strong {
      font-size: var(--fs-xs);
      color: var(--kb-primary-700);
    }
    .reply p {
      margin: 0;
    }
    .empty {
      padding: 1.5rem;
      border: 1px dashed var(--kb-primary-300);
      border-radius: var(--radius-lg);
      background: var(--kb-card);
    }
    .more {
      justify-self: center;
    }
  `,
  template: `
    @if (firstPage.error()) {
      <div class="empty" role="alert">
        <p>Impossible de charger les avis pour le moment.</p>
        <button type="button" class="btn btn-ghost" (click)="firstPage.reload()">Réessayer</button>
      </div>
    } @else if (summary(); as s) {
      @if (s.count === 0) {
        <div class="empty">
          <p>
            <strong>Aucun avis pour le moment.</strong>
          </p>
          <p>
            Les avis sont écrits par des clients après un rendez-vous terminé : chacun correspond à
            une vraie visite.
          </p>
        </div>
      } @else {
        <section class="summary" aria-label="Note moyenne">
          <div>
            <p class="big">{{ average() }}</p>
            <app-star-rating [average]="s.average" [count]="s.count" />
          </div>
          <ul class="bars" aria-label="Répartition des notes">
            @for (star of [5, 4, 3, 2, 1]; track star) {
              <li>
                <span>{{ star }} ★</span>
                <span class="track"
                  ><span [style.width.%]="share(s.distribution[star - 1], s.count)"></span
                ></span>
                <span>{{ s.distribution[star - 1] }}</span>
              </li>
            }
          </ul>
        </section>

        @for (review of items(); track review.id) {
          <article class="review">
            <div class="head">
              <strong>{{ review.authorName }}</strong>
              <app-star-rating [average]="review.rating" [count]="1" [showText]="false" />
              <span class="visually-hidden">{{ review.rating }} sur 5</span>
              <span class="date">{{ day(review.createdAt) }}</span>
            </div>
            @if (review.comment) {
              <p class="comment">{{ review.comment }}</p>
            }
            <app-review-photos [photos]="review.photos" [author]="review.authorName" />
            @if (review.replyText) {
              <div class="reply">
                <strong>Réponse de l'établissement</strong>
                <p>{{ review.replyText }}</p>
              </div>
            }
          </article>
        }

        @if (items().length < total()) {
          <button
            type="button"
            class="btn btn-ghost more"
            [disabled]="nextPage.isLoading()"
            (click)="page.set(page() + 1)"
          >
            {{ nextPage.isLoading() ? 'Chargement…' : "Voir plus d'avis" }}
          </button>
        }
      }
    } @else {
      <div class="empty" aria-busy="true"><p>Chargement des avis…</p></div>
    }
  `,
})
export class ReviewsSection {
  readonly slug = input.required<string>();

  protected readonly page = signal(1);
  protected readonly items = signal<readonly Review[]>([]);

  /** The first page also carries the summary; the following ones only add reviews to the list. */
  protected readonly firstPage = httpResource<ReviewsPage>(() => this.url(1));
  protected readonly nextPage = httpResource<ReviewsPage>(() =>
    this.page() > 1 ? this.url(this.page()) : undefined,
  );

  protected readonly summary = computed(() =>
    this.firstPage.hasValue() ? this.firstPage.value().summary : null,
  );
  protected readonly total = computed(() =>
    this.firstPage.hasValue() ? this.firstPage.value().total : 0,
  );
  protected readonly average = computed(() =>
    (this.summary()?.average ?? 0).toFixed(1).replace('.', ','),
  );

  constructor() {
    effect(() => {
      // Another establishment: start again from its first page.
      this.slug();
      untracked(() => {
        this.page.set(1);
        this.items.set([]);
      });
    });
    effect(() => {
      if (this.firstPage.hasValue()) {
        const value = this.firstPage.value();
        untracked(() => this.items.set(value.items));
      }
    });
    effect(() => {
      if (this.nextPage.hasValue()) {
        const value = this.nextPage.value();
        untracked(() =>
          this.items.update((current) => [
            ...current,
            ...value.items.filter((item) => !current.some((c) => c.id === item.id)),
          ]),
        );
      }
    });
  }

  protected share(count: number, total: number): number {
    return total === 0 ? 0 : Math.round((count / total) * 100);
  }

  protected day(iso: string): string {
    return new Date(iso).toLocaleDateString('fr-FR', {
      day: 'numeric',
      month: 'long',
      year: 'numeric',
    });
  }

  private url(page: number): string {
    return `/api/v1/establishments/${encodeURIComponent(this.slug())}/reviews?page=${page}&pageSize=${PAGE_SIZE}`;
  }
}
