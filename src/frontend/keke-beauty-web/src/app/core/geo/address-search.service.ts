import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export interface GeoPlace {
  readonly label: string;
  readonly latitude: number;
  readonly longitude: number;
}

/** Address search through our own API (which asks OpenStreetMap): the browser never contacts a third party. */
@Injectable({ providedIn: 'root' })
export class AddressSearchService {
  private readonly http = inject(HttpClient);

  search(query: string): Promise<GeoPlace[]> {
    return firstValueFrom(
      this.http.get<GeoPlace[]>('/api/v1/geo/search', { params: new HttpParams().set('q', query) }),
    );
  }

  /** The address at a point of the map (first result, or `null` when OpenStreetMap knows nothing there). */
  async reverse(latitude: number, longitude: number): Promise<GeoPlace | null> {
    const places = await firstValueFrom(
      this.http.get<GeoPlace[]>('/api/v1/geo/reverse', {
        params: new HttpParams().set('lat', latitude).set('lon', longitude),
      }),
    );
    return places[0] ?? null;
  }
}
