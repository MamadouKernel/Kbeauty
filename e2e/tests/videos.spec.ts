import { APIRequestContext, expect, test } from '@playwright/test';
import { Account, Salon, createAccount, createSalon } from '../helpers/api';
import { mp4 } from '../helpers/files';
import { signIn } from '../helpers/ui';

let request: APIRequestContext;
let admin: Account;
let premium: Salon;
let free: Salon;

test.beforeAll(async ({ playwright, baseURL }) => {
  request = await playwright.request.newContext({ baseURL });
  admin = await createAccount(request, 'Client', 'Admin');
  premium = await createSalon(request, admin);
  free = await createSalon(request, admin, { subscribe: false });
});

test.afterAll(async () => {
  await request.dispose();
});

test.describe('Videos on the establishment page', () => {
  test('a Premium partner imports a video; visitors see it after the photos; the partner removes it', async ({
    page,
    browser,
  }) => {
    await signIn(page, premium.owner);
    await page.goto(`/pro/etablissements/${premium.id}/galerie`);
    await expect(page.getByRole('heading', { name: /Vidéos \(0 \/ 3\)/ })).toBeVisible();

    await page.locator('input[type="file"][accept*="video"]').setInputFiles({
      name: 'salon.mp4',
      mimeType: 'video/mp4',
      buffer: mp4(),
    });
    await expect(page.getByRole('heading', { name: /Vidéos \(1 \/ 3\)/ })).toBeVisible();
    await expect(page.locator('video').first()).toBeVisible();

    // The public page lists the photo first, then the video.
    const visitor = await browser.newContext();
    const publicPage = await visitor.newPage();
    await publicPage.goto(`/etablissements/${premium.slug}?onglet=realisations`); // the gallery tab
    const slides = publicPage.locator('figure.slide');
    await expect(slides).toHaveCount(2);
    await expect(slides.nth(0).locator('img')).toBeVisible();
    await expect(slides.nth(1).locator('video')).toBeAttached();
    await visitor.close();

    await page.getByRole('button', { name: 'Supprimer la vidéo 1' }).click();
    await expect(page.getByRole('heading', { name: /Vidéos \(0 \/ 3\)/ })).toBeVisible();
  });

  test('a file that is not a video is refused', async ({ page }) => {
    await signIn(page, premium.owner);
    await page.goto(`/pro/etablissements/${premium.id}/galerie`);
    await page.locator('input[type="file"][accept*="video"]').setInputFiles({
      name: 'faux.mp4',
      mimeType: 'video/mp4',
      buffer: Buffer.from('<html><script>alert(1)</script></html>'),
    });
    await expect(page.getByRole('alert')).toContainText(/Format de vidéo non pris en charge/);
    await expect(page.getByRole('heading', { name: /Vidéos \(0 \/ 3\)/ })).toBeVisible();
  });

  test('a partner on the free plan is told that videos need Premium', async ({ page }) => {
    await signIn(page, free.owner);
    await page.goto(`/pro/etablissements/${free.id}/galerie`);
    await expect(page.getByText(/réservées à la formule Premium/)).toBeVisible();
    await expect(page.locator('input[type="file"][accept*="video"]')).toHaveCount(0);
  });
});
