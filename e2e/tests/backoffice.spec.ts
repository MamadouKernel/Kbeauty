import { APIRequestContext, expect, test } from '@playwright/test';
import { Account, createAccount } from '../helpers/api';
import { signIn } from '../helpers/ui';
import { readFileSync } from 'node:fs';

let request: APIRequestContext;
let superAdmin: Account;

/** Each sign-in needs a new one-time code and the API accepts about three per account per 90 s: tests get their own admin. */
const newAdmin = (): Promise<Account> => createAccount(request, 'Client', 'Admin');

test.beforeAll(async ({ playwright, baseURL }) => {
  request = await playwright.request.newContext({ baseURL });
  superAdmin = await createAccount(request, 'Client', 'SuperAdmin');
});

test.afterAll(async () => {
  await request.dispose();
});

test.describe('Back-office', () => {
  test('the administrator sets the inactivity delay and the identity-document retention', async ({ page }) => {
    const admin = await newAdmin();
    await signIn(page, admin);
    await page.goto('/admin/parametres');
    await page.getByRole('button', { name: 'Général' }).click();

    const idle = page.getByLabel('Déconnexion après inactivité (minutes)');
    const retention = page.getByLabel(/Conservation des pièces d'identité/);
    await expect(idle).toHaveValue('60');
    await idle.fill('45');
    await retention.fill('200');
    await page.getByRole('button', { name: 'Enregistrer' }).click();
    await expect(page.getByText('Paramètres enregistrés.')).toBeVisible();

    const saved = (await (await request.get('/api/v1/admin/settings', { headers: admin.auth })).json()) as {
      sessionIdleMinutes: number;
      kycRetentionDays: number;
    };
    expect(saved).toMatchObject({ sessionIdleMinutes: 45, kycRetentionDays: 200 });

    // Out-of-range values are refused with an explanation.
    await idle.fill('2');
    await page.getByRole('button', { name: 'Enregistrer' }).click();
    await expect(page.getByRole('alert')).toContainText(/comprise entre 5 et 1440/);

    // Put the defaults back for the other tests.
    await idle.fill('60');
    await retention.fill('365');
    await page.getByRole('button', { name: 'Enregistrer' }).click();
    await expect(page.getByText('Paramètres enregistrés.')).toBeVisible();
  });

  test('the administrator writes and publishes a legal text, visitors read it, a placeholder is refused', async ({
    page,
    browser,
  }) => {
    const admin = await newAdmin();
    await signIn(page, admin);
    await page.goto('/admin/parametres');
    await page.getByRole('button', { name: 'Textes légaux' }).click();
    await page.getByRole('button', { name: /Mentions légales/ }).click();

    const marker = `Éditeur du site : Keke Beauty SAS, test ${Date.now()}`;
    await page
      .getByLabel('Texte', { exact: true })
      .fill(`## Éditeur\n${marker}\n\n- Hébergeur : à renseigner\n- Contact : contact@kekebeauty.ci`);
    await page.getByLabel(/Publié/).check();
    await page.getByRole('button', { name: /Enregistrer \(version/ }).click();
    await expect(page.getByText('Texte enregistré.')).toBeVisible();
    await expect(page.getByRole('table').getByRole('row').nth(1)).toBeVisible();

    const visitor = await browser.newContext();
    const visitorPage = await visitor.newPage();
    await visitorPage.goto('/mentions-legales');
    await expect(visitorPage.getByText(marker)).toBeVisible();
    await expect(visitorPage.getByRole('heading', { name: 'Éditeur' })).toBeVisible();
    await expect(visitorPage.getByRole('listitem').filter({ hasText: 'Contact : contact@kekebeauty.ci' })).toBeVisible();
    await visitor.close();

    // A text that still contains the "[à compléter]" marker cannot be published.
    await page.getByLabel('Texte', { exact: true }).fill('## Éditeur\nRaison sociale : [à compléter]');
    await page.getByRole('button', { name: /Enregistrer \(version/ }).click();
    await expect(page.getByRole('alert')).toContainText(/compléter/);

    // Leave the notice unpublished, as it was.
    await page.getByLabel('Texte', { exact: true }).fill(`## Éditeur\n${marker}`);
    await page.getByLabel(/Publié/).uncheck();
    await page.getByRole('button', { name: /Enregistrer \(version/ }).click();
    await expect(page.getByText('Texte enregistré.')).toBeVisible();
  });

  test('the super administrator takes, downloads and deletes a database backup', async ({ page }) => {
    await signIn(page, superAdmin);
    await page.goto('/admin/parametres');
    await page.getByRole('button', { name: 'Sauvegardes' }).click();

    await page.getByRole('button', { name: 'Créer une sauvegarde maintenant' }).click();
    await expect(page.getByText('Sauvegarde terminée.')).toBeVisible({ timeout: 60_000 });
    const row = page.getByRole('row').filter({ hasText: 'Réussie' }).first();
    await expect(row).toBeVisible();
    await expect(row).toContainText(/\d+(\.\d)? (Ko|Mo)/);

    const downloading = page.waitForEvent('download');
    await row.getByRole('button', { name: 'Télécharger' }).click();
    const download = await downloading;
    expect(download.suggestedFilename()).toMatch(/^keke-beauty-\d{8}-\d{6}\.dump$/);
    const bytes = readFileSync(await download.path());
    expect(bytes.subarray(0, 5).toString('ascii')).toBe('PGDMP'); // the header of a real pg_dump custom archive
    expect(bytes.length).toBeGreaterThan(10_000);

    const succeeded = page.getByRole('row').filter({ hasText: 'Réussie' });
    const before = await succeeded.count();
    page.once('dialog', (dialog) => dialog.accept());
    await row.getByRole('button', { name: 'Supprimer' }).click();
    await expect(page.getByText('Sauvegarde supprimée.')).toBeVisible();
    await expect(succeeded).toHaveCount(before - 1);
  });

  test('the administrator creates a plan with its own conditions and professionals see it on the pricing page', async ({
    page,
    browser,
  }) => {
    const admin = await newAdmin();
    await signIn(page, admin);
    await page.goto('/admin/parametres');
    await page.getByRole('button', { name: 'Formules et tarifs' }).click();
    await page.getByRole('button', { name: 'Nouvelle formule' }).click();

    const name = `Pack Test ${Date.now().toString(36)}`;
    const card = page.locator('form.admin-card').last();
    await card.getByLabel('Nom', { exact: true }).fill(name);
    await card.getByLabel(/Étiquette/).fill('Offre test');
    await card.getByLabel('Description courte').fill('Pour tester les formules');
    await card.getByLabel(/Prix mensuel/).fill('7500');
    await card.getByLabel(/Prix annuel/).fill('75000');
    await card.getByLabel('Engagement (mois)').fill('3');
    await card.getByLabel('Rendez-vous par mois').fill('20');
    await card.getByLabel('Employés / ressources').fill('2');
    await expect(card.getByText("Jusqu'à 20 rendez-vous par mois")).toBeVisible(); // live preview
    await card.getByRole('button', { name: 'Créer la formule' }).click();
    await expect(page.getByText('Formule créée.')).toBeVisible();

    const visitor = await browser.newContext();
    const pricing = await visitor.newPage();
    await pricing.goto('/tarifs');
    const published = pricing.locator('article.plan').filter({ hasText: name });
    await expect(published).toContainText('Offre test');
    await expect(published).toContainText('Pour tester les formules');
    await expect(published).toContainText("Jusqu'à 20 rendez-vous par mois");
    await expect(published).toContainText('2 employé(s) ou ressource(s)');
    await expect(published).toContainText('Engagement de 3 mois');
    await visitor.close();

    // Withdraw it from sale so that it does not stay on the pricing page (its code is the slug of its name).
    const reloaded = page.locator('form.admin-card', { hasText: name.toLowerCase().replace(/ /g, '-') });
    await expect(reloaded).toBeVisible();
    await reloaded.getByLabel('Proposée à la vente').uncheck();
    await reloaded.getByRole('button', { name: 'Enregistrer' }).click();
    await expect(page.getByText(/Formule enregistrée/)).toBeVisible();
  });

  test('a plain administrator does not get the backups', async ({ page }) => {
    const admin = await newAdmin();
    await signIn(page, admin);
    await page.goto('/admin/parametres');
    await expect(page.getByRole('button', { name: 'Textes légaux' })).toBeVisible();
    await expect(page.getByRole('button', { name: 'Sauvegardes' })).toHaveCount(0);
    expect((await request.get('/api/v1/admin/backups', { headers: admin.auth })).status()).toBe(403);
    expect((await request.post('/api/v1/admin/backups', { headers: admin.auth })).status()).toBe(403);
  });

  test('the channels page shows the state of the notification providers', async ({ page }) => {
    const admin = await newAdmin();
    await signIn(page, admin);
    await page.goto('/admin/parametres');
    await page.getByRole('button', { name: 'Canaux' }).click();
    await expect(page.getByRole('heading', { name: 'État des canaux' })).toBeVisible();
    for (const channel of ['SMS', 'WhatsApp', 'Notifications push', 'E-mail']) {
      await expect(page.locator('.state').filter({ hasText: channel }).first()).toBeVisible();
    }
    // LeTexto is configured but switched off: nothing leaves the platform.
    await expect(page.locator('.state').filter({ hasText: 'SMS' }).first()).toContainText(/Désactivé|non prêt/);
  });
});
