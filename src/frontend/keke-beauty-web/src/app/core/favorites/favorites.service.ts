import { HttpClient } from '@angular/common/http';
import { Injectable, computed, effect, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../auth/auth.service';
import type { EstablishmentSummary } from '../establishments/establishment.service';

/**
 * The signed-in client's favorites. The ids are loaded once after sign-in and kept in a signal, so every heart on the page (cards,
 * map, fiche) shows its state at once; toggling is optimistic and rolls back when the server refuses.
 */
@Injectable({ providedIn: 'root' })
export class FavoritesService {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  private readonly ids = signal<ReadonlySet<string>>(new Set());
  readonly count = computed(() => this.ids().size);

  constructor() {
    effect(() => {
      if (this.auth.user()) {
        void this.refresh();
      } else {
        this.ids.set(new Set());
      }
    });
  }

  /** Reading it inside a template or a `computed` makes that view follow the favorites. */
  isFavorite(establishmentId: string): boolean {
    return this.ids().has(establishmentId);
  }

  /** Saves or removes an establishment; a visitor who is not signed in is sent to the sign-in page and brought back here. */
  async toggle(establishmentId: string): Promise<void> {
    if (!this.auth.user()) {
      await this.router.navigate(['/connexion'], { queryParams: { retour: this.router.url } });
      return;
    }

    const wasFavorite = this.isFavorite(establishmentId);
    this.set(establishmentId, !wasFavorite);
    try {
      const url = `/api/v1/me/favorites/${establishmentId}`;
      await firstValueFrom(
        wasFavorite ? this.http.delete<void>(url) : this.http.put<void>(url, null),
      );
    } catch {
      this.set(establishmentId, wasFavorite);
    }
  }

  list(): Promise<EstablishmentSummary[]> {
    return firstValueFrom(this.http.get<EstablishmentSummary[]>('/api/v1/me/favorites'));
  }

  private async refresh(): Promise<void> {
    try {
      const ids = await firstValueFrom(this.http.get<string[]>('/api/v1/me/favorites/ids'));
      this.ids.set(new Set(ids));
    } catch {
      // The hearts stay empty: favorites are a convenience, never a reason to disturb the page.
    }
  }

  private set(establishmentId: string, favorite: boolean): void {
    this.ids.update((current) => {
      const next = new Set(current);
      if (favorite) {
        next.add(establishmentId);
      } else {
        next.delete(establishmentId);
      }

      return next;
    });
  }
}
