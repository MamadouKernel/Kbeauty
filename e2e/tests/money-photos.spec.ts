import { APIRequestContext, expect, test } from '@playwright/test';
import { Account, Salon, createAccount, createSalon, updateSettings } from '../helpers/api';
import { sql } from '../helpers/env';
import { png } from '../helpers/files';
import { signIn } from '../helpers/ui';

const PHONE = '07 00 00 12 34';

let request: APIRequestContext;
let admin: Account;
let salon: Salon;

/** The next open day (Tuesday to Saturday) at 10:00 UTC, a couple of days ahead. */
function nextOpenDay(daysAhead = 2): string {
  const day = new Date();
  day.setUTCDate(day.getUTCDate() + daysAhead);
  while (day.getUTCDay() === 0 || day.getUTCDay() === 1) {
    day.setUTCDate(day.getUTCDate() + 1);
  }
  day.setUTCHours(10, 0, 0, 0);
  return day.toISOString();
}

test.beforeAll(async ({ playwright, baseURL }) => {
  request = await playwright.request.newContext({ baseURL });
  admin = await createAccount(request, 'Client', 'Admin');
  await updateSettings(request, admin, { appointmentPaymentsEnabled: true, platformFeePercent: 0 });
  salon = await createSalon(request, admin, { autoConfirm: true, payment: { mode: 'Full' } });
});

test.afterAll(async () => {
  // The platform switch is global: later suites expect booking without online payment.
  await updateSettings(request, admin, { appointmentPaymentsEnabled: false, platformFeePercent: 0 });
  await request.dispose();
});

/** Books through the API, then pays the way the payment page would (the fake gateway simulates the provider). */
async function bookAndPay(client: Account): Promise<string> {
  const booked = await request.post('/api/v1/appointments', {
    headers: client.auth,
    data: {
      establishmentId: salon.id,
      serviceIds: [salon.hourService],
      resourceId: null,
      startsAt: nextOpenDay(),
      phone: PHONE,
      note: null,
    },
  });
  expect(booked.ok(), await booked.text()).toBeTruthy();
  const appointment = (await booked.json()) as { id: string; payment: { paymentUrl: string } };
  const transaction = new URL(appointment.payment.paymentUrl).searchParams.get('tx')!;
  const paid = await request.post(`/api/v1/dev/payments/${transaction}/simulate`, { data: { status: 'accepted' } });
  expect(paid.ok(), await paid.text()).toBeTruthy();
  return appointment.id;
}

test.describe('Late-cancellation fee', () => {
  test('a client who cancels late sees the fee, accepts it, and the salon keeps it', async ({ page }) => {
    const settings = {
      bookingEnabled: true,
      autoConfirm: true,
      minLeadMinutes: 0,
      maxHorizonDays: 60,
      cancelLimitMinutes: 10080,
      slotStepMinutes: 30,
      lateCancelFeePercent: 30,
    };
    const saved = await request.put(`/api/v1/pro/establishments/${salon.id}/booking-settings`, {
      headers: salon.owner.auth,
      data: settings,
    });
    expect(saved.ok(), await saved.text()).toBeTruthy();

    const client = await createAccount(request, 'Client');
    await bookAndPay(client);

    await signIn(page, client);
    await page.goto('/compte/rendez-vous');
    await expect(page.getByText(/Annulation tardive : .*3\s000.* seront retenus/)).toBeVisible();

    let question = '';
    page.once('dialog', (dialog) => {
      question = dialog.message();
      void dialog.accept();
    });
    await page.getByRole('button', { name: 'Annuler le rendez-vous' }).click();
    await expect(page.getByText('Annulé par vous').first()).toBeVisible();
    expect(question).toMatch(/frais d.annulation tardive/);

    // Back to the normal rule for the other journeys of this suite.
    await request.put(`/api/v1/pro/establishments/${salon.id}/booking-settings`, {
      headers: salon.owner.auth,
      data: { ...settings, cancelLimitMinutes: 120, lateCancelFeePercent: 0 },
    });
  });
});

test.describe('Tips, payouts to collaborators and review photos', () => {
  test('a tip is paid online, the review carries photos, and the owner pays the collaborator', async ({
    page,
    browser,
    baseURL,
  }) => {
    test.setTimeout(240_000);

    // The owner links a collaborator to the salon's resource, at 30 %.
    const collaborator = await createAccount(request, 'Client');
    const team = (await (
      await request.get(`/api/v1/pro/establishments/${salon.id}/team`, { headers: salon.owner.auth })
    ).json()) as { resourceId: string }[];
    const linked = await request.put(`/api/v1/pro/establishments/${salon.id}/team/${team[0].resourceId}`, {
      headers: salon.owner.auth,
      data: { email: collaborator.email, commissionPercent: 30 },
    });
    expect(linked.ok(), await linked.text()).toBeTruthy();

    // A client books and pays online; the visit took place.
    const client = await createAccount(request, 'Client');
    const appointmentId = await bookAndPay(client);
    sql(
      `UPDATE scheduling.appointments SET status = 'Completed', starts_at = now() - interval '3 hours', ` +
        `ends_at = now() - interval '2 hours' WHERE id = '${appointmentId}'`,
    );

    // She leaves a tip: the payment page, then back with the tip counted.
    await signIn(page, client);
    await page.goto('/compte/rendez-vous');
    await page.getByRole('button', { name: /^500/ }).click();
    await page.getByRole('button', { name: 'Payer', exact: true }).click();
    await expect(page.getByText(/Merci pour votre pourboire/)).toBeVisible();
    await expect(page.getByText(/Pourboire laissé : 500/)).toBeVisible({ timeout: 20_000 });

    // Her review comes with a before and an after photo.
    await page.getByRole('button', { name: 'Donner mon avis' }).click();
    await page.getByRole('radio', { name: /5 sur 5/ }).check();
    await page.getByPlaceholder(/Racontez votre visite/).fill('Transformation incroyable, merci !');
    await page.getByLabel('Ajouter des photos').setInputFiles([
      { name: 'avant.png', mimeType: 'image/png', buffer: png() },
      { name: 'apres.png', mimeType: 'image/png', buffer: png(640, 480) },
    ]);
    await page.getByRole('button', { name: 'Publier mon avis' }).click();
    await expect(page.getByText('Votre avis :')).toBeVisible();

    const visitor = await (await browser.newContext({ baseURL })).newPage();
    await visitor.goto(`/etablissements/${salon.slug}?onglet=avis`);
    await expect(visitor.getByText('Transformation incroyable, merci !')).toBeVisible();
    await expect(visitor.getByRole('img', { name: /Avant — photo de/ })).toBeVisible();
    await expect(visitor.getByRole('img', { name: /Après — photo de/ })).toBeVisible();

    // The collaborator gives her payout account.
    const staff = await (await browser.newContext({ baseURL })).newPage();
    await signIn(staff, collaborator);
    await staff.goto('/equipe');
    await staff.getByLabel('Numéro de paiement mobile').fill('07 00 00 99 88');
    await staff.getByLabel('Nom du titulaire du compte').fill('Awa Koné');
    await staff.getByRole('button', { name: 'Enregistrer mon compte' }).click();
    await expect(staff.getByText(/Vos gains sont versés sur/)).toBeVisible();

    // The owner pays her commission and her tip.
    const owner = await (await browser.newContext({ baseURL })).newPage();
    await signIn(owner, salon.owner);
    await owner.goto(`/pro/etablissements/${salon.id}/equipe`);
    const row = owner.getByRole('row').filter({ hasText: 'Awa' }).filter({ hasText: 'Payer par mobile money' });
    await expect(row).toContainText(/3\s500/);
    owner.once('dialog', (dialog) => void dialog.accept());
    await row.getByRole('button', { name: 'Payer par mobile money' }).click();
    await expect(owner.getByText(/versés à/)).toBeVisible();

    // She sees the transfer, and nothing left to receive.
    await staff.reload();
    await staff.getByText('Versements reçus').click();
    await expect(staff.getByRole('cell', { name: 'Versé' })).toBeVisible();
  });
});
