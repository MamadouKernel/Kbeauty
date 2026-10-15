import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { LucideCircleAlert, LucideCircleCheck, LucideLoaderCircle } from '@lucide/angular';
import { HealthService } from '../../core/health/health.service';

/** Live API status pill. State is conveyed by icon AND text, never by color alone. */
@Component({
  selector: 'app-api-status-badge',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [LucideCircleAlert, LucideCircleCheck, LucideLoaderCircle],
  template: `
    <span class="badge" [attr.data-status]="status()" role="status" aria-live="polite">
      @switch (status()) {
        @case ('online') {
          <svg lucideCircleCheck aria-hidden="true"></svg>
          Service en ligne
        }
        @case ('offline') {
          <svg lucideCircleAlert aria-hidden="true"></svg>
          Service indisponible
        }
        @default {
          <svg lucideLoaderCircle class="spin" aria-hidden="true"></svg>
          Vérification…
        }
      }
    </span>
  `,
  styles: `
    .badge {
      display: inline-flex;
      align-items: center;
      gap: 0.375rem;
      min-height: 2rem;
      padding: 0.25rem 0.75rem;
      border-radius: var(--radius-pill);
      border: 1px solid var(--kb-border);
      background: var(--kb-surface);
      font-size: var(--fs-xs);
      font-weight: 600;
      white-space: nowrap;
      color: var(--kb-ink-muted);
    }
    svg {
      width: 1rem;
      height: 1rem;
    }
    [data-status='online'] {
      color: var(--kb-success);
      border-color: color-mix(in srgb, var(--kb-success) 30%, white);
      background: color-mix(in srgb, var(--kb-success) 8%, white);
    }
    [data-status='offline'] {
      color: var(--kb-warning);
      border-color: color-mix(in srgb, var(--kb-warning) 35%, white);
      background: color-mix(in srgb, var(--kb-warning) 8%, white);
    }
    .spin {
      animation: spin 1s linear infinite;
    }
    @keyframes spin {
      to {
        transform: rotate(360deg);
      }
    }
  `,
})
export class ApiStatusBadge {
  protected readonly status = inject(HealthService).status;
}
