import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, input, resource, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { InputText } from 'primeng/inputtext';
import { problemMessage } from '../../../core/auth/auth.models';
import { AgendaService } from '../../../core/partner/agenda.service';
import { toInstant } from './agenda-page';

/** Exceptional closures: holidays, one-off blocks, an employee's absence (PRD AGD-04). Times are on the establishment's clock. */
@Component({
  selector: 'app-time-off-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, InputText, DatePipe],
  styles: `
    :host {
      display: grid;
      gap: 1rem;
    }
    .card {
      display: grid;
      gap: 0.75rem;
      padding: 1rem 1.25rem;
      border: 1px solid var(--kb-border);
      border-radius: var(--radius-lg);
      background: #fff;
      box-shadow: var(--shadow-soft);
    }
    .row {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: space-between;
      gap: 0.5rem 1rem;
    }
    form {
      display: grid;
      gap: 1rem;

      @media (min-width: 640px) {
        grid-template-columns: 1fr 1fr;
      }
    }
    .field {
      display: grid;
      gap: 0.375rem;

      label {
        font-size: var(--fs-sm);
        font-weight: 600;
      }
      input.p-inputtext,
      select {
        width: 100%;
        min-height: 3rem;
        padding: 0.5rem 0.75rem;
        border: 1px solid var(--kb-border);
        border-radius: var(--radius-sm);
        font: inherit;
      }
    }
    .full {
      grid-column: 1 / -1;
    }
  `,
  template: `
    @if (error()) {
      <p class="form-error" role="alert">{{ error() }}</p>
    }

    @if (closures.hasValue()) {
      @for (c of closures.value(); track c.id) {
        <article class="card">
          <div class="row">
            <div>
              <strong
                >{{ c.startsAt | date: 'dd/MM/yyyy HH:mm' : 'UTC' }} →
                {{ c.endsAt | date: 'dd/MM/yyyy HH:mm' : 'UTC' }}</strong
              >
              <p class="hint">
                {{ resourceName(c.resourceId) }}{{ c.reason ? ' · ' + c.reason : '' }}
              </p>
            </div>
            <button
              type="button"
              class="btn btn-ghost btn-sm"
              [disabled]="busy()"
              (click)="remove(c.id)"
            >
              Supprimer
            </button>
          </div>
        </article>
      } @empty {
        <p class="hint">Aucune indisponibilité à venir.</p>
      }
    }

    <section class="card" aria-labelledby="timeoff-title">
      <h3 id="timeoff-title">Ajouter une indisponibilité</h3>
      <form [formGroup]="form" (ngSubmit)="add()" novalidate>
        <div class="field">
          <label for="to-start">Début</label>
          <input pInputText id="to-start" type="datetime-local" formControlName="startsAt" />
        </div>
        <div class="field">
          <label for="to-end">Fin</label>
          <input pInputText id="to-end" type="datetime-local" formControlName="endsAt" />
        </div>
        <div class="field">
          <label for="to-resource">Concerne</label>
          <select id="to-resource" formControlName="resourceId">
            <option value="">Tout l'établissement</option>
            @if (resources.hasValue()) {
              @for (r of resources.value(); track r.id) {
                <option [value]="r.id">{{ r.name }}</option>
              }
            }
          </select>
        </div>
        <div class="field">
          <label for="to-reason">Motif (facultatif)</label>
          <input pInputText id="to-reason" formControlName="reason" maxlength="250" />
        </div>
        @if (formError()) {
          <p class="form-error full" role="alert">{{ formError() }}</p>
        }
        <div class="full">
          <button class="btn btn-primary btn-sm" type="submit" [disabled]="busy()">Ajouter</button>
        </div>
      </form>
    </section>
  `,
})
export class TimeOffPanel {
  readonly establishmentId = input.required<string>();

  private readonly agenda = inject(AgendaService);

  protected readonly closures = resource({
    params: () => this.establishmentId(),
    loader: ({ params }) => this.agenda.timeOff(params),
  });
  protected readonly resources = resource({
    params: () => this.establishmentId(),
    loader: ({ params }) => this.agenda.resources(params),
  });
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly formError = signal('');

  protected readonly form = inject(NonNullableFormBuilder).group({
    startsAt: ['', Validators.required],
    endsAt: ['', Validators.required],
    resourceId: [''],
    reason: [''],
  });

  protected resourceName(id: string | null): string {
    if (!id) {
      return "Tout l'établissement";
    }

    return this.resources.hasValue()
      ? (this.resources.value().find((r) => r.id === id)?.name ?? 'Ressource')
      : 'Ressource';
  }

  protected async add(): Promise<void> {
    this.formError.set('');
    const value = this.form.getRawValue();
    if (!value.startsAt || !value.endsAt || value.endsAt <= value.startsAt) {
      this.formError.set('Indiquez un début et une fin, la fin devant suivre le début.');
      return;
    }

    await this.run(async () => {
      await this.agenda.addTimeOff(this.establishmentId(), {
        resourceId: value.resourceId || null,
        startsAt: toInstant(value.startsAt),
        endsAt: toInstant(value.endsAt),
        reason: value.reason.trim() || null,
      });
      this.form.reset({ startsAt: '', endsAt: '', resourceId: '', reason: '' });
    });
  }

  protected remove(id: string): Promise<void> {
    return this.run(() => this.agenda.deleteTimeOff(this.establishmentId(), id));
  }

  private async run(action: () => Promise<unknown>): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    try {
      await action();
    } catch (error) {
      this.error.set(problemMessage(error));
    } finally {
      this.busy.set(false);
      this.closures.reload();
    }
  }
}
