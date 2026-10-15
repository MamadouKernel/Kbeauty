import { expect, test } from '@playwright/test';
import { expectNoHorizontalScroll } from '../helpers/ui';

interface Manifest {
  name: string;
  short_name: string;
  display: string;
  start_url: string;
  theme_color: string;
  background_color: string;
  icons: { src: string; sizes: string; type: string; purpose?: string }[];
}

test.describe('Progressive web app', () => {
  test('the manifest makes the site installable', async ({ request }) => {
    const response = await request.get('/manifest.webmanifest');
    expect(response.ok()).toBeTruthy();
    const manifest = (await response.json()) as Manifest;
    expect(manifest.name).toMatch(/Keke Beauty/);
    expect(manifest.short_name).toBeTruthy();
    expect(['standalone', 'minimal-ui', 'fullscreen']).toContain(manifest.display);
    expect(manifest.start_url).toBeTruthy();
    expect(manifest.theme_color).toMatch(/^#/);
    const sizes = manifest.icons.map((icon) => icon.sizes);
    expect(sizes).toEqual(expect.arrayContaining(['192x192', '512x512']));
    for (const icon of manifest.icons) {
      expect((await request.get(icon.src)).ok(), icon.src).toBeTruthy();
    }
  });

  test('the page declares a mobile viewport and a theme color', async ({ page }) => {
    await page.goto('/');
    await expect(page.locator('meta[name="viewport"]')).toHaveAttribute('content', /width=device-width/);
    await expect(page.locator('meta[name="theme-color"]').first()).toHaveAttribute('content', /#/);
    await expect(page.locator('link[rel="manifest"]')).toHaveCount(1);
  });

  test('the service worker takes control and the app shell opens offline', async ({ page, context }) => {
    await page.goto('/');
    // The worker registers once the app is stable (up to 30 s after load).
    await page.evaluate(async () => {
      await navigator.serviceWorker.ready;
    });
    await page.reload();
    await expect
      .poll(() => page.evaluate(() => navigator.serviceWorker.controller !== null), { timeout: 30_000 })
      .toBeTruthy();

    await context.setOffline(true);
    try {
      await page.reload();
      await expect(page).toHaveTitle(/Keke Beauty/);
      await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
    } finally {
      await context.setOffline(false);
    }
  });

  test('key pages fit the screen', async ({ page }) => {
    for (const path of ['/', '/etablissements', '/etablissements/maison-eclat-coiffure', '/connexion', '/tarifs']) {
      await page.goto(path);
      await page.waitForLoadState('networkidle');
      await expectNoHorizontalScroll(page);
    }
  });
});

test.describe('Phone layout', () => {
  test.skip(({ isMobile }) => !isMobile, 'phone project only');

  test('the bottom navigation bar is shown and the touch targets are large enough', async ({ page }) => {
    await page.goto('/');
    const bar = page.getByRole('navigation').last();
    await expect(bar).toBeVisible();
    const links = page.locator('a.btn, button.btn');
    const count = Math.min(await links.count(), 8);
    for (let i = 0; i < count; i++) {
      const box = await links.nth(i).boundingBox();
      if (box && box.width > 0) {
        expect(box.height, `button #${i} height`).toBeGreaterThanOrEqual(36);
      }
    }
  });
});
