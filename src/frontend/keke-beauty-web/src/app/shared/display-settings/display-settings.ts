import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import {
  DisplayPreferences,
  TEXT_SIZE_LABELS,
  THEME_LABELS,
  type TextSize,
  type ThemeChoice,
} from '../../core/display/display-preferences';

/** Theme (light, dark, like the device) and text size, as two groups of radio buttons; applied immediately. */
@Component({
  selector: 'app-display-settings',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    :host {
      display: grid;
      gap: 1rem;
    }
    fieldset {
      display: flex;
      flex-wrap: wrap;
      gap: 0.5rem;
      margin: 0;
      padding: 0;
      border: 0;
    }
    legend {
      margin-bottom: 0.4rem;
      font-weight: 600;
    }
    label {
      display: inline-flex;
      align-items: center;
      gap: 0.4rem;
      min-height: 2.75rem;
      padding: 0.4rem 0.9rem;
      border: 1.5px solid var(--kb-border);
      border-radius: var(--radius-pill);
      cursor: pointer;
    }
    label:has(input:checked) {
      border-color: var(--kb-primary-500);
      background: var(--kb-primary-50);
      color: var(--kb-primary-800);
      font-weight: 600;
    }
    label:has(input:focus-visible) {
      outline: 2px solid var(--kb-primary-500);
      outline-offset: 2px;
    }
    input {
      accent-color: var(--kb-primary-600);
    }
  `,
  template: `
    <fieldset>
      <legend>Thème</legend>
      @for (choice of themes; track choice.id) {
        <label>
          <input
            type="radio"
            name="kb-theme"
            [value]="choice.id"
            [checked]="prefs.theme() === choice.id"
            (change)="prefs.setTheme(choice.id)"
          />
          {{ choice.label }}
        </label>
      }
    </fieldset>
    <fieldset>
      <legend>Taille du texte</legend>
      @for (size of sizes; track size.id) {
        <label>
          <input
            type="radio"
            name="kb-text-size"
            [value]="size.id"
            [checked]="prefs.textSize() === size.id"
            (change)="prefs.setTextSize(size.id)"
          />
          {{ size.label }}
        </label>
      }
    </fieldset>
  `,
})
export class DisplaySettings {
  protected readonly prefs = inject(DisplayPreferences);
  protected readonly themes = (Object.keys(THEME_LABELS) as ThemeChoice[]).map((id) => ({
    id,
    label: THEME_LABELS[id],
  }));
  protected readonly sizes = (Object.keys(TEXT_SIZE_LABELS) as TextSize[]).map((id) => ({
    id,
    label: TEXT_SIZE_LABELS[id],
  }));
}
