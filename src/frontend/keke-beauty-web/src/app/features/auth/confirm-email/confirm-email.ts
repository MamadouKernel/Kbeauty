import { AuthLayout } from '../../../shared/auth-layout/auth-layout';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { LucideCircleAlert, LucideCircleCheck, LucideLoaderCircle } from '@lucide/angular';
import { problemMessage } from '../../../core/auth/auth.models';
import { AuthService } from '../../../core/auth/auth.service';

type ConfirmState = 'working' | 'confirmed' | 'failed';

@Component({
  selector: 'app-confirm-email',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [AuthLayout, RouterLink, LucideCircleAlert, LucideCircleCheck, LucideLoaderCircle],
  styles: `
    .confirm-state {
      display: grid;
      gap: 1rem;
    }
    .state-icon {
      width: 3rem;
      height: 3rem;
      color: var(--kb-primary-600);
    }
    .state-icon.ok {
      color: var(--kb-success);
    }
    .state-icon.ko {
      color: var(--kb-danger);
    }
    .spin {
      animation: spin 1s linear infinite;
    }
    @keyframes spin {
      to {
        transform: rotate(360deg);
      }
    }
  `,
  template: `
    <app-auth-layout labelledBy="confirm-title">
      <div class="confirm-state" role="status" aria-live="polite">
        @switch (state()) {
          @case ('working') {
            <svg lucideLoaderCircle class="state-icon spin" aria-hidden="true"></svg>
            <h1 id="confirm-title">Confirmation en cours…</h1>
          }
          @case ('confirmed') {
            <svg lucideCircleCheck class="state-icon ok" aria-hidden="true"></svg>
            <h1 id="confirm-title">Adresse confirmée</h1>
            <p class="auth-lead">Votre compte est activé. Vous pouvez vous connecter.</p>
            <a class="btn btn-primary" routerLink="/connexion">Se connecter</a>
          }
          @default {
            <svg lucideCircleAlert class="state-icon ko" aria-hidden="true"></svg>
            <h1 id="confirm-title">Lien invalide</h1>
            <p class="form-error">{{ message() }}</p>
            <a class="btn btn-ghost" routerLink="/inscription">Créer un compte</a>
          }
        }
      </div>
    </app-auth-layout>
  `,
})
export class ConfirmEmail {
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);

  protected readonly state = signal<ConfirmState>('working');
  protected readonly message = signal('');

  constructor() {
    void this.confirm();
  }

  private async confirm(): Promise<void> {
    const token = this.route.snapshot.queryParamMap.get('token');
    if (!token) {
      this.message.set('Ce lien de confirmation est incomplet.');
      this.state.set('failed');
      return;
    }

    try {
      await this.auth.confirmEmail(token);
      this.state.set('confirmed');
    } catch (error) {
      this.message.set(problemMessage(error, 'Ce lien est invalide ou a expiré.'));
      this.state.set('failed');
    }
  }
}
