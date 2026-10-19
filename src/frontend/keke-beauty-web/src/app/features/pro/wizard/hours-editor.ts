import { ChangeDetectionStrategy, Component, computed, input, output, signal } from '@angular/core';
import { LucidePlus, LucideX } from '@lucide/angular';
import type { OpeningHourInput } from '../../../core/partner/partner.service';

export interface TimeRange {
  readonly opens: string;
  readonly closes: string;
}

export interface DayRow {
  readonly day: number;
  readonly label: string;
  readonly open: boolean;
  readonly ranges: readonly TimeRange[];
}

/** Monday first, as displayed on the public page (JavaScript/.NET numbering: Sunday = 0). */
const DAYS: readonly { readonly day: number; readonly label: string }[] = [
  { day: 1, label: 'Lundi' },
  { day: 2, label: 'Mardi' },
  { day: 3, label: 'Mercredi' },
  { day: 4, label: 'Jeudi' },
  { day: 5, label: 'Vendredi' },
  { day: 6, label: 'Samedi' },
  { day: 0, label: 'Dimanche' },
];

export const MAX_RANGES_PER_DAY = 4;
const DEFAULT_RANGE: TimeRange = { opens: '09:00', closes: '18:00' };

export function rowsFrom(hours: readonly OpeningHourInput[]): DayRow[] {
  return DAYS.map(({ day, label }) => {
    const ranges = hours
      .filter((h) => h.dayOfWeek === day)
      .map((h) => ({ opens: h.opensAt, closes: h.closesAt }))
      .sort((a, b) => a.opens.localeCompare(b.opens));
    return { day, label, open: ranges.length > 0, ranges };
  });
}

export function inputsFrom(rows: readonly DayRow[]): OpeningHourInput[] {
  return rows
    .filter((row) => row.open)
    .flatMap((row) =>
      row.ranges.map((r) => ({ dayOfWeek: row.day, opensAt: r.opens, closesAt: r.closes })),
    );
}

/** French message for the first problem found, or `null` when every open day is consistent. */
export function validateRows(rows: readonly DayRow[]): string | null {
  for (const row of rows.filter((r) => r.open)) {
    const ranges = [...row.ranges].sort((a, b) => a.opens.localeCompare(b.opens));
    if (ranges.length === 0) {
      return `${row.label} : ajoutez une plage horaire ou fermez ce jour.`;
    }

    for (const [index, range] of ranges.entries()) {
      if (!range.opens || !range.closes || range.opens >= range.closes) {
        return `${row.label} : l'heure de fermeture doit être postérieure à l'heure d'ouverture.`;
      }

      if (index > 0 && range.opens < ranges[index - 1].closes) {
        return `${row.label} : les plages horaires ne doivent pas se chevaucher.`;
      }
    }
  }

  return null;
}

/** Opening hours editor: one row per weekday, several ranges per day (e.g. a lunch break). */
@Component({
  selector: 'app-hours-editor',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [LucidePlus, LucideX],
  styles: `
    :host {
      display: grid;
      gap: 1rem;
    }
    .days {
      display: grid;
      gap: 0.75rem;
    }
    .day {
      display: grid;
      gap: 0.5rem;
      padding: 0.75rem 1rem;
      border: 1px solid var(--kb-border);
      border-radius: var(--radius-md);
      background: var(--kb-card);

      @media (min-width: 640px) {
        grid-template-columns: 11rem 1fr;
        align-items: start;
      }
    }
    .day__name {
      display: flex;
      align-items: center;
      gap: 0.625rem;
      min-height: 2.75rem;
      font-weight: 600;
    }
    .day__name input {
      width: 1.25rem;
      height: 1.25rem;
      accent-color: var(--kb-primary-600);
    }
    .ranges {
      display: grid;
      gap: 0.5rem;
    }
    .range {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 0.5rem;
    }
    .range input[type='time'] {
      min-height: 2.75rem;
      padding: 0.5rem 0.75rem;
      border: 1px solid var(--kb-border);
      border-radius: var(--radius-sm);
      font: inherit;
      color: inherit;
    }
    .closed {
      min-height: 2.75rem;
      display: flex;
      align-items: center;
      color: var(--kb-ink-muted);
    }
    .icon-btn {
      display: inline-grid;
      place-items: center;
      width: 2.75rem;
      height: 2.75rem;
      border: 0;
      border-radius: var(--radius-pill);
      background: transparent;
      color: var(--kb-ink-muted);
      cursor: pointer;

      &:hover {
        background: var(--kb-primary-50);
        color: var(--kb-primary-700);
      }
    }
    .icon-btn svg {
      width: 1.125rem;
      height: 1.125rem;
    }
    .toolbar {
      display: flex;
      flex-wrap: wrap;
      gap: 0.5rem;
    }
  `,
  template: `
    <div class="days">
      @for (row of rows(); track row.day) {
        <div class="day">
          <label class="day__name">
            <input
              type="checkbox"
              [checked]="row.open"
              [disabled]="disabled()"
              (change)="toggle(row.day, $any($event.target).checked)"
            />
            {{ row.label }}
          </label>

          @if (row.open) {
            <div class="ranges">
              @for (range of row.ranges; track $index) {
                <div class="range">
                  <input
                    type="time"
                    [value]="range.opens"
                    [disabled]="disabled()"
                    [attr.aria-label]="row.label + ', ouverture'"
                    (change)="setTime(row.day, $index, 'opens', $any($event.target).value)"
                  />
                  <span aria-hidden="true">à</span>
                  <input
                    type="time"
                    [value]="range.closes"
                    [disabled]="disabled()"
                    [attr.aria-label]="row.label + ', fermeture'"
                    (change)="setTime(row.day, $index, 'closes', $any($event.target).value)"
                  />
                  @if (row.ranges.length > 1 && !disabled()) {
                    <button
                      type="button"
                      class="icon-btn"
                      [attr.aria-label]="'Supprimer cette plage du ' + row.label.toLowerCase()"
                      (click)="removeRange(row.day, $index)"
                    >
                      <svg lucideX aria-hidden="true"></svg>
                    </button>
                  }
                </div>
              }
              @if (row.ranges.length < maxRanges && !disabled()) {
                <button type="button" class="btn btn-ghost btn-sm" (click)="addRange(row.day)">
                  <svg lucidePlus aria-hidden="true" width="16" height="16"></svg>
                  Ajouter une plage
                </button>
              }
            </div>
          } @else {
            <p class="closed">Fermé</p>
          }
        </div>
      }
    </div>

    @if (!disabled()) {
      <div class="toolbar">
        <button type="button" class="btn btn-ghost btn-sm" (click)="copyMondayToWeekdays()">
          Appliquer les horaires du lundi aux jours ouvrés (lundi → vendredi)
        </button>
      </div>
    }

    @if (error()) {
      <p class="form-error" role="alert">{{ error() }}</p>
    }

    @if (!disabled()) {
      <button type="button" class="btn btn-primary" (click)="submit()">
        {{ saveLabel() }}
      </button>
    }
  `,
})
export class HoursEditor {
  readonly hours = input<readonly OpeningHourInput[]>([]);
  readonly disabled = input(false);
  readonly saveLabel = input('Enregistrer les horaires');
  readonly saved = output<OpeningHourInput[]>();

  protected readonly maxRanges = MAX_RANGES_PER_DAY;
  private readonly edits = signal<DayRow[] | null>(null);
  protected readonly error = signal('');

  /** Edits made in the form win over the saved value until the parent supplies a new one. */
  protected readonly rows = computed(() => this.edits() ?? rowsFrom(this.hours()));

  protected toggle(day: number, open: boolean): void {
    this.update(day, (row) => ({
      ...row,
      open,
      ranges: open && row.ranges.length === 0 ? [DEFAULT_RANGE] : row.ranges,
    }));
  }

  protected setTime(day: number, index: number, field: keyof TimeRange, value: string): void {
    this.update(day, (row) => ({
      ...row,
      ranges: row.ranges.map((range, i) => (i === index ? { ...range, [field]: value } : range)),
    }));
  }

  protected addRange(day: number): void {
    this.update(day, (row) => {
      const last = row.ranges.at(-1);
      const opens = last ? last.closes : DEFAULT_RANGE.opens;
      return { ...row, ranges: [...row.ranges, { opens, closes: DEFAULT_RANGE.closes }] };
    });
  }

  protected removeRange(day: number, index: number): void {
    this.update(day, (row) => ({ ...row, ranges: row.ranges.filter((_, i) => i !== index) }));
  }

  protected copyMondayToWeekdays(): void {
    const monday = this.rows().find((row) => row.day === 1);
    if (!monday) {
      return;
    }

    this.edits.set(
      this.rows().map((row) =>
        row.day >= 2 && row.day <= 5 ? { ...row, open: monday.open, ranges: monday.ranges } : row,
      ),
    );
  }

  protected submit(): void {
    const problem = validateRows(this.rows());
    this.error.set(problem ?? '');
    if (!problem) {
      this.saved.emit(inputsFrom(this.rows()));
    }
  }

  private update(day: number, change: (row: DayRow) => DayRow): void {
    this.error.set('');
    this.edits.set(this.rows().map((row) => (row.day === day ? change(row) : row)));
  }
}
