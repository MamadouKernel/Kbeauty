import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  resource,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AdminApiService } from '../../../core/admin/admin-api.service';
import { problemMessage } from '../../../core/auth/auth.models';

const MAX_SENDER_LENGTH = 11;

/** Health of the notification channels, LeTexto balance, SMS test and sender creation (PRD NOT-01, NOT-02). */
@Component({
  selector: 'app-channels-tab',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  styles: `
    .states {
      display: grid;
      gap: 0.75rem;
      grid-template-columns: repeat(auto-fit, minmax(13rem, 1fr));
    }
    .state {
      display: grid;
      gap: 0.25rem;
      padding: 0.875rem;
      border: 1px solid var(--kb-border);
      border-radius: var(--radius-md);
    }
    .state strong {
      font-size: 1rem;
    }
    .hint {
      margin: 0;
      font-size: var(--fs-sm);
      color: var(--kb-ink-muted);
    }
  `,
  template: `
    <section class="admin-card" aria-labelledby="channels-title">
      <h2 id="channels-title">État des canaux</h2>
      @if (status.hasValue()) {
        @let s = status.value();
        <div class="states">
          @for (c of cards(); track c.label) {
            <div class="state">
              <strong>{{ c.label }}</strong>
              <span class="badge" [attr.data-tone]="c.enabled && c.providerReady ? 'ok' : 'warn'">
                {{
                  c.enabled
                    ? c.providerReady
                      ? 'Actif'
                      : 'Activé, fournisseur non prêt'
                    : 'Désactivé'
                }}
              </span>
              <span class="hint">Fournisseur : {{ c.provider }}</span>
              @if (c.detail) {
                <span class="hint">{{ c.detail }}</span>
              }
            </div>
          }
        </div>
        @if (s.leTextoBalance) {
          <p>Solde LeTexto : {{ s.leTextoBalance }}</p>
        }
        <p class="hint">
          Les canaux s'activent dans l'onglet « Général » ; la clé d'API et les expéditeurs LeTexto
          se configurent sur le serveur (variables d'environnement, voir le guide de déploiement).
        </p>
      } @else if (status.error()) {
        <p class="form-error" role="alert">Impossible de charger l'état des canaux.</p>
      }
    </section>

    <section class="admin-card" aria-labelledby="sms-test-title">
      <h2 id="sms-test-title">Envoyer un SMS de test</h2>
      <form class="admin-filters" (ngSubmit)="sendTest()">
        <label>
          Numéro
          <input
            class="admin-input"
            name="phone"
            type="tel"
            inputmode="tel"
            autocomplete="off"
            placeholder="07 00 00 00 00"
            [(ngModel)]="phone"
          />
        </label>
        <button type="submit" class="btn btn-primary" [disabled]="busy() || !phone.trim()">
          Envoyer
        </button>
      </form>
    </section>

    <section class="admin-card" aria-labelledby="sender-title">
      <h2 id="sender-title">Ajouter un expéditeur SMS</h2>
      <form class="admin-filters" (ngSubmit)="addSender()">
        <label>
          Nom (11 caractères au plus)
          <input
            class="admin-input"
            name="sender"
            maxlength="11"
            autocomplete="off"
            [(ngModel)]="sender"
          />
        </label>
        <button type="submit" class="btn btn-primary" [disabled]="busy() || !senderValid()">
          Demander à LeTexto
        </button>
      </form>
      <p class="hint">
        LeTexto valide l'expéditeur avant son utilisation ; une fois validé, indiquez-le dans la
        configuration du serveur (<code>LETEXTO_SMS_SENDER</code>).
      </p>
    </section>

    @if (message(); as text) {
      <p role="status" class="badge" data-tone="ok">{{ text }}</p>
    }
    @if (error(); as text) {
      <p class="form-error" role="alert">{{ text }}</p>
    }
  `,
})
export class ChannelsTab {
  private readonly api = inject(AdminApiService);

  protected phone = '';
  protected sender = '';
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly message = signal<string | null>(null);
  protected readonly status = resource({ loader: () => this.api.channels() });

  protected readonly cards = computed(() => {
    const s = this.status.value();
    return s
      ? [
          { label: 'SMS', ...s.sms },
          { label: 'WhatsApp', ...s.whatsApp },
          { label: 'Notifications push', ...s.push },
          { label: 'E-mail', ...s.email },
        ]
      : [];
  });

  protected senderValid(): boolean {
    const name = this.sender.trim();
    return name.length >= 3 && name.length <= MAX_SENDER_LENGTH;
  }

  protected sendTest(): Promise<void> {
    return this.run(async () => {
      await this.api.sendTestSms(this.phone.trim());
      return 'SMS de test envoyé (ou consigné dans les journaux tant que LeTexto est désactivé).';
    });
  }

  protected addSender(): Promise<void> {
    return this.run(async () => {
      await this.api.createSender(this.sender.trim());
      this.sender = '';
      return 'Demande d’expéditeur envoyée à LeTexto.';
    });
  }

  private async run(action: () => Promise<string>): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    this.message.set(null);
    try {
      this.message.set(await action());
      this.status.reload();
    } catch (e) {
      this.error.set(problemMessage(e));
    } finally {
      this.busy.set(false);
    }
  }
}
