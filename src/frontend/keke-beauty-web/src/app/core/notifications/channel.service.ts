import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { SwPush } from '@angular/service-worker';
import { firstValueFrom } from 'rxjs';
import type { components } from '../api/api.generated';

type Schemas = components['schemas'];

export type NotificationPreferences = Schemas['NotificationPreferencesDto'];
export type PushConfig = Schemas['PushConfigDto'];

/** Whether a phone number can be linked to WhatsApp, and the masked number when it is. */
export interface WhatsAppStatus {
  readonly available: boolean;
  readonly linked: boolean;
  readonly phoneMasked: string | null;
}

/**
 * The channels a client receives notifications on (PRD NOT-01): preferences, Web Push subscription of this browser and the
 * WhatsApp number. Push needs the Angular service worker, which only runs in the production build over HTTPS.
 */
@Injectable({ providedIn: 'root' })
export class ChannelService {
  private readonly http = inject(HttpClient);
  private readonly swPush = inject(SwPush);

  /** True when this browser can do Web Push (service worker active, push API present). */
  pushSupported(): boolean {
    return this.swPush.isEnabled && 'PushManager' in window;
  }

  preferences(): Promise<NotificationPreferences> {
    return firstValueFrom(
      this.http.get<NotificationPreferences>('/api/v1/me/notification-preferences'),
    );
  }

  savePreferences(preferences: NotificationPreferences): Promise<NotificationPreferences> {
    return firstValueFrom(
      this.http.put<NotificationPreferences>('/api/v1/me/notification-preferences', preferences),
    );
  }

  pushConfig(): Promise<PushConfig> {
    return firstValueFrom(this.http.get<PushConfig>('/api/v1/me/push/config'));
  }

  /** The subscription of this browser, if it has one. */
  async currentSubscription(): Promise<PushSubscription | null> {
    if (!this.pushSupported()) {
      return null;
    }
    return firstValueFrom(this.swPush.subscription);
  }

  /** Asks the browser's permission, subscribes to the push service with the server's VAPID key and registers the endpoint. */
  async enablePush(): Promise<void> {
    const config = await this.pushConfig();
    if (!config.enabled || !config.publicKey) {
      throw new Error('Les notifications push ne sont pas activées par la plateforme.');
    }
    const subscription = await this.swPush.requestSubscription({
      serverPublicKey: config.publicKey,
    });
    const json = subscription.toJSON();
    await firstValueFrom(
      this.http.post<void>('/api/v1/me/push/subscribe', {
        endpoint: json.endpoint,
        keys: { p256dh: json.keys?.['p256dh'], auth: json.keys?.['auth'] },
        userAgent: navigator.userAgent.slice(0, 200),
      }),
    );
  }

  async disablePush(): Promise<void> {
    const subscription = await this.currentSubscription();
    if (!subscription) {
      return;
    }
    const endpoint = subscription.endpoint;
    await this.swPush.unsubscribe();
    await firstValueFrom(this.http.post<void>('/api/v1/me/push/unsubscribe', { endpoint }));
  }

  whatsAppStatus(): Promise<WhatsAppStatus> {
    return firstValueFrom(this.http.get<WhatsAppStatus>('/api/v1/me/whatsapp'));
  }

  beginWhatsAppLink(phone: string): Promise<void> {
    return firstValueFrom(this.http.post<void>('/api/v1/me/whatsapp/link', { phone }));
  }

  confirmWhatsAppLink(code: string): Promise<void> {
    return firstValueFrom(this.http.post<void>('/api/v1/me/whatsapp/verify', { code }));
  }

  unlinkWhatsApp(): Promise<void> {
    return firstValueFrom(this.http.delete<void>('/api/v1/me/whatsapp'));
  }
}
