import { ChangeDetectionStrategy, Component, inject, input, output, signal } from '@angular/core';
import { problemMessage } from '../../core/auth/auth.models';
import { ReviewService, type ReviewPhotoKind } from '../../core/reviews/review.service';

const MAX_COMMENT = 1000;
const MAX_PHOTOS = 4;

interface PickedPhoto {
  readonly id: number;
  readonly file: File;
  readonly kind: ReviewPhotoKind;
}

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
    .photos {
      display: grid;
      gap: 0.5rem;
      margin: 0;
      padding: 0;
      border: 0;
    }
    .photo-row {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 0.5rem;
    }
    .photo-row span {
      flex: 1 1 8rem;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
    select {
      padding: 0.375rem 0.5rem;
      border: 1px solid var(--kb-border);
      border-radius: 0.5rem;
      font: inherit;
    }
    .count {
      margin-left: auto;
      font-size: var(--fs-xs);
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
      @if (!published()) {
        <fieldset class="photos">
          <legend>Photos avant / après (facultatif, {{ maxPhotos }} au plus)</legend>
          <input
            type="file"
            accept="image/*"
            multiple
            aria-label="Ajouter des photos"
            [disabled]="busy() || photos().length >= maxPhotos"
            (change)="pick($event)"
          />
          @for (photo of photos(); track photo.id) {
            <div class="photo-row">
              <span>{{ photo.file.name }}</span>
              <select
                #kind
                [attr.aria-label]="'Type de la photo ' + photo.file.name"
                [value]="photo.kind"
                (change)="setKind(photo.id, kind.value)"
              >
                <option value="Before">Avant</option>
                <option value="After">Après</option>
                <option value="Other">Autre</option>
              </select>
              <button type="button" class="btn btn-ghost btn-sm" (click)="removePhoto(photo.id)">
                Retirer
              </button>
            </div>
          }
        </fieldset>
      }
      @if (error()) {
        <p class="form-error" role="alert">{{ error() }}</p>
      }
      <div class="row">
        @if (published()) {
          <button type="button" class="btn btn-primary btn-sm" (click)="submitted.emit()">
            Fermer
          </button>
        } @else {
          <button
            type="submit"
            class="btn btn-primary btn-sm"
            [disabled]="busy() || rating() === 0"
          >
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
        }
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
  protected readonly maxPhotos = MAX_PHOTOS;
  protected readonly photos = signal<readonly PickedPhoto[]>([]);

  /** The review exists once created: a failed photo upload must not make her publish it twice. */
  protected readonly published = signal(false);
  private nextPhotoId = 0;

  protected pick(event: Event): void {
    const input = event.target as HTMLInputElement;
    const room = MAX_PHOTOS - this.photos().length;
    const files = Array.from(input.files ?? [])
      .filter((file) => file.type.startsWith('image/'))
      .slice(0, room);
    this.photos.update((all) => [
      ...all,
      ...files.map((file, index) => ({
        id: this.nextPhotoId++,
        file,
        kind: (all.length + index === 0
          ? 'Before'
          : all.length + index === 1
            ? 'After'
            : 'Other') as ReviewPhotoKind,
      })),
    ]);
    input.value = '';
  }

  protected setKind(id: number, kind: string): void {
    this.photos.update((all) =>
      all.map((p) => (p.id === id ? { ...p, kind: kind as ReviewPhotoKind } : p)),
    );
  }

  protected removePhoto(id: number): void {
    this.photos.update((all) => all.filter((p) => p.id !== id));
  }

  protected async submit(event: Event): Promise<void> {
    event.preventDefault();
    if (this.rating() === 0 || this.comment().length > MAX_COMMENT) {
      return;
    }

    this.busy.set(true);
    this.error.set('');
    try {
      const review = await this.reviews.create(this.appointmentId(), this.rating(), this.comment());
      this.published.set(true);
      let failed = 0;
      for (const photo of this.photos()) {
        try {
          await this.reviews.uploadPhoto(review.id, photo.file, photo.kind);
        } catch {
          failed++;
        }
      }

      if (failed === 0) {
        this.submitted.emit();
      } else {
        this.error.set(
          `Votre avis est publié, mais ${failed} photo(s) n'ont pas pu être envoyée(s).`,
        );
      }
    } catch (error) {
      this.error.set(problemMessage(error));
    } finally {
      this.busy.set(false);
    }
  }
}
