import { APIRequestContext, expect, test } from '@playwright/test';
import { Account, Salon, createAccount, createSalon } from '../helpers/api';
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

test.describe('Counter and client pass', () => {
  test('the partner records a walk-in, then verifies the QR pass of a client who booked online', async ({
    page,
    browser,
    baseURL,
  }) => {
    await signIn(page, salon.owner);
    await page.goto(`/pro/etablissements/${salon.id}/comptoir`);

    await page.getByRole('checkbox', { name: /Brushing/ }).check();
    await page.getByLabel('Nom de la cliente').fill('Mariam Traoré');
    await page.getByLabel(/Téléphone/).fill('07 00 00 55 66');
    await page.getByRole('button', { name: 'Enregistrer le rendez-vous' }).click();

    await expect(page.getByRole('heading', { name: "C'est enregistré" })).toBeVisible();
    await expect(page.getByText(/Mariam Traoré/).first()).toBeVisible();

    await page.getByRole('link', { name: "Voir l'agenda" }).click();
    await expect(page.getByText('Mariam Traoré').first()).toBeVisible();

    // A client books online and shows the QR pass from their account.
    const client = await createAccount(request, 'Client');
    const visitor = await (await browser.newContext({ baseURL })).newPage();
    await signIn(visitor, client);
    await visitor.goto(`/etablissements/${salon.slug}/reserver`);
    await visitor.getByRole('checkbox', { name: /Coupe longue/ }).check();
    await pickFirstSlot(visitor, 2);
    await visitor.getByLabel(/Votre téléphone/).fill('07 00 00 12 34');
    await visitor.getByRole('button', { name: 'Envoyer ma demande' }).click();
    await expect(visitor.getByRole('heading', { name: 'Rendez-vous confirmé' })).toBeVisible();

    await visitor.goto('/compte/rendez-vous');
    await visitor.getByRole('button', { name: 'Mon pass QR' }).click();
    await expect(visitor.getByRole('img', { name: 'QR code de votre rendez-vous' })).toBeVisible();

    // The QR encodes the token; the salon's scanner page accepts it pasted.
    const mine = await request.get('/api/v1/me/appointments', { headers: client.auth });
    const [appointment] = (await mine.json()) as { id: string }[];
    const pass = (await (
      await request.get(`/api/v1/appointments/${appointment.id}/pass`, { headers: client.auth })
    ).json()) as { token: string };

    await page.goto(`/pro/etablissements/${salon.id}/scanner`);
    await page.getByLabel(/collez le code/i).fill(pass.token);
    await page.getByRole('button', { name: 'Vérifier' }).click();
    await expect(page.getByRole('heading', { name: 'Pass valide' })).toBeVisible();
  });
});
