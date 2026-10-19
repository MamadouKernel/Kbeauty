import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { LucideHeart } from '@lucide/angular';
import { FavoritesService } from '../../core/favorites/favorites.service';

/** A heart that saves an establishment to the client's favorites; usable on a card, on the map and on the fiche. */
@Component({
  selector: 'app-favorite-button',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [LucideHeart],
  styles: `
    button {
      display: inline-grid;
      place-items: center;
      width: 2.75rem;
      height: 2.75rem;
      border: 1px solid var(--kb-border);
      border-radius: 50%;
      background: rgb(var(--kb-paper-rgb) / 94%);
      color: var(--kb-primary-700);
      cursor: pointer;
      transition:
        transform 150ms var(--ease-out),
        background 150ms ease;
    }
    button:hover {
      background: var(--kb-card);
    }
    button:active {
      transform: scale(0.92);
    }
    svg {
      width: 1.25rem;
      height: 1.25rem;
    }
    button[aria-pressed='true'] {
      color: #c2185b;
    }
    button[aria-pressed='true'] svg {
      fill: currentcolor;
    }
  `,
  template: `
    <button
      type="button"
      [attr.aria-pressed]="saved()"
      [attr.aria-label]="
        saved() ? 'Retirer des favoris : ' + name() : 'Ajouter aux favoris : ' + name()
      "
      (click)="toggle($event)"
    >
      <svg lucideHeart aria-hidden="true"></svg>
    </button>
  `,
})
export class FavoriteButton {
  private readonly favorites = inject(FavoritesService);

  readonly establishmentId = input.required<string>();
  readonly name = input('cet établissement');

  protected readonly saved = computed(() => this.favorites.isFavorite(this.establishmentId()));

  protected toggle(event: Event): void {
    // Sits on top of a card that is a link: the click must not open the card.
    event.preventDefault();
    event.stopPropagation();
    void this.favorites.toggle(this.establishmentId());
  }
}
