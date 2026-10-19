import { httpResource } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { problemMessage } from '../../core/auth/auth.models';
import type { EstablishmentDetails } from '../../core/establishments/establishment.service';
import { GROUP_EVENTS, GrowthService, type GroupBooking } from '../../core/growth/growth.service';

/** Earliest date offered: the establishment needs at least a day to answer. */
function tomorrowLocal(): string {
  const d = new Date(Date.now() + 36 * 3600 * 1000);
  const pad = (n: number) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}T10:00`;
}

/**
 * `/etablissements/:slug/groupe` — ask an establishment to welcome a group (a wedding, a birthday). It answers with a
 * quote or declines; the client follows the request in "Fidélité et demandes".
 */
@Component({
  selector: 'app-group-booking-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, RouterLink],
  styles: `
    .page {
      display: grid;
      gap: 1.25rem;
      max-width: 40rem;
      padding-block: clamp(1.5rem, 4vw, 3rem);
    }
    form {
      display: grid;
      gap: 1rem;
      padding: 1.25rem;
      border: 1px solid var(--kb-border);
      border-radius: var(--radius-lg);
      background: var(--kb-card);
      box-shadow: var(--shadow-soft);
    }
    .two {
      display: grid;
      gap: 1rem;
      grid-template-columns: repeat(auto-fit, minmax(min(100%, 12rem), 1fr));
    }
    label {
      display: grid;
      gap: 0.3rem;
      font-weight: 600;
    }
    input,
    select,
    textarea {
      min-height: 2.75rem;
      padding: 0.55rem 0.75rem;
      border: 1.5px solid var(--kb-border);
      border-radius: var(--radius-md);
      background: var(--kb-card);
      color: var(--kb-ink);
      font: inherit;
      font-weight: 400;
    }
    textarea {
      min-height: 5rem;
      resize: vertical;
    }
  `,
  template: `
    <div class="container page">
      <a class="link" [routerLink]="['/etablissements', slug()]">← Retour à l’établissement</a>
      <div>
        <p class="eyebrow">Réservation de groupe</p>
        <h1>
          Un événement chez <em>{{ details.hasValue() ? details.value().name : '…' }}</em>
        </h1>
        <p class="hint">
          Mariage, anniversaire, sortie entre amies : décrivez votre projet, l’établissement vous
          répond avec un devis et vous rappelle pour organiser la journée.
        </p>
      </div>

      @if (sent(); as request) {
        <div class="form-success" role="status">
          <p>
            Demande envoyée pour {{ request.groupSize }} personnes. Vous serez prévenu(e) dès que
            l’établissement répond.
          </p>
          <p><a class="link" routerLink="/compte/fidelite">Suivre mes demandes</a></p>
        </div>
      } @else {
        <form [formGroup]="form" (ngSubmit)="submit()">
          <div class="two">
            <label>
              Type d’événement
              <select formControlName="eventType">
                @for (e of events; track e.id) {
                  <option [value]="e.id">{{ e.label }}</option>
                }
              </select>
            </label>
            <label>
              Nombre de personnes
              <input type="number" min="2" max="50" formControlName="groupSize" />
            </label>
          </div>
          <div class="two">
            <label>
              Date et heure souhaitées
              <input type="datetime-local" [min]="minDate" formControlName="desiredStartsAt" />
            </label>
            <label>
              Téléphone
              <input
                type="tel"
                autocomplete="tel"
                formControlName="phone"
                placeholder="07 00 00 00 00"
              />
            </label>
          </div>
          <label>
            Prestations souhaitées
            <textarea
              maxlength="500"
              formControlName="services"
              placeholder="Ex. : coiffure et maquillage de la mariée, tresses pour 6 demoiselles d’honneur"
            ></textarea>
          </label>
          <label>
            Message (facultatif)
            <textarea maxlength="1000" formControlName="message"></textarea>
          </label>
          @if (error()) {
            <p class="form-error" role="alert">{{ error() }}</p>
          }
          <div>
            <button
              class="btn btn-primary"
              type="submit"
              [disabled]="busy() || form.invalid || !details.hasValue()"
            >
              Envoyer la demande
            </button>
          </div>
        </form>
      }
    </div>
  `,
})
export class GroupBookingPage {
  /** Bound from the `:slug` route parameter. */
  readonly slug = input.required<string>();

  private readonly growth = inject(GrowthService);

  protected readonly details = httpResource<EstablishmentDetails>(
    () => `/api/v1/establishments/${encodeURIComponent(this.slug())}`,
  );
  protected readonly events = Object.entries(GROUP_EVENTS).map(([id, label]) => ({ id, label }));
  protected readonly minDate = tomorrowLocal();
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly sent = signal<GroupBooking | null>(null);

  protected readonly form = inject(NonNullableFormBuilder).group({
    eventType: ['Wedding', Validators.required],
    groupSize: [4, [Validators.required, Validators.min(2), Validators.max(50)]],
    desiredStartsAt: [tomorrowLocal(), Validators.required],
    phone: ['', [Validators.required, Validators.pattern(/^\+?[0-9][0-9 .-]{6,18}[0-9]$/)]],
    services: ['', [Validators.required, Validators.minLength(3), Validators.maxLength(500)]],
    message: [''],
  });

  protected async submit(): Promise<void> {
    if (this.form.invalid || !this.details.hasValue()) {
      return;
    }

    const value = this.form.getRawValue();
    this.busy.set(true);
    this.error.set('');
    try {
      this.sent.set(
        await this.growth.requestGroup({
          establishmentId: this.details.value().id,
          eventType: value.eventType,
          // The local date and time typed by the client, sent as an instant.
          desiredStartsAt: new Date(value.desiredStartsAt).toISOString(),
          groupSize: Number(value.groupSize),
          services: value.services.trim(),
          message: value.message.trim() || null,
          phone: value.phone.trim(),
        }),
      );
    } catch (error) {
      this.error.set(problemMessage(error));
    } finally {
      this.busy.set(false);
    }
  }
}
