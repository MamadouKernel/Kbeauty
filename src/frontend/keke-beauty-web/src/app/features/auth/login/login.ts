import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import {
  AbstractControl,
  NonNullableFormBuilder,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { InputText } from 'primeng/inputtext';
import { Password } from 'primeng/password';
import { problemMessage } from '../../../core/auth/auth.models';
import { AuthService } from '../../../core/auth/auth.service';
import { GoogleSignIn } from '../../../shared/google-sign-in/google-sign-in';

@Component({
  selector: 'app-login',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink, InputText, Password, GoogleSignIn],
  template: `
    <section class="auth-page" aria-labelledby="login-title">
      <div class="auth-card">
        <p class="eyebrow">Espace membre</p>
        <h1 id="login-title">Connexion</h1>
        <p class="auth-lead">Accédez à votre compte Keke Beauty.</p>
        @if (inactive) {
          <p class="form-error" role="status">
            Vous avez été déconnecté après une période d'inactivité. Reconnectez-vous pour
            continuer.
          </p>
        }

        <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
          <div class="field">
            <label for="login-email">Adresse e-mail</label>
            <input
              pInputText
              id="login-email"
              type="email"
              formControlName="email"
              autocomplete="email"
              inputmode="email"
            />
            @if (invalid(form.controls.email)) {
              <p class="field-error" role="alert">Saisissez une adresse e-mail valide.</p>
            }
          </div>

          <div class="field">
            <label for="login-password">Mot de passe</label>
            <p-password
              inputId="login-password"
              formControlName="password"
              autocomplete="current-password"
              [feedback]="false"
              [toggleMask]="true"
              [fluid]="true"
            />
            @if (invalid(form.controls.password)) {
              <p class="field-error" role="alert">Saisissez votre mot de passe.</p>
            }
          </div>

          <a class="link" routerLink="/mot-de-passe-oublie">Mot de passe oublié ?</a>

          @if (error()) {
            <p class="form-error" role="alert">{{ error() }}</p>
          }

          <button class="btn btn-primary" type="submit" [disabled]="busy()">
            {{ busy() ? 'Connexion…' : 'Se connecter' }}
          </button>
        </form>

        @if (
          providers.hasValue() &&
            providers.value().google.enabled &&
            providers.value().google.clientId;
          as clientId
        ) {
          <div class="auth-divider"><span>ou</span></div>
          <app-google-sign-in [clientId]="clientId" (credential)="signInWithGoogle($event)" />
        }

        <p class="auth-switch">
          Pas encore de compte ? <a class="link" routerLink="/inscription">Créer un compte</a>
        </p>
      </div>
    </section>
  `,
})
export class Login {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  /** Set by the idle sign-out (`?inactif=1`). */
  protected readonly inactive = this.route.snapshot.queryParamMap.has('inactif');

  protected readonly providers = this.auth.providers;
  protected readonly busy = signal(false);
  protected readonly error = signal('');

  protected readonly form = inject(NonNullableFormBuilder).group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required]],
  });

  protected invalid(control: AbstractControl): boolean {
    return control.invalid && (control.touched || control.dirty);
  }

  protected async submit(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const { email, password } = this.form.getRawValue();
    await this.run(() => this.auth.login(email, password));
  }

  protected async signInWithGoogle(idToken: string): Promise<void> {
    await this.run(() => this.auth.loginWithGoogle(idToken));
  }

  private async run(action: () => Promise<unknown>): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    try {
      await action();
      await this.router.navigate(['/verification'], {
        queryParamsHandling: 'preserve',
        relativeTo: this.route,
      });
    } catch (error) {
      this.error.set(problemMessage(error, 'Connexion impossible. Réessayez.'));
    } finally {
      this.busy.set(false);
    }
  }
}
