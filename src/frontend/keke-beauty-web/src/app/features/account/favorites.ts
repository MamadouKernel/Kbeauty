import { ChangeDetectionStrategy, Component, computed, inject, resource } from '@angular/core';
import { RouterLink } from '@angular/router';
import { FavoritesService } from '../../core/favorites/favorites.service';
import { EstablishmentCard } from '../../shared/establishment-card/establishment-card';

/** `/compte/favoris` — the establishments the client saved. Removing a heart takes the card off the page at once. */
@Component({
  selector: 'app-favorites',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [EstablishmentCard, RouterLink],
  styles: `
    .page {
      display: grid;
      gap: 1.5rem;
      padding-block: clamp(1.5rem, 4vw, 3rem);
    }
    .grid {
      display: grid;
      gap: 1.25rem;
      grid-template-columns: repeat(auto-fill, minmax(min(100%, 17rem), 1fr));
    }
    .state {
      display: grid;
      justify-items: start;
      gap: 0.75rem;
      padding: 1.5rem;
      border: 1px dashed var(--kb-primary-300);
      border-radius: var(--radius-lg);
      background: var(--kb-card);
    }
  `,
  template: `
    <div class="container page">
      <div>
        <p class="eyebrow">Mon compte</p>
        <h1>Mes favoris</h1>
      </div>

      @if (saved.hasValue()) {
        @if (visible().length > 0) {
          <div class="grid">
            @for (item of visible(); track item.id) {
              <app-establishment-card [establishment]="item" />
            }
          </div>
        } @else {
          <div class="state">
            <h2>Aucun favori pour le moment</h2>
            <p>
              Touchez le cœur d'un salon pour le retrouver ici, quand vous voulez réserver à
              nouveau.
            </p>
            <a class="btn btn-primary" routerLink="/etablissements">Découvrir les salons</a>
          </div>
        }
      } @else if (saved.error()) {
        <div class="state" role="alert">
          <p>Impossible de charger vos favoris pour le moment.</p>
          <button type="button" class="btn btn-ghost" (click)="saved.reload()">Réessayer</button>
        </div>
      } @else {
        <p role="status">Chargement…</p>
      }
    </div>
  `,
})
export class Favorites {
  private readonly favorites = inject(FavoritesService);

  protected readonly saved = resource({ loader: () => this.favorites.list() });

  /** The cards still saved: a heart removed on this page drops its card without waiting for the server. */
  protected readonly visible = computed(() =>
    this.saved.hasValue() ? this.saved.value().filter((e) => this.favorites.isFavorite(e.id)) : [],
  );
}
