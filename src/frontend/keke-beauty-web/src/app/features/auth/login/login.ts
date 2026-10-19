import { AuthLayout } from '../../../shared/auth-layout/auth-layout';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import {
  AbstractControl,
  NonNullableFormBuilder,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { LucideArrowRight } from '@lucide/angular';
import { InputText } from 'primeng/inputtext';
import { Password } from 'primeng/password';
import { problemMessage } from '../../../core/auth/auth.models';
import { AuthService } from '../../../core/auth/auth.service';
import { GoogleSignIn } from '../../../shared/google-sign-in/google-sign-in';

@Component({
  selector: 'app-login',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    AuthLayout,
    ReactiveFormsModule,
    RouterLink,
    InputText,
    Password,
    GoogleSignIn,
    LucideArrowRight,
  ],
  styles: `
    .forgot {
      justify-self: end;
      margin-top: -0.4rem;
      font-size: var(--fs-sm);
    }
    .submit {
      display: inline-flex;
      justify-content: center;
      align-items: center;
      gap: 0.5rem;

      svg {
        width: 1.15rem;
        height: 1.15rem;
      }
    }
  `,
  template: `
    <app-auth-layout labelledBy="login-title">
      <p class="eyebrow">Bon retour parmi nous</p>
      <h1 id="login-title">Connexion</h1>
      <p class="auth-lead">Retrouvez vos rendez-vous, vos favoris et vos points de fidélité.</p>
      @if (inactive) {
        <p class="form-error" role="status">
          Vous avez été déconnecté(e) après une période d’inactivité. Reconnectez-vous pour
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
            placeholder="vous@exemple.com"
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

        <a class="link forgot" routerLink="/mot-de-passe-oublie">Mot de passe oublié ?</a>

        @if (error()) {
          <p class="form-error" role="alert">{{ error() }}</p>
        }

        <button class="btn btn-primary submit" type="submit" [disabled]="busy()">
          {{ busy() ? 'Connexion en cours…' : 'Se connecter' }}
          @if (!busy()) {
            <svg lucideArrowRight aria-hidden="true"></svg>
          }
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

      @if (providers.hasValue() && providers.value().whatsApp.enabled) {
        <div class="auth-divider"><span>ou</span></div>
        @if (!whatsAppStep()) {
          <button
            type="button"
            class="btn btn-ghost"
            [disabled]="busy()"
            (click)="whatsAppStep.set('phone')"
          >
            Se connecter avec WhatsApp
          </button>
        } @else {
          <div class="field">
            <label for="login-whatsapp-phone">Numéro WhatsApp</label>
            <input
              pInputText
              id="login-whatsapp-phone"
              type="tel"
              [formControl]="whatsAppForm.controls.phone"
              placeholder="+225 07 00 00 00 00"
              autocomplete="tel"
              inputmode="tel"
            />
          </div>

          @if (whatsAppStep() === 'code') {
            <div class="field">
              <label for="login-whatsapp-code">Code reçu sur WhatsApp</label>
              <input
                pInputText
                id="login-whatsapp-code"
                type="text"
                [formControl]="whatsAppForm.controls.code"
                autocomplete="one-time-code"
                inputmode="numeric"
                maxlength="6"
              />
            </div>
            <button
              type="button"
              class="btn btn-primary"
              [disabled]="busy()"
              (click)="verifyWhatsAppCode()"
            >
              {{ busy() ? 'Vérification…' : 'Valider le code WhatsApp' }}
            </button>
          } @else {
            <button
              type="button"
              class="btn btn-ghost"
              [disabled]="busy()"
              (click)="sendWhatsAppCode()"
            >
              {{ busy() ? 'Envoi…' : 'Recevoir un code WhatsApp' }}
            </button>
          }
        }
      }

      <p class="auth-switch">
        Pas encore de compte ? <a class="link" routerLink="/inscription">Créer un compte</a>
      </p>
    </app-auth-layout>
  `,
})
export class Login {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly fb = inject(NonNullableFormBuilder);

  /** Set by the idle sign-out (`?inactif=1`). */
  protected readonly inactive = this.route.snapshot.queryParamMap.has('inactif');

  protected readonly providers = this.auth.providers;
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly whatsAppStep = signal<'phone' | 'code' | null>(null);

  protected readonly form = this.fb.group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required]],
  });

  protected readonly whatsAppForm = this.fb.group({
    phone: ['', [Validators.required]],
    code: ['', [Validators.required]],
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

  protected async sendWhatsAppCode(): Promise<void> {
    const phone = this.whatsAppForm.controls.phone.value.trim();
    if (!phone) {
      this.error.set('Saisissez votre numéro WhatsApp.');
      return;
    }

    this.busy.set(true);
    this.error.set('');
    try {
      await this.auth.requestWhatsAppOtp(phone);
      this.whatsAppStep.set('code');
    } catch (error) {
      this.error.set(problemMessage(error, "Impossible d'envoyer le code WhatsApp."));
    } finally {
      this.busy.set(false);
    }
  }

  protected async verifyWhatsAppCode(): Promise<void> {
    const { phone, code } = this.whatsAppForm.getRawValue();
    if (!phone.trim() || !code.trim()) {
      this.error.set('Saisissez le code à 6 chiffres reçu sur WhatsApp.');
      return;
    }

    await this.run(() => this.auth.verifyWhatsAppOtp(phone.trim(), code.trim()));
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
