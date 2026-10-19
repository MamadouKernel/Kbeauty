import { ChangeDetectionStrategy, Component, inject, resource, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AdminApiService, AdminUser } from '../../core/admin/admin-api.service';
import { AuthService } from '../../core/auth/auth.service';
import { problemMessage } from '../../core/auth/auth.models';

export const ADMIN_ROLE_OPTIONS = [
  { value: 'Admin', label: 'Administrateur complet' },
  { value: 'AdminKyc', label: 'Administrateur KYC' },
  { value: 'AdminSupport', label: 'Administrateur support' },
  { value: 'AdminAccountant', label: 'Administrateur comptabilité' },
] as const;

/** `/admin/administrateurs` — grant, change or withdraw delegated admin roles; SuperAdmin only (PRD ADM-07). */
@Component({
  selector: 'app-admins-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  template: `
    <div class="container admin-page">
      <div>
        <p class="eyebrow">Administration</p>
        <h1>Administrateurs</h1>
        <p class="auth-lead">
          Le compte doit déjà exister (adresse e-mail confirmée). La personne devra configurer la
          double authentification à sa prochaine connexion.
        </p>
      </div>

      <form class="admin-filters" (ngSubmit)="grant()">
        <label>
          Adresse e-mail du compte
          <input class="admin-input" type="email" name="email" [(ngModel)]="email" required />
        </label>
        <label>
          Rôle attribué
          <select class="admin-input" name="role" [(ngModel)]="role">
            @for (opt of roleOptions; track opt.value) {
              <option [value]="opt.value">{{ opt.label }}</option>
            }
          </select>
        </label>
        <button type="submit" class="btn btn-primary" [disabled]="busy()">
          Nommer administrateur
        </button>
      </form>

      @if (message(); as text) {
        <p role="status" class="badge" data-tone="ok">{{ text }}</p>
      }
      @if (error(); as text) {
        <p class="form-error" role="alert">{{ text }}</p>
      }

      @if (admins.hasValue()) {
        <div class="table-scroll">
          <table class="admin-table">
            <caption class="visually-hidden">
              Administrateurs
            </caption>
            <thead>
              <tr>
                <th scope="col">Nom</th>
                <th scope="col">E-mail</th>
                <th scope="col">Rôle</th>
                <th scope="col"><span class="visually-hidden">Actions</span></th>
              </tr>
            </thead>
            <tbody>
              @for (a of admins.value(); track a.id) {
                <tr>
                  <td>{{ a.firstName }} {{ a.lastName }}</td>
                  <td>{{ a.email }}</td>
                  <td>
                    @if (a.roles.includes('SuperAdmin')) {
                      Super-administrateur
                    } @else if (a.id !== me()?.id) {
                      <select
                        class="admin-input"
                        [attr.aria-label]="'Rôle de ' + a.email"
                        [ngModel]="primaryAdminRole(a)"
                        (ngModelChange)="changeRole(a.id, $event)"
                        [disabled]="busy()"
                      >
                        @for (opt of roleOptions; track opt.value) {
                          <option [value]="opt.value">{{ opt.label }}</option>
                        }
                      </select>
                    } @else {
                      {{ roleLabel(primaryAdminRole(a)) }}
                    }
                  </td>
                  <td>
                    @if (!a.roles.includes('SuperAdmin') && a.id !== me()?.id) {
                      <button
                        type="button"
                        class="btn btn-ghost btn-sm"
                        [disabled]="busy()"
                        (click)="revoke(a.id)"
                      >
                        Retirer le rôle
                      </button>
                    }
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      } @else if (admins.error()) {
        <p class="form-error" role="alert">Impossible de charger la liste.</p>
      } @else {
        <p role="status">Chargement…</p>
      }
    </div>
  `,
})
export class AdminsPage {
  private readonly api = inject(AdminApiService);

  protected readonly me = inject(AuthService).user;
  protected readonly roleOptions = ADMIN_ROLE_OPTIONS;
  protected email = '';
  protected role = 'Admin';
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly message = signal<string | null>(null);
  protected readonly admins = resource({ loader: () => this.api.admins() });

  protected primaryAdminRole(user: AdminUser): string {
    for (const opt of ADMIN_ROLE_OPTIONS) {
      if (user.roles.includes(opt.value)) {
        return opt.value;
      }
    }
    return 'Admin';
  }

  protected roleLabel(role: string): string {
    return ADMIN_ROLE_OPTIONS.find((o) => o.value === role)?.label ?? 'Administrateur';
  }

  protected async grant(): Promise<void> {
    await this.run('Rôle administrateur accordé.', async () => {
      await this.api.grantAdmin(this.email.trim(), this.role);
      this.email = '';
    });
  }

  protected async changeRole(id: string, newRole: string): Promise<void> {
    await this.run('Rôle administrateur mis à jour.', () => this.api.updateAdminRole(id, newRole));
  }

  protected async revoke(id: string): Promise<void> {
    await this.run('Rôle administrateur retiré.', () => this.api.revokeAdmin(id));
  }

  private async run(success: string, action: () => Promise<unknown>): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    this.message.set(null);
    try {
      await action();
      this.message.set(success);
      this.admins.reload();
    } catch (e) {
      this.error.set(problemMessage(e));
    } finally {
      this.busy.set(false);
    }
  }
}
