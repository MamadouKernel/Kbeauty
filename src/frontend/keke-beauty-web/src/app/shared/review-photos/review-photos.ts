import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/** The photos a client attached to her review (before / after of a treatment), as small links to the full-size image. */
export interface ReviewPhotoView {
  readonly id: string;
  readonly kind: string;
  readonly url: string;
  readonly thumbUrl: string | null;
}

const KIND_LABELS: Readonly<Record<string, string>> = {
  Before: 'Avant',
  After: 'Après',
  Other: 'Photo',
};

@Component({
  selector: 'app-review-photos',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    ul {
      display: flex;
      flex-wrap: wrap;
      gap: 0.5rem;
      margin: 0;
      padding: 0;
      list-style: none;
    }
    li {
      display: grid;
      gap: 0.125rem;
      justify-items: center;
      font-size: var(--fs-xs);
      color: var(--kb-ink-muted);
    }
    img {
      width: 5.5rem;
      height: 5.5rem;
      border-radius: var(--radius-md);
      object-fit: cover;
      background: var(--kb-primary-50);
    }
  `,
  template: `
    @if (photos()?.length) {
      <ul [attr.aria-label]="'Photos de ' + author()">
        @for (photo of photos(); track photo.id) {
          <li>
            <a [href]="photo.url" target="_blank" rel="noopener">
              <img
                [src]="photo.thumbUrl ?? photo.url"
                [alt]="label(photo.kind) + ' — photo de ' + author()"
                width="88"
                height="88"
                loading="lazy"
              />
            </a>
            <span>{{ label(photo.kind) }}</span>
          </li>
        }
      </ul>
    }
  `,
})
export class ReviewPhotos {
  readonly photos = input<readonly ReviewPhotoView[] | null | undefined>(null);
  readonly author = input('une cliente');

  protected label(kind: string): string {
    return KIND_LABELS[kind] ?? 'Photo';
  }
}
