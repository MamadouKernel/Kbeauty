import { APIRequestContext, expect, test } from '@playwright/test';
import { Account, Salon, createAccount, createSalon } from '../helpers/api';
import { sql } from '../helpers/env';
import { pickFirstSlot, signIn } from '../helpers/ui';

const PHONE = '07 00 00 12 34';

let request: APIRequestContext;
let admin: Account;
let salon: Salon;

test.beforeAll(async ({ playwright, baseURL }) => {
  request = await playwright.request.newContext({ baseURL });
  admin = await createAccount(request, 'Client', 'Admin');
  salon = await createSalon(request, admin, { autoConfirm: true });
});

test.afterAll(async () => {
  await request.dispose();
});

test.describe('Favorites and reviews', () => {
  test('a client saves a salon from its page, finds it in "Mes favoris" and removes it', async ({ page }) => {
    const client = await createAccount(request, 'Client');
    await signIn(page, client);

    await page.goto('/etablissements/maison-eclat-coiffure');
    const heart = page.getByRole('button', { name: /Ajouter aux favoris : Maison Éclat Coiffure/ });
    await heart.click();
    await expect(
      page.getByRole('button', { name: /Retirer des favoris : Maison Éclat Coiffure/ }),
    ).toHaveAttribute('aria-pressed', 'true');

    await page.goto('/compte/favoris');
    await expect(page.getByRole('link', { name: /Maison Éclat Coiffure/ }).first()).toBeVisible();

    await page.getByRole('button', { name: /Retirer des favoris : Maison Éclat Coiffure/ }).click();
    await expect(page.getByRole('heading', { name: 'Aucun favori pour le moment' })).toBeVisible();
  });

  test('a visitor who is not signed in is sent to sign in, and a client reviews a completed visit that the owner answers', async ({
    page,
    browser,
    baseURL,
  }) => {
    // The heart asks a visitor to sign in first.
    const visitor = await (await browser.newContext({ baseURL })).newPage();
    await visitor.goto(`/etablissements/${salon.slug}`);
    await visitor.getByRole('button', { name: /Ajouter aux favoris/ }).click();
    await expect(visitor).toHaveURL(/\/connexion\?retour=/);

    // A client books, the visit takes place (the appointment is completed), and the client gives an opinion.
    const client = await createAccount(request, 'Client');
    await signIn(page, client);
    await page.goto(`/etablissements/${salon.slug}/reserver`);
    await page.getByRole('checkbox', { name: /Brushing/ }).check();
    await pickFirstSlot(page, 1);
    await page.getByLabel(/Votre téléphone/).fill(PHONE);
    await page.getByRole('button', { name: 'Envoyer ma demande' }).click();
    await expect(page.getByRole('heading', { name: 'Rendez-vous confirmé' })).toBeVisible();
    sql(
      `UPDATE scheduling.appointments SET status = 'Completed', starts_at = now() - interval '2 days', ` +
        `ends_at = now() - interval '2 days' + interval '30 minutes' WHERE client_user_id = '${client.userId}'`,
    );

    await page.goto('/compte/rendez-vous');
    await page.getByRole('button', { name: 'Donner mon avis' }).click();
    await page.getByRole('radio', { name: /4 sur 5/ }).check();
    await page.getByPlaceholder(/Racontez votre visite/).fill('Accueil chaleureux, coiffure impeccable.');
    await page.getByRole('button', { name: 'Publier mon avis' }).click();
    await expect(page.getByText('Votre avis :')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Donner mon avis' })).toHaveCount(0);

    // Everybody sees it on the salon page, with the rating and the author's first name and initial.
    await visitor.goto(`/etablissements/${salon.slug}?onglet=avis`);
    await expect(visitor.getByText('Accueil chaleureux, coiffure impeccable.')).toBeVisible();
    await expect(visitor.getByText('Awa K.')).toBeVisible();
    await expect(visitor.getByRole('tab', { name: /Avis \(1\)/ })).toBeVisible();

    // The owner answers, publicly.
    const owner = await (await browser.newContext({ baseURL })).newPage();
    await signIn(owner, salon.owner);
    await owner.goto(`/pro/etablissements/${salon.id}/avis`);
    await owner.getByPlaceholder(/Remerciez/).fill('Merci beaucoup, à très bientôt !');
    await owner.getByRole('button', { name: 'Publier ma réponse' }).click();
    await expect(owner.getByText('Votre réponse :')).toBeVisible();

    await visitor.reload();
    await expect(visitor.getByText('Merci beaucoup, à très bientôt !')).toBeVisible();
    await expect(visitor.getByText("Réponse de l'établissement")).toBeVisible();
  });
});
