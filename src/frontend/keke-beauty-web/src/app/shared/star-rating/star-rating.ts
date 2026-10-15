import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

/**
 * Read-only stars with the average and the number of reviews next to them ("4,6 · 12 avis"). Without review it says so instead
 * of showing empty stars, which would read as a bad rating.
 */
@Component({
  selector: 'app-star-rating',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    :host {
      display: inline-flex;
      align-items: center;
      gap: 0.375rem;
      font-size: 0.875rem;
    }
    .stars {
      position: relative;
      display: inline-block;
      color: var(--kb-border);
      letter-spacing: 0.0625rem;
      line-height: 1;
    }
    .fill {
      position: absolute;
      inset: 0 auto 0 0;
      overflow: hidden;
      white-space: nowrap;
      color: var(--kb-gold);
    }
    .value {
      font-weight: 700;
    }
    .muted {
      color: var(--kb-ink-muted);
    }
  `,
  template: `
    @if (count() > 0 && average() !== null) {
      <span class="stars" aria-hidden="true">
        ★★★★★
        <span class="fill" [style.width.%]="percent()">★★★★★</span>
      </span>
      @if (showText()) {
        <span class="value">{{ label() }}</span>
        <span class="muted">({{ count() }} avis)</span>
        <span class="visually-hidden">Note moyenne {{ label() }} sur 5, {{ count() }} avis</span>
      }
    } @else {
      <span class="muted">Pas encore d’avis</span>
    }
  `,
})
export class StarRating {
  readonly average = input<number | null | undefined>(null);
  readonly count = input(0);

  /** False for a single review's stars: only the stars, the text around them says the rest. */
  readonly showText = input(true);

  protected readonly percent = computed(() =>
    Math.max(0, Math.min(100, ((this.average() ?? 0) / 5) * 100)),
  );
  protected readonly label = computed(() => (this.average() ?? 0).toFixed(1).replace('.', ','));
}
