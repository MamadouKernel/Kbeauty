import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { providePrimeNG } from 'primeng/config';
import { vi } from 'vitest';
import { AuthService } from '../../core/auth/auth.service';
import { Login } from './login/login';
import { Register } from './register/register';
import { TwoFactor } from './two-factor/two-factor';

/** Random for each run: no password is written in the repository. Meets the rules (length, a letter, a digit). */
const PASSWORD = `Zx-${crypto.randomUUID().slice(0, 12)}7`;

/** A throw-away authenticator secret (base32), random for each run. */
const TOTP_SECRET = Array.from(
  crypto.getRandomValues(new Uint8Array(16)),
  (b) => 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'[b % 32],
).join('');

describe('authentication pages', () => {
  let http: HttpTestingController;
  let router: Router;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ path: 'inscription', component: Register }]),
        provideHttpClient(),
        provideHttpClientTesting(),
        providePrimeNG(),
      ],
    });
    http = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
  });

  const eventually = (fixture: ComponentFixture<unknown>, assertion: () => void) =>
    vi.waitFor(() => {
      fixture.detectChanges();
      assertion();
    });

  function type(root: HTMLElement, selector: string, value: string): void {
    const input = root.querySelector<HTMLInputElement>(selector)!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
  }

  describe('Login', () => {
    it('refuses to submit an empty form and explains why', async () => {
      const fixture = TestBed.createComponent(Login);
      fixture.detectChanges();
      const el = fixture.nativeElement as HTMLElement;

      el.querySelector<HTMLButtonElement>('button[type="submit"]')!.click();

      await eventually(fixture, () =>
        expect(el.querySelectorAll('[role="alert"]').length).toBeGreaterThan(0),
      );
      http.expectNone('/api/v1/auth/login');
    });

    it('moves on to the two-factor step after a correct password', async () => {
      const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
      const fixture = TestBed.createComponent(Login);
      fixture.detectChanges();
      const el = fixture.nativeElement as HTMLElement;

      type(el, '#login-email', 'awa@example.com');
      type(el, 'input[type="password"]', PASSWORD);
      el.querySelector<HTMLButtonElement>('button[type="submit"]')!.click();

      const request = await vi.waitFor(() => http.expectOne('/api/v1/auth/login'));
      expect(request.request.body).toEqual({
        email: 'awa@example.com',
        password: PASSWORD,
      });
      request.flush({ status: 'TwoFactorRequired', challengeToken: 'c1' });

      await vi.waitFor(() => expect(navigate).toHaveBeenCalled());
      expect(navigate.mock.calls[0][0]).toEqual(['/verification']);
    });

    it('shows the server message when the credentials are wrong', async () => {
      const fixture = TestBed.createComponent(Login);
      fixture.detectChanges();
      const el = fixture.nativeElement as HTMLElement;

      type(el, '#login-email', 'awa@example.com');
      type(el, 'input[type="password"]', 'nope');
      el.querySelector<HTMLButtonElement>('button[type="submit"]')!.click();

      const request = await vi.waitFor(() => http.expectOne('/api/v1/auth/login'));
      request.flush(
        { title: 'E-mail ou mot de passe incorrect.', code: 'auth.invalid_credentials' },
        { status: 401, statusText: 'Unauthorized' },
      );

      await eventually(fixture, () =>
        expect(el.querySelector('.form-error')?.textContent).toContain('incorrect'),
      );
    });

    it('offers Google sign-in only when the API enables it, and never WhatsApp', async () => {
      const fixture = TestBed.createComponent(Login);
      fixture.detectChanges();
      const request = await vi.waitFor(() => http.expectOne('/api/v1/auth/providers'));
      request.flush({
        password: true,
        google: { enabled: false, clientId: null },
        whatsApp: { enabled: false, clientId: null },
      });

      await eventually(fixture, () => expect(fixture.componentInstance).toBeTruthy());
      const el = fixture.nativeElement as HTMLElement;
      expect(el.querySelector('app-google-sign-in')).toBeNull();
      expect(el.textContent?.toLowerCase()).not.toContain('whatsapp');
    });
  });

  describe('Register', () => {
    it('preselects the professional account type from the link', async () => {
      const harness = await RouterTestingHarness.create();
      await harness.navigateByUrl('/inscription?type=partenaire', Register);
      harness.detectChanges();
      const root = harness.routeNativeElement as HTMLElement;

      expect(root.querySelector<HTMLInputElement>('input[value="PartnerOwner"]')!.checked).toBe(
        true,
      );
      expect(root.querySelector<HTMLInputElement>('input[value="Client"]')!.checked).toBe(false);
    });

    it('refuses two different passwords and sends nothing', async () => {
      const fixture = TestBed.createComponent(Register);
      fixture.detectChanges();
      const el = fixture.nativeElement as HTMLElement;

      type(el, '#reg-first', 'Awa');
      type(el, '#reg-last', 'Koné');
      type(el, '#reg-email', 'awa@example.com');
      type(el, '#reg-password', PASSWORD);
      type(el, '#reg-confirm', `${PASSWORD}x`);
      el.querySelector<HTMLInputElement>('input[type="checkbox"]')!.click();
      el.querySelector<HTMLButtonElement>('button[type="submit"]')!.click();

      await eventually(fixture, () =>
        expect(el.textContent).toContain('Les deux mots de passe ne sont pas identiques.'),
      );
      http.expectNone('/api/v1/auth/register');
    });

    it('sends the registration and then asks the user to check their mailbox', async () => {
      const fixture = TestBed.createComponent(Register);
      fixture.detectChanges();
      const el = fixture.nativeElement as HTMLElement;

      type(el, '#reg-first', 'Awa');
      type(el, '#reg-last', 'Koné');
      type(el, '#reg-email', 'awa@example.com');
      type(el, '#reg-password', PASSWORD);
      type(el, '#reg-confirm', PASSWORD);
      el.querySelector<HTMLInputElement>('input[type="checkbox"]')!.click();
      el.querySelector<HTMLButtonElement>('button[type="submit"]')!.click();

      const request = await vi.waitFor(() => http.expectOne('/api/v1/auth/register'));
      expect(request.request.body).toMatchObject({
        email: 'awa@example.com',
        firstName: 'Awa',
        lastName: 'Koné',
        role: 'Client',
        acceptTerms: true,
      });
      expect(request.request.body).not.toHaveProperty('confirmPassword');
      request.flush(null, { status: 202, statusText: 'Accepted' });

      await eventually(fixture, () =>
        expect(el.textContent).toContain('Vérifiez votre boîte mail'),
      );
    });
    it('does not send anything until the terms are accepted', async () => {
      const fixture = TestBed.createComponent(Register);
      fixture.detectChanges();
      const el = fixture.nativeElement as HTMLElement;

      type(el, '#reg-first', 'Awa');
      type(el, '#reg-last', 'Koné');
      type(el, '#reg-email', 'awa@example.com');
      type(el, 'input[type="password"]', PASSWORD);
      el.querySelector<HTMLButtonElement>('button[type="submit"]')!.click();

      await eventually(fixture, () => expect(el.textContent).toContain('accepter les conditions'));
      http.expectNone('/api/v1/auth/register');
    });
  });

  describe('TwoFactor', () => {
    it('sends visitors without a pending challenge back to the sign-in page', () => {
      const navigate = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);

      TestBed.createComponent(TwoFactor);

      expect(navigate).toHaveBeenCalledWith('/connexion');
    });

    it('asks for the authenticator code when two-factor is already enabled', async () => {
      TestBed.inject(AuthService).challenge.set({ status: 'TwoFactorRequired', token: 'c1' });
      const navigate = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
      const fixture = TestBed.createComponent(TwoFactor);
      fixture.detectChanges();
      const el = fixture.nativeElement as HTMLElement;

      expect(el.querySelector('h1')?.textContent).toContain('Entrez votre code');
      type(el, '#twofa-code', '123456');
      el.querySelector<HTMLButtonElement>('button[type="submit"]')!.click();

      const request = await vi.waitFor(() => http.expectOne('/api/v1/auth/2fa/verify'));
      expect(request.request.body).toEqual({ challengeToken: 'c1', code: '123456' });
      request.flush({
        accessToken: 'a',
        expiresAt: '2026-10-11T12:15:00Z',
        user: {
          id: 'u',
          email: 'awa@example.com',
          firstName: 'Awa',
          lastName: 'K',
          roles: ['Client'],
          twoFactorEnabled: true,
        },
        recoveryCodes: null,
      });

      await vi.waitFor(() => expect(navigate).toHaveBeenCalledWith('/compte'));
    });

    it('walks through enrolment: shows the key, then the one-time recovery codes', async () => {
      TestBed.inject(AuthService).challenge.set({ status: 'TwoFactorSetupRequired', token: 'c1' });
      const fixture = TestBed.createComponent(TwoFactor);
      fixture.detectChanges();
      const el = fixture.nativeElement as HTMLElement;

      const setup = await vi.waitFor(() => http.expectOne('/api/v1/auth/2fa/setup'));
      setup.flush({
        secret: TOTP_SECRET,
        otpauthUri: `otpauth://totp/Keke:awa?secret=${TOTP_SECRET}`,
      });
      await eventually(fixture, () =>
        expect(el.querySelector('code')?.textContent).toContain(
          TOTP_SECRET.match(/.{1,4}/g)!.join(' '),
        ),
      );

      type(el, '#twofa-code', '654321');
      el.querySelector<HTMLButtonElement>('button[type="submit"]')!.click();
      const enable = await vi.waitFor(() => http.expectOne('/api/v1/auth/2fa/enable'));
      enable.flush({
        accessToken: 'a',
        expiresAt: '2026-10-11T12:15:00Z',
        user: {
          id: 'u',
          email: 'awa@example.com',
          firstName: 'Awa',
          lastName: 'K',
          roles: ['Client'],
          twoFactorEnabled: true,
        },
        recoveryCodes: ['AAAAA-BBBBB', 'CCCCC-DDDDD'],
      });

      await eventually(fixture, () => expect(el.textContent).toContain('AAAAA-BBBBB'));
      const proceed = el.querySelector<HTMLButtonElement>('button.btn-primary')!;
      expect(proceed.disabled).toBe(true);
    });

    it('auto-completes when the challenge is TrustedDevice', async () => {
      TestBed.inject(AuthService).challenge.set({ status: 'TrustedDevice', token: 'td-1' });
      const navigate = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
      const fixture = TestBed.createComponent(TwoFactor);
      fixture.detectChanges();

      const request = await vi.waitFor(() => http.expectOne('/api/v1/auth/2fa/trusted'));
      expect(request.request.body).toEqual({ challengeToken: 'td-1' });
      request.flush({
        accessToken: 'a',
        expiresAt: '2026-10-11T12:15:00Z',
        user: {
          id: 'u',
          email: 'awa@example.com',
          firstName: 'Awa',
          lastName: 'K',
          roles: ['Client'],
          twoFactorEnabled: true,
        },
        recoveryCodes: null,
      });

      await vi.waitFor(() => expect(navigate).toHaveBeenCalledWith('/compte'));
    });

    it('sends rememberDevice: true when the 30-day checkbox is ticked', async () => {
      TestBed.inject(AuthService).challenge.set({ status: 'TwoFactorRequired', token: 'c1' });
      const navigate = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
      const fixture = TestBed.createComponent(TwoFactor);
      fixture.detectChanges();
      const el = fixture.nativeElement as HTMLElement;

      type(el, '#twofa-code', '123456');
      el.querySelector<HTMLInputElement>('#twofa-remember')!.click();
      el.querySelector<HTMLButtonElement>('button[type="submit"]')!.click();

      const request = await vi.waitFor(() => http.expectOne('/api/v1/auth/2fa/verify'));
      expect(request.request.body).toEqual({
        challengeToken: 'c1',
        code: '123456',
        rememberDevice: true,
      });
      request.flush({
        accessToken: 'a',
        expiresAt: '2026-10-11T12:15:00Z',
        user: {
          id: 'u',
          email: 'awa@example.com',
          firstName: 'Awa',
          lastName: 'K',
          roles: ['Client'],
          twoFactorEnabled: true,
        },
        recoveryCodes: null,
      });

      await vi.waitFor(() => expect(navigate).toHaveBeenCalledWith('/compte'));
    });
  });
});
