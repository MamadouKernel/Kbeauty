import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { LucideChevronLeft, LucideChevronRight } from '@lucide/angular';
import { AvailabilityDay, isoDay } from '../../core/booking/booking.service';

export interface CalendarCell {
  readonly iso: string;
  readonly day: number;
  readonly inMonth: boolean;
  readonly available: boolean;
  readonly selected: boolean;
}

const WEEKDAYS = ['lun.', 'mar.', 'mer.', 'jeu.', 'ven.', 'sam.', 'dim.'];

/**
 * Month calendar for picking a day (PRD RDV-02). A day without any bookable slot (closed, fully booked, in the past
 * or beyond the horizon) is shown in red and cannot be selected: state is conveyed by text and pattern as well as colour.
 */
@Component({
  selector: 'app-availability-calendar',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [LucideChevronLeft, LucideChevronRight],
  styles: `
    :host {
      display: block;
    }
    .head {
      display: flex;
      align-items: center;
      justify-content: space-between;
      margin-bottom: 0.75rem;
    }
    .title {
      font-family: var(--font-display);
      font-size: 1.25rem;
      font-weight: 600;
      text-transform: capitalize;
    }
    .nav {
      display: inline-grid;
      place-items: center;
      width: 2.75rem;
      height: 2.75rem;
      border: 1px solid var(--kb-border);
      border-radius: 50%;
      background: #fff;
      color: var(--kb-primary-700);
      cursor: pointer;

      &:disabled {
        opacity: 0.35;
        cursor: not-allowed;
      }
      svg {
        width: 1.125rem;
        height: 1.125rem;
      }
    }
    .grid {
      display: grid;
      grid-template-columns: repeat(7, minmax(0, 1fr));
      gap: 0.25rem;
    }
    .weekday {
      padding-block: 0.25rem;
      text-align: center;
      font-size: 0.75rem;
      font-weight: 700;
      text-transform: uppercase;
      letter-spacing: 0.04em;
      color: var(--kb-ink-muted);
    }
    .cell {
      position: relative;
      aspect-ratio: 1;
      min-height: 2.75rem;
      border: 1.5px solid transparent;
      border-radius: 0.875rem;
      background: transparent;
      font: inherit;
      font-weight: 600;
      cursor: pointer;
      transition:
        background-color 160ms ease,
        transform 160ms var(--ease-out);
    }
    .cell.available {
      color: var(--kb-primary-800);
      background: var(--kb-primary-50);

      &:hover {
        background: var(--kb-primary-100);
        transform: translateY(-1px);
      }
    }
    .cell.unavailable {
      color: var(--kb-danger);
      background: repeating-linear-gradient(
        135deg,
        color-mix(in srgb, var(--kb-danger) 7%, white),
        color-mix(in srgb, var(--kb-danger) 7%, white) 4px,
        color-mix(in srgb, var(--kb-danger) 13%, white) 4px,
        color-mix(in srgb, var(--kb-danger) 13%, white) 8px
      );
      cursor: not-allowed;
    }
    .cell.outside {
      opacity: 0.35;
    }
    .cell.selected {
      border-color: var(--kb-primary-700);
      color: #fff;
      background: linear-gradient(135deg, var(--kb-primary-500), var(--kb-primary-700));
      box-shadow: var(--shadow-soft);
    }
    .legend {
      display: flex;
      flex-wrap: wrap;
      gap: 0.5rem 1.25rem;
      margin-top: 0.75rem;
      font-size: 0.8125rem;
      color: var(--kb-ink-muted);
    }
    .legend span::before {
      content: '';
      display: inline-block;
      width: 0.75rem;
      height: 0.75rem;
      margin-right: 0.375rem;
      border-radius: 0.25rem;
      vertical-align: -1px;
    }
    .legend .free::before {
      background: var(--kb-primary-200);
    }
    .legend .taken::before {
      background: color-mix(in srgb, var(--kb-danger) 35%, white);
    }
  `,
  template: `
    <div class="head">
      <button
        type="button"
        class="nav"
        [disabled]="!canGoBack()"
        aria-label="Mois précédent"
        (click)="monthChange.emit(-1)"
      >
        <svg lucideChevronLeft aria-hidden="true"></svg>
      </button>
      <h3 class="title" aria-live="polite">{{ title() }}</h3>
      <button
        type="button"
        class="nav"
        [disabled]="!canGoForward()"
        aria-label="Mois suivant"
        (click)="monthChange.emit(1)"
      >
        <svg lucideChevronRight aria-hidden="true"></svg>
      </button>
    </div>

    <div class="grid" role="grid" [attr.aria-label]="title()">
      @for (name of weekdays; track name) {
        <span class="weekday" role="columnheader">{{ name }}</span>
      }
      @for (cell of cells(); track cell.iso) {
        <button
          type="button"
          role="gridcell"
          class="cell"
          [class.available]="cell.available && !cell.selected"
          [class.unavailable]="!cell.available"
          [class.outside]="!cell.inMonth"
          [class.selected]="cell.selected"
          [disabled]="!cell.available"
          [attr.aria-selected]="cell.selected"
          [attr.aria-label]="label(cell)"
          (click)="daySelected.emit(cell.iso)"
        >
          {{ cell.day }}
        </button>
      }
    </div>

    <p class="legend">
      <span class="free">Créneaux disponibles</span>
      <span class="taken">Complet, fermé ou indisponible</span>
    </p>
  `,
})
export class AvailabilityCalendar {
  /** First day of the displayed month. */
  readonly month = input.required<Date>();
  readonly days = input.required<readonly AvailabilityDay[]>();
  readonly selected = input<string | null>(null);
  readonly minMonth = input.required<Date>();
  readonly maxMonth = input.required<Date>();
  readonly daySelected = output<string>();
  readonly monthChange = output<-1 | 1>();

  protected readonly weekdays = WEEKDAYS;

  protected readonly title = computed(() =>
    new Intl.DateTimeFormat('fr-FR', { month: 'long', year: 'numeric' }).format(this.month()),
  );

  protected readonly canGoBack = computed(() => this.sameOrAfter(this.month(), this.minMonth()));
  protected readonly canGoForward = computed(() => this.month() < this.maxMonth());

  protected readonly cells = computed<CalendarCell[]>(() => {
    const month = this.month();
    const byDate = new Map(this.days().map((d) => [d.date, d] as const));
    const first = new Date(month.getFullYear(), month.getMonth(), 1);
    const offset = (first.getDay() + 6) % 7; // Monday first
    const start = new Date(first.getFullYear(), first.getMonth(), 1 - offset);
    const daysInMonth = new Date(first.getFullYear(), first.getMonth() + 1, 0).getDate();
    const total = offset + daysInMonth > 35 ? 42 : 35; // the sixth week only when the month needs it
    const cells: CalendarCell[] = [];

    for (let i = 0; i < total; i++) {
      const date = new Date(start.getFullYear(), start.getMonth(), start.getDate() + i);
      const iso = isoDay(date);
      const inMonth = date.getMonth() === month.getMonth();

      cells.push({
        iso,
        day: date.getDate(),
        inMonth,
        available: byDate.get(iso)?.hasAvailability ?? false,
        selected: iso === this.selected(),
      });
    }

    return cells;
  });

  protected label(cell: CalendarCell): string {
    const date = new Date(`${cell.iso}T12:00:00`);
    const text = new Intl.DateTimeFormat('fr-FR', {
      weekday: 'long',
      day: 'numeric',
      month: 'long',
    }).format(date);
    return `${text} : ${cell.available ? 'créneaux disponibles' : 'indisponible'}`;
  }

  private sameOrAfter(a: Date, b: Date): boolean {
    return a.getFullYear() * 12 + a.getMonth() > b.getFullYear() * 12 + b.getMonth();
  }
}
