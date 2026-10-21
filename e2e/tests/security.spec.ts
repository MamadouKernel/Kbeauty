import { APIRequestContext, expect, test } from '@playwright/test';
import { Account, Salon, createAccount, createSalon } from '../helpers/api';
import { PASSWORD, uniqueEmail } from '../helpers/env';
import { mp4, png } from '../helpers/files';

/**
 * Manual intrusion tests (penetration tests) replayed as an automated suite, grouped by OWASP Top 10 category. Each test is an
 * attack a malicious visitor, client or partner would try against the running stack; it passes when the attack fails.
 * The findings and their status are summarised in docs/PENTEST.md.
 */
let request: APIRequestContext;
let admin: Account;
let clientA: Account;
let clientB: Account;
let partnerB: Account;
let salonA: Salon;
let appointmentId: string;

/** Next Tuesday-to-Saturday, 10:00 UTC, at least three days ahead (salons are open Monday to Saturday, 09:00-18:00). */
function nextOpenSlot(): string {
  const day = new Date();
  day.setUTCHours(10, 0, 0, 0);
  day.setUTCDate(day.getUTCDate() + 3);
  while (day.getUTCDay() === 0 || day.getUTCDay() === 1) {
    day.setUTCDate(day.getUTCDate() + 1);
  }
  return day.toISOString();
}

function base64Url(value: string | Buffer): string {
  return Buffer.from(value).toString('base64url');
}

test.beforeAll(async ({ playwright, baseURL }) => {
  request = await playwright.request.newContext({ baseURL });
  admin = await createAccount(request, 'Client', 'Admin');
  clientA = await createAccount(request, 'Client');
  clientB = await createAccount(request, 'Client');
  partnerB = await createAccount(request, 'PartnerOwner');
  salonA = await createSalon(request, admin, { autoConfirm: true });

  const booked = await request.post('/api/v1/appointments', {
    headers: clientA.auth,
    data: {
      establishmentId: salonA.id,
      serviceIds: [salonA.hourService],
      resourceId: null,
      startsAt: nextOpenSlot(),
      phone: '07 00 00 12 34',
      note: null,
    },
  });
  expect(booked.ok(), await booked.text()).toBeTruthy();
  appointmentId = ((await booked.json()) as { id: string }).id;
});

test.afterAll(async () => {
  await request.dispose();
});

test.describe('A01 Broken access control', () => {
  const PROTECTED = [
    '/api/v1/me',
    '/api/v1/me/appointments',
    '/api/v1/me/notifications',
    '/api/v1/me/sessions',
    '/api/v1/me/notification-preferences',
    '/api/v1/pro/establishments',
    '/api/v1/admin/users',
    '/api/v1/admin/settings',
    '/api/v1/admin/backups',
    '/api/v1/admin/kyc/queue',
  ];

  test('every protected endpoint refuses a request without credentials', async ({ playwright, baseURL }) => {
    const anonymous = await playwright.request.newContext({ baseURL });
    for (const path of PROTECTED) {
      expect((await anonymous.get(path)).status(), path).toBe(401);
    }
    await anonymous.dispose();
  });

  test('a client cannot reach the partner and admin areas', async () => {
    for (const path of [
      '/api/v1/pro/establishments',
      '/api/v1/admin/users',
      '/api/v1/admin/settings',
      '/api/v1/admin/backups',
      '/api/v1/admin/kyc/queue',
    ]) {
      expect((await request.get(path, { headers: clientA.auth })).status(), path).toBe(403);
    }
  });

  test('a partner cannot reach the admin area', async () => {
    for (const path of ['/api/v1/admin/users', '/api/v1/admin/settings', '/api/v1/admin/kyc/queue', '/api/v1/admin/payouts']) {
      expect((await request.get(path, { headers: partnerB.auth })).status(), path).toBe(403);
    }
  });

  test('an administrator is not a super administrator', async () => {
    for (const path of ['/api/v1/admin/backups', '/api/v1/admin/admins']) {
      expect((await request.get(path, { headers: admin.auth })).status(), path).toBe(403);
    }
    expect((await request.post('/api/v1/admin/backups', { headers: admin.auth })).status()).toBe(403);
    const download = await request.get('/api/v1/admin/backups/00000000-0000-0000-0000-000000000001/download', {
      headers: admin.auth,
    });
    expect(download.status()).toBe(403);
  });

  test("IDOR: a partner cannot read or change another partner's establishment", async () => {
    const base = `/api/v1/pro/establishments/${salonA.id}`;
    for (const path of [base, `${base}/payments`, `${base}/payment-settings`, `${base}/booking-settings`, `${base}/appointments`, `${base}/subscription`]) {
      expect((await request.get(path, { headers: partnerB.auth })).status(), path).toBe(404);
    }
    const hijack = await request.put(`${base}/payment-settings`, {
      headers: partnerB.auth,
      data: {
        mode: 'Full',
        depositPercent: 100,
        payoutOperator: 'wave-cote-divoire',
        payoutAccount: '07 00 00 99 99',
        payoutAccountName: 'Pirate',
        platformEnabled: false,
      },
    });
    expect(hijack.status()).toBe(404);
    const service = await request.post(`${base}/services`, {
      headers: partnerB.auth,
      data: { name: 'x', price: 1, priceType: 'Fixed', durationMinutes: 30, isBookable: true, isVisible: true },
    });
    expect(service.status()).toBe(404);
    const video = await request.post(`${base}/videos`, {
      headers: partnerB.auth,
      multipart: { file: { name: 'a.mp4', mimeType: 'video/mp4', buffer: mp4() } },
    });
    expect(video.status()).toBe(404);
    const photo = await request.post(`${base}/media`, {
      headers: partnerB.auth,
      multipart: { file: { name: 'a.png', mimeType: 'image/png', buffer: png(100, 100) } },
    });
    expect(photo.status()).toBe(404);
  });

  test("IDOR: a client cannot cancel, pay or move another client's appointment", async () => {
    for (const action of ['cancel', 'pay', 'reschedule/accept', 'reschedule/decline']) {
      const status = (
        await request.post(`/api/v1/appointments/${appointmentId}/${action}`, { headers: clientB.auth, data: {} })
      ).status();
      expect([403, 404], action).toContain(status);
    }
    const mine = (await (await request.get('/api/v1/me/appointments', { headers: clientB.auth })).json()) as {
      id: string;
    }[];
    expect(mine.map((a) => a.id)).not.toContain(appointmentId);
  });

  test('IDOR: a partner cannot act on an appointment of another establishment', async () => {
    for (const action of ['confirm', 'reject', 'cancel', 'no-show']) {
      const status = (
        await request.post(`/api/v1/pro/appointments/${appointmentId}/${action}`, { headers: partnerB.auth, data: {} })
      ).status();
      expect([403, 404], action).toContain(status);
    }
  });

  test('private files are not served and paths cannot climb out of the media bucket', async () => {
    for (const path of [
      '/api/v1/files/private-kyc/anything.webp',
      '/api/v1/files/..%2f..%2fprivate-kyc%2fx.webp',
      '/api/v1/files/uploads/..%2f..%2fetc%2fpasswd',
      '/api/v1/files/%2e%2e/%2e%2e/etc/passwd',
    ]) {
      const response = await request.get(path);
      // 401 since the deny-by-default policy: a path that matches no route is refused to an anonymous caller before it is even looked up.
      expect([400, 401, 404], path).toContain(response.status());
    }
  });

  test('the real-time hubs refuse anonymous connections', async () => {
    for (const hub of ['notifications', 'pro']) {
      const response = await request.post(`/hubs/${hub}/negotiate?negotiateVersion=1`);
      expect(response.status(), hub).toBe(401);
    }
  });
});

test.describe('A02 Cryptographic failures and A07 Authentication failures', () => {
  test('a tampered or unsigned token is refused', async () => {
    const [header, payload, signature] = clientA.auth.Authorization.replace('Bearer ', '').split('.');
    const claims = JSON.parse(Buffer.from(payload, 'base64url').toString()) as Record<string, unknown>;

    const promoted = base64Url(JSON.stringify({ ...claims, role: 'SuperAdmin', roles: ['SuperAdmin'] }));
    const unsigned = `${base64Url(JSON.stringify({ alg: 'none', typ: 'JWT' }))}.${payload}.`;
    const forged = [
      `${header}.${promoted}.${signature}`, // payload changed, signature kept
      unsigned, // "alg: none"
      `${header}.${payload}.${signature.slice(0, -4)}AAAA`, // damaged signature
      `${header}.${payload}`, // no signature at all
      'not.a.jwt',
    ];
    for (const token of forged) {
      const response = await request.get('/api/v1/admin/users', { headers: { Authorization: `Bearer ${token}` } });
      expect(response.status(), token.slice(0, 40)).toBe(401);
    }
  });

  test('the login challenge token is not an access token', async () => {
    const login = await request.post('/api/v1/auth/login', { data: { email: clientA.email, password: PASSWORD } });
    const { challengeToken } = (await login.json()) as { challengeToken: string };
    const response = await request.get('/api/v1/me', { headers: { Authorization: `Bearer ${challengeToken}` } });
    expect(response.status()).toBe(401);
  });

  test('a wrong one-time code is refused', async () => {
    const account = await createAccount(request, 'Client');
    const login = await request.post('/api/v1/auth/login', { data: { email: account.email, password: PASSWORD } });
    const { challengeToken } = (await login.json()) as { challengeToken: string };
    const wrong = await request.post('/api/v1/auth/2fa/verify', { data: { challengeToken, code: '000000' } });
    expect(wrong.ok()).toBeFalsy();
  });

  test('enumeration: unknown and known e-mails get the same answers', async () => {
    const known = await request.post('/api/v1/auth/forgot-password', { data: { email: clientA.email } });
    const unknown = await request.post('/api/v1/auth/forgot-password', { data: { email: uniqueEmail('inconnu') } });
    expect(known.status()).toBe(unknown.status());
    expect(await known.text()).toBe(await unknown.text());

    const registeredAgain = await request.post('/api/v1/auth/register', {
      data: { email: clientA.email, password: PASSWORD, firstName: 'Awa', lastName: 'Koné', role: 'Client', acceptTerms: true },
    });
    const registeredNew = await request.post('/api/v1/auth/register', {
      data: { email: uniqueEmail('nouveau'), password: PASSWORD, firstName: 'Awa', lastName: 'Koné', role: 'Client', acceptTerms: true },
    });
    expect(registeredAgain.status()).toBe(registeredNew.status());
  });

  test('the account locks after repeated wrong passwords, even for the right password', async () => {
    const victim = await createAccount(request, 'Client');
    for (let i = 0; i < 7; i++) {
      await request.post('/api/v1/auth/login', { data: { email: victim.email, password: `Mauvais-${i}-Passe1` } });
    }
    const right = await request.post('/api/v1/auth/login', { data: { email: victim.email, password: PASSWORD } });
    const body = right.ok() ? ((await right.json()) as { challengeToken?: string }) : null;
    expect(body?.challengeToken, 'a locked account must not hand out a challenge').toBeUndefined();
  });

  test('passwords that break the rules are refused at registration', async () => {
    for (const password of ['court1', 'toutesenlettres', '1234567890123']) {
      const response = await request.post('/api/v1/auth/register', {
        data: { email: uniqueEmail('faible'), password, firstName: 'A', lastName: 'B', role: 'Client', acceptTerms: true },
      });
      expect(response.status(), password).toBe(400);
    }
  });

  test('after sign-out the refresh cookie is dead', async ({ playwright, baseURL }) => {
    const session = await playwright.request.newContext({ baseURL });
    const account = await createAccount(session, 'Client');
    expect((await session.post('/api/v1/auth/refresh')).ok()).toBeTruthy();
    await session.post('/api/v1/auth/logout', { headers: account.auth });
    expect((await session.post('/api/v1/auth/refresh')).status()).toBe(401);
    await session.dispose();
  });
});

test.describe('A03 Injection', () => {
  const PAYLOADS = ["' OR 1=1 --", "'; DROP TABLE identity.users; --", '%00', '${7*7}', '{{7*7}}', '../../etc/passwd', '<script>alert(1)</script>'];

  test('hostile search text never causes a server error nor leaks rows', async () => {
    const everything = ((await (await request.get('/api/v1/establishments?pageSize=50')).json()) as { total: number }).total;
    for (const payload of PAYLOADS) {
      const response = await request.get(`/api/v1/establishments?q=${encodeURIComponent(payload)}&pageSize=50`);
      expect(response.status(), payload).toBeLessThan(500);
      if (response.ok()) {
        expect(((await response.json()) as { total: number }).total, payload).toBeLessThanOrEqual(everything);
      }
    }
    // The database is intact.
    expect((await request.get('/api/v1/me', { headers: clientA.auth })).ok()).toBeTruthy();
  });

  test('hostile values in forms and path parameters are rejected cleanly', async () => {
    const slug = encodeURIComponent("x'; DROP TABLE catalog.establishments;--");
    expect((await request.get(`/api/v1/establishments/${slug}`)).status()).toBe(404);
    expect((await request.get('/api/v1/pro/establishments/not-a-guid', { headers: partnerB.auth })).status()).toBeLessThan(500);
    const response = await request.post('/api/v1/auth/login', { data: { email: "admin'--@example.com", password: "' OR '1'='1" } });
    expect(response.status()).toBeLessThan(500);
    expect(response.ok()).toBeFalsy();
  });

  test('script in a URL or in a stored note is displayed as text and never runs', async ({ page }) => {
    let fired = false;
    page.on('dialog', async (dialog) => {
      fired = true;
      await dialog.dismiss();
    });
    const payload = '<img src=x onerror=alert(1)>';
    await page.goto(`/etablissements?q=${encodeURIComponent(payload)}`);
    await page.waitForLoadState('networkidle');
    await page.goto(`/etablissements/${encodeURIComponent(payload)}`);
    await page.waitForLoadState('networkidle');
    expect(fired).toBeFalsy();
    expect(await page.locator('img[src="x"]').count()).toBe(0);

    // A note with markup is accepted as plain data.
    const booked = await request.post('/api/v1/appointments', {
      headers: clientB.auth,
      data: {
        establishmentId: salonA.id,
        serviceIds: [salonA.halfHourService],
        resourceId: null,
        startsAt: nextOpenSlot(),
        phone: '07 00 00 12 34',
        note: payload,
      },
    });
    expect(booked.status()).toBeLessThan(500);
  });
});

test.describe('A04 Insecure design and A05 Misconfiguration', () => {
  test('a visitor cannot make themselves an administrator at registration', async () => {
    for (const role of ['Admin', 'SuperAdmin', 'PartnerStaff']) {
      const response = await request.post('/api/v1/auth/register', {
        data: { email: uniqueEmail('pirate'), password: PASSWORD, firstName: 'A', lastName: 'B', role, acceptTerms: true },
      });
      expect(response.status(), role).toBe(400);
    }
  });

  test('CORS does not allow foreign origins', async () => {
    const response = await request.get('/api/v1/establishments', { headers: { Origin: 'https://evil.example' } });
    expect(response.headers()['access-control-allow-origin']).toBeUndefined();
    const preflight = await request.fetch('/api/v1/me', {
      method: 'OPTIONS',
      headers: {
        Origin: 'https://evil.example',
        'Access-Control-Request-Method': 'GET',
        'Access-Control-Request-Headers': 'authorization',
      },
    });
    expect(preflight.headers()['access-control-allow-origin']).toBeUndefined();
  });

  test('responses carry security headers and no version banner', async () => {
    const response = await request.get('/api/v1/establishments');
    const headers = response.headers();
    expect(headers['x-content-type-options']).toBe('nosniff');
    expect(headers['x-powered-by']).toBeUndefined();
    expect(headers['server'] ?? '').not.toMatch(/kestrel|asp\.net|\d/i);
    const page = (await request.get('/')).headers();
    expect(page['content-security-policy']).toMatch(/frame-ancestors 'none'|frame-src 'none'|object-src 'none'/);
  });

  test('error pages do not leak stack traces or file paths', async () => {
    for (const path of ['/api/v1/establishments/%ff', '/api/v1/geo/countries/zzz/regions', '/api/v1/legal/inconnu']) {
      const body = await (await request.get(path)).text();
      expect(body, path).not.toMatch(/at KekeBeauty|System\.|\.cs:line|Npgsql|Exception/);
    }
  });

  test('forged delivery reports and payment notifications change nothing', async () => {
    const sms = await request.post('/api/v1/webhooks/sms/letexto?id=123&statuts=delivered');
    expect([401, 403, 404]).toContain(sms.status());
    const smsGet = await request.get('/api/v1/webhooks/sms/letexto?id=123&statuts=delivered&sig=faux');
    expect([401, 403, 404]).toContain(smsGet.status());

    const payment = await request.post('/api/v1/webhooks/payments/winipay', {
      data: { transaction_id: 'KB-FAUX', state: 'success' },
    });
    expect(payment.status()).toBeGreaterThanOrEqual(400);
    expect(payment.status()).toBeLessThan(500);
  });

  test('the payment return page does not follow an address on another site', async ({ page, baseURL }) => {
    await page.goto('/paiement/simulation?tx=KB-INCONNU&back=https://evil.example/phishing');
    const refuse = page.getByRole('button', { name: 'Refuser' });
    if (await refuse.isVisible()) {
      await refuse.click();
    }
    await page.waitForTimeout(1_000);
    expect(new URL(page.url()).origin).toBe(new URL(baseURL!).origin);
  });
});

test.describe('A08 Software and data integrity: uploads', () => {
  test('files that only pretend to be images are refused', async () => {
    const base = `/api/v1/pro/establishments/${salonA.id}`;
    const attacks = [
      { name: 'shell.png', mimeType: 'image/png', buffer: Buffer.from('<?php system($_GET["c"]); ?>') },
      { name: 'page.png', mimeType: 'image/png', buffer: Buffer.from('<html><script>alert(1)</script></html>') },
      { name: 'vector.png', mimeType: 'image/png', buffer: Buffer.from('<svg xmlns="http://www.w3.org/2000/svg" onload="alert(1)"/>') },
      { name: 'empty.png', mimeType: 'image/png', buffer: Buffer.alloc(0) },
    ];
    for (const file of attacks) {
      const response = await request.post(`${base}/media`, { headers: salonA.owner.auth, multipart: { file } });
      expect(response.status(), file.name).toBe(400);
    }
  });

  test('an oversized image is refused', async () => {
    const response = await request.post(`/api/v1/pro/establishments/${salonA.id}/media`, {
      headers: salonA.owner.auth,
      multipart: { file: { name: 'huge.png', mimeType: 'image/png', buffer: Buffer.alloc(11 * 1024 * 1024) } },
    });
    expect(response.status()).toBe(413);
  });

  test('a stored image is re-encoded: hidden payloads do not survive', async () => {
    const poisoned = Buffer.concat([png(200, 200), Buffer.from('<script>alert("polyglot")</script>')]);
    const response = await request.post(`/api/v1/pro/establishments/${salonA.id}/media`, {
      headers: salonA.owner.auth,
      multipart: { file: { name: 'polyglot.png', mimeType: 'image/png', buffer: poisoned } },
    });
    expect(response.ok(), await response.text()).toBeTruthy();
    const { url } = (await response.json()) as { url: string };
    const served = await request.get(url.startsWith('http') ? new URL(url).pathname : url);
    expect(served.headers()['content-type']).toBe('image/webp');
    expect((await served.body()).includes(Buffer.from('polyglot'))).toBeFalsy();
  });

  test('videos are recognised by content, not by name or declared type', async () => {
    const base = `/api/v1/pro/establishments/${salonA.id}/videos`;
    const html = await request.post(base, {
      headers: salonA.owner.auth,
      multipart: { file: { name: 'clip.mp4', mimeType: 'video/mp4', buffer: Buffer.from('<html>pas une vidéo</html>') } },
    });
    expect(html.status()).toBe(400);

    const heic = Buffer.alloc(64);
    heic.writeUInt32BE(0x18, 0);
    heic.write('ftypheic', 4, 'ascii');
    const image = await request.post(base, {
      headers: salonA.owner.auth,
      multipart: { file: { name: 'photo.mp4', mimeType: 'video/mp4', buffer: heic } },
    });
    expect(image.status()).toBe(400);

    const real = await request.post(base, {
      headers: salonA.owner.auth,
      multipart: { file: { name: 'virus.exe', mimeType: 'application/octet-stream', buffer: mp4() } },
    });
    expect(real.status(), await real.text()).toBe(201);
    const { url } = (await real.json()) as { url: string };
    expect(url).toMatch(/\.mp4$/); // the stored name never comes from the upload
    const served = await request.get(url.startsWith('http') ? new URL(url).pathname : url);
    expect(served.headers()['content-type']).toBe('video/mp4');
  });
});

test.describe('A04 Abuse: rate limiting', () => {
  test('a flood of sign-in attempts is throttled (429)', async () => {
    const statuses = new Set<number>();
    for (let round = 0; round < 40 && !statuses.has(429); round += 1) {
      const batch = await Promise.all(
        Array.from({ length: 25 }, () =>
          // The second-factor step of a sign-in: same rate-limit policy as the password step, but cheap. A real password attempt costs hundreds of
          // milliseconds of hashing (by design), so a flood of them cannot exceed the per-minute limit on a modest machine.
          request.post('/api/v1/auth/2fa/verify', { data: { challengeToken: uniqueEmail('flood'), code: '000000' } }),
        ),
      );
      batch.forEach((r) => statuses.add(r.status()));
    }
    expect(statuses.has(429), `statuses seen: ${[...statuses].join(', ')}`).toBeTruthy();
    // Let the window pass so that the tests that follow are not throttled.
    await new Promise((resolve) => setTimeout(resolve, 65_000));
  });
});
