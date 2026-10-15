import { Injectable, inject } from '@angular/core';
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr';
import { AuthService } from '../auth/auth.service';

export type HubHandler = (...args: unknown[]) => void;

export interface RealtimeOptions {
  /** Event name to handler. */
  readonly handlers: Readonly<Record<string, HubHandler>>;
  /** Hub method called after every (re)connection, e.g. to join an establishment group. */
  readonly join?: { readonly method: string; readonly args: readonly unknown[] };
  /** Send the access token (the hub requires a signed-in user). */
  readonly authenticated?: boolean;
}

export interface RealtimeConnection {
  readonly connected: () => boolean;
  stop(): Promise<void>;
}

/**
 * SignalR connections (PRD §9 hubs). Live updates are an enhancement: when a connection cannot be opened the page
 * keeps working from the data it already loaded, so failures are reported as a "not connected" handle, never thrown.
 */
@Injectable({ providedIn: 'root' })
export class RealtimeService {
  private readonly auth = inject(AuthService);

  async connect(hub: string, options: RealtimeOptions): Promise<RealtimeConnection> {
    const connection = new HubConnectionBuilder()
      .withUrl(hub, {
        accessTokenFactory: options.authenticated ? () => this.auth.accessToken() ?? '' : undefined,
      })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.None)
      .build();

    for (const [event, handler] of Object.entries(options.handlers)) {
      connection.on(event, handler);
    }

    const join = async (): Promise<void> => {
      if (options.join) {
        await connection.invoke(options.join.method, ...options.join.args);
      }
    };
    // After a network drop the server forgot the groups: join again, and let the page refresh what it missed.
    connection.onreconnected(() => {
      void join()
        .then(() => options.handlers['Reconnected']?.())
        .catch(() => undefined);
    });

    try {
      await connection.start();
      await join();
    } catch {
      await stop(connection);
    }

    return {
      connected: () => connection.state === HubConnectionState.Connected,
      stop: () => stop(connection),
    };
  }
}

async function stop(connection: HubConnection): Promise<void> {
  try {
    await connection.stop();
  } catch {
    // Already closed.
  }
}
