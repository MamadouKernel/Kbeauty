import { ChangeDetectionStrategy, Component, inject, input, output, signal } from '@angular/core';
import { problemMessage } from '../../core/auth/auth.models';
import { DISPUTE_REASONS, DisputeService } from '../../core/disputes/dispute.service';

const MIN_TEXT = 10;
const MAX_TEXT = 1000;

/** A client reports a problem about a past appointment (Keke Protect): reason and a few words of explanation. */
@Component({
  selector: 'app-dispute-form',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    form {
      display: grid;
      gap: 0.75rem;
      padding: 1rem;
      border-radius: 0.75rem;
      background: var(--kb-primary-50);
    }
    label {
      display: grid;
      gap: 0.25rem;
      font-weight: 600;
    }
    select,
    textarea {
      width: 100%;
      padding: 0.75rem;
      border: 1px solid var(--kb-border);
      border-radius: 0.75rem;
      font: inherit;
      font-weight: 400;
      background: var(--kb-card);
    }
    textarea {
      min-height: 6rem;
      resize: vertical;
    }
    .row {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 0.5rem;
    }
    .count {
      margin-left: auto;
      font-size: var(--fs-xs);
      color: var(--kb-ink-muted);
    }
    .hint {
      margin: 0;
      font-size: var(--fs-xs);
      color: var(--kb-ink-muted);
    }
  `,
  template: `
    <form (submit)="submit($event)">
      <label>
        Que s'est-il passé ?
        <select name="reason" [value]="reason()" (change)="reason.set(pick.value)" #pick>
          @for (r of reasons; track r.value) {
            <option [value]="r.value" [selected]="r.value === reason()">{{ r.label }}</option>
          }
        </select>
      </label>
      <label>
        Expliquez-nous en quelques mots
        <textarea
          #box
          name="description"
          maxlength="1000"
          [value]="description()"
          (input)="description.set(box.value)"
        ></textarea>
      </label>
      <p class="hint">
        L'établissement pourra répondre, puis notre équipe tranchera. Son versement est suspendu
        tant que le litige est ouvert.
      </p>
      @if (error()) {
        <p class="form-error" role="alert">{{ error() }}</p>
      }
      <div class="row">
        <button type="submit" class="btn btn-primary btn-sm" [disabled]="busy() || !valid()">
          Envoyer
        </button>
        <button
          type="button"
          class="btn btn-ghost btn-sm"
          [disabled]="busy()"
          (click)="cancelled.emit()"
        >
          Annuler
        </button>
        <span class="count">{{ description().length }} / 1000</span>
      </div>
    </form>
  `,
})
export class DisputeForm {
  private readonly disputes = inject(DisputeService);

  readonly appointmentId = input.required<string>();
  readonly submitted = output<void>();
  readonly cancelled = output<void>();

  protected readonly reasons = Object.entries(DISPUTE_REASONS).map(([value, label]) => ({
    value,
    label,
  }));
  protected readonly reason = signal('SalonClosed');
  protected readonly description = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal('');

  protected valid(): boolean {
    const length = this.description().trim().length;
    return length >= MIN_TEXT && length <= MAX_TEXT;
  }

  protected async submit(event: Event): Promise<void> {
    event.preventDefault();
    if (!this.valid()) {
      return;
    }

    this.busy.set(true);
    this.error.set('');
    try {
      await this.disputes.open(this.appointmentId(), this.reason(), this.description().trim());
      this.submitted.emit();
    } catch (error) {
      this.error.set(problemMessage(error));
    } finally {
      this.busy.set(false);
    }
  }
}
