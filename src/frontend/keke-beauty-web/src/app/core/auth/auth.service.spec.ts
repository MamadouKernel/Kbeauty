import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import {
  ActivatedRouteSnapshot,
  Router,
  RouterStateSnapshot,
  UrlTree,
  provideRouter,
} from '@angular/router';
import { vi } from 'vitest';
import { AuthResponse } from './auth.models';
import { authGuard, guestGuard, roleGuard, safeReturnUrl } from './auth.guards';
import { authInterceptor } from './auth.interceptor';
import { AuthService } from './auth.service';

/** Random for each run: no password is written in the repository. Meets the rules (length, a letter, a digit). */
const PASSWORD = `Zx-${crypto.randomUUID().slice(0, 12)}7`;

const SESSION: AuthResponse = {
  accessToken: 'access-1',
  expiresAt: '2026-10-11T12:15:00Z',
  user: {
    id: 'u1',
    email: 'awa@example.com',
    firstName: 'Awa',
    lastName: 'Koné',
    roles: ['Client'],
    twoFactorEnabled: true,
  },
  recoveryCodes: null,
};

describe('AuthService', () => {
  let auth: AuthService;
  let http: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    auth = TestBed.inject(AuthService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    // AuthService exposes an httpResource for /auth/providers that issues its request as soon as it is created.
    http.match('/api/v1/auth/providers');
    http.verify();
  });

  async function signIn(): Promise<void> {
    const refreshed = auth.refresh();
    http.expectOne('/api/v1/auth/refresh').flush(SESSION);
    await refreshed;
  }

  it('does not call the API on startup when no session was ever opened', async () => {
    await auth.restore();

    http.expectNone('/api/v1/auth/refresh');
    expect(auth.isAuthenticated()).toBe(false);
  });

  it('restores a remembered session through the refresh cookie', async () => {
    localStorage.setItem('kb_session', '1');

    const restored = auth.restore();
    http.expectOne('/api/v1/auth/refresh').flush(SESSION);
    await restored;

    expect(auth.user()?.email).toBe('awa@example.com');
    expect(auth.accessToken()).toBe('access-1');
  });

  it('forgets the hint when the refresh cookie is no longer valid', async () => {
    localStorage.setItem('kb_session', '1');

    const restored = auth.restore();
    http
      .expectOne('/api/v1/auth/refresh')
      .flush({ title: 'expired' }, { status: 401, statusText: 'Unauthorized' });
    await restored;

    expect(auth.isAuthenticated()).toBe(false);
    expect(localStorage.getItem('kb_session')).toBeNull();
  });

  it('shares one request between concurrent refreshes (rotation would otherwise revoke the session)', async () => {
    const first = auth.refresh();
    const second = auth.refresh();

    http.expectOne('/api/v1/auth/refresh').flush(SESSION);

    expect(await first).toBe(true);
    expect(await second).toBe(true);
  });

  it('keeps the two-factor challenge after a correct password but opens no session', async () => {
    const pending = auth.login('awa@example.com', PASSWORD);
    http
      .expectOne('/api/v1/auth/login')
      .flush({ status: 'TwoFactorSetupRequired', challengeToken: 'challenge-1' });

    expect(await pending).toBe('TwoFactorSetupRequired');
    expect(auth.challenge()).toEqual({ status: 'TwoFactorSetupRequired', token: 'challenge-1' });
    expect(auth.isAuthenticated()).toBe(false);
  });

  it('opens the session and exposes recovery codes once two-factor is enabled', async () => {
    auth.challenge.set({ status: 'TwoFactorSetupRequired', token: 'challenge-1' });

    const pending = auth.enableTwoFactor('123456');
    const request = http.expectOne('/api/v1/auth/2fa/enable');
    expect(request.request.body).toEqual({ challengeToken: 'challenge-1', code: '123456' });
    request.flush({ ...SESSION, recoveryCodes: ['AAAAA-BBBBB'] });
    await pending;

    expect(auth.isAuthenticated()).toBe(true);
    expect(auth.recoveryCodes()).toEqual(['AAAAA-BBBBB']);
    expect(auth.challenge()).toBeNull();
    expect(localStorage.getItem('kb_session')).toBe('1');
  });

  it('clears everything on logout', async () => {
    await signIn();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);

    const pending = auth.logout();
    http.expectOne('/api/v1/auth/logout').flush(null, { status: 204, statusText: 'No Content' });
    await pending;

    expect(auth.isAuthenticated()).toBe(false);
    expect(localStorage.getItem('kb_session')).toBeNull();
    expect(navigate).toHaveBeenCalledWith('/');
  });

  it('adds the bearer token to API calls but not to auth calls', async () => {
    await signIn();

    TestBed.inject(HttpClient).get('/api/v1/me').subscribe();
    expect(http.expectOne('/api/v1/me').request.headers.get('Authorization')).toBe(
      'Bearer access-1',
    );

    TestBed.inject(HttpClient).post('/api/v1/auth/logout', {}).subscribe();
    expect(http.expectOne('/api/v1/auth/logout').request.headers.has('Authorization')).toBe(false);
  });

  it('refreshes once on a 401 and replays the request with the new token', async () => {
    await signIn();
    let status = 0;

    TestBed.inject(HttpClient)
      .get('/api/v1/me', { observe: 'response' })
      .subscribe((response) => (status = response.status));
    http.expectOne('/api/v1/me').flush({}, { status: 401, statusText: 'Unauthorized' });

    await vi.waitFor(() =>
      http.expectOne('/api/v1/auth/refresh').flush({ ...SESSION, accessToken: 'access-2' }),
    );
    const replay = await vi.waitFor(() => http.expectOne('/api/v1/me'));
    expect(replay.request.headers.get('Authorization')).toBe('Bearer access-2');
    replay.flush({ ok: true });

    expect(status).toBe(200);
  });

  it('sends rememberDevice when requested during two-factor verification', async () => {
    auth.challenge.set({ status: 'TwoFactorRequired', token: 'challenge-td' });

    const pending = auth.verifyTwoFactor('654321', true);
    const request = http.expectOne('/api/v1/auth/2fa/verify');
    expect(request.request.body).toEqual({
      challengeToken: 'challenge-td',
      code: '654321',
      rememberDevice: true,
    });
    request.flush(SESSION);
    await pending;

    expect(auth.isAuthenticated()).toBe(true);
  });

  it('completes a trusted device challenge without asking for a TOTP code', async () => {
    auth.challenge.set({ status: 'TrustedDevice', token: 'challenge-trusted' });

    const pending = auth.completeTrustedDevice();
    const request = http.expectOne('/api/v1/auth/2fa/trusted');
    expect(request.request.body).toEqual({ challengeToken: 'challenge-trusted' });
    request.flush(SESSION);
    await pending;

    expect(auth.isAuthenticated()).toBe(true);
    expect(auth.challenge()).toBeNull();
  });

  it('requests and verifies a WhatsApp OTP sign-in code', async () => {
    const reqOtp = auth.requestWhatsAppOtp('+2250700000000');
    const r1 = http.expectOne('/api/v1/auth/whatsapp/request');
    expect(r1.request.body).toEqual({ phone: '+2250700000000' });
    r1.flush(null, { status: 202, statusText: 'Accepted' });
    await reqOtp;

    const verifyOtp = auth.verifyWhatsAppOtp('+2250700000000', '112233');
    const r2 = http.expectOne('/api/v1/auth/whatsapp/verify');
    expect(r2.request.body).toEqual({ phone: '+2250700000000', code: '112233' });
    r2.flush({ status: 'TwoFactorRequired', challengeToken: 'wa-c1' });
    expect(await verifyOtp).toBe('TwoFactorRequired');
  });
});

describe('route guards', () => {
  const route = {} as ActivatedRouteSnapshot;
  const state = { url: '/compte' } as RouterStateSnapshot;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
  });

  it('sends anonymous visitors to the sign-in page and remembers the destination', () => {
    const result = TestBed.runInInjectionContext(() => authGuard(route, state)) as UrlTree;

    expect(result.toString()).toBe('/connexion?retour=%2Fcompte');
  });

  it('lets signed-in users through and keeps them away from the guest pages', () => {
    const auth = TestBed.inject(AuthService);
    auth.updateUser(SESSION.user); // no session yet: nothing changes
    expect(auth.isAuthenticated()).toBe(false);
    (auth as unknown as { session: { set(value: AuthResponse): void } }).session.set(SESSION);

    expect(TestBed.runInInjectionContext(() => authGuard(route, state))).toBe(true);
    expect(
      (TestBed.runInInjectionContext(() => guestGuard(route, state)) as UrlTree).toString(),
    ).toBe('/compte');
  });

  it('role guard only admits the listed roles', () => {
    (
      TestBed.inject(AuthService) as unknown as { session: { set(value: AuthResponse): void } }
    ).session.set(SESSION);

    expect(TestBed.runInInjectionContext(() => roleGuard('Client')(route, state))).toBe(true);
    expect(
      (TestBed.runInInjectionContext(() => roleGuard('Admin')(route, state)) as UrlTree).toString(),
    ).toBe('/');
  });

  it.each([
    ['/compte', '/compte'],
    ['/pro/agenda', '/pro/agenda'],
    ['//evil.example', '/compte'],
    ['https://evil.example', '/compte'],
    ['javascript:alert(1)', '/compte'],
    [null, '/compte'],
  ])('only follows same-site return URLs (%s)', (candidate, expected) => {
    expect(safeReturnUrl(candidate)).toBe(expected);
  });
});
