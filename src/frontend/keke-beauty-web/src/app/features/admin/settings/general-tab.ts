import {
  ChangeDetectionStrategy,
  Component,
  effect,
  inject,
  resource,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AdminApiService, AppSettings } from '../../../core/admin/admin-api.service';
import { problemMessage } from '../../../core/auth/auth.models';

/** Global parameters (PRD ADM-05): sessions, retention of identity documents, reminders, channels, appointment payments. */
@Component({
  selector: 'app-general-tab',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  styles: `
    form {
      display: grid;
      gap: 1.25rem;
    }
    fieldset {
      display: grid;
      gap: 0.75rem;
      margin: 0;
      padding: 1rem;
      border: 1px solid var(--kb-border);
      border-radius: var(--radius-md);
    }
    legend {
      padding-inline: 0.5rem;
      font-weight: 700;
    }
    .fields {
      display: flex;
      flex-wrap: wrap;
      gap: 1rem;
    }
    label.field {
      display: grid;
      gap: 0.25rem;
      font-size: var(--fs-sm);
      font-weight: 600;
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
    .hint {
      margin: 0;
      font-size: var(--fs-sm);
      color: var(--kb-ink-muted);
    }
  `,
  template: `
    <section class="admin-card" aria-labelledby="general-title">
      <h2 id="general-title">Paramètres généraux</h2>
      @if (settings.hasValue()) {
        <form (ngSubmit)="save()">
          <fieldset>
            <legend>Abonnements</legend>
            <label class="field">
              Délai de grâce avant impayé (jours)
              <input
                class="admin-input"
                type="number"
                name="grace"
                min="0"
                max="30"
                [(ngModel)]="form.graceDays"
              />
            </label>
            <p class="hint">
              Après l'échéance d'une facture, le propriétaire garde ses fonctionnalités Premium
              pendant ce délai.
            </p>
          </fieldset>

          <fieldset>
            <legend>Sécurité et données personnelles</legend>
            <div class="fields">
              <label class="field">
                Déconnexion après inactivité (minutes)
                <input
                  class="admin-input"
                  type="number"
                  name="idle"
                  min="5"
                  max="1440"
                  [(ngModel)]="form.sessionIdleMinutes"
                />
              </label>
              <label class="field">
                Conservation des pièces d'identité (jours)
                <input
                  class="admin-input"
                  type="number"
                  name="retention"
                  min="30"
                  max="3650"
                  [(ngModel)]="form.kycRetentionDays"
                />
              </label>
            </div>
            <p class="hint">
              Sans action pendant ce délai, la personne doit se reconnecter (60 minutes par défaut).
              Les photos des pièces d'identité sont effacées une fois la durée de conservation
              écoulée depuis la décision sur le dossier (la décision, elle, est conservée).
            </p>
          </fieldset>

          <fieldset>
            <legend>Rappels de rendez-vous</legend>
            <div class="fields">
              <label class="field">
                Premier rappel (heures avant)
                <input
                  class="admin-input"
                  type="number"
                  name="reminder1"
                  min="0"
                  max="168"
                  [(ngModel)]="form.reminderFirstHours"
                />
              </label>
              <label class="field">
                Second rappel (heures avant)
                <input
                  class="admin-input"
                  type="number"
                  name="reminder2"
                  min="0"
                  max="168"
                  [(ngModel)]="form.reminderSecondHours"
                />
              </label>
            </div>
            <p class="hint">
              0 désactive le rappel. Le rappel part sur les canaux choisis par le client
              (notification, e-mail, SMS, WhatsApp si activés ci-dessous).
            </p>
            <label class="field">
              Message « revenez nous voir » (jours après la dernière visite)
              <input
                class="admin-input"
                type="number"
                name="reengagement"
                min="0"
                max="365"
                [(ngModel)]="form.reengagementDays"
              />
            </label>
            <p class="hint">
              Envoyé une fois, seulement si la cliente n'a pas déjà repris rendez-vous dans cet
              établissement. 0 le désactive.
            </p>
          </fieldset>

          <fieldset>
            <legend>Canaux de notification</legend>
            <label class="check">
              <input type="checkbox" name="sms" [(ngModel)]="form.smsEnabled" />
              SMS (LeTexto)
            </label>
            <label class="check">
              <input type="checkbox" name="whatsapp" [(ngModel)]="form.whatsAppEnabled" />
              WhatsApp
            </label>
            <label class="check">
              <input type="checkbox" name="push" [(ngModel)]="form.pushEnabled" />
              Notifications push (navigateur et application installée)
            </label>
            <p class="hint">
              Un canal activé ici n'envoie réellement des messages que si son fournisseur est
              configuré et prêt (voir l'onglet « Canaux »).
            </p>
          </fieldset>

          <div>
            <button type="submit" class="btn btn-primary" [disabled]="busy()">Enregistrer</button>
          </div>
        </form>
      } @else if (settings.error()) {
        <p class="form-error" role="alert">Impossible de charger les paramètres.</p>
      }
      @if (message(); as text) {
        <p role="status" class="badge" data-tone="ok">{{ text }}</p>
      }
      @if (error(); as text) {
        <p class="form-error" role="alert">{{ text }}</p>
      }
    </section>
  `,
})
export class GeneralTab {
  private readonly api = inject(AdminApiService);

  protected form: AppSettings = {
    graceDays: 7,
    sessionIdleMinutes: 60,
    kycRetentionDays: 365,
    reminderFirstHours: 24,
    reminderSecondHours: 2,
    smsEnabled: false,
    whatsAppEnabled: false,
    pushEnabled: false,
    appointmentPaymentsEnabled: false,
    platformFeePercent: 0,
    reengagementDays: 30,
  };
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly message = signal<string | null>(null);
  protected readonly settings = resource({ loader: () => this.api.settings() });

  constructor() {
    effect(() => {
      const loaded = this.settings.value();
      if (loaded) {
        this.form = { ...loaded };
      }
    });
  }

  protected async save(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    this.message.set(null);
    try {
      const f = this.form;
      this.form = await this.api.updateSettings({
        ...f,
        graceDays: Number(f.graceDays),
        sessionIdleMinutes: Number(f.sessionIdleMinutes),
        kycRetentionDays: Number(f.kycRetentionDays),
        reminderFirstHours: Number(f.reminderFirstHours),
        reminderSecondHours: Number(f.reminderSecondHours),
        platformFeePercent: Number(f.platformFeePercent),
        reengagementDays: Number(f.reengagementDays),
      });
      this.message.set('Paramètres enregistrés.');
    } catch (e) {
      this.error.set(problemMessage(e));
    } finally {
      this.busy.set(false);
    }
  }
}
