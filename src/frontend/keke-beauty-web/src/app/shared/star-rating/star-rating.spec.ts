import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { StarRating } from './star-rating';

describe('StarRating', () => {
  function render(inputs: { average?: number | null; count?: number; showText?: boolean }) {
    const fixture = TestBed.createComponent(StarRating);
    for (const [key, value] of Object.entries(inputs)) {
      fixture.componentRef.setInput(key, value);
    }
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('shows the average with a comma, the number of reviews and a readable label', () => {
    const element = render({ average: 4.6, count: 12 });

    expect(element.textContent).toContain('4,6');
    expect(element.textContent).toContain('(12 avis)');
    expect(element.textContent).toContain('Note moyenne 4,6 sur 5, 12 avis');
    expect(element.querySelector<HTMLElement>('.fill')?.style.width).toBe('92%');
  });

  it('says there is no review yet instead of showing empty stars', () => {
    const element = render({ average: null, count: 0 });

    expect(element.textContent).toContain('Pas encore d’avis');
    expect(element.querySelector('.stars')).toBeNull();
  });

  it('can show only the stars, for a single review', () => {
    const element = render({ average: 3, count: 1, showText: false });

    expect(element.querySelector('.stars')).not.toBeNull();
    expect(element.textContent).not.toContain('avis');
  });
});
