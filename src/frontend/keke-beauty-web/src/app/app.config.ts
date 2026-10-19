import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import {
  ApplicationConfig,
  inject,
  provideAppInitializer,
  isDevMode,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { provideServiceWorker } from '@angular/service-worker';
import {
  provideRouter,
  withComponentInputBinding,
  withInMemoryScrolling,
  withViewTransitions,
} from '@angular/router';
import { providePrimeNG } from 'primeng/config';
import { routes } from './app.routes';
import { authInterceptor } from './core/auth/auth.interceptor';
import { AuthService } from './core/auth/auth.service';
import { DisplayPreferences } from './core/display/display-preferences';
import { RuntimeConfig } from './core/config/runtime-config';
import { AppUpdates } from './core/updates/app-updates';
import { MAP_CONFIG } from './core/config/map-config';
import { DIRECTIONS_CONFIG } from './core/directions/directions.service';
import { KekeBeautyPreset } from './theme/keke-beauty.preset';

export function createAppConfig(runtime: RuntimeConfig): ApplicationConfig {
  return {
    providers: [
      provideBrowserGlobalErrorListeners(),
      // Installable app: static files are cached, API calls and the live hubs never are (see ngsw-config.json).
      provideServiceWorker('ngsw-worker.js', {
        enabled: !isDevMode(),
        registrationStrategy: 'registerWhenStable:30000',
      }),
      // A returning visitor moves to a newly released version at once instead of after two reloads.
      provideAppInitializer(() => void inject(AppUpdates)),
      provideRouter(
        routes,
        withComponentInputBinding(),
        withViewTransitions(),
        withInMemoryScrolling({ anchorScrolling: 'enabled', scrollPositionRestoration: 'top' }),
      ),
      provideHttpClient(withFetch(), withInterceptors([authInterceptor])),
      { provide: DIRECTIONS_CONFIG, useValue: { yangoUrlTemplate: runtime.yangoUrlTemplate } },
      { provide: MAP_CONFIG, useValue: runtime.map },
      providePrimeNG({
        theme: {
          preset: KekeBeautyPreset,
          // Dark mode follows the data-theme attribute set by DisplayPreferences (lot 7).
          options: { darkModeSelector: '[data-theme="dark"]' },
        },
      }),
      // Restore a previous session (httpOnly refresh cookie) before the first navigation so guards see it.
      provideAppInitializer(() => inject(DisplayPreferences).init()),
      provideAppInitializer(() => inject(AuthService).restore()),
    ],
  };
}
