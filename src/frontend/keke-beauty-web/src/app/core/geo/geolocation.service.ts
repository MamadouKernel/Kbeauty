import { Injectable } from '@angular/core';

export interface Position {
  readonly lat: number;
  readonly lng: number;
}

export type GeolocationFailureReason = 'unsupported' | 'denied' | 'unavailable' | 'timeout';

export class GeolocationFailure extends Error {
  constructor(readonly reason: GeolocationFailureReason) {
    super(reason);
  }

  /** User-facing explanation (French). */
  get userMessage(): string {
    switch (this.reason) {
      case 'denied':
        return "L'accès à votre position est refusé. Autorisez-le dans votre navigateur ou choisissez une commune.";
      case 'timeout':
        return 'Votre position met trop de temps à répondre. Réessayez ou choisissez une commune.';
      case 'unsupported':
        return 'Votre navigateur ne permet pas la géolocalisation. Choisissez une commune.';
      default:
        return "Votre position n'est pas disponible pour le moment. Choisissez une commune.";
    }
  }
}

/** Thin promise wrapper over the browser Geolocation API. Coordinates are rounded (privacy + stable URLs). */
@Injectable({ providedIn: 'root' })
export class GeolocationService {
  locate(): Promise<Position> {
    if (typeof navigator === 'undefined' || !('geolocation' in navigator)) {
      return Promise.reject(new GeolocationFailure('unsupported'));
    }

    return new Promise((resolve, reject) => {
      navigator.geolocation.getCurrentPosition(
        (position) =>
          resolve({
            lat: Math.round(position.coords.latitude * 10_000) / 10_000,
            lng: Math.round(position.coords.longitude * 10_000) / 10_000,
          }),
        (error) =>
          reject(
            new GeolocationFailure(
              error.code === error.PERMISSION_DENIED
                ? 'denied'
                : error.code === error.TIMEOUT
                  ? 'timeout'
                  : 'unavailable',
            ),
          ),
        { enableHighAccuracy: false, timeout: 10_000, maximumAge: 60_000 },
      );
    });
  }
}
