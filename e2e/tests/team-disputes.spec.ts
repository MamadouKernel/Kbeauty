import { APIRequestContext, expect, test } from '@playwright/test';
import { Account, Salon, createAccount, createSalon } from '../helpers/api';
import { sql } from '../helpers/env';
import { pickFirstSlot, signIn } from '../helpers/ui';

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

test.describe('Team portal (lot 3)', () => {
  test('the owner links a collaborator who runs a treatment from her own planning', async ({
    page,
    browser,
    baseURL,
  }) => {
    test.setTimeout(240_000);
    const collaborator = await createAccount(request, 'Client');

    // The owner links her by e-mail and sets her commission.
    await signIn(page, salon.owner);
    await page.goto(`/pro/etablissements/${salon.id}/equipe`);
    await page.getByLabel('E-mail de la collaboratrice').fill(collaborator.email);
    await page.getByLabel('Commission (%)').fill('30');
    await page.getByRole('button', { name: 'Enregistrer' }).click();
    await expect(page.getByText(/Compte lié/)).toBeVisible();

    // A client walks in at the counter.
    await page.goto(`/pro/etablissements/${salon.id}/comptoir`);
    await page.getByRole('checkbox', { name: /Brushing/ }).check();
    await page.getByLabel('Nom de la cliente').fill('Fatou Bamba');
    await page.getByRole('button', { name: 'Enregistrer le rendez-vous' }).click();
    await expect(page.getByRole('heading', { name: "C'est enregistré" })).toBeVisible();

    // The collaborator signs in: she sees only her day and runs the timer.
    const staff = await (await browser.newContext({ baseURL })).newPage();
    await signIn(staff, collaborator);
    await staff.goto('/equipe');
    await expect(staff.getByRole('heading', { name: /Mon planning/ })).toBeVisible();
    await expect(staff.getByText(/Fatou Bamba/)).toBeVisible();

    await staff.getByRole('button', { name: 'Démarrer le soin' }).click();
    await expect(staff.getByRole('timer')).toBeVisible();
    await expect(staff.getByText('En cours')).toBeVisible();

    // A treatment cannot be finished the instant it started.
    await staff.waitForTimeout(62_000);
    await staff.getByRole('button', { name: 'Terminer le soin' }).click();
    await expect(staff.getByText('Terminé').first()).toBeVisible();
    await expect(staff.getByRole('heading', { name: /Mes gains/ })).toBeVisible();
    await expect(staff.getByText('Soins terminés').locator('xpath=following-sibling::dd')).toHaveText('1');

    // The collaborator portal is closed to the salon owner.
    await page.goto('/equipe');
    await expect(page).not.toHaveURL(/\/equipe$/);
  });
});

test.describe('Keke Protect (lot 4)', () => {
  test('a client reports a problem, the salon answers and an administrator decides', async ({
    page,
    browser,
    baseURL,
  }) => {
    test.setTimeout(240_000);
    const client = await createAccount(request, 'Client');

    // The client books online.
    const visitor = await (await browser.newContext({ baseURL })).newPage();
    await signIn(visitor, client);
    await visitor.goto(`/etablissements/${salon.slug}/reserver`);
    await visitor.getByRole('checkbox', { name: /Coupe longue/ }).check();
    await pickFirstSlot(visitor, 2);
    await visitor.getByLabel(/Votre téléphone/).fill('07 00 00 12 34');
    await visitor.getByRole('button', { name: 'Envoyer ma demande' }).click();
    await expect(visitor.getByRole('heading', { name: 'Rendez-vous confirmé' })).toBeVisible();

    // The appointment took place three hours ago.
    const mine = await request.get('/api/v1/me/appointments', { headers: client.auth });
    const [appointment] = (await mine.json()) as { id: string }[];
    sql(
      `UPDATE scheduling.appointments SET status = 'Completed', starts_at = now() - interval '3 hours', ` +
        `ends_at = now() - interval '2 hours' WHERE id = '${appointment.id}'`,
    );

    await visitor.goto('/compte/rendez-vous');
    await visitor.getByRole('button', { name: 'Signaler un problème' }).click();
    await visitor.getByLabel(/Que s.est-il passé/).selectOption('SalonClosed');
    await visitor
      .getByLabel('Expliquez-nous en quelques mots')
      .fill('Le salon était fermé quand je suis arrivée.');
    await visitor.getByRole('button', { name: 'Envoyer' }).click();
    await expect(visitor.getByText(/Litige : .*en cours d’examen/)).toBeVisible();

    // The establishment sees the claim and answers.
    await signIn(page, salon.owner);
    await page.goto(`/pro/etablissements/${salon.id}/litiges`);
    await expect(page.getByText('Le salon était fermé quand je suis arrivée.')).toBeVisible();
    await page.getByLabel('Votre réponse').fill('Nous étions ouverts toute la journée.');
    await page.getByRole('button', { name: 'Répondre' }).click();
    await expect(page.getByText(/Votre réponse :/)).toBeVisible();

    // An administrator rejects the claim.
    const back = await (await browser.newContext({ baseURL })).newPage();
    await signIn(back, admin);
    await back.goto('/admin/litiges');
    await expect(back.getByText('Le salon était fermé quand je suis arrivée.')).toBeVisible();
    await expect(back.getByText('Nous étions ouverts toute la journée.')).toBeVisible();
    await back.getByLabel('Message aux deux parties').first().fill('Le salon prouve son ouverture.');
    await back.getByRole('button', { name: 'Trancher' }).first().click();
    await expect(back.getByText('Litige rejeté.')).toBeVisible();

    // The client sees the decision.
    await visitor.goto('/compte/rendez-vous');
    await expect(visitor.getByText(/Litige : .*Litige rejeté/)).toBeVisible();
  });
});

test.describe('Finances & formulas (lot 5)', () => {
  test('a salon publishes a promoted formula with perks, exports its accounting CSV and the admin views the analytical dashboard', async ({
    page,
    browser,
    baseURL,
  }) => {
    test.setTimeout(180_000);

    // 1. Partner adds a promoted formula with included perks.
    await signIn(page, salon.owner);
    await page.goto(`/pro/etablissements/${salon.id}/prestations`);
    await page.getByLabel('Nom', { exact: true }).fill('Forfait Mariée Royale');
    await page.getByLabel('Prix (FCFA)').fill('25000');
    await page.getByLabel('Durée (minutes)').fill('90');
    await page.getByLabel('Promotion (%)').fill('20');
    await page
      .getByLabel(/Éléments inclus \/ avantages de la formule/)
      .fill('Coiffure complète\nMaquillage soirée');
    await page.getByRole('button', { name: 'Ajouter la prestation' }).click();
    await expect(page.getByText('Forfait Mariée Royale')).toBeVisible();
    await expect(page.getByText('Promo −20 %')).toBeVisible();
    await expect(page.getByText('Inclus : Coiffure complète · Maquillage soirée')).toBeVisible();

    // 2. Public establishment page shows the promo and formula perks.
    const pub = await (await browser.newContext({ baseURL })).newPage();
    await pub.goto(`/etablissements/${salon.slug}`);
    // The name shows on the line and again, for screen readers, in its "Réserver" button.
    await expect(pub.getByText('Forfait Mariée Royale').first()).toBeVisible();
    await expect(pub.getByText('Promo −20 %')).toBeVisible();
    await expect(pub.getByText('Inclus : Coiffure complète · Maquillage soirée')).toBeVisible();
    await pub.close();

    // 3. Partner checks detailed finances and downloads the accounting CSV.
    await page.goto(`/pro/etablissements/${salon.id}/finances`);
    await expect(page.getByRole('heading', { name: /Finances et reçus/ })).toBeVisible();
    await expect(page.getByText("Chiffre d'affaires brut")).toBeVisible();
    const csvDownload = page.waitForEvent('download');
    await page.getByRole('button', { name: 'Exporter en CSV' }).click();
    const downloaded = await csvDownload;
    expect(downloaded.suggestedFilename()).toMatch(/^comptabilite-.*\.csv$/);

    // 4. Administrator views the analytical dashboard on /admin.
    const adminPage = await (await browser.newContext({ baseURL })).newPage();
    await signIn(adminPage, admin);
    await adminPage.goto('/admin');
    await expect(adminPage.getByRole('heading', { name: /Tableau de bord analytique/ })).toBeVisible();
    await expect(adminPage.getByRole('heading', { name: 'Établissements & KYC' })).toBeVisible();
    await expect(adminPage.getByRole('heading', { name: 'Rendez-vous et activité' })).toBeVisible();
    await expect(adminPage.getByRole('heading', { name: 'Abonnements & Finances' })).toBeVisible();
    await expect(adminPage.getByRole('heading', { name: 'Communauté et qualité' })).toBeVisible();
    await adminPage.close();
  });
});
