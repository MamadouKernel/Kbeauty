import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { LucideCopy, LucideShieldCheck } from '@lucide/angular';
import { InputText } from 'primeng/inputtext';
import { problemCode, problemMessage } from '../../../core/auth/auth.models';
import { safeReturnUrl } from '../../../core/auth/auth.guards';
import { AuthService } from '../../../core/auth/auth.service';

type Phase = 'loading' | 'setup' | 'verify' | 'recovery' | 'expired';

/** Second sign-in step: Google Authenticator enrolment (first time) or code entry (every other time). */
@Component({
  selector: 'app-two-factor',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    NgTemplateOutlet,
    RouterLink,
    InputText,
    LucideCopy,
    LucideShieldCheck,
  ],
  styles: `
    .shield {
      width: 2.5rem;
      height: 2.5rem;
      color: var(--kb-primary-600);
    }
    ol {
      display: grid;
      gap: 0.5rem;
      padding-left: 1.25rem;
      list-style: decimal;
      color: var(--kb-ink-muted);
    }
    .qr {
      justify-self: center;
      padding: 0.75rem;
      border: 1px solid var(--kb-border);
      border-radius: var(--radius-md);
      background: #fff;
    }
    .secret {
      justify-self: center;
      padding: 0.5rem 0.75rem;
      border-radius: var(--radius-sm);
      background: var(--kb-primary-50);
      font-size: 0.9375rem;
      letter-spacing: 0.08em;
      word-break: break-all;
      text-align: center;
    }
    .code-input {
      font-size: 1.5rem;
      letter-spacing: 0.35em;
      text-align: center;
    }
    .codes {
      display: grid;
      grid-template-columns: 1fr 1fr;
      gap: 0.5rem;
      padding: 1rem;
      border: 1px dashed var(--kb-primary-300);
      border-radius: var(--radius-md);
      background: var(--kb-primary-50);
      font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
      font-size: 1rem;
      font-weight: 600;
      text-align: center;
    }
    .confirm {
      display: flex;
      gap: 0.625rem;
      align-items: flex-start;
      font-size: 0.9375rem;
    }
    .confirm input {
      width: 1.25rem;
      height: 1.25rem;
      margin-top: 0.2rem;
      accent-color: var(--kb-primary-600);
    }
  `,
  template: `
    <section class="auth-page" aria-labelledby="twofa-title">
      <div class="auth-card">
        @switch (phase()) {
          @case ('loading') {
            <h1 id="twofa-title">Sécurisation du compte…</h1>
            <p class="auth-lead" role="status">Préparation de la double authentification.</p>
          }

          @case ('setup') {
            <svg lucideShieldCheck class="shield" aria-hidden="true"></svg>
            <p class="eyebrow">Étape de sécurité</p>
            <h1 id="twofa-title">Activer Google Authenticator</h1>
            <p class="auth-lead">
              La double authentification protège votre compte, même si votre mot de passe fuite.
            </p>
            <ol>
              <li>
                Installez <strong>Google Authenticator</strong> (ou une application compatible) sur
                votre téléphone.
              </li>
              <li>Scannez le QR code ci-dessous, ou saisissez la clé manuellement.</li>
              <li>Entrez le code à 6 chiffres affiché par l'application.</li>
            </ol>

            @if (qr()) {
              <img
                class="qr"
                [src]="qr()"
                width="220"
                height="220"
                alt="QR code à scanner avec Google Authenticator"
              />
            }
            <p class="secret">
              <span class="visually-hidden">Clé de configuration manuelle : </span
              ><code>{{ spacedSecret() }}</code>
            </p>

            <ng-container *ngTemplateOutlet="codeForm" />
          }

          @case ('verify') {
            <svg lucideShieldCheck class="shield" aria-hidden="true"></svg>
            <p class="eyebrow">Vérification en deux étapes</p>
            <h1 id="twofa-title">Entrez votre code</h1>
            <p class="auth-lead">
              Saisissez le code à 6 chiffres de Google Authenticator, ou l'un de vos codes de
              récupération.
            </p>
            <ng-container *ngTemplateOutlet="codeForm" />
          }

          @case ('recovery') {
            <svg lucideShieldCheck class="shield" aria-hidden="true"></svg>
            <p class="eyebrow">Double authentification activée</p>
            <h1 id="twofa-title">Conservez vos codes de récupération</h1>
            <p class="auth-lead">
              Chaque code ne sert qu'une fois si vous perdez votre téléphone. Ils ne seront
              <strong>plus jamais affichés</strong>.
            </p>
            <ul class="codes" aria-label="Codes de récupération">
              @for (code of recoveryCodes(); track code) {
                <li>{{ code }}</li>
              }
            </ul>
            <button type="button" class="btn btn-ghost" (click)="copyCodes()">
              <svg lucideCopy aria-hidden="true"></svg>
              {{ copied() ? 'Copiés !' : 'Copier les codes' }}
            </button>
            <label class="confirm">
              <input type="checkbox" [checked]="saved()" (change)="saved.set(!saved())" />
              <span>J'ai conservé ces codes dans un endroit sûr.</span>
            </label>
            <button type="button" class="btn btn-primary" [disabled]="!saved()" (click)="finish()">
              Continuer
            </button>
          }

          @default {
            <h1 id="twofa-title">Connexion expirée</h1>
            <p class="form-error" role="alert">{{ error() }}</p>
            <a class="btn btn-primary" routerLink="/connexion">Se reconnecter</a>
          }
        }
      </div>
    </section>

    <ng-template #codeForm>
      <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
        <div class="field">
          <label for="twofa-code">Code</label>
          <input
            pInputText
            id="twofa-code"
            class="code-input"
            formControlName="code"
            autocomplete="one-time-code"
            inputmode="numeric"
            maxlength="20"
          />
        </div>

        @if (error()) {
          <p class="form-error" role="alert">{{ error() }}</p>
        }

        <button class="btn btn-primary" type="submit" [disabled]="busy()">
          {{
            busy() ? 'Vérification…' : phase() === 'setup' ? 'Activer et me connecter' : 'Valider'
          }}
        </button>
      </form>
    </ng-template>
  `,
})
export class TwoFactor {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected readonly phase = signal<Phase>('loading');
  protected readonly qr = signal('');
  protected readonly secret = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly saved = signal(false);
  protected readonly copied = signal(false);
  protected readonly recoveryCodes = this.auth.recoveryCodes;
  protected readonly spacedSecret = computed(
    () =>
      this.secret()
        .match(/.{1,4}/g)
        ?.join(' ') ?? '',
  );

  protected readonly form = inject(NonNullableFormBuilder).group({
    code: ['', [Validators.required]],
  });

  constructor() {
    const challenge = this.auth.challenge();
    if (!challenge) {
      void this.router.navigateByUrl('/connexion');
      return;
    }

    if (challenge.status === 'TwoFactorSetupRequired') {
      void this.startSetup();
    } else {
      this.phase.set('verify');
    }
  }

  protected async submit(): Promise<void> {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const { code } = this.form.getRawValue();
    this.busy.set(true);
    this.error.set('');
    try {
      if (this.phase() === 'setup') {
        await this.auth.enableTwoFactor(code);
        this.phase.set('recovery');
      } else {
        await this.auth.verifyTwoFactor(code);
        await this.finish();
      }
    } catch (error) {
      if (problemCode(error) === 'auth.challenge_expired') {
        this.auth.clearChallenge();
        this.error.set(problemMessage(error));
        this.phase.set('expired');
      } else {
        this.error.set(problemMessage(error, 'Code invalide. Réessayez.'));
        this.form.reset();
      }
    } finally {
      this.busy.set(false);
    }
  }

  protected async copyCodes(): Promise<void> {
    try {
      await navigator.clipboard.writeText((this.recoveryCodes() ?? []).join('\n'));
      this.copied.set(true);
    } catch {
      this.copied.set(false); // Clipboard blocked: the codes stay visible to copy by hand.
    }
  }

  protected async finish(): Promise<void> {
    this.auth.clearChallenge();
    await this.router.navigateByUrl(safeReturnUrl(this.route.snapshot.queryParamMap.get('retour')));
  }

  private async startSetup(): Promise<void> {
    try {
      const setup = await this.auth.beginTwoFactorSetup();
      this.secret.set(setup.secret);
      this.phase.set('setup');
      await this.renderQr(setup.otpauthUri);
    } catch (error) {
      this.error.set(problemMessage(error, 'Impossible de préparer la double authentification.'));
      this.phase.set('expired');
    }
  }

  private async renderQr(uri: string): Promise<void> {
    try {
      const { default: QRCode } = await import('qrcode');
      this.qr.set(
        await QRCode.toDataURL(uri, {
          margin: 1,
          width: 220,
          color: { dark: '#36174f', light: '#ffffff' },
        }),
      );
    } catch {
      this.qr.set(''); // The manual key remains available.
    }
  }
}
