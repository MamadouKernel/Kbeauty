import { ChangeDetectionStrategy, Component, inject, resource, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AdminApiService } from '../../core/admin/admin-api.service';
import { AuthService } from '../../core/auth/auth.service';
import { problemMessage } from '../../core/auth/auth.models';

/** `/admin/administrateurs` — grant or withdraw the Admin role; SuperAdmin only (PRD ADM-07). */
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
                    {{ a.roles.includes('SuperAdmin') ? 'Super-administrateur' : 'Administrateur' }}
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
  protected email = '';
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly message = signal<string | null>(null);
  protected readonly admins = resource({ loader: () => this.api.admins() });

  protected async grant(): Promise<void> {
    await this.run('Rôle administrateur accordé.', async () => {
      await this.api.grantAdmin(this.email.trim());
      this.email = '';
    });
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
