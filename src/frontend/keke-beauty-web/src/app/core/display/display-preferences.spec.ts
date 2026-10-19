import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { DisplayPreferences } from './display-preferences';

describe('DisplayPreferences', () => {
  const root = document.documentElement;

  beforeEach(() => {
    localStorage.clear();
    root.removeAttribute('data-theme');
    root.removeAttribute('data-text-size');
    TestBed.configureTestingModule({});
  });

  afterEach(() => {
    localStorage.clear();
    root.removeAttribute('data-theme');
    root.removeAttribute('data-text-size');
  });

  it('follows the device by default and stamps the root element', () => {
    const prefs = TestBed.inject(DisplayPreferences);
    prefs.init();

    expect(prefs.theme()).toBe('system');
    expect(['light', 'dark']).toContain(root.getAttribute('data-theme'));
    expect(root.hasAttribute('data-text-size')).toBe(false);
  });

  it('applies and remembers an explicit choice', () => {
    const prefs = TestBed.inject(DisplayPreferences);
    prefs.init();

    prefs.setTheme('dark');
    prefs.setTextSize('xlarge');

    expect(root.getAttribute('data-theme')).toBe('dark');
    expect(root.getAttribute('data-text-size')).toBe('xlarge');
    expect(localStorage.getItem('kb.theme')).toBe('dark');
    expect(localStorage.getItem('kb.text-size')).toBe('xlarge');

    prefs.setTextSize('normal');
    expect(root.hasAttribute('data-text-size')).toBe(false);
  });

  it('restores the saved choice and ignores garbage', () => {
    localStorage.setItem('kb.theme', 'light');
    localStorage.setItem('kb.text-size', 'gigantic');

    const prefs = TestBed.inject(DisplayPreferences);
    prefs.init();

    expect(prefs.theme()).toBe('light');
    expect(prefs.textSize()).toBe('normal');
    expect(root.getAttribute('data-theme')).toBe('light');
  });
});
