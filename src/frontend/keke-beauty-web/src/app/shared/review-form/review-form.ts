import { ChangeDetectionStrategy, Component, inject, input, output, signal } from '@angular/core';
import { problemMessage } from '../../core/auth/auth.models';
import { ReviewService } from '../../core/reviews/review.service';

const MAX_COMMENT = 1000;

/** Rating (1 to 5 stars, required) and an optional comment for a completed appointment. */
@Component({
  selector: 'app-review-form',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    form {
      display: grid;
      gap: 0.75rem;
      padding: 1rem;
      border-radius: 0.75rem;
      background: var(--kb-primary-50);
    }
    fieldset {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 0.25rem;
      margin: 0;
      padding: 0;
      border: 0;
    }
    legend {
      width: 100%;
      margin-bottom: 0.25rem;
      padding: 0;
      font-weight: 600;
    }
    .star {
      position: relative;
      font-size: 2rem;
      line-height: 1;
      color: var(--kb-border);
      cursor: pointer;
    }
    .star[data-on='true'] {
      color: var(--kb-gold);
    }
    .star input {
      position: absolute;
      inset: 0;
      margin: 0;
      opacity: 0;
      cursor: pointer;
    }
    .star:focus-within {
      outline: 2px solid var(--kb-primary-600);
      outline-offset: 2px;
      border-radius: 0.25rem;
    }
    textarea {
      width: 100%;
      min-height: 5.5rem;
      padding: 0.75rem;
      border: 1px solid var(--kb-border);
      border-radius: 0.75rem;
      font: inherit;
      resize: vertical;
    }
    .row {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 0.5rem;
    }
    .count {
      margin-left: auto;
      font-size: 0.8125rem;
      color: var(--kb-ink-muted);
    }
  `,
  template: `
    <form (submit)="submit($event)">
      <fieldset>
        <legend>Votre note</legend>
        @for (star of stars; track star) {
          <label class="star" [attr.data-on]="star <= rating()">
            <input
              type="radio"
              [name]="'rating-' + appointmentId()"
              [value]="star"
              [checked]="rating() === star"
              (change)="rating.set(star)"
            />
            <span aria-hidden="true">★</span>
            <span class="visually-hidden">{{ star }} sur 5</span>
          </label>
        }
      </fieldset>
      <label>
        <span class="visually-hidden">Votre commentaire (facultatif)</span>
        <textarea
          #box
          name="comment"
          maxlength="1000"
          placeholder="Racontez votre visite (facultatif) : accueil, résultat, propreté…"
          [value]="comment()"
          (input)="comment.set(box.value)"
        ></textarea>
      </label>
      @if (error()) {
        <p class="form-error" role="alert">{{ error() }}</p>
      }
      <div class="row">
        <button type="submit" class="btn btn-primary btn-sm" [disabled]="busy() || rating() === 0">
          Publier mon avis
        </button>
        <button
          type="button"
          class="btn btn-ghost btn-sm"
          [disabled]="busy()"
          (click)="cancelled.emit()"
        >
          Annuler
        </button>
        <span class="count">{{ comment().length }} / 1000</span>
      </div>
    </form>
  `,
})
export class ReviewForm {
  private readonly reviews = inject(ReviewService);

  readonly appointmentId = input.required<string>();
  readonly submitted = output<void>();
  readonly cancelled = output<void>();

  protected readonly stars = [1, 2, 3, 4, 5];
  protected readonly rating = signal(0);
  protected readonly comment = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal('');

  protected async submit(event: Event): Promise<void> {
    event.preventDefault();
    if (this.rating() === 0 || this.comment().length > MAX_COMMENT) {
      return;
    }

    this.busy.set(true);
    this.error.set('');
    try {
      await this.reviews.create(this.appointmentId(), this.rating(), this.comment());
      this.submitted.emit();
    } catch (error) {
      this.error.set(problemMessage(error));
    } finally {
      this.busy.set(false);
    }
  }
}
