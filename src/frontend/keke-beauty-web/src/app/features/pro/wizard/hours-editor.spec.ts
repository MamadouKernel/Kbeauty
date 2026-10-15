import { TestBed } from '@angular/core/testing';
import { describe, expect, it, vi } from 'vitest';
import { HoursEditor, inputsFrom, rowsFrom, validateRows } from './hours-editor';

const WEEK = [
  { dayOfWeek: 1, opensAt: '09:00', closesAt: '12:30' },
  { dayOfWeek: 1, opensAt: '14:00', closesAt: '18:00' },
  { dayOfWeek: 0, opensAt: '10:00', closesAt: '14:00' },
];

describe('hours helpers', () => {
  it('lists Monday first and Sunday last, with several ranges per day sorted', () => {
    const rows = rowsFrom([WEEK[1], WEEK[0], WEEK[2]]);

    expect(rows.map((r) => r.label)).toEqual([
      'Lundi',
      'Mardi',
      'Mercredi',
      'Jeudi',
      'Vendredi',
      'Samedi',
      'Dimanche',
    ]);
    expect(rows[0].open).toBe(true);
    expect(rows[0].ranges.map((r) => r.opens)).toEqual(['09:00', '14:00']);
    expect(rows[1].open).toBe(false);
    expect(rows[6].open).toBe(true);
  });

  it('round-trips through the API shape and ignores closed days', () => {
    const rows = rowsFrom(WEEK);

    expect(inputsFrom(rows)).toEqual([WEEK[0], WEEK[1], WEEK[2]]);
    expect(inputsFrom(rows.map((r) => ({ ...r, open: false })))).toEqual([]);
  });

  it('accepts consistent hours', () => {
    expect(validateRows(rowsFrom(WEEK))).toBeNull();
  });

  it('rejects a closing time before the opening time', () => {
    const rows = rowsFrom([{ dayOfWeek: 2, opensAt: '18:00', closesAt: '09:00' }]);

    expect(validateRows(rows)).toContain('Mardi');
  });

  it('rejects overlapping ranges', () => {
    const rows = rowsFrom([
      { dayOfWeek: 3, opensAt: '09:00', closesAt: '13:00' },
      { dayOfWeek: 3, opensAt: '12:00', closesAt: '18:00' },
    ]);

    expect(validateRows(rows)).toContain('chevaucher');
  });

  it('rejects an open day without any range', () => {
    const rows = rowsFrom([]).map((r) => (r.day === 4 ? { ...r, open: true } : r));

    expect(validateRows(rows)).toContain('Jeudi');
  });
});

describe('HoursEditor', () => {
  function create(hours = WEEK) {
    const fixture = TestBed.createComponent(HoursEditor);
    fixture.componentRef.setInput('hours', hours);
    fixture.detectChanges();
    return fixture;
  }

  it('emits the saved hours when valid', () => {
    const fixture = create();
    const saved = vi.fn();
    fixture.componentInstance.saved.subscribe(saved);

    (fixture.nativeElement as HTMLElement)
      .querySelector<HTMLButtonElement>('button.btn-primary')
      ?.click();

    expect(saved).toHaveBeenCalledWith(WEEK);
  });

  it('opens a closed day with default hours and copies Monday to the weekdays', () => {
    const fixture = create();
    const root = fixture.nativeElement as HTMLElement;
    const saved = vi.fn();
    fixture.componentInstance.saved.subscribe(saved);

    const copy = [...root.querySelectorAll<HTMLButtonElement>('button')].find((b) =>
      b.textContent?.includes('Appliquer les horaires du lundi'),
    );
    copy?.click();
    fixture.detectChanges();
    root.querySelector<HTMLButtonElement>('button.btn-primary')?.click();

    const sent = saved.mock.calls[0][0] as { dayOfWeek: number }[];
    expect(sent.filter((h) => h.dayOfWeek === 5)).toHaveLength(2);
    expect(sent.filter((h) => h.dayOfWeek === 6)).toHaveLength(0);
  });

  it('shows an error and emits nothing when the hours are inconsistent', () => {
    const fixture = create([{ dayOfWeek: 1, opensAt: '18:00', closesAt: '09:00' }]);
    const saved = vi.fn();
    fixture.componentInstance.saved.subscribe(saved);

    (fixture.nativeElement as HTMLElement)
      .querySelector<HTMLButtonElement>('button.btn-primary')
      ?.click();
    fixture.detectChanges();

    expect(saved).not.toHaveBeenCalled();
    expect(
      (fixture.nativeElement as HTMLElement).querySelector('[role="alert"]')?.textContent,
    ).toContain('Lundi');
  });

  it('is read-only when disabled', () => {
    const fixture = TestBed.createComponent(HoursEditor);
    fixture.componentRef.setInput('hours', WEEK);
    fixture.componentRef.setInput('disabled', true);
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('button.btn-primary')).toBeNull();
  });
});
