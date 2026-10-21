import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  output,
  resource,
  signal,
} from '@angular/core';
import { Appointment, BookingService, formatInZone } from '../../core/booking/booking.service';

const ZONE = 'Africa/Abidjan';

/** Local day (Abidjan) of an instant as `YYYY-MM-DD`, the format of the `<input type="date">`. */
function dayOf(instant: Date): string {
  return new Intl.DateTimeFormat('en-CA', { timeZone: ZONE }).format(instant);
}

/**
 * "Déplacer mon rendez-vous": pick a day, then one of the free slots for the same services. The server re-checks everything
 * (slot still free, cut-off, maximum number of moves) when the client confirms.
 */
@Component({
  selector: 'app-reschedule-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    :host {
      display: block;
    }
    .panel {
      display: grid;
      gap: 0.75rem;
      padding: 0.9rem 1rem;
      border: 1px solid var(--kb-border);
      border-radius: var(--radius-md);
      background: var(--kb-primary-50);
    }
    .slots {
      display: flex;
      flex-wrap: wrap;
      gap: 0.5rem;
    }
  `,
  template: `
    <div class="panel">
      <div class="field">
        <label [for]="'day-' + appointment().id">Nouveau jour</label>
        <input
          [id]="'day-' + appointment().id"
          type="date"
          [min]="today"
          [value]="day()"
          (change)="day.set($any($event.target).value)"
        />
      </div>
      @if (slots.isLoading()) {
        <p class="meta" role="status">Recherche des créneaux…</p>
      } @else if (slots.error()) {
        <p class="form-error" role="alert">Impossible de charger les créneaux. Réessayez.</p>
      } @else if (day()) {
        @if (free().length === 0) {
          <p class="meta">Aucun créneau libre ce jour-là. Essayez un autre jour.</p>
        } @else {
          <div class="slots" role="group" aria-label="Créneaux libres">
            @for (slot of free(); track slot.startsAt) {
              <button class="btn btn-light btn-sm" type="button" (click)="pick(slot.startsAt)">
                {{ time(slot.startsAt) }}
              </button>
            }
          </div>
        }
      }
      <div class="actions">
        <button class="btn btn-ghost btn-sm" type="button" (click)="closed.emit()">Fermer</button>
      </div>
    </div>
  `,
})
export class ReschedulePanel {
  readonly appointment = input.required<Appointment>();
  /** The chosen start: the parent performs the move and shows any refusal at the top of the page. */
  readonly picked = output<string>();
  readonly closed = output<void>();

  private readonly booking = inject(BookingService);
  protected readonly today = dayOf(new Date());
  protected readonly day = signal('');

  protected readonly slots = resource({
    params: () => ({ day: this.day(), appointment: this.appointment() }),
    loader: async ({ params }) => {
      const serviceIds = params.appointment.lines
        .map((l) => l.serviceId)
        .filter((id): id is string => !!id);
      if (!params.day || serviceIds.length === 0) {
        return [];
      }

      const response = await this.booking.availability(
        params.appointment.establishmentId,
        params.day,
        params.day,
        serviceIds,
        null,
      );
      return response.days.flatMap((d) => d.slots);
    },
  });

  protected readonly free = computed(() =>
    (this.slots.hasValue() ? this.slots.value() : []).filter(
      (s) => s.available && s.startsAt !== this.appointment().startsAt,
    ),
  );

  protected time(instant: string): string {
    return formatInZone(instant, ZONE, { hour: '2-digit', minute: '2-digit' });
  }

  protected pick(startsAt: string): void {
    const when = formatInZone(startsAt, ZONE, {
      weekday: 'long',
      day: 'numeric',
      month: 'long',
      hour: '2-digit',
      minute: '2-digit',
    });
    if (confirm(`Déplacer votre rendez-vous au ${when} ?`)) {
      this.picked.emit(startsAt);
    }
  }
}
