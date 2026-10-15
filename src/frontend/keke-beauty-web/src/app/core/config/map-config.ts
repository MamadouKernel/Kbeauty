import { InjectionToken } from '@angular/core';

export interface MapConfig {
  /** Leaflet tile URL template (`{z}/{x}/{y}`, optional `{s}` and `{r}`). */
  readonly tilesUrl: string;
  /** Attribution as trusted HTML (Leaflet renders it as markup); text coming from configuration is escaped first. */
  readonly attributionHtml: string;
}

// OpenStreetMap's standard tiles. The deployed site serves them through its own cached proxy (/tiles/, see nginx.conf); this
// direct address is only the fallback (development server). CARTO's free tiles now require an API key, so they are not used.
export const DEFAULT_MAP_TILES_URL = 'https://tile.openstreetmap.org/{z}/{x}/{y}.png';
const OSM_ATTRIBUTION =
  '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors';

export const DEFAULT_MAP_CONFIG: MapConfig = {
  tilesUrl: DEFAULT_MAP_TILES_URL,
  attributionHtml: OSM_ATTRIBUTION,
};

export const MAP_CONFIG = new InjectionToken<MapConfig>('MAP_CONFIG', {
  providedIn: 'root',
  factory: () => DEFAULT_MAP_CONFIG,
});

/**
 * Builds the map settings from the runtime configuration. Only https tile servers are accepted and the attribution
 * text is escaped, so a mistyped or hostile value can neither downgrade the transport nor inject markup.
 */
export function mapConfigFrom(tilesUrl: unknown, attribution: unknown): MapConfig {
  const url = typeof tilesUrl === 'string' ? tilesUrl.trim() : '';
  // An https tile server, or the site's own tile proxy (same origin, so it cannot downgrade the transport either).
  if (!/^(?:https:\/\/|\/tiles\/)[^\s]*\{z\}[^\s]*\{x\}[^\s]*\{y\}[^\s]*$/.test(url)) {
    return DEFAULT_MAP_CONFIG;
  }

  const text = typeof attribution === 'string' ? attribution.trim() : '';
  return { tilesUrl: url, attributionHtml: text ? escapeHtml(text) : OSM_ATTRIBUTION };
}

function escapeHtml(value: string): string {
  return value
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;');
}
