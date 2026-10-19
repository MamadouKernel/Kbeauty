import {
  ChangeDetectionStrategy,
  Component,
  effect,
  inject,
  input,
  signal,
  untracked,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { problemMessage } from '../../../core/auth/auth.models';
import { AgendaService, BookingSettings } from '../../../core/partner/agenda.service';

const STEPS = [5, 10, 15, 20, 30, 60];

/** Booking settings (PRD AGD-07): enable booking, auto-confirm, lead time, horizon, cancellation window, grid step. */
@Component({
  selector: 'app-settings-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  styles: `
    form {
      display: grid;
      gap: 1rem;
      max-width: 36rem;
      padding: 1.25rem;
      border: 1px solid var(--kb-border);
      border-radius: var(--radius-lg);
      background: var(--kb-card);
    }
    label {
      display: grid;
      gap: 0.25rem;
      font-weight: 600;
    }
    input[type='number'],
    select {
      min-height: 2.75rem;
      padding: 0.5rem 0.75rem;
      border: 1px solid var(--kb-border);
      border-radius: var(--radius-sm);
      font: inherit;
    }
    .check {
      display: flex;
      align-items: center;
      gap: 0.625rem;
    }
  `,
  template: `
    @if (settings(); as s) {
      <form (ngSubmit)="save()">
        <label class="check"
          ><input
            type="checkbox"
            name="enabled"
            [ngModel]="s.bookingEnabled"
            (ngModelChange)="patch({ bookingEnabled: $event })"
          />
          Réservation en ligne activée</label
        >
        <label class="check"
          ><input
            type="checkbox"
            name="auto"
            [ngModel]="s.autoConfirm"
            (ngModelChange)="patch({ autoConfirm: $event })"
          />
          Confirmer automatiquement les demandes</label
        >
        <label
          >Intervalle entre deux créneaux
          <select
            name="step"
            [ngModel]="s.slotStepMinutes"
            (ngModelChange)="patch({ slotStepMinutes: +$event })"
          >
            @for (step of steps; track step) {
              <option [ngValue]="step">{{ step }} min</option>
            }
          </select>
        </label>
        <label
          >Délai minimum avant un rendez-vous (minutes)
          <input
            type="number"
            name="lead"
            min="0"
            max="10080"
            [ngModel]="s.minLeadMinutes"
            (ngModelChange)="patch({ minLeadMinutes: +$event })"
          />
        </label>
        <label
          >Horizon de réservation (jours)
          <input
            type="number"
            name="horizon"
            min="1"
            max="365"
            [ngModel]="s.maxHorizonDays"
            (ngModelChange)="patch({ maxHorizonDays: +$event })"
          />
        </label>
        <label
          >Annulation par le client possible jusqu'à (minutes avant)
          <input
            type="number"
            name="cancel"
            min="0"
            max="10080"
            [ngModel]="s.cancelLimitMinutes"
            (ngModelChange)="patch({ cancelLimitMinutes: +$event })"
          />
        </label>
        <label
          >Frais d'annulation tardive (% du prix retenu sur le paiement en ligne)
          <input
            type="number"
            name="lateFee"
            min="0"
            max="100"
            [ngModel]="s.lateCancelFeePercent ?? 0"
            (ngModelChange)="patch({ lateCancelFeePercent: +$event })"
          />
        </label>
        <p class="hint">
          Passé le délai ci-dessus, une cliente qui a payé en ligne peut encore annuler : ce
          pourcentage du prix reste à l'établissement, le reste lui est remboursé. À 0 %,
          l'annulation tardive reste refusée.
        </p>
        @if (error()) {
          <p class="form-error" role="alert">{{ error() }}</p>
        }
        @if (saved()) {
          <p class="form-success" role="status">Paramètres enregistrés.</p>
        }
        <button class="btn btn-primary" type="submit" [disabled]="busy()">Enregistrer</button>
      </form>
    } @else {
      <p role="status">Chargement…</p>
    }
  `,
})
export class SettingsPanel {
  readonly establishmentId = input.required<string>();

  private readonly agenda = inject(AgendaService);
  protected readonly steps = STEPS;
  protected readonly settings = signal<BookingSettings | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly saved = signal(false);

  constructor() {
    effect(() => {
      const id = this.establishmentId();
      untracked(
        () =>
          void this.agenda
            .settings(id)
            .then((s) => this.settings.set(s))
            .catch((e) => this.error.set(problemMessage(e))),
      );
    });
  }

  protected patch(change: Partial<BookingSettings>): void {
    this.saved.set(false);
    this.settings.update((s) => (s ? { ...s, ...change } : s));
  }

  protected async save(): Promise<void> {
    const s = this.settings();
    if (!s) {
      return;
    }

    this.busy.set(true);
    this.error.set('');
    try {
      this.settings.set(await this.agenda.saveSettings(this.establishmentId(), s));
      this.saved.set(true);
    } catch (error) {
      this.error.set(problemMessage(error));
    } finally {
      this.busy.set(false);
    }
  }
}
