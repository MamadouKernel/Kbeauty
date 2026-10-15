import { Page, expect } from '@playwright/test';
import { Account } from './api';
import { freshCode } from './totp';

/** Signs in through the real forms: e-mail and password, then the Google Authenticator code. */
export async function signIn(page: Page, account: Account): Promise<void> {
  await page.goto('/connexion');
  await page.getByLabel('Adresse e-mail').fill(account.email);
  await page.getByLabel('Mot de passe').fill(account.password);
  await page.getByRole('button', { name: 'Se connecter' }).click();
  await page.waitForURL(/\/verification/);
  await page.locator('#twofa-code').fill(await freshCode(account.secret));
  await page.getByRole('button', { name: 'Valider' }).click();
  await page.waitForURL((url) => !/\/(verification|connexion)/.test(url.pathname));
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
