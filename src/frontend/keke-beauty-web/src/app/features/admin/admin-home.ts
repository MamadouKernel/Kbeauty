import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { LucideShieldCheck } from '@lucide/angular';
import { AuthService } from '../../core/auth/auth.service';

/** Landing page of the back-office: one card per section. */
@Component({
  selector: 'app-admin-home',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [LucideShieldCheck, RouterLink],
  styles: `
    .page {
      display: grid;
      gap: 1rem;
      padding-block: clamp(2rem, 5vw, 4rem);
    }
    .card {
      display: grid;
      gap: 0.75rem;
      max-width: 40rem;
      padding: clamp(1.25rem, 3vw, 2rem);
      border: 1px solid var(--kb-border);
      border-radius: var(--radius-lg);
      background: #fff;
      box-shadow: var(--shadow-soft);
    }
    svg {
      width: 2.5rem;
      height: 2.5rem;
      color: var(--kb-primary-600);
    }
    .cards {
      display: grid;
      gap: 1rem;
      grid-template-columns: repeat(auto-fit, minmax(min(100%, 16rem), 1fr));
    }
    .link-card {
      max-width: none;
      color: inherit;
      text-decoration: none;

      &:hover {
        border-color: var(--kb-primary-400);
      }
    }
  `,
  template: `
    <div class="container page">
      <p class="eyebrow">Administration</p>
      <h1>Espace administration</h1>
      @if (user(); as me) {
        <p class="auth-lead">Connecté en tant que {{ me.firstName }} {{ me.lastName }}.</p>
      }
      <div class="cards">
        @for (c of cards(); track c.path) {
          <a class="card link-card" [routerLink]="c.path">
            <svg lucideShieldCheck aria-hidden="true"></svg>
            <h2>{{ c.title }}</h2>
            <p>{{ c.text }}</p>
          </a>
        }
      </div>
    </div>
  `,
})
export class AdminHome {
  protected readonly user = inject(AuthService).user;

  protected readonly cards = computed(() => {
    const sections = [
      {
        path: '/admin/kyc',
        title: 'Dossiers KYC',
        text: 'Examiner les inscriptions des partenaires.',
      },
      {
        path: '/admin/utilisateurs',
        title: 'Utilisateurs',
        text: 'Rechercher, suspendre ou réactiver un compte.',
      },
      {
        path: '/admin/etablissements',
        title: 'Établissements',
        text: 'Modérer les fiches et les photos.',
      },
      {
        path: '/admin/finances',
        title: 'Finances',
        text: 'Abonnements, revenus, impayés et export CSV.',
      },
      {
        path: '/admin/parametres',
        title: 'Paramètres',
        text: 'Formules, catégories, zones et messages.',
      },
      { path: '/admin/journal', title: 'Journal d’audit', text: 'Qui a fait quoi, quand et d’où.' },
    ];
    if (this.user()?.roles.includes('SuperAdmin')) {
      sections.push({
        path: '/admin/administrateurs',
        title: 'Administrateurs',
        text: 'Nommer ou retirer un administrateur.',
      });
    }

    return sections;
  });
}
