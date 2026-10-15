import { DEFAULT_YANGO_URL_TEMPLATE } from '../directions/directions.service';
import { DEFAULT_MAP_CONFIG, MapConfig, mapConfigFrom } from './map-config';

/**
 * Settings that change per environment without rebuilding the bundle. They come from `/config.json`,
 * which the web container writes at startup from environment variables (see docker/40-runtime-config.sh).
 */
export interface RuntimeConfig {
  /** Yango link template with {lat}/{lng} placeholders; the official one by default, `off` hides the option. */
  readonly yangoUrlTemplate: string;
  /** Map tiles (https template + attribution text): OpenStreetMap by default, a commercial provider in production. */
  readonly map: MapConfig;
}

const DEFAULTS: RuntimeConfig = {
  yangoUrlTemplate: DEFAULT_YANGO_URL_TEMPLATE,
  map: DEFAULT_MAP_CONFIG,
};
const YANGO_OFF = 'off';

export async function loadRuntimeConfig(): Promise<RuntimeConfig> {
  try {
    // No explicit cache mode: the server already forbids caching this file, and a default fetch can reuse the early <link rel="preload">.
    const response = await fetch('/config.json');
    if (!response.ok) {
      return DEFAULTS;
    }

    const loaded = (await response.json()) as {
      yangoUrlTemplate?: unknown;
      mapTilesUrl?: unknown;
      mapAttribution?: unknown;
    };
    const map = mapConfigFrom(loaded.mapTilesUrl, loaded.mapAttribution);
    const template =
      typeof loaded.yangoUrlTemplate === 'string' ? loaded.yangoUrlTemplate.trim() : '';
    if (template.toLowerCase() === YANGO_OFF) {
      return { yangoUrlTemplate: '', map };
    }

    return { yangoUrlTemplate: template || DEFAULT_YANGO_URL_TEMPLATE, map };
  } catch {
    return DEFAULTS; // Missing or malformed config must never prevent the app from starting.
  }
}
