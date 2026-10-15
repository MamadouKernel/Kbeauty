import { DatePipe } from '@angular/common';
import { HttpClient, httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import {
  FormControl,
  NonNullableFormBuilder,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { RouterLink } from '@angular/router';
import { LucideLogOut, LucideShieldCheck } from '@lucide/angular';
import { InputText } from 'primeng/inputtext';
import { firstValueFrom } from 'rxjs';
import { SessionInfo, UserProfile, problemMessage } from '../../core/auth/auth.models';
import { AuthService } from '../../core/auth/auth.service';
import { NotificationSettings } from './notification-settings';

const ROLE_LABELS: Record<string, string> = {
  Client: 'Client',
  PartnerOwner: "Propriétaire d'établissement",
  PartnerStaff: 'Équipe établissement',
  Admin: 'Administrateur',
  SuperAdmin: 'Super administrateur',
};

@Component({
  selector: 'app-account',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    DatePipe,
    InputText,
    LucideLogOut,
    LucideShieldCheck,
    NotificationSettings,
    RouterLink,
  ],
  styles: `
    .page {
      display: grid;
      gap: 1.5rem;
      padding-block: clamp(2rem, 5vw, 4rem);
    }
    .card {
      display: grid;
      gap: 1rem;
      padding: clamp(1.25rem, 3vw, 2rem);
      border: 1px solid var(--kb-border);
      border-radius: var(--radius-lg);
      background: #fff;
      box-shadow: var(--shadow-soft);
    }
    .quick {
      display: flex;
      flex-wrap: wrap;
      gap: 0.5rem;
      margin: 0.75rem 0 0;
    }
    .grid {
      display: grid;
      gap: 1.5rem;

      @media (min-width: 900px) {
        grid-template-columns: 1fr 1fr;
        align-items: start;
      }
    }
    h2 {
      font-size: 1.375rem;
    }
    .meta {
      display: flex;
      flex-wrap: wrap;
      gap: 0.5rem;
    }
    .chip {
      display: inline-flex;
      align-items: center;
      gap: 0.375rem;
      padding: 0.25rem 0.75rem;
      border-radius: var(--radius-pill);
      background: var(--kb-primary-50);
      color: var(--kb-primary-800);
      font-size: 0.8125rem;
      font-weight: 600;
    }
    .chip svg {
      width: 1rem;
      height: 1rem;
    }
    .names {
      display: grid;
      gap: 1rem;

      @media (min-width: 480px) {
        grid-template-columns: 1fr 1fr;
      }
    }
    form {
      display: grid;
      gap: 1rem;
    }
    .field {
      display: grid;
      gap: 0.375rem;
    }
    .field label {
      font-size: 0.875rem;
      font-weight: 600;
    }
    .field input {
      width: 100%;
      min-height: 3rem;
    }
    .session {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: space-between;
      gap: 0.75rem;
      padding: 0.875rem 1rem;
      border: 1px solid var(--kb-border);
      border-radius: var(--radius-md);
    }
    .session small {
      display: block;
      color: var(--kb-ink-muted);
    }
  `,
  template: `
    <div class="container page">
      @if (user(); as me) {
        <header>
          <p class="eyebrow">Mon compte</p>
          <h1>Bonjour {{ me.firstName }}</h1>
          <p class="quick">
            <a class="btn btn-ghost btn-sm" routerLink="/compte/rendez-vous">Mes rendez-vous</a>
            <a class="btn btn-ghost btn-sm" routerLink="/compte/favoris">Mes favoris</a>
          </p>
          <div class="meta">
            @for (role of me.roles; track role) {
              <span class="chip">{{ roleLabel(role) }}</span>
            }
            <span class="chip"
              ><svg lucideShieldCheck aria-hidden="true"></svg>Double authentification activée</span
            >
          </div>
        </header>

        <div class="grid">
          <section class="card" aria-labelledby="profile-title">
            <h2 id="profile-title">Mes informations</h2>
            <form [formGroup]="form" (ngSubmit)="save()" novalidate>
              <div class="names">
                <div class="field">
                  <label for="acc-first">Prénom</label>
                  <input
                    pInputText
                    id="acc-first"
                    formControlName="firstName"
                    autocomplete="given-name"
                  />
                </div>
                <div class="field">
                  <label for="acc-last">Nom</label>
                  <input
                    pInputText
                    id="acc-last"
                    formControlName="lastName"
                    autocomplete="family-name"
                  />
                </div>
              </div>
              <div class="field">
                <label for="acc-email">Adresse e-mail</label>
                <input pInputText id="acc-email" [value]="me.email" disabled />
              </div>
              @if (message()) {
                <p class="form-success" role="status">{{ message() }}</p>
              }
              @if (error()) {
                <p class="form-error" role="alert">{{ error() }}</p>
              }
              <button class="btn btn-primary" type="submit" [disabled]="busy() || form.invalid">
                Enregistrer
              </button>
            </form>
          </section>

          <section class="card" aria-labelledby="notifications-title">
            <h2 id="notifications-title">Mes notifications</h2>
            <app-notification-settings />
          </section>

          <section class="card" aria-labelledby="sessions-title">
            <h2 id="sessions-title">Mes appareils connectés</h2>
            @if (sessions.hasValue()) {
              @for (session of sessions.value(); track session.id) {
                <div class="session">
                  <div>
                    <strong>{{ session.isCurrent ? 'Cet appareil' : 'Autre appareil' }}</strong>
                    <small
                      >{{ session.startedAt | date: 'dd/MM/yyyy HH:mm' }} ·
                      {{ session.ipAddress ?? 'IP inconnue' }}</small
                    >
                  </div>
                  @if (!session.isCurrent) {
                    <button type="button" class="btn btn-ghost" (click)="revoke(session)">
                      Déconnecter
                    </button>
                  }
                </div>
              }
            } @else if (sessions.error()) {
              <p class="form-error" role="alert">Impossible de charger vos sessions.</p>
            }
            <button type="button" class="btn btn-light" (click)="logout()">
              <svg lucideLogOut aria-hidden="true"></svg>
              Se déconnecter
            </button>
          </section>

          <section class="card" aria-labelledby="data-title">
            <h2 id="data-title">Mes données personnelles</h2>
            <p>
              Vous pouvez télécharger les données que nous détenons sur vous ou supprimer votre
              compte. Voir la
              <a class="link" routerLink="/confidentialite">politique de confidentialité</a>.
            </p>
            @if (error()) {
              <p class="form-error" role="alert">{{ error() }}</p>
            }
            <div>
              <button
                type="button"
                class="btn btn-light"
                [disabled]="busy()"
                (click)="exportData()"
              >
                Télécharger mes données
              </button>
            </div>
            @if (confirmingDelete()) {
              <form (ngSubmit)="deleteAccount()" novalidate>
                <div class="field">
                  <label for="delete-password">Mot de passe (pour confirmer)</label>
                  <input
                    pInputText
                    id="delete-password"
                    type="password"
                    autocomplete="current-password"
                    [formControl]="deletePassword"
                  />
                </div>
                <p class="form-error" role="alert">
                  La suppression est définitive : vos informations personnelles sont effacées.
                </p>
                <div class="actions">
                  <button type="submit" class="btn btn-primary" [disabled]="busy()">
                    Supprimer définitivement mon compte
                  </button>
                  <button type="button" class="btn btn-ghost" (click)="confirmingDelete.set(false)">
                    Annuler
                  </button>
                </div>
              </form>
            } @else {
              <div>
                <button type="button" class="btn btn-ghost" (click)="confirmingDelete.set(true)">
                  Supprimer mon compte…
                </button>
              </div>
            }
          </section>
        </div>
      }
    </div>
  `,
})
export class Account {
  private readonly auth = inject(AuthService);
  private readonly http = inject(HttpClient);

  protected readonly user = this.auth.user;
  protected readonly sessions = httpResource<SessionInfo[]>(() => '/api/v1/me/sessions');
  protected readonly busy = signal(false);
  protected readonly confirmingDelete = signal(false);
  protected readonly deletePassword = new FormControl('', { nonNullable: true });
  protected readonly message = signal('');
  protected readonly error = signal('');

  protected readonly form = inject(NonNullableFormBuilder).group({
    firstName: [
      this.auth.user()?.firstName ?? '',
      [Validators.required, Validators.maxLength(100)],
    ],
    lastName: [this.auth.user()?.lastName ?? '', [Validators.required, Validators.maxLength(100)]],
  });

  protected roleLabel(role: string): string {
    return ROLE_LABELS[role] ?? role;
  }

  protected async save(): Promise<void> {
    if (this.form.invalid) {
      return;
    }

    this.busy.set(true);
    this.message.set('');
    this.error.set('');
    try {
      const updated = await firstValueFrom(
        this.http.patch<UserProfile>('/api/v1/me', this.form.getRawValue()),
      );
      this.auth.updateUser(updated);
      this.message.set('Informations enregistrées.');
    } catch (error) {
      this.error.set(problemMessage(error));
    } finally {
      this.busy.set(false);
    }
  }

  protected async revoke(session: SessionInfo): Promise<void> {
    try {
      await firstValueFrom(this.http.delete<void>(`/api/v1/me/sessions/${session.id}`));
      this.sessions.reload();
    } catch (error) {
      this.error.set(problemMessage(error));
    }
  }

  /** Saves the JSON export (the endpoint needs the bearer token, so the file is fetched first). */
  protected async exportData(): Promise<void> {
    this.error.set('');
    try {
      const blob = await firstValueFrom(
        this.http.get('/api/v1/me/export', { responseType: 'blob' }),
      );
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = 'mes-donnees-keke-beauty.json';
      link.click();
      URL.revokeObjectURL(url);
    } catch (error) {
      this.error.set(problemMessage(error));
    }
  }

  protected async deleteAccount(): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    try {
      await firstValueFrom(
        this.http.post<void>('/api/v1/me/delete', { password: this.deletePassword.value }),
      );
      await this.auth.logout();
    } catch (error) {
      this.error.set(problemMessage(error));
    } finally {
      this.busy.set(false);
    }
  }

  protected async logout(): Promise<void> {
    await this.auth.logout();
  }
}
