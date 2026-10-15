import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AuthService } from '../auth/auth.service';
import { RealtimeService } from '../realtime/realtime.service';
import { NotificationService } from './notification.service';

describe('NotificationService', () => {
  const user = signal<{ id: string } | null>(null);
  let http: HttpTestingController;
  let handlers: Record<string, (...args: unknown[]) => void>;
  const stop = vi.fn(() => Promise.resolve());

  beforeEach(() => {
    user.set(null);
    handlers = {};
    stop.mockClear();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: { user } },
        {
          provide: RealtimeService,
          useValue: {
            connect: vi.fn((_hub: string, options: { handlers: typeof handlers }) => {
              handlers = options.handlers;
              return Promise.resolve({ connected: () => true, stop });
            }),
          },
        },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  async function signIn(): Promise<NotificationService> {
    const service = TestBed.inject(NotificationService);
    user.set({ id: 'u1' });
    TestBed.tick();
    (await vi.waitFor(() => http.expectOne('/api/v1/me/notifications'))).flush({
      items: [
        {
          id: 'n1',
          title: 'Hello',
          body: 'b',
          templateCode: 't',
          createdAt: '2026-10-12T10:00:00Z',
          isRead: false,
        },
      ],
      unread: 1,
    });
    await vi.waitFor(() => expect(service.unread()).toBe(1));
    await vi.waitFor(() => expect(Object.keys(handlers)).toContain('NotificationReceived'));
    return service;
  }

  it('does nothing while nobody is signed in', () => {
    const service = TestBed.inject(NotificationService);
    TestBed.tick();

    http.expectNone('/api/v1/me/notifications');
    expect(service.items()).toEqual([]);
  });

  it('loads the list on sign-in and prepends live notifications once', async () => {
    const service = await signIn();

    const payload = {
      id: 'n2',
      title: 'Rendez-vous confirmé',
      body: 'ok',
      templateCode: 'appointment.confirmed',
      createdAt: '2026-10-12T11:00:00Z',
    };
    handlers['NotificationReceived'](payload);
    handlers['NotificationReceived'](payload); // duplicates (e.g. after a reconnection) are ignored

    expect(service.items().map((n) => n.id)).toEqual(['n2', 'n1']);
    expect(service.unread()).toBe(2);
    expect(service.latest()?.title).toBe('Rendez-vous confirmé');
  });

  it('marks one notification read optimistically, then all', async () => {
    const service = await signIn();

    const one = service.markRead('n1');
    expect(service.unread()).toBe(0);
    http
      .expectOne('/api/v1/me/notifications/n1/read')
      .flush(null, { status: 204, statusText: 'No Content' });
    await one;
    expect(service.items()[0].isRead).toBe(true);

    const all = service.markAllRead();
    http
      .expectOne('/api/v1/me/notifications/read-all')
      .flush(null, { status: 204, statusText: 'No Content' });
    await all;
    expect(service.unread()).toBe(0);
  });

  it('clears everything and disconnects on sign-out', async () => {
    const service = await signIn();

    user.set(null);
    TestBed.tick();

    await vi.waitFor(() => expect(stop).toHaveBeenCalled());
    expect(service.items()).toEqual([]);
    expect(service.unread()).toBe(0);
  });
});
