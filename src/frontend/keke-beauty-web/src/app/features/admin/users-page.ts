import { ChangeDetectionStrategy, Component, inject, resource, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AdminApiService } from '../../core/admin/admin-api.service';
import { problemMessage } from '../../core/auth/auth.models';
import { ReasonForm } from './shared/reason-form';

const ROLES = [
  { value: '', label: 'Tous les rôles' },
  { value: 'Client', label: 'Clients' },
  { value: 'PartnerOwner', label: 'Partenaires' },
  { value: 'Admin', label: 'Administrateurs' },
];

const STATUSES = [
  { value: '', label: 'Tous les statuts' },
  { value: 'Active', label: 'Actifs' },
  { value: 'Suspended', label: 'Suspendus' },
];

const ROLE_LABELS: Record<string, string> = {
  Client: 'Client',
  PartnerOwner: 'Partenaire',
  PartnerStaff: 'Équipe partenaire',
  Admin: 'Administrateur',
  SuperAdmin: 'Super-administrateur',
};

/** `/admin/utilisateurs` — search, details, suspension and reactivation with a reason (PRD ADM-01). */
@Component({
  selector: 'app-users-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, DatePipe, ReasonForm],
  template: `
    <div class="container admin-page">
      <div>
        <p class="eyebrow">Administration</p>
        <h1>Utilisateurs</h1>
      </div>

      <form class="admin-filters" (ngSubmit)="search()">
        <label>
          Recherche
          <input class="admin-input" name="q" [(ngModel)]="q" placeholder="E-mail ou nom" />
        </label>
        <label>
          Rôle
          <select class="admin-input" name="role" [(ngModel)]="role">
            @for (r of roles; track r.value) {
              <option [value]="r.value">{{ r.label }}</option>
            }
          </select>
        </label>
        <label>
          Statut
          <select class="admin-input" name="status" [(ngModel)]="status">
            @for (s of statuses; track s.value) {
              <option [value]="s.value">{{ s.label }}</option>
            }
          </select>
        </label>
        <button type="submit" class="btn btn-primary">Rechercher</button>
      </form>

      @if (message(); as text) {
        <p role="status" class="badge" data-tone="ok">{{ text }}</p>
      }
      @if (error(); as text) {
        <p class="form-error" role="alert">{{ text }}</p>
      }

      @if (users.hasValue()) {
        @let result = users.value();
        <div class="table-scroll">
          <table class="admin-table">
            <caption class="visually-hidden">
              Utilisateurs
            </caption>
            <thead>
              <tr>
                <th scope="col">Nom</th>
                <th scope="col">E-mail</th>
                <th scope="col">Rôles</th>
                <th scope="col">Statut</th>
                <th scope="col">Inscription</th>
                <th scope="col"><span class="visually-hidden">Actions</span></th>
              </tr>
            </thead>
            <tbody>
              @for (u of result.items; track u.id) {
                <tr>
                  <td>{{ u.firstName }} {{ u.lastName }}</td>
                  <td>{{ u.email }}</td>
                  <td>{{ roleNames(u.roles) }}</td>
                  <td>
                    <span class="badge" [attr.data-tone]="u.status === 'Active' ? 'ok' : 'bad'">{{
                      u.status === 'Active' ? 'Actif' : 'Suspendu'
                    }}</span>
                  </td>
                  <td>{{ u.createdAt | date: 'dd/MM/yyyy' }}</td>
                  <td>
                    <button type="button" class="btn btn-ghost btn-sm" (click)="open(u.id)">
                      Détails
                    </button>
                  </td>
                </tr>
              } @empty {
                <tr>
                  <td colspan="6">Aucun utilisateur ne correspond.</td>
                </tr>
              }
            </tbody>
          </table>
        </div>
        <div class="pager">
          <button
            type="button"
            class="btn btn-ghost btn-sm"
            [disabled]="page() <= 1"
            (click)="go(-1)"
          >
            Précédent
          </button>
          <span>Page {{ result.page }} · {{ result.total }} résultat(s)</span>
          <button
            type="button"
            class="btn btn-ghost btn-sm"
            [disabled]="result.page * result.pageSize >= result.total"
            (click)="go(1)"
          >
            Suivant
          </button>
        </div>
      } @else if (users.error()) {
        <p class="form-error" role="alert">Impossible de charger les utilisateurs.</p>
      } @else {
        <p role="status">Chargement…</p>
      }

      @if (detail.hasValue() && selected()) {
        @let d = detail.value();
        <section class="admin-card" aria-labelledby="detail-title">
          <h2 id="detail-title">{{ d.user.firstName }} {{ d.user.lastName }}</h2>
          <p>
            {{ d.user.email }} · {{ roleNames(d.user.roles) }} ·
            {{ d.user.emailConfirmed ? 'e-mail confirmé' : 'e-mail non confirmé' }} ·
            {{
              d.user.totpEnabled
                ? 'double authentification active'
                : 'double authentification à configurer'
            }}
          </p>
          @if (d.establishments.length > 0) {
            <h3>Établissements</h3>
            <ul>
              @for (e of d.establishments; track e.id) {
                <li>{{ e.name }} ({{ e.status }})</li>
              }
            </ul>
          }
          @if (d.history.length > 0) {
            <h3>Historique</h3>
            <ul>
              @for (h of d.history; track h.id) {
                <li>
                  {{ h.at | date: 'dd/MM/yyyy HH:mm' }} — {{ h.action }} par {{ h.actorEmail }}
                </li>
              }
            </ul>
          }
          @if (acting()) {
            <app-reason-form
              [confirmLabel]="
                d.user.status === 'Active' ? 'Suspendre le compte' : 'Réactiver le compte'
              "
              [busy]="busy()"
              (confirmed)="apply(d.user.id, d.user.status === 'Active', $event)"
              (cancelled)="acting.set(false)"
            />
          } @else {
            <div class="row-actions">
              <button
                type="button"
                class="btn btn-sm"
                [class.btn-primary]="d.user.status !== 'Active'"
                [class.btn-ghost]="d.user.status === 'Active'"
                (click)="acting.set(true)"
              >
                {{ d.user.status === 'Active' ? 'Suspendre…' : 'Réactiver…' }}
              </button>
              <button type="button" class="btn btn-ghost btn-sm" (click)="selected.set(null)">
                Fermer
              </button>
            </div>
          }
        </section>
      }
    </div>
  `,
})
export class UsersPage {
  private readonly api = inject(AdminApiService);

  protected readonly roles = ROLES;
  protected readonly statuses = STATUSES;
  protected q = '';
  protected role = '';
  protected status = '';

  protected readonly page = signal(1);
  protected readonly applied = signal({ q: '', role: '', status: '' });
  protected readonly selected = signal<string | null>(null);
  protected readonly acting = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly message = signal<string | null>(null);

  protected readonly users = resource({
    params: () => ({ ...this.applied(), page: this.page() }),
    loader: ({ params }) => this.api.users(params),
  });
  protected readonly detail = resource({
    params: () => this.selected(),
    loader: ({ params }) => (params ? this.api.user(params) : Promise.reject(new Error('none'))),
  });

  protected roleNames(roles: string[]): string {
    return roles.map((r) => ROLE_LABELS[r] ?? r).join(', ');
  }

  protected search(): void {
    this.page.set(1);
    this.applied.set({ q: this.q.trim(), role: this.role, status: this.status });
  }

  protected go(step: number): void {
    this.page.update((p) => Math.max(1, p + step));
  }

  protected open(id: string): void {
    this.acting.set(false);
    this.error.set(null);
    this.message.set(null);
    this.selected.set(id);
  }

  protected async apply(id: string, suspend: boolean, reason: string): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await (suspend ? this.api.suspendUser(id, reason) : this.api.reactivateUser(id, reason));
      this.message.set(suspend ? 'Compte suspendu.' : 'Compte réactivé.');
      this.acting.set(false);
      this.detail.reload();
      this.users.reload();
    } catch (e) {
      this.error.set(problemMessage(e));
    } finally {
      this.busy.set(false);
    }
  }
}
