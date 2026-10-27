import {
  ChangeDetectionStrategy,
  Component,
  effect,
  inject,
  resource,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { problemMessage } from '../../core/auth/auth.models';
import { ChannelService, NotificationPreferences } from '../../core/notifications/channel.service';

/** "Mes notifications": which channels the client wants, this browser's push subscription, the WhatsApp number (PRD NOT-01). */
@Component({
  selector: 'app-notification-settings',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  styles: `
    :host {
      display: grid;
      gap: 1rem;
    }
    .block {
      display: grid;
      gap: 0.5rem;
      padding-top: 0.75rem;
      border-top: 1px solid var(--kb-border);
    }
    fieldset.block {
      margin: 0;
      padding-inline: 0;
      padding-bottom: 0;
      border-inline: 0;
      border-bottom: 0;
    }
    label.check {
      display: flex;
      align-items: center;
      gap: 0.625rem;
      min-height: 2.75rem;
      font-weight: 600;
    }
    label.check input {
      width: 1.25rem;
      height: 1.25rem;
    }
    .row {
      display: flex;
      flex-wrap: wrap;
      align-items: end;
      gap: 0.75rem;
    }
    .row label {
      display: grid;
      gap: 0.25rem;
      font-weight: 600;
    }
    .hint {
      margin: 0;
      font-size: var(--fs-sm);
      color: var(--kb-ink-muted);
    }
  `,
  template: `
    @if (preferences.hasValue()) {
      <fieldset class="block">
        <legend><strong>Je veux être prévenu(e) par</strong></legend>
        <label class="check">
          <input
            type="checkbox"
            name="push"
            [checked]="form.push"
            (change)="toggle('push', $any($event.target).checked)"
          />
          Notification (navigateur ou application installée)
        </label>
        <label class="check">
          <input
            type="checkbox"
            name="email"
            [checked]="form.email"
            (change)="toggle('email', $any($event.target).checked)"
          />
          E-mail (confirmations, rappels, annulations)
        </label>
        <label class="check">
          <input
            type="checkbox"
            name="sms"
            [checked]="form.sms"
            (change)="toggle('sms', $any($event.target).checked)"
          />
          SMS
        </label>
        <label class="check">
          <input
            type="checkbox"
            name="whatsApp"
            [checked]="form.whatsApp"
            (change)="toggle('whatsApp', $any($event.target).checked)"
          />
          WhatsApp
        </label>
        <label class="check">
          <input
            type="checkbox"
            name="reminders"
            [checked]="form.reminders"
            (change)="toggle('reminders', $any($event.target).checked)"
          />
          Rappels avant mes rendez-vous
        </label>
        <p class="hint">
          Les notifications dans l'application et les e-mails de compte (confirmation d'adresse, mot
          de passe oublié) sont toujours envoyés. Les SMS et WhatsApp ne partent que si ces canaux
          sont ouverts par la plateforme.
        </p>
      </fieldset>
    }

    <div class="block">
      <strong>Notifications sur cet appareil</strong>
      @if (!pushSupported) {
        <p class="hint">
          Ce navigateur ne gère pas les notifications push (ou l'application n'est pas servie en
          HTTPS). Installez l'application depuis le menu du navigateur pour les recevoir.
        </p>
      } @else if (subscribed()) {
        <p>Les notifications sont activées sur cet appareil.</p>
        <div>
          <button
            type="button"
            class="btn btn-ghost btn-sm"
            [disabled]="busy()"
            (click)="disablePush()"
          >
            Désactiver sur cet appareil
          </button>
        </div>
      } @else {
        <p class="hint">
          Recevez vos confirmations et rappels de rendez-vous même quand le site est fermé.
        </p>
        <div>
          <button
            type="button"
            class="btn btn-primary btn-sm"
            [disabled]="busy()"
            (click)="enablePush()"
          >
            Activer sur cet appareil
          </button>
        </div>
      }
    </div>

    @if (whatsApp.hasValue() && whatsApp.value().available) {
      @let w = whatsApp.value();
      <div class="block">
        <strong>WhatsApp</strong>
        @if (w.linked) {
          <p>Numéro lié : {{ w.phoneMasked }}</p>
          @if (consent.hasValue()) {
            @let c = consent.value();
            <fieldset class="block">
              <legend>
                <strong>Mon accord pour les messages WhatsApp</strong>
              </legend>
              <label class="check">
                <input
                  type="checkbox"
                  name="consentService"
                  [checked]="c.service.granted"
                  [disabled]="busy()"
                  (change)="setConsent('Service', $any($event.target).checked, c.textVersion)"
                />
                {{ c.serviceText }}
              </label>
              @if (c.service.granted && c.service.at) {
                <p class="hint">Accord enregistré le {{ stamp(c.service.at) }}.</p>
              }
              <label class="check">
                <input
                  type="checkbox"
                  name="consentMarketing"
                  [checked]="c.marketing.granted"
                  [disabled]="busy()"
                  (change)="setConsent('Marketing', $any($event.target).checked, c.textVersion)"
                />
                {{ c.marketingText }}
              </label>
              @if (c.marketing.granted && c.marketing.at) {
                <p class="hint">Accord enregistré le {{ stamp(c.marketing.at) }}.</p>
              }
              <p class="hint">
                Sans accord, rien n'est envoyé sur WhatsApp : vous gardez les e-mails et les
                notifications dans l'application.
              </p>
            </fieldset>
          }
          <div>
            <button
              type="button"
              class="btn btn-ghost btn-sm"
              [disabled]="busy()"
              (click)="unlink()"
            >
              Délier ce numéro
            </button>
          </div>
        } @else if (codeSent()) {
          <form class="row" (ngSubmit)="confirm()">
            <label>
              Code reçu sur WhatsApp
              <input
                class="admin-input"
                name="code"
                inputmode="numeric"
                autocomplete="one-time-code"
                maxlength="8"
                [(ngModel)]="code"
              />
            </label>
            <button
              type="submit"
              class="btn btn-primary btn-sm"
              [disabled]="busy() || !code.trim()"
            >
              Valider
            </button>
          </form>
        } @else {
          <form class="row" (ngSubmit)="begin()">
            <label>
              Numéro WhatsApp
              <input
                class="admin-input"
                name="phone"
                type="tel"
                inputmode="tel"
                autocomplete="tel"
                placeholder="07 00 00 00 00"
                [(ngModel)]="phone"
              />
            </label>
            <button
              type="submit"
              class="btn btn-primary btn-sm"
              [disabled]="busy() || !phone.trim()"
            >
              Recevoir un code
            </button>
          </form>
        }
      </div>
    }

    @if (message(); as text) {
      <p role="status" class="badge" data-tone="ok">{{ text }}</p>
    }
    @if (error(); as text) {
      <p class="form-error" role="alert">{{ text }}</p>
    }
  `,
})
export class NotificationSettings {
  private readonly channels = inject(ChannelService);

  protected readonly pushSupported = this.channels.pushSupported();
  protected form: NotificationPreferences = {
    push: true,
    sms: true,
    whatsApp: true,
    reminders: true,
    email: true,
  };
  protected phone = '';
  protected code = '';
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly message = signal<string | null>(null);
  protected readonly subscribed = signal(false);
  protected readonly codeSent = signal(false);
  protected readonly preferences = resource({ loader: () => this.channels.preferences() });
  protected readonly whatsApp = resource({ loader: () => this.channels.whatsAppStatus() });
  protected readonly consent = resource({ loader: () => this.channels.whatsAppConsent() });

  constructor() {
    effect(() => {
      const loaded = this.preferences.value();
      if (loaded) {
        this.form = { ...loaded };
      }
    });
    void this.channels.currentSubscription().then((s) => this.subscribed.set(s !== null));
  }

  protected setConsent(
    purpose: 'Service' | 'Marketing',
    granted: boolean,
    textVersion: string,
  ): Promise<void> {
    return this.run(async () => {
      await this.channels.setWhatsAppConsent(purpose, granted, textVersion);
      this.consent.reload();
      return granted ? 'Accord enregistré.' : 'Accord retiré.';
    });
  }

  protected stamp(instant: string): string {
    return new Date(instant).toLocaleString('fr-FR', {
      dateStyle: 'long',
      timeStyle: 'short',
      timeZone: 'Africa/Abidjan',
    });
  }

  protected toggle(key: keyof NotificationPreferences, value: boolean): Promise<void> {
    const next = { ...this.form, [key]: value };
    return this.run(async () => {
      this.form = await this.channels.savePreferences(next);
      return 'Préférences enregistrées.';
    });
  }

  protected enablePush(): Promise<void> {
    return this.run(async () => {
      await this.channels.enablePush();
      this.subscribed.set(true);
      return 'Notifications activées sur cet appareil.';
    });
  }

  protected disablePush(): Promise<void> {
    return this.run(async () => {
      await this.channels.disablePush();
      this.subscribed.set(false);
      return 'Notifications désactivées sur cet appareil.';
    });
  }

  protected begin(): Promise<void> {
    return this.run(async () => {
      await this.channels.beginWhatsAppLink(this.phone.trim());
      this.codeSent.set(true);
      return 'Code envoyé sur WhatsApp.';
    });
  }

  protected confirm(): Promise<void> {
    return this.run(async () => {
      await this.channels.confirmWhatsAppLink(this.code.trim());
      this.codeSent.set(false);
      this.code = '';
      this.whatsApp.reload();
      this.consent.reload();
      return 'Numéro WhatsApp lié.';
    });
  }

  protected unlink(): Promise<void> {
    return this.run(async () => {
      await this.channels.unlinkWhatsApp();
      this.whatsApp.reload();
      this.consent.reload();
      return 'Numéro WhatsApp délié.';
    });
  }

  private async run(action: () => Promise<string>): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    this.message.set(null);
    try {
      this.message.set(await action());
    } catch (e) {
      this.error.set(e instanceof Error && !('status' in e) ? e.message : problemMessage(e));
    } finally {
      this.busy.set(false);
    }
  }
}
