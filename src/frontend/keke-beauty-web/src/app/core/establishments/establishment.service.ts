import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import type { components } from '../api/api.generated';

export type EstablishmentSummary = components['schemas']['EstablishmentSummary'];
export type EstablishmentDetails = components['schemas']['EstablishmentDetails'];
export type EstablishmentPage = components['schemas']['EstablishmentPage'];
export type MediaItem = components['schemas']['MediaItem'];
export type ServiceGroupItem = components['schemas']['ServiceGroupItem'];
export type ServiceItem = components['schemas']['ServiceItem'];
export type OpeningHourItem = components['schemas']['OpeningHourItem'];

export interface SearchCriteria {
  readonly category?: string;
  readonly cityId?: string;
  readonly communeId?: string;
  readonly lat?: number;
  readonly lng?: number;
  readonly radiusKm?: number;
  readonly q?: string;
  readonly bookable?: boolean;
  readonly sort?: string;
  readonly page?: number;
  readonly pageSize?: number;
}

export type EstablishmentEvent = 'view' | 'call' | 'directions';

@Injectable({ providedIn: 'root' })
export class EstablishmentService {
  private readonly http = inject(HttpClient);

  search(criteria: SearchCriteria): Promise<EstablishmentPage> {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(criteria)) {
      if (value !== undefined && value !== '' && value !== false) {
        params = params.set(key, String(value));
      }
    }

    return firstValueFrom(this.http.get<EstablishmentPage>('/api/v1/establishments', { params }));
  }

  /** Statistics are best effort: a failure here must never disturb the visitor. */
  recordEvent(establishmentId: string, type: EstablishmentEvent): void {
    this.http.post<void>(`/api/v1/establishments/${establishmentId}/events`, { type }).subscribe({
      error: () => undefined,
    });
  }
}
