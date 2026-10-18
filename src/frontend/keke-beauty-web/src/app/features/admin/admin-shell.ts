import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';

interface Section {
  readonly path: string;
  readonly label: string;
  readonly superOnly?: boolean;
}

const SECTIONS: readonly Section[] = [
  { path: '/admin/kyc', label: 'Dossiers KYC' },
  { path: '/admin/utilisateurs', label: 'Utilisateurs' },
  { path: '/admin/etablissements', label: 'Établissements' },
  { path: '/admin/finances', label: 'Finances' },
  { path: '/admin/litiges', label: 'Litiges' },
  { path: '/admin/parametres', label: 'Paramètres' },
  { path: '/admin/journal', label: 'Journal d’audit' },
  { path: '/admin/administrateurs', label: 'Administrateurs', superOnly: true },
];

/** Frame of the back-office: section navigation above the routed page. */
@Component({
  selector: 'app-admin-shell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, RouterLinkActive, RouterOutlet],
  styles: `
    nav {
      border-bottom: 1px solid var(--kb-border);
      background: #fff;
    }
    ul {
      display: flex;
      gap: 0.25rem;
      margin: 0;
      padding: 0.5rem 0;
      list-style: none;
      overflow-x: auto;
      scrollbar-width: none;
      -webkit-overflow-scrolling: touch;
    }
    li {
      /* Never squeezed: the row scrolls instead of letting the labels overlap. */
      flex: none;
    }
    a {
      display: inline-block;
      padding: 0.5rem 1rem;
      border-radius: var(--radius-pill);
      font-weight: 600;
      white-space: nowrap;
      color: var(--kb-ink);
      text-decoration: none;

      &:hover {
        background: var(--kb-primary-50);
      }

      &.active {
        background: var(--kb-primary-100, var(--kb-primary-50));
        color: var(--kb-primary-800);
      }
    }
  `,
  template: `
    <nav aria-label="Administration">
      <div class="container">
        <ul>
          <li>
            <a
              routerLink="/admin"
              routerLinkActive="active"
              [routerLinkActiveOptions]="{ exact: true }"
              >Accueil</a
            >
          </li>
          @for (section of sections(); track section.path) {
            <li>
              <a [routerLink]="section.path" routerLinkActive="active">{{ section.label }}</a>
            </li>
          }
        </ul>
      </div>
    </nav>
    <router-outlet />
  `,
})
export class AdminShell {
  private readonly auth = inject(AuthService);

  protected readonly sections = computed(() => {
    const isSuper = this.auth.user()?.roles.includes('SuperAdmin') ?? false;
    return SECTIONS.filter((s) => !s.superOnly || isSuper);
  });
}
