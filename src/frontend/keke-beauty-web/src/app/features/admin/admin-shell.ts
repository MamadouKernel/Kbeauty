import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';

interface Section {
  readonly path: string;
  readonly label: string;
  readonly roles: readonly string[];
}

const ALL_ADMIN = ['SuperAdmin', 'Admin', 'AdminKyc', 'AdminSupport', 'AdminAccountant'] as const;

const SECTIONS: readonly Section[] = [
  { path: '/admin/kyc', label: 'Dossiers KYC', roles: ['SuperAdmin', 'Admin', 'AdminKyc'] },
  {
    path: '/admin/utilisateurs',
    label: 'Utilisateurs',
    roles: ['SuperAdmin', 'Admin', 'AdminSupport'],
  },
  {
    path: '/admin/etablissements',
    label: 'Établissements',
    roles: ['SuperAdmin', 'Admin', 'AdminKyc', 'AdminSupport'],
  },
  {
    path: '/admin/finances',
    label: 'Finances',
    roles: ['SuperAdmin', 'Admin', 'AdminAccountant'],
  },
  { path: '/admin/litiges', label: 'Litiges', roles: ['SuperAdmin', 'Admin', 'AdminSupport'] },
  { path: '/admin/parametres', label: 'Paramètres', roles: ['SuperAdmin', 'Admin'] },
  { path: '/admin/journal', label: 'Journal d’audit', roles: ALL_ADMIN },
  { path: '/admin/administrateurs', label: 'Administrateurs', roles: ['SuperAdmin'] },
];

/** Frame of the back-office: section navigation above the routed page. */
@Component({
  selector: 'app-admin-shell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, RouterLinkActive, RouterOutlet],
  styles: `
    nav {
      border-bottom: 1px solid var(--kb-border);
      background: var(--kb-card);
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
    const userRoles = this.auth.user()?.roles ?? [];
    return SECTIONS.filter((s) => s.roles.some((r) => userRoles.includes(r)));
  });
}
