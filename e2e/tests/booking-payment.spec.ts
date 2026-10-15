import { APIRequestContext, expect, test } from '@playwright/test';
import { Account, Salon, createAccount, createSalon } from '../helpers/api';
import { expectNoHorizontalScroll, pickFirstSlot, signIn } from '../helpers/ui';

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

test.describe('Booking', () => {
  test('a client books a demo salon, sees it in "Mes rendez-vous" and cancels it', async ({ page }) => {
    const client = await createAccount(request, 'Client');
    await signIn(page, client);

    await page.goto('/etablissements/maison-eclat-coiffure/reserver');
    await expectNoHorizontalScroll(page);
    await page.getByRole('checkbox').first().check();
    await pickFirstSlot(page);
    await page.getByLabel(/Votre téléphone/).fill(PHONE);
    await page.getByRole('button', { name: 'Envoyer ma demande' }).click();
    await expect(page.getByRole('heading', { name: /Rendez-vous confirmé|Demande envoyée/ })).toBeVisible();

    await page.goto('/compte/rendez-vous');
    await expectNoHorizontalScroll(page);
    const card = page.getByRole('article').filter({ hasText: 'Maison Éclat Coiffure' }).first();
    await expect(card).toBeVisible();
    page.once('dialog', (dialog) => dialog.accept());
    await card.getByRole('button', { name: 'Annuler le rendez-vous' }).click();
    await expect(page.getByText('Annulé par vous').first()).toBeVisible();
  });
});

test.describe('Paying the establishment for the service', () => {
  test('the partner declares cash and mobile money; visitors and the booking page show how to pay', async ({
    page,
    browser,
  }) => {
    await signIn(page, salon.owner);
    await page.goto(`/pro/etablissements/${salon.id}/paiements`);
    await expect(page.getByRole('heading', { name: /Moyens de paiement/ }).first()).toBeVisible();

    const cash = page.getByLabel('Espèces, sur place');
    await expect(cash).toBeChecked();
    await page.getByRole('button', { name: 'Ajouter un compte Mobile Money' }).click();
    await page.getByLabel('Numéro Mobile Money').fill('07 00 00 12 34');
    await page.getByLabel('Nom du titulaire').fill('Awa Koné');
    await page.getByRole('button', { name: 'Enregistrer' }).click();
    await expect(page.getByText('Moyens de paiement enregistrés.')).toBeVisible();

    // A refused entry is explained.
    await page.getByRole('button', { name: 'Ajouter un compte Mobile Money' }).click();
    await page.getByLabel('Numéro Mobile Money').nth(1).fill('abc');
    await page.getByLabel('Nom du titulaire').nth(1).fill('Awa');
    await page.getByRole('button', { name: 'Enregistrer' }).click();
    await expect(page.getByRole('alert')).toContainText(/numéro valide/);

    const visitor = await browser.newContext();
    const publicPage = await visitor.newPage();
    await publicPage.goto(`/etablissements/${salon.slug}?onglet=apropos`); // the practical information tab
    const methods = publicPage.getByRole('region', { name: 'Moyens de paiement' });
    await expect(methods).toContainText('Espèces, sur place');
    await expect(methods).toContainText('Wave');
    await expect(methods).toContainText('+2250700001234');
    await expect(methods).toContainText('Awa Koné');
    await visitor.close();

    // The booking page says that the client pays the establishment directly.
    const client = await createAccount(request, 'Client');
    const clientPage = await (await browser.newContext()).newPage();
    await signIn(clientPage, client);
    await clientPage.goto(`/etablissements/${salon.slug}/reserver`);
    await clientPage.getByRole('checkbox').first().check();
    await pickFirstSlot(clientPage);
    await expect(clientPage.getByText(/Vous réglez la prestation directement auprès de/)).toBeVisible();
    await expectNoHorizontalScroll(clientPage);
  });

  test('the partner marks an appointment as paid with the mobile-money reference and the client sees it', async ({
    page,
    browser,
  }) => {
    await request.put(`/api/v1/pro/establishments/${salon.id}/accepted-payments`, {
      headers: salon.owner.auth,
      data: { cash: true, mobileMoney: [{ operator: 'wave-cote-divoire', account: '07 00 00 12 34', holderName: 'Awa Koné' }] },
    });
    const client = await createAccount(request, 'Client');
    const clientPage = await (await browser.newContext()).newPage();
    await signIn(clientPage, client);
    await clientPage.goto(`/etablissements/${salon.slug}/reserver`);
    await clientPage.getByRole('checkbox', { name: /Brushing/ }).check();
    await pickFirstSlot(clientPage, 1);
    await clientPage.getByLabel(/Votre téléphone/).fill(PHONE);
    await clientPage.getByRole('button', { name: 'Envoyer ma demande' }).click();
    await expect(clientPage.getByRole('heading', { name: 'Rendez-vous confirmé' })).toBeVisible();

    await signIn(page, salon.owner);
    await page.goto(`/pro/etablissements/${salon.id}/agenda`);
    await page.getByRole('tab', { name: 'Agenda' }).click();
    await page.getByRole('button', { name: 'Mois' }).click();
    await page.getByRole('button', { name: 'Marquer payé' }).first().click();
    await page.getByLabel('Réglé par').selectOption({ label: 'Wave' });
    const save = page.getByRole('button', { name: 'Enregistrer le paiement' });
    await expect(save).toBeDisabled(); // a mobile-money payment needs its reference
    await page.getByLabel('Référence de la transaction').fill('WV-E2E-0042');
    await save.click();
    await expect(page.getByText(/Payé : Wave, réf\. WV-E2E-0042/)).toBeVisible();

    await clientPage.goto('/compte/rendez-vous');
    await expect(clientPage.getByText(/Réglé à l'établissement \(Wave, réf\. WV-E2E-0042\)/)).toBeVisible();
  });

  test('no payment page is shown when booking: the slot is confirmed without paying the platform', async ({ page }) => {
    const client = await createAccount(request, 'Client');
    await signIn(page, client);
    await page.goto(`/etablissements/${salon.slug}/reserver`);
    await page.getByRole('checkbox', { name: /Coupe longue/ }).check();
    await pickFirstSlot(page);
    await page.getByLabel(/Votre téléphone/).fill(PHONE);
    await page.getByRole('button', { name: 'Envoyer ma demande' }).click();
    await expect(page.getByRole('heading', { name: 'Rendez-vous confirmé' })).toBeVisible();
    expect(page.url()).not.toMatch(/paiement/);
  });
});
