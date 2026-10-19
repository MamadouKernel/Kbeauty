import { ChangeDetectionStrategy, Component, computed, inject, resource } from '@angular/core';
import { RouterLink } from '@angular/router';
import { LucideShieldCheck } from '@lucide/angular';
import { AdminApiService } from '../../core/admin/admin-api.service';
import { AuthService } from '../../core/auth/auth.service';
import { formatMoney } from '../../core/format/format';

/** Landing page of the back-office: analytical KPI dashboard (#32) and one card per section. */
@Component({
  selector: 'app-admin-home',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [LucideShieldCheck, RouterLink],
  styles: `
    .page {
      display: grid;
      gap: 1.5rem;
      padding-block: clamp(2rem, 5vw, 4rem);
    }
    .kpis {
      display: grid;
      gap: 1rem;
      grid-template-columns: repeat(auto-fit, minmax(min(100%, 15rem), 1fr));
    }
    .kpi-box {
      display: grid;
      gap: 0.5rem;
      padding: 1.25rem;
      border: 1px solid var(--kb-border);
      border-radius: var(--radius-lg);
      background: var(--kb-card);
      box-shadow: var(--shadow-soft);
    }
    .kpi-box h2 {
      font-size: var(--fs-sm);
      text-transform: uppercase;
      letter-spacing: 0.06em;
      color: var(--kb-ink-muted);
      margin: 0;
    }
    .kpi-main {
      font-family: var(--font-display);
      font-size: 1.875rem;
      font-weight: 700;
      color: var(--kb-primary-700);
    }
    .kpi-list {
      margin: 0;
      padding: 0;
      list-style: none;
      display: grid;
      gap: 0.25rem;
      font-size: var(--fs-sm);
    }
    .kpi-list li {
      display: flex;
      justify-content: space-between;
      gap: 0.5rem;
    }
    .card {
      display: grid;
      gap: 0.75rem;
      max-width: 40rem;
      padding: clamp(1.25rem, 3vw, 2rem);
      border: 1px solid var(--kb-border);
      border-radius: var(--radius-lg);
      background: var(--kb-card);
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
      <div>
        <p class="eyebrow">Administration</p>
        <h1>Tableau de bord <em>analytique</em></h1>
        @if (user(); as me) {
          <p class="auth-lead">Connecté en tant que {{ me.firstName }} {{ me.lastName }}.</p>
        }
      </div>

      @if (dashboard.hasValue()) {
        @let d = dashboard.value();
        <section class="kpis" aria-label="Indicateurs globaux de la plateforme">
          <article class="kpi-box">
            <h2>Établissements & KYC</h2>
            <div class="kpi-main">{{ d.activeEstablishments }} publiés</div>
            <ul class="kpi-list">
              <li>
                <span>En attente KYC</span> <strong>{{ d.pendingReviewEstablishments }}</strong>
              </li>
              <li>
                <span>Brouillons</span> <strong>{{ d.draftEstablishments }}</strong>
              </li>
              <li>
                <span>Suspendus / refusés</span>
                <strong>{{ d.suspendedEstablishments + d.rejectedEstablishments }}</strong>
              </li>
            </ul>
          </article>

          <article class="kpi-box">
            <h2>Rendez-vous et activité</h2>
            <div class="kpi-main">{{ d.completedAppointments }} honorés</div>
            <ul class="kpi-list">
              <li>
                <span>Volume honoré</span>
                <strong>{{ money(d.completedVolume, d.currencyCode) }}</strong>
              </li>
              <li>
                <span>Confirmés / en attente</span>
                <strong>{{ d.confirmedAppointments }} / {{ d.pendingAppointments }}</strong>
              </li>
              <li>
                <span>Absences / annulés</span>
                <strong
                  >{{ d.noShowAppointments }} / {{ d.cancelledOrRejectedAppointments }}</strong
                >
              </li>
            </ul>
          </article>

          <article class="kpi-box">
            <h2>Abonnements & Finances</h2>
            <div class="kpi-main">{{ money(d.monthlyRecurringRevenue, d.currencyCode) }} MRR</div>
            <ul class="kpi-list">
              <li>
                <span>Abonnements actifs / essai</span>
                <strong>{{ d.activeSubscriptions }} / {{ d.trialingSubscriptions }}</strong>
              </li>
              <li>
                <span>Abonnements impayés</span> <strong>{{ d.pastDueSubscriptions }}</strong>
              </li>
              <li>
                <span>Litiges ouverts</span> <strong>{{ d.openDisputes }}</strong>
              </li>
            </ul>
          </article>

          <article class="kpi-box">
            <h2>Communauté et qualité</h2>
            <div class="kpi-main">{{ d.totalUsers }} comptes</div>
            <ul class="kpi-list">
              <li>
                <span>Partenaires / équipe</span>
                <strong>{{ d.totalPartners }} / {{ d.totalStaff }}</strong>
              </li>
              <li>
                <span>Avis publiés</span> <strong>{{ d.publishedReviews }}</strong>
              </li>
              <li>
                <span>Note moyenne</span>
                <strong>{{ d.averageRating > 0 ? d.averageRating + ' / 5' : '—' }}</strong>
              </li>
            </ul>
          </article>
        </section>
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
  private readonly api = inject(AdminApiService);
  protected readonly user = inject(AuthService).user;
  protected readonly dashboard = resource({ loader: () => this.api.dashboard() });

  protected money(amount: number, currency: string): string {
    return formatMoney(amount, currency);
  }

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
        path: '/admin/litiges',
        title: 'Litiges',
        text: 'Arbitrer les réclamations clients et partenaires.',
      },
      {
        path: '/admin/reversements',
        title: 'Reversements',
        text: 'Suivre les virements aux salons et collaboratrices.',
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
