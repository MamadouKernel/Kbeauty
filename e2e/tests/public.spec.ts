import { expect, test } from '@playwright/test';
import { expectNoHorizontalScroll } from '../helpers/ui';

test.describe('Public pages', () => {
  test('the home page presents the directory and has no horizontal scroll', async ({ page }) => {
    await page.goto('/');
    await expect(page).toHaveTitle(/Keke Beauty/);
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
    await expectNoHorizontalScroll(page);
  });

  test('the directory lists the establishments and opens a page with directions and booking', async ({ page }) => {
    await page.goto('/etablissements');
    const cards = page.getByRole('link', { name: /Maison Éclat Coiffure/ });
    await expect(cards.first()).toBeVisible();
    await expectNoHorizontalScroll(page);

    await page.goto('/etablissements/maison-eclat-coiffure');
    await expect(page.getByRole('heading', { level: 1 })).toHaveText(/Maison Éclat Coiffure/);
    await expect(page.getByRole('link', { name: /Réserver/ }).first()).toBeVisible();
    await expectNoHorizontalScroll(page);

    // "S'y rendre avec Yango" opens Yango with the shop as destination (coordinates in the link).
    const yango = page.getByRole('link', { name: /S'y rendre avec Yango/ }).first();
    await expect(yango).toBeVisible();
    const href = (await yango.getAttribute('href')) ?? '';
    expect(href).toMatch(/yango/i);
    expect(href).toMatch(/5\.3\d+/);
    expect(href).toMatch(/-3\.9\d+/);
    expect(await yango.getAttribute('target')).toBe('_blank');
    expect(await yango.getAttribute('rel')).toContain('noopener');
  });

  test('the map shows its background tiles (needs internet access)', async ({ page }) => {
    // Tiles come through the site's own cached proxy (same origin), which fetches them from the provider.
    const tile = page.waitForResponse((r) => /\/tiles\/\d+\/\d+\/\d+\.png$/.test(r.url()) && r.ok());
    await page.goto('/etablissements?vue=carte');
    await tile;
    await expect(page.locator('.leaflet-tile-loaded').first()).toBeVisible();
  });

  test('a pin on the map shows the name, phone, commune, quartier and number of services', async ({ page }) => {
    await page.goto('/etablissements?vue=carte');
    const pin = page.locator('.leaflet-marker-icon[title="Maison Éclat Coiffure"]');
    await expect(pin).toBeVisible();
    // Opened from the keyboard (pins are focusable buttons): other test salons share the same coordinates and cover it.
    await pin.focus();
    await page.keyboard.press('Enter');

    // The map is the main screen: the pin brings its card forward, and the card carries the information.
    const card = page.locator('#tile-maison-eclat-coiffure');
    await expect(card).toHaveAttribute('data-active', 'true');
    await expect(card).toContainText('Maison Éclat Coiffure');
    await expect(card).toContainText('Cocody');
    await expect(card).toContainText(/Rue des Jardins/); // the quartier (address line)
    await expect(card).toContainText(/\d+ prestations?/);
    await expect(card.locator('a[href^="tel:"]')).toHaveAttribute('href', /^tel:\+?\d+$/);
    await expect(card.getByRole('link', { name: /Détails/ })).toBeVisible();
    await expect(card.getByRole('link', { name: /Réserver/ })).toBeVisible();
  });

  test('searching narrows the directory', async ({ page }) => {
    await page.goto('/etablissements?q=Nail');
    await expect(page.getByRole('link', { name: /Nail Bar/ }).first()).toBeVisible();
    await expect(page.getByRole('link', { name: /Maison Éclat Coiffure/ })).toHaveCount(0);
  });

  for (const [path, heading] of [
    ['/cgu', /conditions/i],
    ['/confidentialite', /confidentialité/i],
  ] as const) {
    test(`${path} shows the text published by the administrators`, async ({ page }) => {
      await page.goto(path);
      await expect(page.getByRole('heading', { level: 1 })).toHaveText(heading);
      await expect(page.getByText(/Version \d+/)).toBeVisible();
      await expectNoHorizontalScroll(page);
    });
  }

  test('the legal notice stays hidden until the administrators have filled it in', async ({ page }) => {
    await page.goto('/mentions-legales');
    await expect(page.getByRole('heading', { level: 1 })).toHaveText(/Mentions légales|Document indisponible/);
    await expectNoHorizontalScroll(page);
  });

  test('pricing page is reachable', async ({ page }) => {
    await page.goto('/tarifs');
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
  });

  test('an unknown address goes back to the home page', async ({ page }) => {
    await page.goto('/cette-page-n-existe-pas');
    await expect(page).toHaveURL(/\/$/);
  });

  test('the site is served with its security headers', async ({ request }) => {
    const response = await request.get('/');
    const headers = response.headers();
    expect(headers['content-security-policy']).toContain("default-src 'self'");
    expect(headers['x-content-type-options']).toBe('nosniff');
    expect(headers['x-frame-options'] ?? headers['content-security-policy']).toBeTruthy();
    expect(headers['referrer-policy']).toBeTruthy();
    expect(headers['server']).not.toMatch(/\d/);
  });
});
