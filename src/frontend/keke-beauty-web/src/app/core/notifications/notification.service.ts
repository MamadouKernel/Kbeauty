import { HttpClient } from '@angular/common/http';
import { Injectable, effect, inject, signal, untracked } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import type { components } from '../api/api.generated';
import { AuthService } from '../auth/auth.service';
import { RealtimeConnection, RealtimeService } from '../realtime/realtime.service';

export type AppNotification = components['schemas']['NotificationDto'];
type NotificationList = components['schemas']['NotificationList'];

/** Payload pushed by the notifications hub (same fields as the stored notification, minus the read flag). */
interface PushedNotification {
  readonly id: string;
  readonly title: string;
  readonly body: string;
  readonly templateCode: string;
  readonly createdAt: string;
}

/**
 * In-app notification centre (PRD NOT-01): the list and unread counter of the signed-in user, kept live through the
 * notifications hub. `latest` lets the shell show a transient toast for a message that just arrived.
 */
@Injectable({ providedIn: 'root' })
export class NotificationService {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly realtime = inject(RealtimeService);

  readonly items = signal<readonly AppNotification[]>([]);
  readonly unread = signal(0);
  readonly latest = signal<AppNotification | null>(null);

  private connection: RealtimeConnection | null = null;
  private watching: string | null = null;

  constructor() {
    effect(() => {
      const userId = this.auth.user()?.id ?? null;
      untracked(() => void this.follow(userId));
    });
  }

  async refresh(): Promise<void> {
    try {
      const list = await firstValueFrom(
        this.http.get<NotificationList>('/api/v1/me/notifications'),
      );
      this.items.set(list.items);
      this.unread.set(list.unread);
    } catch {
      // The bell is secondary: keep what is already shown.
    }
  }

  async markRead(id: string): Promise<void> {
    const target = this.items().find((n) => n.id === id);
    if (!target || target.isRead) {
      return;
    }

    this.items.update((items) => items.map((n) => (n.id === id ? { ...n, isRead: true } : n)));
    this.unread.update((count) => Math.max(0, count - 1));
    try {
      await firstValueFrom(this.http.post<void>(`/api/v1/me/notifications/${id}/read`, {}));
    } catch {
      await this.refresh();
    }
  }

  async markAllRead(): Promise<void> {
    this.items.update((items) => items.map((n) => ({ ...n, isRead: true })));
    this.unread.set(0);
    try {
      await firstValueFrom(this.http.post<void>('/api/v1/me/notifications/read-all', {}));
    } catch {
      await this.refresh();
    }
  }

  dismissLatest(): void {
    this.latest.set(null);
  }

  private async follow(userId: string | null): Promise<void> {
    if (userId === this.watching) {
      return;
    }

    this.watching = userId;
    await this.connection?.stop();
    this.connection = null;
    this.items.set([]);
    this.unread.set(0);
    this.latest.set(null);
    if (!userId) {
      return;
    }

    await this.refresh();
    const connection = await this.realtime.connect('/hubs/notifications', {
      authenticated: true,
      handlers: {
        NotificationReceived: (payload) => this.receive(payload as PushedNotification),
        Reconnected: () => void this.refresh(),
      },
    });
    if (this.watching === userId) {
      this.connection = connection;
    } else {
      await connection.stop();
    }
  }

  private receive(payload: PushedNotification): void {
    if (this.items().some((n) => n.id === payload.id)) {
      return;
    }

    const notification: AppNotification = { ...payload, isRead: false };
    this.items.update((items) => [notification, ...items].slice(0, 30));
    this.unread.update((count) => count + 1);
    this.latest.set(notification);
  }
}
