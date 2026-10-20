import { randomBytes } from 'node:crypto';
import { defineConfig, devices } from '@playwright/test';

// A password for the throw-away accounts, random for each run (no password is written in the repository). The workers inherit it.
process.env.E2E_PASSWORD ??= `Zx-${randomBytes(8).toString('hex')}7`;

/**
 * End-to-end suite for Keke Beauty, run against the docker compose stack (`docker compose up -d` at the repository root).
 *
 * - `E2E_BASE_URL` (default http://localhost:4200) is the web container; its nginx proxies /api to the API.
 * - `E2E_BROWSER_CHANNEL` (default `msedge`) uses the Edge already installed on Windows; set it to `chrome`, or to an empty
 *   string after `npx playwright install chromium`, to use another browser.
 * - The tests share one database and create their own accounts, so they run one at a time.
 */
const channel = process.env.E2E_BROWSER_CHANNEL ?? 'msedge';
const browser = channel ? { channel } : {};

export default defineConfig({
  testDir: './tests',
  fullyParallel: false,
  workers: 1,
  retries: 0,
  timeout: 120_000,
  expect: { timeout: 15_000 },
  reporter: [['list'], ['html', { open: 'never' }]],
  use: {
    baseURL: process.env.E2E_BASE_URL ?? 'http://localhost:4200',
    locale: 'fr-FR',
    timezoneId: 'Africa/Abidjan',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    ...browser,
  },
  projects: [
    {
      name: 'desktop',
      use: { ...devices['Desktop Edge'], ...browser, viewport: { width: 1280, height: 800 } },
    },
    {
      // A phone: touch, 412 px wide, mobile user agent. The journeys a client does on a phone are replayed here.
      name: 'mobile',
      use: { ...devices['Pixel 7'], ...browser },
      testMatch: /(public|a11y|pwa|booking-payment)\.spec\.ts/,
    },
  ],
});
