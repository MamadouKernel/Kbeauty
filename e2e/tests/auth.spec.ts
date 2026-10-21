import { expect, test } from '@playwright/test';
import { createAccount } from '../helpers/api';
import { PASSWORD, uniqueEmail } from '../helpers/env';
import { tokenIn, waitForMail } from '../helpers/mail';
import { freshCode } from '../helpers/totp';
import { signIn } from '../helpers/ui';

test.describe('Accounts', () => {
  test('a partner registers, confirms the e-mail, enrols Google Authenticator and signs in again', async ({
    page,
    browser,
  }) => {
    const email = uniqueEmail('inscrit');

    // A partner: since the adaptive second factor, a plain client signs in without enrolling one; partners and admins must.
    await page.goto('/inscription');
    await page.locator('label.role', { hasText: 'Professionnel' }).click();
    await page.getByLabel('Prénom').fill('Awa');
    await page.getByLabel('Nom', { exact: true }).fill('Koné');
    await page.getByLabel('Adresse e-mail').fill(email);
    await page.getByLabel('Mot de passe', { exact: true }).fill(PASSWORD);
    await page.getByLabel('Confirmer le mot de passe').fill(PASSWORD);
    await page.getByLabel(/J’accepte les/).check();
    await page.getByRole('button', { name: 'Créer mon compte' }).click();
    await expect(page.getByRole('heading', { name: 'Vérifiez votre boîte mail' })).toBeVisible();

    const message = await waitForMail(email, /Confirmez/i);
    await page.goto(`/confirmation-email?token=${encodeURIComponent(tokenIn(message))}`);
    await expect(page.getByRole('heading', { name: 'Adresse confirmée' })).toBeVisible();

    // First sign-in: the account must enrol two-factor authentication before it gets a session.
    await page.getByRole('link', { name: 'Se connecter' }).first().click();
    await page.getByLabel('Adresse e-mail').fill(email);
    await page.getByLabel('Mot de passe', { exact: true }).fill(PASSWORD);
    await page.getByRole('button', { name: 'Se connecter' }).click();
    await expect(page.getByRole('heading', { name: 'Activer Google Authenticator' })).toBeVisible();
    // textContent: the key is checked as written in the page, whatever font the browser can draw it with.
    const secret = ((await page.locator('.secret code').textContent()) ?? '').replace(/\s/g, '');
    expect(secret).toMatch(/^[A-Z2-7]{16,}$/);
    await page.locator('#twofa-code').fill(await freshCode(secret));
    await page.getByRole('button', { name: 'Activer et me connecter' }).click();

    await expect(page.getByRole('heading', { name: 'Conservez vos codes de récupération' })).toBeVisible();
    await expect(page.getByRole('list', { name: 'Codes de récupération' }).getByRole('listitem')).not.toHaveCount(0);
    await page.getByLabel(/J'ai conservé ces codes/).check();
    await page.getByRole('button', { name: 'Continuer' }).click();
    await page.waitForURL((url) => !/verification|connexion/.test(url.pathname));

    // A brand-new browser has no session: it needs the password and a code again.
    const fresh = await browser.newContext({ locale: 'fr-FR' });
    const second = await fresh.newPage();
    await signIn(second, { email, password: PASSWORD, secret, userId: '', auth: { Authorization: '' } });
    await second.goto('/compte');
    await expect(second.getByRole('heading', { name: 'Mes informations' })).toBeVisible();
    await fresh.close();
  });

  test('a wrong password is refused without saying which part was wrong', async ({ page, request }) => {
    const account = await createAccount(request, 'Client');
    await page.goto('/connexion');
    await page.getByLabel('Adresse e-mail').fill(account.email);
    await page.getByLabel('Mot de passe', { exact: true }).fill('Mauvais-Mot-De-Passe-1');
    await page.getByRole('button', { name: 'Se connecter' }).click();
    const known = await page.getByRole('alert').innerText();

    await page.getByLabel('Adresse e-mail').fill(uniqueEmail('inconnu'));
    await page.getByRole('button', { name: 'Se connecter' }).click();
    await expect(page.getByRole('alert')).toHaveText(known);
  });

  test('protected pages send a visitor to the sign-in page', async ({ page }) => {
    for (const path of ['/compte', '/compte/rendez-vous', '/pro', '/admin']) {
      await page.goto(path);
      await expect(page).toHaveURL(/\/connexion/);
    }
  });

  test('the session ends after the inactivity period', async ({ page, request }) => {
    const account = await createAccount(request, 'Client');
    await page.clock.install();
    await signIn(page, account);
    await page.goto('/compte');
    await expect(page.getByRole('heading', { name: 'Mes informations' })).toBeVisible();

    // 61 minutes without touching the page (the default limit is 60).
    await page.clock.fastForward('01:01:00');
    await expect(page).toHaveURL(/\/connexion\?inactif=1/);
    await expect(page.getByText(/déconnecté\(e\) après une période d’inactivité/)).toBeVisible();
    await page.goto('/compte');
    await expect(page).toHaveURL(/\/connexion/);
  });

  test('a client manages the notification channels', async ({ page, request }) => {
    const account = await createAccount(request, 'Client');
    await signIn(page, account);
    await page.goto('/compte');
    const section = page.getByRole('region', { name: 'Mes notifications' });
    await expect(section).toBeVisible();

    const reminders = section.getByLabel('Rappels avant mes rendez-vous');
    await expect(reminders).toBeChecked();
    await reminders.uncheck();
    await expect(section.getByText('Préférences enregistrées.')).toBeVisible();
    await page.reload();
    await expect(
      page.getByRole('region', { name: 'Mes notifications' }).getByLabel('Rappels avant mes rendez-vous'),
    ).not.toBeChecked();
  });
});
