import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  resource,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import {
  LucideBadgeCheck,
  LucideCalendarCheck,
  LucideCamera,
  LucideCircleCheck,
  LucideMapPin,
  LucideSearch,
  LucideSparkles,
  LucideStore,
} from '@lucide/angular';
import { InputText } from 'primeng/inputtext';
import { Select } from 'primeng/select';
import { CatalogService } from '../../../core/catalog/catalog.service';
import { EstablishmentService } from '../../../core/establishments/establishment.service';
import {
  GeolocationFailure,
  GeolocationService,
  type Position,
} from '../../../core/geo/geolocation.service';
import {
  GrowthService,
  progressToReward,
  type LoyaltyCard,
} from '../../../core/growth/growth.service';
import { AuthService } from '../../../core/auth/auth.service';
import { CategoryIcon } from '../../../shared/category-icon/category-icon';
import { EstablishmentCard } from '../../../shared/establishment-card/establishment-card';

/** Public landing page (lot 0): brand hero, category entry points, how it works, partner call to action. */
@Component({
  selector: 'app-home',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    FormsModule,
    RouterLink,
    InputText,
    Select,
    CategoryIcon,
    EstablishmentCard,
    LucideBadgeCheck,
    LucideCalendarCheck,
    LucideCamera,
    LucideCircleCheck,
    LucideMapPin,
    LucideSearch,
    LucideSparkles,
    LucideStore,
  ],
  templateUrl: './home.html',
  styleUrl: './home.scss',
})
export class Home {
  private readonly router = inject(Router);

  protected readonly categories = inject(CatalogService).categories;
  protected readonly skeletons = [1, 2, 3, 4, 5, 6, 7];

  private readonly establishments = inject(EstablishmentService);
  /** A handful of establishments for the "à la une" row; a failure just hides the row. */
  protected readonly featured = resource({
    loader: () => this.establishments.search({ pageSize: 4 }).then((page) => page.items),
  });

  private readonly growth = inject(GrowthService);
  private readonly auth = inject(AuthService);
  private readonly geolocation = inject(GeolocationService);

  /** A logged-in client's loyalty cards; nothing is fetched for visitors. */
  protected readonly loyalty = resource({
    params: () => ({ signedIn: !!this.auth.user() }),
    loader: ({ params }) => (params.signedIn ? this.growth.myLoyalty() : Promise.resolve([])),
  });

  /** The one card to nudge on the home page: a reward ready first, otherwise the closest to the next one. */
  protected readonly loyaltyTeaser = computed<LoyaltyCard | null>(() => {
    const cards = this.loyalty.hasValue() ? this.loyalty.value() : [];
    const active = cards.filter((c) => c.programActive && c.points > 0 && c.rewardThreshold > 0);
    if (active.length === 0) {
      return null;
    }
    return [...active].sort((a, b) => {
      const reward = (b.rewardsAvailable > 0 ? 1 : 0) - (a.rewardsAvailable > 0 ? 1 : 0);
      return reward !== 0
        ? reward
        : progressToReward(b.points, b.rewardThreshold) - progressToReward(a.points, a.rewardThreshold);
    })[0];
  });
  protected readonly position = signal<Position | null>(null);
  protected readonly locating = signal(false);
  protected readonly locateError = signal('');

  /** "Salons près de chez vous": best rated places until the visitor shares her position; a failure hides the row. */
  protected readonly nearby = resource({
    params: () => ({ position: this.position() }),
    loader: ({ params }) => this.growth.recommendations(params.position, 4),
  });

  protected readonly selectedSlug = signal<string | null>(null);
  protected readonly text = signal('');

  protected readonly options = computed(() =>
    this.categories.hasValue() ? this.categories.value() : [],
  );

  protected async locate(): Promise<void> {
    this.locating.set(true);
    this.locateError.set('');
    try {
      this.position.set(await this.geolocation.locate());
    } catch (error) {
      this.locateError.set(
        error instanceof GeolocationFailure
          ? error.userMessage
          : 'Votre position n’est pas disponible pour le moment.',
      );
    } finally {
      this.locating.set(false);
    }
  }

  protected loyaltyProgress(card: LoyaltyCard): number {
    return progressToReward(card.points, card.rewardThreshold);
  }

  protected loyaltyRemaining(card: LoyaltyCard): number {
    return card.rewardThreshold - (card.points % card.rewardThreshold);
  }

  protected toggle(slug: string): void {
    this.selectedSlug.update((current) => (current === slug ? null : slug));
  }

  protected search(): void {
    void this.router.navigate(['/etablissements'], {
      queryParams: { categorie: this.selectedSlug(), q: this.text().trim() || null },
    });
  }
}
