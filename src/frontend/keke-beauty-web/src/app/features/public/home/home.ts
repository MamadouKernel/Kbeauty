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

  protected readonly selectedSlug = signal<string | null>(null);
  protected readonly text = signal('');

  protected readonly options = computed(() =>
    this.categories.hasValue() ? this.categories.value() : [],
  );

  protected toggle(slug: string): void {
    this.selectedSlug.update((current) => (current === slug ? null : slug));
  }

  protected search(): void {
    void this.router.navigate(['/etablissements'], {
      queryParams: { categorie: this.selectedSlug(), q: this.text().trim() || null },
    });
  }
}
