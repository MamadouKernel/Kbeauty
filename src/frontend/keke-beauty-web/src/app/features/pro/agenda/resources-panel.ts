import { ChangeDetectionStrategy, Component, inject, input, resource, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { InputText } from 'primeng/inputtext';
import { problemMessage } from '../../../core/auth/auth.models';
import { AgendaResource, AgendaService } from '../../../core/partner/agenda.service';
import type { PartnerServiceItem } from '../../../core/partner/partner.service';

/** Employees or workstations and the services each performs (PRD AGD-05). */
@Component({
  selector: 'app-resources-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, InputText],
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
    .tag {
      padding: 0.0625rem 0.625rem;
      border-radius: var(--radius-pill);
      background: var(--kb-primary-50);
      font-size: 0.8125rem;
      font-weight: 600;
      color: var(--kb-primary-700);
    }
    .tag.off {
      background: color-mix(in srgb, var(--kb-danger) 10%, white);
      color: var(--kb-danger);
    }
    .tags {
      display: flex;
      flex-wrap: wrap;
      gap: 0.375rem;
    }
    form {
      display: grid;
      gap: 1rem;
    }
    .field {
      display: grid;
      gap: 0.375rem;

      label {
        font-size: 0.875rem;
        font-weight: 600;
      }
      input.p-inputtext {
        width: 100%;
        min-height: 3rem;
      }
    }
    fieldset {
      display: grid;
      gap: 0.25rem;
      padding: 0;
      border: 0;
      margin: 0;
    }
    legend {
      padding: 0;
      font-size: 0.875rem;
      font-weight: 600;
    }
    .check {
      display: flex;
      align-items: center;
      gap: 0.625rem;
      min-height: 2.75rem;
    }
    .check input {
      width: 1.25rem;
      height: 1.25rem;
      accent-color: var(--kb-primary-600);
    }
    .actions {
      display: flex;
      flex-wrap: wrap;
      gap: 0.5rem;
    }
  `,
  template: `
    @if (error()) {
      <p class="form-error" role="alert">{{ error() }}</p>
    }

    @if (resources.hasValue()) {
      @for (r of resources.value(); track r.id) {
        <article class="card">
          <div class="row">
            <h3>{{ r.name }}</h3>
            <span class="tags">
              @if (!r.isActive) {
                <span class="tag off">Inactive</span>
              }
            </span>
          </div>
          <p class="tags">
            @if (r.serviceIds.length === 0) {
              <span class="tag">Toutes les prestations</span>
            } @else {
              @for (name of serviceNames(r); track name) {
                <span class="tag">{{ name }}</span>
              }
            }
          </p>
          <div class="actions">
            <button type="button" class="btn btn-ghost btn-sm" (click)="edit(r)">Modifier</button>
            <button
              type="button"
              class="btn btn-ghost btn-sm"
              [disabled]="busy()"
              (click)="remove(r)"
            >
              Supprimer
            </button>
          </div>
        </article>
      }
    }

    <section class="card" aria-labelledby="resource-form-title">
      <h3 id="resource-form-title">
        {{ editing() ? 'Modifier la ressource' : 'Ajouter une ressource' }}
      </h3>
      <form [formGroup]="form" (ngSubmit)="save()" novalidate>
        <div class="field">
          <label for="res-name">Nom (employé ou poste)</label>
          <input pInputText id="res-name" formControlName="name" maxlength="120" />
          @if (form.controls.name.invalid && form.controls.name.touched) {
            <p class="field-error" role="alert">Le nom est requis (2 caractères minimum).</p>
          }
        </div>
        <fieldset>
          <legend>Prestations réalisées (aucune case cochée = toutes)</legend>
          @for (s of services(); track s.id) {
            <label class="check">
              <input
                type="checkbox"
                [checked]="selected().includes(s.id)"
                (change)="toggle(s.id)"
              />
              {{ s.name }}
            </label>
          }
        </fieldset>
        <label class="check">
          <input type="checkbox" formControlName="isActive" />
          Active (peut recevoir des rendez-vous)
        </label>
        <div class="actions">
          <button class="btn btn-primary btn-sm" type="submit" [disabled]="busy()">
            {{ editing() ? 'Enregistrer' : 'Ajouter' }}
          </button>
          @if (editing()) {
            <button class="btn btn-ghost btn-sm" type="button" (click)="reset()">Annuler</button>
          }
        </div>
      </form>
    </section>
  `,
})
export class ResourcesPanel {
  readonly establishmentId = input.required<string>();
  readonly services = input.required<readonly PartnerServiceItem[]>();

  private readonly agenda = inject(AgendaService);

  protected readonly resources = resource({
    params: () => this.establishmentId(),
    loader: ({ params }) => this.agenda.resources(params),
  });
  protected readonly editing = signal<string | null>(null);
  protected readonly selected = signal<readonly string[]>([]);
  protected readonly busy = signal(false);
  protected readonly error = signal('');

  protected readonly form = inject(NonNullableFormBuilder).group({
    name: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(120)]],
    isActive: [true],
  });

  protected serviceNames(r: AgendaResource): string[] {
    const known = new Map(this.services().map((s) => [s.id, s.name] as const));
    return r.serviceIds.map((id) => known.get(id) ?? 'Prestation supprimée');
  }

  protected toggle(id: string): void {
    this.selected.update((ids) => (ids.includes(id) ? ids.filter((x) => x !== id) : [...ids, id]));
  }

  protected edit(r: AgendaResource): void {
    this.editing.set(r.id);
    this.selected.set(r.serviceIds);
    this.form.reset({ name: r.name, isActive: r.isActive });
  }

  protected reset(): void {
    this.editing.set(null);
    this.selected.set([]);
    this.form.reset({ name: '', isActive: true });
  }

  protected async save(): Promise<void> {
    this.form.markAllAsTouched();
    if (this.form.invalid) {
      return;
    }

    const input = { ...this.form.getRawValue(), serviceIds: [...this.selected()] };
    const editing = this.editing();
    await this.run(async () => {
      if (editing) {
        await this.agenda.updateResource(this.establishmentId(), editing, input);
      } else {
        await this.agenda.addResource(this.establishmentId(), input);
      }

      this.reset();
    });
  }

  protected remove(r: AgendaResource): Promise<void> {
    return confirm(
      `Supprimer « ${r.name} » ? S'il a déjà des rendez-vous, il sera seulement désactivé.`,
    )
      ? this.run(() => this.agenda.deleteResource(this.establishmentId(), r.id))
      : Promise.resolve();
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
      this.resources.reload();
    }
  }
}
