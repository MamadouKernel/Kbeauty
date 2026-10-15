import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import {
  AbstractControl,
  NonNullableFormBuilder,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { InputText } from 'primeng/inputtext';
import { Password } from 'primeng/password';
import { AccountRole, problemMessage } from '../../../core/auth/auth.models';
import { AuthService } from '../../../core/auth/auth.service';

@Component({
  selector: 'app-register',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink, InputText, Password],
  styles: `
    .consent {
      display: flex;
      align-items: flex-start;
      gap: 0.5rem;
      font-size: var(--fs-md);
    }
    .roles {
      display: grid;
      gap: 0.75rem;
      border: 0;
      padding: 0;
      margin: 0;

      @media (min-width: 480px) {
        grid-template-columns: 1fr 1fr;
      }
    }
    legend {
      padding: 0;
      margin-bottom: 0.375rem;
      font-size: var(--fs-sm);
      font-weight: 600;
    }
    .role {
      display: grid;
      gap: 0.125rem;
      padding: 0.875rem 1rem;
      border: 1.5px solid var(--kb-border);
      border-radius: var(--radius-md);
      cursor: pointer;
      transition:
        border-color 200ms ease,
        background-color 200ms ease;

      &:has(input:checked) {
        border-color: var(--kb-primary-600);
        background: var(--kb-primary-50);
      }
      &:has(input:focus-visible) {
        outline: 3px solid var(--kb-primary-400);
        outline-offset: 2px;
      }
      input {
        position: absolute;
        opacity: 0;
        pointer-events: none;
      }
      strong {
        font-size: var(--fs-md);
      }
      span {
        font-size: var(--fs-xs);
        color: var(--kb-ink-muted);
      }
    }
    .names {
      display: grid;
      gap: 1rem;

      @media (min-width: 480px) {
        grid-template-columns: 1fr 1fr;
      }
    }
    .hint {
      font-size: var(--fs-xs);
      color: var(--kb-ink-muted);
    }
  `,
  template: `
    <section class="auth-page" aria-labelledby="register-title">
      <div class="auth-card">
        @if (done()) {
          <p class="eyebrow">Presque terminé</p>
          <h1 id="register-title">Vérifiez votre boîte mail</h1>
          <p class="form-success" role="status">
            Si cette adresse peut être utilisée, un e-mail de confirmation vient de lui être envoyé.
            Cliquez sur le lien qu'il contient pour activer votre compte.
          </p>
          <a class="btn btn-ghost" routerLink="/connexion">Aller à la connexion</a>
        } @else {
          <p class="eyebrow">Rejoignez Keke Beauty</p>
          <h1 id="register-title">Créer un compte</h1>

          <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
            <fieldset class="roles">
              <legend>Je suis</legend>
              <label class="role">
                <input type="radio" formControlName="role" value="Client" />
                <strong>Client</strong>
                <span>Je cherche un soin et je réserve.</span>
              </label>
              <label class="role">
                <input type="radio" formControlName="role" value="PartnerOwner" />
                <strong>Professionnel</strong>
                <span>Je gère un salon, un spa ou une onglerie.</span>
              </label>
            </fieldset>

            <div class="names">
              <div class="field">
                <label for="reg-first">Prénom</label>
                <input
                  pInputText
                  id="reg-first"
                  formControlName="firstName"
                  autocomplete="given-name"
                />
                @if (invalid(form.controls.firstName)) {
                  <p class="field-error" role="alert">Votre prénom est requis.</p>
                }
              </div>
              <div class="field">
                <label for="reg-last">Nom</label>
                <input
                  pInputText
                  id="reg-last"
                  formControlName="lastName"
                  autocomplete="family-name"
                />
                @if (invalid(form.controls.lastName)) {
                  <p class="field-error" role="alert">Votre nom est requis.</p>
                }
              </div>
            </div>

            <div class="field">
              <label for="reg-email">Adresse e-mail</label>
              <input
                pInputText
                id="reg-email"
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
              <label for="reg-password">Mot de passe</label>
              <p-password
                inputId="reg-password"
                formControlName="password"
                autocomplete="new-password"
                [feedback]="false"
                [toggleMask]="true"
                [fluid]="true"
              />
              <p class="hint">10 caractères minimum, avec au moins une lettre et un chiffre.</p>
              @if (invalid(form.controls.password)) {
                <p class="field-error" role="alert">
                  Le mot de passe doit contenir au moins 10 caractères.
                </p>
              }
            </div>

            <div class="field">
              <label class="consent">
                <input type="checkbox" formControlName="acceptTerms" />
                <span>
                  J'accepte les
                  <a class="link" routerLink="/cgu" target="_blank">conditions d'utilisation</a>
                  et la
                  <a class="link" routerLink="/confidentialite" target="_blank"
                    >politique de confidentialité</a
                  >.
                </span>
              </label>
              @if (invalid(form.controls.acceptTerms)) {
                <p class="field-error" role="alert">
                  Vous devez accepter les conditions pour créer un compte.
                </p>
              }
            </div>

            @if (error()) {
              <p class="form-error" role="alert">{{ error() }}</p>
            }

            <button class="btn btn-primary" type="submit" [disabled]="busy()">
              {{ busy() ? 'Création…' : 'Créer mon compte' }}
            </button>
          </form>

          <p class="auth-switch">
            Déjà inscrit ? <a class="link" routerLink="/connexion">Se connecter</a>
          </p>
        }
      </div>
    </section>
  `,
})
export class Register {
  private readonly auth = inject(AuthService);
  private readonly route = inject(ActivatedRoute);

  protected readonly busy = signal(false);
  protected readonly done = signal(false);
  protected readonly error = signal('');

  protected readonly form = inject(NonNullableFormBuilder).group({
    role: [
      (this.route.snapshot.queryParamMap.get('type') === 'partenaire'
        ? 'PartnerOwner'
        : 'Client') as AccountRole,
    ],
    firstName: ['', [Validators.required, Validators.maxLength(100)]],
    lastName: ['', [Validators.required, Validators.maxLength(100)]],
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required, Validators.minLength(10)]],
    acceptTerms: [false, [Validators.requiredTrue]],
  });

  protected invalid(control: AbstractControl): boolean {
    return control.invalid && (control.touched || control.dirty);
  }

  protected async submit(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    this.busy.set(true);
    this.error.set('');
    try {
      await this.auth.register(this.form.getRawValue());
      this.done.set(true);
    } catch (error) {
      this.error.set(
        problemMessage(error, 'Inscription impossible. Vérifiez les informations saisies.'),
      );
    } finally {
      this.busy.set(false);
    }
  }
}
