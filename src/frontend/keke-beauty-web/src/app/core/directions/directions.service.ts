import { Injectable, InjectionToken, inject } from '@angular/core';

/**
 * Official Yango route link (partner documentation, https://yango.com/en_int/partner-program/documentation/): an
 * Adjust "go.link" that opens the Yango app on a phone where it is installed (destination pre-filled), and falls
 * back to Yango's web order page elsewhere. `ref` is the partner source id, Latin characters only.
 */
export const DEFAULT_YANGO_URL_TEMPLATE =
  'https://yango.go.link/route?end-lat={lat}&end-lon={lng}&ref=kekebeauty&adj_t=vokme8e_nd9s9z9&lang=fr&adj_deeplink_js=1' +
  '&adj_fallback=https%3A%2F%2Fyango.com%2Fen_int%2Forder%2F%3Fgto%3D{lng}%2C{lat}%26ref%3Dkekebeauty';

export interface DirectionsConfig {
  /** URL template with `{lat}` and `{lng}` placeholders for the Yango app (PRD §10.3); empty hides the option. */
  readonly yangoUrlTemplate: string;
}

export const DIRECTIONS_CONFIG = new InjectionToken<DirectionsConfig>('DIRECTIONS_CONFIG', {
  providedIn: 'root',
  factory: () => ({ yangoUrlTemplate: DEFAULT_YANGO_URL_TEMPLATE }),
});

export interface DirectionsOption {
  readonly id: 'google' | 'apple' | 'yango';
  readonly label: string;
  readonly url: string;
}

const FORBIDDEN_SCHEMES = /^\s*(javascript|data|vbscript):/i;

/** Builds the "S'y rendre" links with the destination pre-filled (PRD EST-04). */
@Injectable({ providedIn: 'root' })
export class DirectionsLinkService {
  private readonly config = inject(DIRECTIONS_CONFIG);

  options(lat: number, lng: number, isApplePlatform = detectApplePlatform()): DirectionsOption[] {
    const options: DirectionsOption[] = [
      {
        id: 'google',
        label: 'Google Maps',
        url: `https://www.google.com/maps/dir/?api=1&destination=${lat},${lng}`,
      },
    ];

    if (isApplePlatform) {
      options.push({
        id: 'apple',
        label: 'Plans (Apple)',
        url: `https://maps.apple.com/?daddr=${lat},${lng}`,
      });
    }

    const yango = this.fill(this.config.yangoUrlTemplate, lat, lng);
    if (yango) {
      options.push({ id: 'yango', label: 'Yango', url: yango });
    }

    return options;
  }

  private fill(template: string, lat: number, lng: number): string | null {
    const trimmed = template.trim();
    if (!trimmed || FORBIDDEN_SCHEMES.test(trimmed) || !/^[a-z][a-z0-9+.-]*:/i.test(trimmed)) {
      return null;
    }

    return trimmed
      .replaceAll('{lat}', encodeURIComponent(String(lat)))
      .replaceAll('{lng}', encodeURIComponent(String(lng)));
  }
}

function detectApplePlatform(): boolean {
  if (typeof navigator === 'undefined') {
    return false;
  }

  return /iPhone|iPad|iPod|Macintosh/i.test(navigator.userAgent);
}
