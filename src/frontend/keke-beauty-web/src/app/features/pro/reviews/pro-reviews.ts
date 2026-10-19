import { ChangeDetectionStrategy, Component, inject, input, resource, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { problemMessage } from '../../../core/auth/auth.models';
import { ReviewService } from '../../../core/reviews/review.service';
import { ReviewPhotos } from '../../../shared/review-photos/review-photos';
import { StarRating } from '../../../shared/star-rating/star-rating';

/** `/pro/etablissements/:id/avis` — what clients wrote about the establishment, and the owner's public answers. */
@Component({
  selector: 'app-pro-reviews',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, StarRating, ReviewPhotos],
  styles: `
    .page {
      display: grid;
      gap: 1.5rem;
      max-width: 52rem;
      padding-block: clamp(1.5rem, 4vw, 3rem);
    }
    .review {
      display: grid;
      gap: 0.5rem;
      padding: 1rem 1.25rem;
      border: 1px solid var(--kb-border);
      border-radius: var(--radius-lg);
      background: var(--kb-card);
      box-shadow: var(--shadow-soft);
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
      gap: 0.5rem;
    }
    textarea {
      width: 100%;
      min-height: 4.5rem;
      padding: 0.75rem;
      border: 1px solid var(--kb-border);
      border-radius: 0.75rem;
      font: inherit;
      resize: vertical;
    }
    .answer {
      margin: 0;
      padding: 0.75rem 1rem;
      border-left: 3px solid var(--kb-primary-300);
      border-radius: 0 0.5rem 0.5rem 0;
      background: var(--kb-primary-50);
    }
    .hidden {
      padding: 0.125rem 0.75rem;
      border-radius: var(--radius-pill);
      background: color-mix(in srgb, var(--kb-danger) 10%, var(--kb-card));
      color: var(--kb-danger);
      font-size: var(--fs-xs);
      font-weight: 700;
    }
    .state {
      padding: 1.5rem;
      border: 1px dashed var(--kb-primary-300);
      border-radius: var(--radius-lg);
      background: var(--kb-card);
    }
  `,
  template: `
    <div class="container page">
      <a class="link" routerLink="/pro">← Mes établissements</a>
      <div>
        <p class="eyebrow">Avis clients</p>
        <h1>Vos avis et vos <em>réponses</em></h1>
      </div>

      @if (page.hasValue()) {
        @let p = page.value();
        <p>
          <app-star-rating [average]="p.summary.average" [count]="p.summary.count" />
        </p>
        @if (error()) {
          <p class="form-error" role="alert">{{ error() }}</p>
        }
        @for (review of p.items; track review.id) {
          <article class="review">
            <div class="head">
              <strong>{{ review.authorName }}</strong>
              <app-star-rating [average]="review.rating" [count]="1" [showText]="false" />
              <span class="visually-hidden">{{ review.rating }} sur 5</span>
              <span class="date">{{ day(review.createdAt) }}</span>
              @if (review.status === 'Hidden') {
                <span class="hidden">Masqué par la modération</span>
              }
            </div>
            @if (review.comment) {
              <p class="comment">{{ review.comment }}</p>
            }
            <app-review-photos [photos]="review.photos" [author]="review.authorName" />
            @if (review.status === 'Published') {
              @if (review.replyText && editing() !== review.id) {
                <p class="answer"><strong>Votre réponse :</strong> {{ review.replyText }}</p>
                <div>
                  <button
                    type="button"
                    class="btn btn-ghost btn-sm"
                    (click)="edit(review.id, review.replyText)"
                  >
                    Modifier ma réponse
                  </button>
                </div>
              } @else {
                <form class="reply" (submit)="send($event, review.id)">
                  <label>
                    <span class="visually-hidden">Votre réponse publique</span>
                    <textarea
                      #box
                      maxlength="600"
                      placeholder="Remerciez, répondez avec courtoisie : votre réponse est publique."
                      [value]="draft()"
                      (input)="draft.set(box.value)"
                    ></textarea>
                  </label>
                  <div>
                    <button
                      type="submit"
                      class="btn btn-primary btn-sm"
                      [disabled]="busy() || draft().trim().length === 0"
                    >
                      Publier ma réponse
                    </button>
                  </div>
                </form>
              }
            }
          </article>
        } @empty {
          <div class="state">
            <p>
              <strong>Pas encore d'avis.</strong> Ils apparaissent ici quand vos clients en laissent
              après un rendez-vous : un rendez-vous confirmé passe automatiquement en « terminé »
              après son heure de fin, et le client peut alors donner son avis.
            </p>
          </div>
        }
      } @else if (page.error()) {
        <p class="form-error" role="alert">Impossible de charger les avis. Réessayez.</p>
      } @else {
        <p role="status">Chargement…</p>
      }
    </div>
  `,
})
export class ProReviews {
  /** Bound from the `:id` route parameter. */
  readonly id = input.required<string>();

  private readonly reviews = inject(ReviewService);

  protected readonly page = resource({
    params: () => this.id(),
    loader: ({ params }) => this.reviews.ofEstablishment(params, 1),
  });
  protected readonly editing = signal<string | null>(null);
  protected readonly draft = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal('');

  protected edit(reviewId: string, current: string): void {
    this.editing.set(reviewId);
    this.draft.set(current);
  }

  protected async send(event: Event, reviewId: string): Promise<void> {
    event.preventDefault();
    this.busy.set(true);
    this.error.set('');
    try {
      await this.reviews.reply(reviewId, this.draft());
      this.editing.set(null);
      this.draft.set('');
      this.page.reload();
    } catch (error) {
      this.error.set(problemMessage(error));
    } finally {
      this.busy.set(false);
    }
  }

  protected day(iso: string): string {
    return new Date(iso).toLocaleDateString('fr-FR', {
      day: 'numeric',
      month: 'long',
      year: 'numeric',
    });
  }
}
