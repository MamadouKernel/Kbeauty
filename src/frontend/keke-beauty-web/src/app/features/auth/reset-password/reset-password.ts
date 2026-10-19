import { AuthLayout } from '../../../shared/auth-layout/auth-layout';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Password } from 'primeng/password';
import { problemMessage } from '../../../core/auth/auth.models';
import { AuthService } from '../../../core/auth/auth.service';

@Component({
  selector: 'app-reset-password',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [AuthLayout, ReactiveFormsModule, RouterLink, Password],
  template: `
    <app-auth-layout labelledBy="reset-title">
      <p class="eyebrow">Nouveau mot de passe</p>
      <h1 id="reset-title">Choisir un mot de passe</h1>

      @if (done()) {
        <p class="form-success" role="status">
          Mot de passe modifié. Toutes vos sessions ont été fermées par sécurité : reconnectez-vous.
        </p>
        <a class="btn btn-primary" routerLink="/connexion">Se connecter</a>
      } @else if (!token) {
        <p class="form-error" role="alert">Ce lien de réinitialisation est incomplet.</p>
        <a class="btn btn-ghost" routerLink="/mot-de-passe-oublie">Demander un nouveau lien</a>
      } @else {
        <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
          <div class="field">
            <label for="reset-password">Nouveau mot de passe</label>
            <p-password
              inputId="reset-password"
              formControlName="password"
              autocomplete="new-password"
              [feedback]="false"
              [toggleMask]="true"
              [fluid]="true"
            />
            <p class="auth-lead">10 caractères minimum, avec au moins une lettre et un chiffre.</p>
            @if (form.controls.password.invalid && form.controls.password.touched) {
              <p class="field-error" role="alert">
                Le mot de passe doit contenir au moins 10 caractères.
              </p>
            }
          </div>

          @if (error()) {
            <p class="form-error" role="alert">{{ error() }}</p>
          }

          <button class="btn btn-primary" type="submit" [disabled]="busy()">
            {{ busy() ? 'Enregistrement…' : 'Enregistrer' }}
          </button>
        </form>
      }
    </app-auth-layout>
  `,
})
export class ResetPassword {
  private readonly auth = inject(AuthService);

  protected readonly token = inject(ActivatedRoute).snapshot.queryParamMap.get('token');
  protected readonly busy = signal(false);
  protected readonly done = signal(false);
  protected readonly error = signal('');

  protected readonly form = inject(NonNullableFormBuilder).group({
    password: ['', [Validators.required, Validators.minLength(10)]],
  });

  protected async submit(): Promise<void> {
    if (this.form.invalid || !this.token) {
      this.form.markAllAsTouched();
      return;
    }

    this.busy.set(true);
    this.error.set('');
    try {
      await this.auth.resetPassword(this.token, this.form.getRawValue().password);
      this.done.set(true);
    } catch (error) {
      this.error.set(problemMessage(error, 'Ce lien est invalide ou a expiré.'));
    } finally {
      this.busy.set(false);
    }
  }
}
