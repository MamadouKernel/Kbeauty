import { AuthLayout } from '../../../shared/auth-layout/auth-layout';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { InputText } from 'primeng/inputtext';
import { problemMessage } from '../../../core/auth/auth.models';
import { AuthService } from '../../../core/auth/auth.service';

@Component({
  selector: 'app-forgot-password',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [AuthLayout, ReactiveFormsModule, RouterLink, InputText],
  template: `
    <app-auth-layout labelledBy="forgot-title">
      <p class="eyebrow">Mot de passe oublié</p>
      <h1 id="forgot-title">Réinitialiser mon mot de passe</h1>

      @if (sent()) {
        <p class="form-success" role="status">
          Si un compte existe pour cette adresse, un e-mail contenant un lien de réinitialisation
          (valable 1 heure) vient d'être envoyé.
        </p>
        <a class="btn btn-ghost" routerLink="/connexion">Retour à la connexion</a>
      } @else {
        <p class="auth-lead">
          Saisissez l'adresse e-mail de votre compte, nous vous enverrons un lien.
        </p>
        <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
          <div class="field">
            <label for="forgot-email">Adresse e-mail</label>
            <input
              pInputText
              id="forgot-email"
              type="email"
              formControlName="email"
              autocomplete="email"
              inputmode="email"
            />
            @if (form.controls.email.invalid && form.controls.email.touched) {
              <p class="field-error" role="alert">Saisissez une adresse e-mail valide.</p>
            }
          </div>

          @if (error()) {
            <p class="form-error" role="alert">{{ error() }}</p>
          }

          <button class="btn btn-primary" type="submit" [disabled]="busy()">
            {{ busy() ? 'Envoi…' : 'Envoyer le lien' }}
          </button>
        </form>
        <p class="auth-switch">
          <a class="link" routerLink="/connexion">Retour à la connexion</a>
        </p>
      }
    </app-auth-layout>
  `,
})
export class ForgotPassword {
  private readonly auth = inject(AuthService);

  protected readonly busy = signal(false);
  protected readonly sent = signal(false);
  protected readonly error = signal('');

  protected readonly form = inject(NonNullableFormBuilder).group({
    email: ['', [Validators.required, Validators.email]],
  });

  protected async submit(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.busy.set(true);
    this.error.set('');
    try {
      await this.auth.forgotPassword(this.form.getRawValue().email);
      this.sent.set(true);
    } catch (error) {
      this.error.set(problemMessage(error));
    } finally {
      this.busy.set(false);
    }
  }
}
