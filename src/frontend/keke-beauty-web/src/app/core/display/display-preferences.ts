import { DOCUMENT } from '@angular/common';
import { Injectable, inject, signal } from '@angular/core';

export type ThemeChoice = 'system' | 'light' | 'dark';
export type TextSize = 'normal' | 'large' | 'xlarge';

export const THEME_LABELS: Readonly<Record<ThemeChoice, string>> = {
  system: 'Comme mon appareil',
  light: 'Clair',
  dark: 'Sombre',
};

export const TEXT_SIZE_LABELS: Readonly<Record<TextSize, string>> = {
  normal: 'Normale',
  large: 'Grande',
  xlarge: 'Très grande',
};

const THEME_KEY = 'kb.theme';
const TEXT_KEY = 'kb.text-size';

/**
 * Dark mode and text size (lot 7). The choice is kept on the device (it is about the screen, not the account) and applied
 * as `data-theme` / `data-text-size` on the root element, which the global stylesheet maps to its tokens. "system"
 * follows the device setting live.
 */
@Injectable({ providedIn: 'root' })
export class DisplayPreferences {
  private readonly document = inject(DOCUMENT);
  private readonly media =
    this.document.defaultView?.matchMedia?.('(prefers-color-scheme: dark)') ?? null;

  readonly theme = signal<ThemeChoice>(read(THEME_KEY, ['system', 'light', 'dark'], 'system'));
  readonly textSize = signal<TextSize>(read(TEXT_KEY, ['normal', 'large', 'xlarge'], 'normal'));
  /** The theme on screen right now ("system" resolved), kept current when the device setting changes. */
  readonly shown = signal<'light' | 'dark'>('light');

  /** Applies the saved choice; called once at start-up, before the first screen is drawn. */
  init(): void {
    this.apply();
    this.media?.addEventListener?.('change', () => {
      if (this.theme() === 'system') {
        this.apply();
      }
    });
  }

  setTheme(choice: ThemeChoice): void {
    this.theme.set(choice);
    write(THEME_KEY, choice);
    this.apply();
  }

  setTextSize(size: TextSize): void {
    this.textSize.set(size);
    write(TEXT_KEY, size);
    this.apply();
  }

  /** The theme actually shown: "system" resolved against the device setting. */
  resolvedTheme(): 'light' | 'dark' {
    const choice = this.theme();
    return choice === 'system' ? (this.media?.matches ? 'dark' : 'light') : choice;
  }

  private apply(): void {
    const root = this.document.documentElement;
    this.shown.set(this.resolvedTheme());
    root.setAttribute('data-theme', this.shown());
    if (this.textSize() === 'normal') {
      root.removeAttribute('data-text-size');
    } else {
      root.setAttribute('data-text-size', this.textSize());
    }
  }
}

function read<T extends string>(key: string, allowed: readonly T[], fallback: T): T {
  try {
    const stored = globalThis.localStorage?.getItem(key);
    return allowed.includes(stored as T) ? (stored as T) : fallback;
  } catch {
    // Storage blocked (private mode, strict settings): the default applies.
    return fallback;
  }
}

function write(key: string, value: string): void {
  try {
    globalThis.localStorage?.setItem(key, value);
  } catch {
    // Not remembered on this device; the choice still applies until the page is closed.
  }
}
