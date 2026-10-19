import { APIRequestContext, expect, test } from '@playwright/test';
import { Account, Salon, createAccount, createSalon } from '../helpers/api';
import { sql } from '../helpers/env';
import { freshCode } from '../helpers/totp';

/**
 * Second round of intrusion tests (October 2026), covering what the first round (security.spec.ts) did not: lots 5 to 7
 * (finances, gift cards, loyalty, waiting list, group bookings) and token/session revocation. A test passes when the
 * attack FAILS. Run against the stack with docker-compose.e2e.yml (raised sign-up limits); see docs/PENTEST.md.
 *
 * The sensitive auth tests (A07) each use their own APIRequestContext: sharing one across many abusive bursts polluted
 * earlier runs with rate-limit noise and produced false results.
 */
let request: APIRequestContext;
let admin: Account;
let salonA: Salon;
let salonB: Salon;
let client: Account;
let intruder: Account;

const PHONE = '0700001234';

test.beforeAll(async ({ playwright, baseURL }) => {
  test.setTimeout(240_000);
  request = await playwright.request.newContext({ baseURL });
  admin = await createAccount(request, 'Client', 'Admin');
  salonA = await createSalon(request, admin, { autoConfirm: true });
  salonB = await createSalon(request, admin, { autoConfirm: true });
  client = await createAccount(request, 'Client');
  intruder = await createAccount(request, 'Client');
});

test.afterAll(async () => {
  await request.dispose();
});

function inDays(days: number): string {
  return new Date(Date.now() + days * 86400_000).toISOString();
}

test.describe('A01 broken access control on lots 5 to 7', () => {
  test('a partner cannot read or act on another establishment’s finances, loyalty, gift cards or requests', async () => {
    const a = `/api/v1/pro/establishments/${salonA.id}`;
    const card = await (
      await request.post(`${a}/gift-cards`, { headers: salonA.owner.auth, data: { amount: 20000, recipientName: 'Aya', validityMonths: 12 } })
    ).json();
    const group = await (
      await request.post('/api/v1/group-bookings', {
        headers: client.auth,
        data: { establishmentId: salonA.id, eventType: 'Wedding', desiredStartsAt: inDays(30), groupSize: 6, services: 'Coiffure', message: null, phone: PHONE },
      })
    ).json();

    const attacks: [string, string, unknown?][] = [
      ['GET', `${a}/finances?from=${inDays(-1)}&to=${inDays(30)}`],
      ['GET', `${a}/finances.csv?from=${inDays(-1)}&to=${inDays(30)}`],
      ['GET', `${a}/waitlist`],
      ['GET', `${a}/loyalty`],
      ['PUT', `${a}/loyalty`, { isActive: true, pointsPer1000: 100, rewardThreshold: 10, rewardDescription: 'Tout gratuit' }],
      ['GET', `${a}/loyalty/members`],
      ['POST', `${a}/loyalty/redeem`, { clientUserId: client.userId }],
      ['GET', `${a}/gift-cards`],
      ['POST', `${a}/gift-cards`, { amount: 50000, recipientName: 'Moi', validityMonths: 24 }],
      ['GET', `${a}/gift-cards/lookup?code=${card.code}`],
      ['POST', `/api/v1/pro/gift-cards/${card.id}/redeem`, { amount: 20000, appointmentId: null }],
      ['POST', `/api/v1/pro/gift-cards/${card.id}/cancel`, {}],
      ['GET', `${a}/group-bookings`],
      ['POST', `/api/v1/pro/group-bookings/${group.id}/answer`, { accept: false, quotedPrice: null, message: 'Refuse par un concurrent' }],
    ];
    for (const [method, url, data] of attacks) {
      const response = await request.fetch(url, { method, headers: salonB.owner.auth, data });
      expect(response.status(), `${method} ${url}`).toBe(404);
    }

    // The gift card of A is not findable from B's counter either.
    const lookup = await request.get(`/api/v1/pro/establishments/${salonB.id}/gift-cards/lookup?code=${card.code}`, { headers: salonB.owner.auth });
    expect(lookup.status()).toBe(404);
  });

  test('a client cannot touch another client’s waiting list or group requests', async () => {
    const day = inDays(6).slice(0, 10);
    const entry = await (
      await request.post('/api/v1/waitlist', { headers: client.auth, data: { establishmentId: salonA.id, date: day, serviceId: null } })
    ).json();
    expect((await request.delete(`/api/v1/me/waitlist/${entry.id}`, { headers: intruder.auth })).status()).toBe(404);

    const mine = await (await request.get('/api/v1/me/group-bookings', { headers: client.auth })).json();
    if (mine.length > 0) {
      expect((await request.post(`/api/v1/me/group-bookings/${mine[0].id}/cancel`, { headers: intruder.auth })).status()).toBe(404);
    }
    expect(await (await request.get('/api/v1/me/waitlist', { headers: intruder.auth })).json()).toEqual([]);
  });

  test('a client is refused on the partner routes of lots 5 to 7', async () => {
    for (const url of [
      `/api/v1/pro/establishments/${salonA.id}/loyalty`,
      `/api/v1/pro/establishments/${salonA.id}/gift-cards`,
      `/api/v1/pro/establishments/${salonA.id}/finances?from=${inDays(-1)}&to=${inDays(1)}`,
    ]) {
      expect((await request.get(url, { headers: client.auth })).status(), url).toBe(403);
    }
  });
});

test.describe('A04 abuse of business rules', () => {
  test('gift card amounts cannot be negative, fractional or beyond the balance', async () => {
    const a = `/api/v1/pro/establishments/${salonA.id}/gift-cards`;
    for (const amount of [-1000, 0, 999, 1000.5, 2_000_000]) {
      expect((await request.post(a, { headers: salonA.owner.auth, data: { amount, recipientName: 'Test', validityMonths: 12 } })).status(), `create ${amount}`).toBe(400);
    }
    const card = await (await request.post(a, { headers: salonA.owner.auth, data: { amount: 5000, recipientName: 'Test', validityMonths: 12 } })).json();
    for (const amount of [-5000, 0, 0.5]) {
      expect((await request.post(`/api/v1/pro/gift-cards/${card.id}/redeem`, { headers: salonA.owner.auth, data: { amount, appointmentId: null } })).status(), `redeem ${amount}`).toBe(400);
    }
    expect((await request.post(`/api/v1/pro/gift-cards/${card.id}/redeem`, { headers: salonA.owner.auth, data: { amount: 5001, appointmentId: null } })).status()).toBe(409);
  });

  test('a group quote cannot be negative and the waiting list cannot be flooded', async () => {
    const req = await (
      await request.post('/api/v1/group-bookings', {
        headers: intruder.auth,
        data: { establishmentId: salonB.id, eventType: 'Birthday', desiredStartsAt: inDays(20), groupSize: 4, services: 'Ongles', message: null, phone: PHONE },
      })
    ).json();
    expect((await request.post(`/api/v1/pro/group-bookings/${req.id}/answer`, { headers: salonB.owner.auth, data: { accept: true, quotedPrice: -50000, message: 'Nous vous payons' } })).status()).toBe(400);

    const statuses: number[] = [];
    for (let i = 1; i <= 12; i++) {
      statuses.push((await request.post('/api/v1/waitlist', { headers: intruder.auth, data: { establishmentId: salonB.id, date: inDays(i + 1).slice(0, 10), serviceId: null } })).status());
    }
    expect(statuses, 'the per-client waiting-list cap is enforced').toContain(409);
  });

  test('gift card codes cannot be guessed in bulk by a client', async () => {
    const statuses: number[] = [];
    for (let i = 0; i < 15; i++) {
      statuses.push((await request.post('/api/v1/me/gift-cards', { headers: intruder.auth, data: { code: `KB-2222-${String(2222 + i).replace(/[01]/g, '2')}` } })).status());
    }
    expect(statuses, 'gift-card code entry is rate-limited').toContain(429);
  });
});

test.describe('A05 malformed input is a client error, not a server error', () => {
  test('a NUL byte in a search term or an e-mail returns 400, never 500', async ({ playwright, baseURL }) => {
    const anon = await playwright.request.newContext({ baseURL });
    expect((await anon.get('/api/v1/establishments?q=a%00b')).status(), 'search with NUL').toBe(400);
    expect((await anon.post('/api/v1/auth/forgot-password', { data: { email: 'a\u0000@example.com' } })).status(), 'forgot-password with NUL').toBe(400);
    await anon.dispose();
  });
});

test.describe('A07 session and privilege revocation (fresh context per test)', () => {
  test('a suspended account loses access on its next request', async ({ playwright, baseURL }) => {
    const ctx = await playwright.request.newContext({ baseURL });
    const victim = await createAccount(ctx, 'Client');
    expect((await ctx.get('/api/v1/me', { headers: victim.auth })).status()).toBe(200);
    expect((await request.post(`/api/v1/admin/users/${victim.userId}/suspend`, { headers: admin.auth, data: { reason: 'Fraude constatee' } })).ok()).toBeTruthy();
    expect((await ctx.get('/api/v1/me', { headers: victim.auth })).status(), 'old access token after suspension').toBe(401);
    await ctx.dispose();
  });

  test('a signed-out session cannot be reused', async ({ playwright, baseURL }) => {
    const ctx = await playwright.request.newContext({ baseURL });
    const victim = await createAccount(ctx, 'Client');
    await ctx.post('/api/v1/auth/logout', { headers: victim.auth });
    expect((await ctx.get('/api/v1/me', { headers: victim.auth })).status(), 'access token after logout').toBe(401);
    await ctx.dispose();
  });

  test('a revoked administrator loses admin rights on the next request', async ({ playwright, baseURL }) => {
    const ctx = await playwright.request.newContext({ baseURL });
    const ex = await createAccount(ctx, 'Client', 'Admin');
    expect((await ctx.get('/api/v1/admin/users?page=1&pageSize=1', { headers: ex.auth })).status()).toBe(200);
    sql(`UPDATE identity.users SET roles = ARRAY['Client'] WHERE id = '${ex.userId}'`);
    expect((await ctx.get('/api/v1/admin/users?page=1&pageSize=1', { headers: ex.auth })).status(), 'old token after the admin role was removed').toBe(403);
    await ctx.dispose();
  });

  test('session cookies are HttpOnly and SameSite', async ({ playwright, baseURL }) => {
    const ctx = await playwright.request.newContext({ baseURL });
    const victim = await createAccount(ctx, 'Client');
    const login = await ctx.post('/api/v1/auth/login', { data: { email: victim.email, password: victim.password } });
    const { challengeToken } = await login.json();
    const verified = await ctx.post('/api/v1/auth/2fa/verify', { data: { challengeToken, code: await freshCode(victim.secret), rememberDevice: true } });
    const cookies = verified.headersArray().filter((h) => h.name.toLowerCase() === 'set-cookie').map((h) => h.value);
    expect(cookies.length).toBeGreaterThan(0);
    for (const cookie of cookies) {
      expect(cookie.toLowerCase(), cookie).toContain('httponly');
      expect(cookie.toLowerCase(), cookie).toMatch(/samesite=(strict|lax)/);
    }
    await ctx.dispose();
  });
});

test.describe('A03 injection in exports', () => {
  test('a service name starting with a formula is neutralised in the accounting CSV', async () => {
    const base = `/api/v1/pro/establishments/${salonA.id}`;
    const detail = await (await request.get(base, { headers: salonA.owner.auth })).json();
    const created = await request.post(`${base}/services`, {
      headers: salonA.owner.auth,
      data: { name: '=HYPERLINK("https://evil.example","Cliquez")', description: null, price: 5000, priceType: 'Fixed', durationMinutes: 30, categoryId: detail.categoryIds[0], groupId: null, isBookable: true, isVisible: true },
    });
    expect(created.ok(), await created.text()).toBeTruthy();
    const serviceId = (await created.json()).id;
    const day = new Date();
    day.setUTCDate(day.getUTCDate() + 10);
    while (day.getUTCDay() === 0) day.setUTCDate(day.getUTCDate() + 1);
    day.setUTCHours(11, 0, 0, 0);
    await request.post('/api/v1/appointments', { headers: client.auth, data: { establishmentId: salonA.id, serviceIds: [serviceId], resourceId: null, startsAt: day.toISOString(), phone: PHONE, note: null } });
    const csv = await (await request.get(`${base}/finances.csv?from=${encodeURIComponent(inDays(-1))}&to=${encodeURIComponent(inDays(30))}`, { headers: salonA.owner.auth })).text();
    expect(csv).toContain('HYPERLINK');
    expect(csv, 'the formula must be neutralised (prefixed), never left live').not.toMatch(/(^|[;,"])=HYPERLINK/m);
  });
});
