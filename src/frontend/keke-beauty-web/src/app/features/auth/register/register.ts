import { AuthLayout } from '../../../shared/auth-layout/auth-layout';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import {
  AbstractControl,
  NonNullableFormBuilder,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import {
  LucideArrowRight,
  LucideCircle,
  LucideCircleCheck,
  LucideSparkles,
  LucideStore,
} from '@lucide/angular';
import { InputText } from 'primeng/inputtext';
import { Password } from 'primeng/password';
import { AccountRole, problemMessage } from '../../../core/auth/auth.models';
import { AuthService } from '../../../core/auth/auth.service';

@Component({
  selector: 'app-register',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    AuthLayout,
    ReactiveFormsModule,
    RouterLink,
    InputText,
    Password,
    LucideArrowRight,
    LucideCircle,
    LucideCircleCheck,
    LucideSparkles,
    LucideStore,
  ],
  styles: `
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
      margin-bottom: 0.5rem;
      font-size: var(--fs-sm);
      font-weight: 600;
    }
    .role {
      position: relative;
      display: grid;
      grid-template-columns: auto 1fr;
      column-gap: 0.75rem;
      row-gap: 0.125rem;
      align-items: center;
      padding: 0.875rem 1rem;
      border: 1.5px solid var(--kb-border);
      border-radius: var(--radius-md);
      cursor: pointer;
      transition:
        border-color 200ms ease,
        background-color 200ms ease,
        box-shadow 200ms ease;

      &:hover {
        border-color: var(--kb-primary-300);
      }
      &:has(input:checked) {
        border-color: var(--kb-primary-500);
        background: var(--kb-primary-50);
        box-shadow: 0 10px 24px -18px rgb(63 15 82 / 60%);
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
      .icon {
        grid-row: span 2;
        display: inline-grid;
        place-items: center;
        width: 2.5rem;
        height: 2.5rem;
        border-radius: 0.85rem;
        background: var(--kb-primary-100);
        color: var(--kb-primary-700);

        svg {
          width: 1.25rem;
          height: 1.25rem;
        }
      }
      strong {
        font-size: var(--fs-md);
      }
      span:not(.icon) {
        font-size: var(--fs-xs);
        color: var(--kb-ink-muted);
      }
    }
    .pair {
      display: grid;
      gap: 1.1rem;

      @media (min-width: 560px) {
        grid-template-columns: 1fr 1fr;
      }
    }
    .rules {
      display: flex;
      flex-wrap: wrap;
      gap: 0.4rem 1rem;
      margin: 0;
      padding: 0;
      list-style: none;
      font-size: var(--fs-xs);
      color: var(--kb-ink-muted);

      li {
        display: inline-flex;
        align-items: center;
        gap: 0.3rem;
      }
      li.ok {
        color: var(--kb-success);
        font-weight: 600;
      }
      svg {
        width: 0.95rem;
        height: 0.95rem;
      }
    }
    .consent {
      display: flex;
      align-items: flex-start;
      gap: 0.6rem;
      font-size: var(--fs-md);

      input {
        flex-shrink: 0;
        width: 1.15rem;
        height: 1.15rem;
        margin-top: 0.2rem;
        accent-color: var(--kb-primary-600);
      }
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
    <app-auth-layout
      labelledBy="register-title"
      [title]="isPro() ? 'Développez votre' : 'Rejoignez'"
      [accent]="isPro() ? 'salon.' : 'Keke Beauty.'"
      [points]="isPro() ? proPoints : clientPoints"
    >
      @if (done()) {
        <p class="eyebrow">Presque terminé</p>
        <h1 id="register-title">Vérifiez votre boîte mail</h1>
        <p class="form-success" role="status">
          Si cette adresse peut être utilisée, un e-mail de confirmation vient de lui être envoyé.
          Cliquez sur le lien qu’il contient pour activer votre compte.
        </p>
        <a class="btn btn-ghost" routerLink="/connexion">Aller à la connexion</a>
      } @else {
        <p class="eyebrow">Inscription gratuite</p>
        <h1 id="register-title">Créer un compte</h1>

        <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
          <fieldset class="roles">
            <legend>Vous êtes</legend>
            <label class="role">
              <input type="radio" formControlName="role" value="Client" />
              <span class="icon"><svg lucideSparkles aria-hidden="true"></svg></span>
              <strong>Client</strong>
              <span>Je cherche un soin et je réserve.</span>
            </label>
            <label class="role">
              <input type="radio" formControlName="role" value="PartnerOwner" />
              <span class="icon"><svg lucideStore aria-hidden="true"></svg></span>
              <strong>Professionnel</strong>
              <span>Je gère un salon, un spa ou une onglerie.</span>
            </label>
          </fieldset>

          <div class="pair">
            <div class="field">
              <label for="reg-first">Prénom</label>
              <input
                pInputText
                id="reg-first"
                formControlName="firstName"
                autocomplete="given-name"
                placeholder="Ex. : Awa"
              />
              @if (invalid(form.controls.firstName)) {
                <p class="field-error" role="alert">Indiquez votre prénom.</p>
              }
            </div>
            <div class="field">
              <label for="reg-last">Nom</label>
              <input
                pInputText
                id="reg-last"
                formControlName="lastName"
                autocomplete="family-name"
                placeholder="Ex. : Koné"
              />
              @if (invalid(form.controls.lastName)) {
                <p class="field-error" role="alert">Indiquez votre nom.</p>
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
              placeholder="vous@exemple.com"
            />
            @if (invalid(form.controls.email)) {
              <p class="field-error" role="alert">Saisissez une adresse e-mail valide.</p>
            }
          </div>

          <div class="pair">
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
              @if (invalid(form.controls.password)) {
                <p class="field-error" role="alert">
                  Le mot de passe ne respecte pas encore les règles ci-dessous.
                </p>
              }
            </div>
            <div class="field">
              <label for="reg-confirm">Confirmer le mot de passe</label>
              <p-password
                inputId="reg-confirm"
                formControlName="confirmPassword"
                autocomplete="new-password"
                [feedback]="false"
                [toggleMask]="true"
                [fluid]="true"
              />
              @if (mismatch()) {
                <p class="field-error" role="alert">
                  Les deux mots de passe ne sont pas identiques.
                </p>
              }
            </div>
          </div>
          <ul class="rules" aria-label="Règles du mot de passe">
            @for (rule of rules(); track rule.label) {
              <li [class.ok]="rule.ok">
                @if (rule.ok) {
                  <svg lucideCircleCheck aria-hidden="true"></svg>
                } @else {
                  <svg lucideCircle aria-hidden="true"></svg>
                }
                {{ rule.label
                }}<span class="visually-hidden">{{
                  rule.ok ? ' : respecté' : ' : à respecter'
                }}</span>
              </li>
            }
          </ul>

          <div class="field">
            <label class="consent">
              <input type="checkbox" formControlName="acceptTerms" />
              <span>
                J’accepte les
                <a class="link" routerLink="/cgu" target="_blank">conditions d’utilisation</a>
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

          <button class="btn btn-primary submit" type="submit" [disabled]="busy()">
            {{ busy() ? 'Création en cours…' : 'Créer mon compte' }}
            @if (!busy()) {
              <svg lucideArrowRight aria-hidden="true"></svg>
            }
          </button>
        </form>

        <p class="auth-switch">
          Vous avez déjà un compte ? <a class="link" routerLink="/connexion">Se connecter</a>
        </p>
      }
    </app-auth-layout>
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
    password: [
      '',
      [
        Validators.required,
        Validators.minLength(10),
        Validators.pattern(/^(?=.*\p{L})(?=.*\d).+$/u),
      ],
    ],
    confirmPassword: ['', [Validators.required]],
    acceptTerms: [false, [Validators.requiredTrue]],
  });

  private readonly values = toSignal(this.form.valueChanges, {
    initialValue: this.form.getRawValue(),
  });

  protected readonly isPro = computed(() => this.values().role === 'PartnerOwner');

  /** The password rules of the API, ticked as the person types. */
  protected readonly rules = computed(() => {
    const password = this.values().password ?? '';
    return [
      { label: '10 caractères minimum', ok: password.length >= 10 },
      { label: 'Au moins une lettre', ok: /\p{L}/u.test(password) },
      { label: 'Au moins un chiffre', ok: /\d/.test(password) },
    ];
  });

  protected readonly mismatch = computed(() => {
    const { password, confirmPassword } = this.values();
    return !!confirmPassword && confirmPassword !== password;
  });

  protected readonly clientPoints = [
    'Des salons, spas et ongleries vérifiés à Abidjan',
    'Réservation en ligne, confirmation immédiate',
    'Points de fidélité et cartes cadeaux',
  ];

  protected readonly proPoints = [
    'Une fiche visible par toutes les clientes d’Abidjan',
    'Agenda en ligne, rappels et équipe',
    'Fidélité, cartes cadeaux et statistiques',
  ];

  protected invalid(control: AbstractControl): boolean {
    return control.invalid && (control.touched || control.dirty);
  }

  protected async submit(): Promise<void> {
    if (this.form.invalid || this.mismatch()) {
      this.form.markAllAsTouched();
      return;
    }

    this.busy.set(true);
    this.error.set('');
    try {
      // The confirmation is checked here and never sent.
      const { role, firstName, lastName, email, password, acceptTerms } = this.form.getRawValue();
      await this.auth.register({ role, firstName, lastName, email, password, acceptTerms });
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
