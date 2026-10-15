import { DOCUMENT } from '@angular/common';
import { TestBed } from '@angular/core/testing';
import { SwUpdate } from '@angular/service-worker';
import { Subject } from 'rxjs';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AppUpdates } from './app-updates';

describe('AppUpdates', () => {
  const events = new Subject<{ type: string }>();
  const reload = vi.fn();
  const activateUpdate = vi.fn(() => Promise.resolve(true));

  beforeEach(() => {
    vi.useFakeTimers();
    reload.mockClear();
    activateUpdate.mockClear();
    TestBed.configureTestingModule({
      providers: [
        {
          provide: SwUpdate,
          useValue: { isEnabled: true, versionUpdates: events, activateUpdate },
        },
        { provide: DOCUMENT, useValue: { location: { reload } } },
      ],
    });
    TestBed.inject(AppUpdates);
  });

  afterEach(() => vi.useRealTimers());

  it('switches to a new version found just after the page was opened', async () => {
    events.next({ type: 'VERSION_READY' });
    await vi.advanceTimersByTimeAsync(0);

    expect(activateUpdate).toHaveBeenCalledOnce();
    expect(reload).toHaveBeenCalledOnce();
  });

  it('never interrupts a page that has been in use for a while', async () => {
    await vi.advanceTimersByTimeAsync(5 * 60_000);
    events.next({ type: 'VERSION_READY' });
    await vi.advanceTimersByTimeAsync(0);

    expect(reload).not.toHaveBeenCalled();
  });

  it('ignores the other service worker events', async () => {
    events.next({ type: 'VERSION_DETECTED' });
    await vi.advanceTimersByTimeAsync(0);

    expect(reload).not.toHaveBeenCalled();
  });
});
