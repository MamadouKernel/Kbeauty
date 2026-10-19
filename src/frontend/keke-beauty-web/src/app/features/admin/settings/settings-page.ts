import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { AuthService } from '../../../core/auth/auth.service';
import { BackupsTab } from './backups-tab';
import { CategoriesTab } from './categories-tab';
import { ChannelsTab } from './channels-tab';
import { GeneralTab } from './general-tab';
import { GeoTab } from './geo-tab';
import { LegalTab } from './legal-tab';
import { PlansTab } from './plans-tab';
import { PromosTab } from './promos-tab';
import { TemplatesTab } from './templates-tab';

interface Tab {
  readonly id: string;
  readonly label: string;
  readonly superOnly?: boolean;
}

const TABS: readonly Tab[] = [
  { id: 'plans', label: 'Formules et tarifs' },
  { id: 'promos', label: 'Promotions' },
  { id: 'categories', label: 'Catégories' },
  { id: 'geo', label: 'Zones géographiques' },
  { id: 'templates', label: 'Messages' },
  { id: 'channels', label: 'Canaux' },
  { id: 'legal', label: 'Textes légaux' },
  { id: 'general', label: 'Général' },
  { id: 'backups', label: 'Sauvegardes', superOnly: true },
];

/** `/admin/parametres` — plans and prices, categories, zones, messages, channels, legal texts, global parameters, backups (PRD ADM-05, NOT-02). */
@Component({
  selector: 'app-settings-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    BackupsTab,
    CategoriesTab,
    ChannelsTab,
    GeneralTab,
    GeoTab,
    LegalTab,
    PlansTab,
    PromosTab,
    TemplatesTab,
  ],
  styles: `
    .tabs {
      display: flex;
      flex-wrap: wrap;
      gap: 0.5rem;
    }
    .tab {
      min-height: 2.75rem;
      padding: 0.5rem 1.125rem;
      border: 1.5px solid var(--kb-border);
      border-radius: var(--radius-pill);
      background: var(--kb-card);
      font: inherit;
      font-weight: 600;
      cursor: pointer;

      &[aria-pressed='true'] {
        border-color: var(--kb-primary-600);
        background: var(--kb-primary-50);
        color: var(--kb-primary-800);
      }
    }
  `,
  template: `
    <div class="container admin-page">
      <div>
        <p class="eyebrow">Administration</p>
        <h1>Paramètres</h1>
      </div>
      <div class="tabs" role="group" aria-label="Rubriques">
        @for (t of tabs(); track t.id) {
          <button
            type="button"
            class="tab"
            [attr.aria-pressed]="tab() === t.id"
            (click)="tab.set(t.id)"
          >
            {{ t.label }}
          </button>
        }
      </div>
      @switch (tab()) {
        @case ('plans') {
          <app-plans-tab />
        }
        @case ('promos') {
          <app-promos-tab />
        }
        @case ('categories') {
          <app-categories-tab />
        }
        @case ('geo') {
          <app-geo-tab />
        }
        @case ('templates') {
          <app-templates-tab />
        }
        @case ('channels') {
          <app-channels-tab />
        }
        @case ('legal') {
          <app-legal-tab />
        }
        @case ('backups') {
          @if (isSuperAdmin()) {
            <app-backups-tab />
          }
        }
        @default {
          <app-general-tab />
        }
      }
    </div>
  `,
})
export class SettingsPage {
  private readonly auth = inject(AuthService);

  protected readonly isSuperAdmin = computed(
    () => this.auth.user()?.roles.includes('SuperAdmin') ?? false,
  );
  protected readonly tabs = computed(() => TABS.filter((t) => !t.superOnly || this.isSuperAdmin()));
  protected readonly tab = signal('plans');
}
