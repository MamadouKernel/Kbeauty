import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export type ApiStatus = 'checking' | 'online' | 'offline';

/** Tracks API availability through `/health/ready` (readiness = API + database reachable). */
@Injectable({ providedIn: 'root' })
export class HealthService {
  private readonly http = inject(HttpClient);
  private readonly state = signal<ApiStatus>('checking');

  readonly status = this.state.asReadonly();

  async check(): Promise<void> {
    this.state.set('checking');
    try {
      await firstValueFrom(this.http.get('/health/ready', { responseType: 'text' }));
      this.state.set('online');
    } catch {
      this.state.set('offline');
    }
  }
}
