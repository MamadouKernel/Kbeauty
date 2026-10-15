import { APIRequestContext, expect } from '@playwright/test';
import { png } from './files';
import { PASSWORD, sql, uniqueEmail } from './env';
import { tokenIn, waitForMail } from './mail';
import { freshCode } from './totp';

export type Role = 'Client' | 'PartnerOwner';

export interface Account {
  readonly email: string;
  readonly password: string;
  readonly userId: string;
  /** TOTP secret of the account, to sign in through the UI. */
  readonly secret: string;
  readonly auth: { Authorization: string };
}

export interface Salon {
  readonly id: string;
  readonly slug: string;
  readonly name: string;
  readonly owner: Account;
  readonly hourService: string;
  readonly halfHourService: string;
}

interface Answer {
  ok(): boolean;
  text(): Promise<string>;
  json(): Promise<unknown>;
}

async function json<T>(response: Answer): Promise<T> {
  expect(response.ok(), await response.text()).toBeTruthy();
  return (await response.json()) as T;
}

async function ok(response: Answer): Promise<void> {
  expect(response.ok(), await response.text()).toBeTruthy();
}

/** Registers, confirms the e-mail (through Mailpit), optionally promotes, and enrols two-factor authentication. */
export async function createAccount(
  request: APIRequestContext,
  role: Role,
  promoteTo?: 'Admin' | 'SuperAdmin',
): Promise<Account> {
  const email = uniqueEmail(promoteTo ? promoteTo.toLowerCase() : role.toLowerCase());
  await ok(
    await request.post('/api/v1/auth/register', {
      data: { email, password: PASSWORD, firstName: 'Awa', lastName: 'Koné', role, acceptTerms: true },
    }),
  );
  const message = await waitForMail(email, /Confirmez/i);
  await ok(await request.post('/api/v1/auth/confirm-email', { data: { token: tokenIn(message) } }));

  if (promoteTo) {
    sql(`UPDATE identity.users SET roles = ARRAY['${promoteTo}'] WHERE email = '${email}'`);
  }

  const challenge = await json<{ challengeToken: string }>(
    await request.post('/api/v1/auth/login', { data: { email, password: PASSWORD } }),
  );
  const { secret } = await json<{ secret: string }>(
    await request.post('/api/v1/auth/2fa/setup', { data: { challengeToken: challenge.challengeToken } }),
  );
  const session = await json<{ accessToken: string; user: { id: string } }>(
    await request.post('/api/v1/auth/2fa/enable', {
      data: { challengeToken: challenge.challengeToken, code: await freshCode(secret) },
    }),
  );
  return {
    email,
    password: PASSWORD,
    userId: session.user.id,
    secret,
    auth: { Authorization: `Bearer ${session.accessToken}` },
  };
}

interface Named {
  readonly id: string;
  readonly name: string;
  readonly slug?: string;
}

/** Changes some of the platform's global settings (read, merge, write back). */
export async function updateSettings(
  request: APIRequestContext,
  admin: Account,
  patch: Record<string, unknown>,
): Promise<void> {
  const current = await json<Record<string, unknown>>(
    await request.get('/api/v1/admin/settings', { headers: admin.auth }),
  );
  await ok(await request.put('/api/v1/admin/settings', { headers: admin.auth, data: { ...current, ...patch } }));
}

export interface SalonOptions {
  /** Subscribe to Premium and switch online booking on (default true). */
  readonly subscribe?: boolean;
  readonly autoConfirm?: boolean;
  /** Ask clients to pay online: a deposit percentage, or the full price. */
  readonly payment?: { mode: 'Deposit' | 'Full'; depositPercent?: number };
}

/**
 * Creates, fills, submits and gets approved a new salon (open Monday to Saturday 09:00-18:00) with a one-hour and a half-hour
 * service, through the real onboarding endpoints, like a partner and an admin would.
 */
export async function createSalon(
  request: APIRequestContext,
  admin: Account,
  options: SalonOptions = {},
): Promise<Salon> {
  const { subscribe = true, autoConfirm = true, payment } = options;
  const owner = await createAccount(request, 'PartnerOwner');

  const country = (await json<Named[]>(await request.get('/api/v1/geo/countries')))[0];
  const regions = await json<Named[]>(await request.get(`/api/v1/geo/countries/${country.id}/regions`));
  const region = regions.find((r) => r.name.includes('Abidjan'))!;
  const city = (await json<Named[]>(await request.get(`/api/v1/geo/regions/${region.id}/cities`)))[0];
  const commune = (await json<Named[]>(await request.get(`/api/v1/geo/cities/${city.id}/communes`))).find(
    (c) => c.name === 'Cocody',
  )!;
  const category = (await json<Named[]>(await request.get('/api/v1/categories'))).find(
    (c) => c.slug === 'salon-de-coiffure',
  )!;

  const name = `Salon E2E ${Date.now().toString(36)}${Math.random().toString(36).slice(2, 5)}`;
  const created = await json<{ id: string }>(
    await request.post('/api/v1/pro/establishments', {
      headers: owner.auth,
      data: {
        name,
        description: 'Salon de test pour les parcours de bout en bout.',
        customerServicePhone: '+22507000011',
        email: null,
        categoryIds: [category.id],
        communeId: commune.id,
        addressLine: 'Rue des Tests',
        latitude: 5.36,
        longitude: -3.99,
      },
    }),
  );
  const base = `/api/v1/pro/establishments/${created.id}`;

  await ok(
    await request.put(`${base}/opening-hours`, {
      headers: owner.auth,
      data: [1, 2, 3, 4, 5, 6].map((dayOfWeek) => ({ dayOfWeek, opensAt: '09:00', closesAt: '18:00' })),
    }),
  );
  await ok(
    await request.post(`${base}/media`, {
      headers: owner.auth,
      multipart: { file: { name: 'photo.png', mimeType: 'image/png', buffer: png() } },
    }),
  );
  await ok(
    await request.put('/api/v1/pro/kyc', {
      headers: owner.auth,
      data: {
        holderName: 'Awa Koné',
        documentType: 'NationalId',
        documentNumber: `CI${Math.floor(10_000_000 + Math.random() * 89_999_999)}`,
        documentExpiry: '2032-05-01',
      },
    }),
  );
  for (const side of ['front', 'back']) {
    await ok(
      await request.put(`/api/v1/pro/kyc/documents/${side}`, {
        headers: owner.auth,
        multipart: { file: { name: `${side}.png`, mimeType: 'image/png', buffer: png(900, 600) } },
      }),
    );
  }
  await ok(await request.post(`${base}/submit`, { headers: owner.auth }));

  const queue = await json<{ items: { establishmentName: string; reviewId: string }[] }>(
    await request.get('/api/v1/admin/kyc/queue?pageSize=50', { headers: admin.auth }),
  );
  const review = queue.items.find((i) => i.establishmentName === name)!;
  await ok(
    await request.post(`/api/v1/admin/kyc/${review.reviewId}/decision`, {
      headers: admin.auth,
      data: { decision: 'Approve', comment: null },
    }),
  );

  const service = async (serviceName: string, price: number, durationMinutes: number): Promise<string> =>
    (
      await json<{ id: string }>(
        await request.post(`${base}/services`, {
          headers: owner.auth,
          data: {
            name: serviceName,
            description: null,
            price,
            priceType: 'Fixed',
            durationMinutes,
            categoryId: category.id,
            groupId: null,
            isBookable: true,
            isVisible: true,
          },
        }),
      )
    ).id;
  const hourService = await service('Coupe longue', 10000, 60);
  const halfHourService = await service('Brushing', 5000, 30);

  if (subscribe) {
    const checkout = await json<{ transactionId: string }>(
      await request.post(`${base}/subscription`, { headers: owner.auth, data: { planCode: 'premium', cycle: 'Monthly' } }),
    );
    await ok(
      await request.post(`/api/v1/dev/payments/${checkout.transactionId}/simulate`, { data: { status: 'accepted' } }),
    );
    await ok(
      await request.put(`${base}/booking-settings`, {
        headers: owner.auth,
        data: {
          bookingEnabled: true,
          autoConfirm,
          minLeadMinutes: 0,
          maxHorizonDays: 60,
          cancelLimitMinutes: 120,
          slotStepMinutes: 30,
        },
      }),
    );
    if (payment) {
      await ok(
        await request.put(`${base}/payment-settings`, {
          headers: owner.auth,
          data: {
            mode: payment.mode,
            depositPercent: payment.depositPercent ?? 30,
            payoutOperator: 'wave-cote-divoire',
            payoutAccount: '07 00 00 12 34',
            payoutAccountName: 'Awa Koné',
            platformEnabled: false,
          },
        }),
      );
    }
  }

  const detail = await json<{ slug: string }>(await request.get(base, { headers: owner.auth }));
  return { id: created.id, slug: detail.slug, name, owner, hourService, halfHourService };
}
