import { Page, expect } from '@playwright/test';
import { Account } from './api';
import { freshCode } from './totp';

/** Signs in through the real forms: e-mail and password, then the Google Authenticator code. */
export async function signIn(page: Page, account: Account): Promise<void> {
  const signedIn = (url: URL) => !/\/(verification|connexion)/.test(url.pathname);
  await page.goto('/connexion');
  await page.getByLabel('Adresse e-mail').fill(account.email);
  await page.getByLabel('Mot de passe').fill(account.password);
  await page.getByRole('button', { name: 'Se connecter' }).click();

  // A plain client is signed in straight away; a partner/admin (or a client who turned 2FA on) must enter a code.
  const codeField = page.locator('#twofa-code');
  await Promise.race([
    codeField.waitFor({ state: 'visible' }).catch(() => {}),
    page.waitForURL(signedIn).catch(() => {}),
  ]);
  if (await codeField.isVisible().catch(() => false)) {
    if (await page.getByRole('heading', { name: 'Activer Google Authenticator' }).isVisible().catch(() => false)) {
      // First sign-in of an account that must have a second factor (partner, staff, admin) but has none yet: enrol one, as a person would.
      const secret = ((await page.locator('.secret code').textContent()) ?? '').replace(/\s/g, '');
      Object.assign(account, { secret });
      await codeField.fill(await freshCode(secret));
      await page.getByRole('button', { name: 'Activer et me connecter' }).click();
      await page.getByLabel(/J'ai conservé ces codes/).check();
      await page.getByRole('button', { name: 'Continuer' }).click();
    } else {
      await codeField.fill(await freshCode(account.secret));
      await page.getByRole('button', { name: 'Valider' }).click();
    }
  }
  await page.waitForURL(signedIn);
}

/** The n-th available day (default the first) and its first free slot in the booking calendar, selected. */
export async function pickFirstSlot(page: Page, dayIndex = 0): Promise<void> {
  const day = page.locator('button.cell.available').nth(dayIndex);
  await expect(day).toBeVisible();
  await day.click();
  const slot = page.locator('.slots button.slot:not([disabled])').first();
  await expect(slot).toBeVisible();
  await slot.click();
}

/** The page has no horizontal scrollbar: nothing is wider than the screen. */
export async function expectNoHorizontalScroll(page: Page): Promise<void> {
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
  expect(overflow, 'horizontal overflow in px').toBeLessThanOrEqual(1);
}
