import { APIRequestContext, expect, test } from '@playwright/test';
import { Account, Salon, createAccount, createSalon } from '../helpers/api';
import { sql } from '../helpers/env';
import { signIn } from '../helpers/ui';

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

/** Books the first free slot starting `daysAhead` days from now or later (closed days are skipped) through the API. */
async function book(client: Account, daysAhead: number): Promise<string> {
  const from = new Date(Date.now() + daysAhead * 86_400_000).toISOString().slice(0, 10);
  const to = new Date(Date.now() + (daysAhead + 9) * 86_400_000).toISOString().slice(0, 10);
  const availability = await (
    await request.get(`/api/v1/establishments/${salon.id}/availability`, {
      params: { from, to, serviceIds: salon.hourService },
    })
  ).json();
  const slots: { startsAt: string; available: boolean }[] = availability.days.flatMap((d: { slots: never[] }) => d.slots);
  const slot = slots.find((s) => s.available);
  expect(slot, 'a free slot in the next days').toBeTruthy();
  const response = await request.post('/api/v1/appointments', {
    headers: client.auth,
    data: { establishmentId: salon.id, serviceIds: [salon.hourService], resourceId: null, startsAt: slot!.startsAt, phone: '07 00 00 12 34', note: null },
  });
  expect(response.ok(), await response.text()).toBeTruthy();
  return (await response.json()).id as string;
}

test.describe('V1 parity: move, delay, address search', () => {
  test('a client moves her appointment to another free slot from "Mes rendez-vous"', async ({ page }) => {
    const client = await createAccount(request, 'Client');
    const id = await book(client, 4);
    await signIn(page, client);

    await page.goto('/compte/rendez-vous');
    await page.getByRole('button', { name: 'Déplacer le rendez-vous' }).click();
    const target = new Date(Date.now() + 8 * 86_400_000).toISOString().slice(0, 10);
    await page.getByLabel('Nouveau jour').fill(target);
    page.once('dialog', (dialog) => void dialog.accept());
    await page.getByRole('group', { name: 'Créneaux libres' }).getByRole('button').first().click();

    await expect(page.getByRole('button', { name: 'Déplacer le rendez-vous' })).toBeVisible();
    await expect(page.getByLabel('Nouveau jour')).toHaveCount(0);
    const moved = sql(`SELECT starts_at::date FROM scheduling.appointments WHERE id = '${id}'`);
    expect(moved).toContain(target);
  });

  test('a client signals a delay shortly before a confirmed appointment and the salon is notified', async ({ page }) => {
    const client = await createAccount(request, 'Client');
    const id = await book(client, 3);
    // Bring the appointment to within the hour (the server only accepts a delay around the visit).
    sql(
      `UPDATE scheduling.appointments SET starts_at = date_trunc('minute', now()) + interval '70 minutes', ends_at = date_trunc('minute', now()) + interval '130 minutes' WHERE id = '${id}'`,
    );
    await signIn(page, client);

    await page.goto('/compte/rendez-vous');
    await expect(page.getByText('Un retard ?')).toBeVisible();
    await page.getByRole('button', { name: '15 min' }).click();
    await expect(page.getByText('Un retard ?')).toHaveCount(0);

    const notified = sql(
      `SELECT count(*) FROM notifications.notifications WHERE user_id = '${salon.owner.userId}' AND template_code = 'appointment.client_late.partner'`,
    );
    expect(Number(notified.trim())).toBeGreaterThanOrEqual(1);
  });

  test('a partner searches an address, picks a result and the pin follows', async ({ page }) => {
    await signIn(page, salon.owner);

    await page.goto(`/pro/etablissements/${salon.id}`);
    await page.getByRole('tab', { name: /Localisation/ }).click();
    await page.getByLabel('Adresse', { exact: true }).fill('Cocody Abidjan');
    await page.getByRole('button', { name: "Chercher l'adresse sur la carte" }).click();

    const results = page.getByRole('list', { name: 'Adresses trouvées' });
    await expect(results.getByRole('button').first()).toBeVisible();
    await results.getByRole('button').first().click();
    await expect(page.getByLabel('Adresse', { exact: true })).toHaveValue(/Cocody/);
    await expect(results).toHaveCount(0);
  });
});
