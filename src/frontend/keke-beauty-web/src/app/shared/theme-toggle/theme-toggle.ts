import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { DisplayPreferences } from '../../core/display/display-preferences';

/**
 * Light / dark switch of the site header, for every visitor. It shows the theme you would switch to; the full choice
 * (including "like my device" and the text size) stays in "Mon compte".
 */
@Component({
  selector: 'app-theme-toggle',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    button {
      display: inline-grid;
      place-items: center;
      width: 2.75rem;
      height: 2.75rem;
      border: 1px solid var(--kb-primary-200);
      border-radius: 50%;
      background: rgb(var(--kb-paper-rgb) / 70%);
      color: var(--kb-primary-700);
      cursor: pointer;
      transition:
        background-color 200ms ease,
        transform 200ms var(--ease-out);

      &:hover {
        background: var(--kb-primary-50);
        transform: translateY(-1px);
      }
      &:focus-visible {
        outline: 3px solid var(--kb-primary-400);
        outline-offset: 2px;
      }
      svg {
        width: 1.25rem;
        height: 1.25rem;
      }
    }
  `,
  template: `
    <button
      type="button"
      [attr.aria-label]="dark() ? 'Passer en mode clair' : 'Passer en mode sombre'"
      [title]="dark() ? 'Mode clair' : 'Mode sombre'"
      (click)="toggle()"
    >
      <!-- Inline icons (Lucide "sun" and "moon"): this button is on every page, so it stays out of the icon library. -->
      <svg
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        stroke-width="2"
        stroke-linecap="round"
        stroke-linejoin="round"
        aria-hidden="true"
      >
        @if (dark()) {
          <circle cx="12" cy="12" r="4" />
          <path
            d="M12 2v2M12 20v2M4.93 4.93l1.41 1.41M17.66 17.66l1.41 1.41M2 12h2M20 12h2M6.34 17.66l-1.41 1.41M19.07 4.93l-1.41 1.41"
          />
        } @else {
          <path d="M12 3a6 6 0 0 0 9 9 9 9 0 1 1-9-9Z" />
        }
      </svg>
    </button>
  `,
})
export class ThemeToggle {
  private readonly prefs = inject(DisplayPreferences);

  protected readonly dark = computed(() => this.prefs.shown() === 'dark');

  protected toggle(): void {
    this.prefs.setTheme(this.dark() ? 'light' : 'dark');
  }
}
