import { ChangeDetectionStrategy, Component, computed, input, output, signal } from '@angular/core';

const MIN = 3;
const MAX = 500;

/** Asks for the reason that every sensitive admin action must carry (kept in the audit trail, shown to the person concerned). */
@Component({
  selector: 'app-reason-form',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    form {
      display: grid;
      gap: 0.5rem;
      padding: 1rem;
      border: 1px solid var(--kb-border);
      border-radius: var(--radius-md);
      background: var(--kb-primary-50);
    }
    textarea {
      min-height: 4.5rem;
      padding: 0.5rem 0.75rem;
      border: 1.5px solid var(--kb-border);
      border-radius: var(--radius-sm, 0.5rem);
      font: inherit;
      resize: vertical;
    }
    .actions {
      display: flex;
      flex-wrap: wrap;
      gap: 0.5rem;
    }
  `,
  template: `
    <form novalidate (submit)="submit($event)">
      <label>
        {{ label() }}
        <textarea
          name="reason"
          [value]="text()"
          (input)="onInput($event)"
          maxlength="500"
          required
          aria-describedby="reason-help"
        ></textarea>
      </label>
      <p id="reason-help" class="auth-lead">
        {{ MIN }} à {{ MAX }} caractères. Ce motif est conservé dans le journal d'audit.
      </p>
      @if (touched() && !valid()) {
        <p class="form-error" role="alert">Indiquez un motif d'au moins {{ MIN }} caractères.</p>
      }
      <div class="actions">
        <button type="submit" class="btn btn-primary btn-sm" [disabled]="busy()">
          {{ confirmLabel() }}
        </button>
        <button type="button" class="btn btn-ghost btn-sm" (click)="cancelled.emit()">
          Annuler
        </button>
      </div>
    </form>
  `,
})
export class ReasonForm {
  readonly label = input('Motif');
  readonly confirmLabel = input('Confirmer');
  readonly busy = input(false);
  readonly confirmed = output<string>();
  readonly cancelled = output<void>();

  protected readonly MIN = MIN;
  protected readonly MAX = MAX;
  protected readonly text = signal('');
  protected readonly touched = signal(false);
  protected readonly valid = computed(() => {
    const length = this.text().trim().length;
    return length >= MIN && length <= MAX;
  });

  protected onInput(event: Event): void {
    this.text.set((event.target as HTMLTextAreaElement).value);
  }

  protected submit(event: Event): void {
    event.preventDefault();
    this.touched.set(true);
    if (this.valid()) {
      this.confirmed.emit(this.text().trim());
    }
  }
}
