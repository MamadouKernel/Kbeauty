import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { SwPush } from '@angular/service-worker';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { NotificationSettings } from './notification-settings';

describe('NotificationSettings', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        // Web Push only runs in the production build over HTTPS: the screen is tested with it switched off.
        { provide: SwPush, useValue: { isEnabled: false } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  async function open(email: boolean, linked = false) {
    const fixture = TestBed.createComponent(NotificationSettings);
    fixture.detectChanges();
    (await vi.waitFor(() => http.expectOne('/api/v1/me/notification-preferences'))).flush({
      push: true,
      sms: true,
      whatsApp: true,
      reminders: true,
      email,
    });
    (await vi.waitFor(() => http.expectOne('/api/v1/me/whatsapp'))).flush({
      available: linked,
      linked,
      phoneMasked: linked ? '******1234' : null,
    });
    (await vi.waitFor(() => http.expectOne('/api/v1/me/whatsapp-consent'))).flush({
      linked,
      phoneMasked: linked ? '******1234' : null,
      service: { granted: false, at: null },
      marketing: { granted: false, at: null },
      textVersion: '2026-10-27',
      serviceText: "J'accepte de recevoir sur WhatsApp les messages sur mes rendez-vous.",
      marketingText: "J'accepte de recevoir sur WhatsApp des offres.",
    });
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(fixture.nativeElement.querySelector('input[name="email"]')).toBeTruthy();
      if (linked) {
        expect(fixture.nativeElement.querySelector('input[name="consentService"]')).toBeTruthy();
      }
    });
    return fixture.nativeElement as HTMLElement;
  }

  it('offers the e-mail channel, ticked when it is on', async () => {
    const root = await open(true);

    expect(root.querySelector<HTMLInputElement>('input[name="email"]')!.checked).toBe(true);
    expect(root.textContent).toContain('E-mail (confirmations, rappels, annulations)');
  });

  it('saves the choice when the e-mail box is unticked', async () => {
    const root = await open(true);

    const box = root.querySelector<HTMLInputElement>('input[name="email"]')!;
    box.checked = false;
    box.dispatchEvent(new Event('change'));

    const save = await vi.waitFor(() => http.expectOne('/api/v1/me/notification-preferences'));
    expect(save.request.method).toBe('PUT');
    expect(save.request.body).toMatchObject({ email: false, push: true });
    save.flush({ push: true, sms: true, whatsApp: true, reminders: true, email: false });
  });

  it('shows no consent boxes until a number is linked', async () => {
    const root = await open(true, false);

    expect(root.querySelector('input[name="consentService"]')).toBeNull();
  });

  it('offers two separate agreements once linked, the marketing one unticked, and records what was ticked with the text version', async () => {
    const root = await open(true, true);

    const service = root.querySelector<HTMLInputElement>('input[name="consentService"]')!;
    const marketing = root.querySelector<HTMLInputElement>('input[name="consentMarketing"]')!;
    expect(service.checked).toBe(false);
    expect(marketing.checked).toBe(false);
    expect(root.textContent).toContain(
      "J'accepte de recevoir sur WhatsApp les messages sur mes rendez-vous.",
    );

    service.checked = true;
    service.dispatchEvent(new Event('change'));

    const save = await vi.waitFor(() => http.expectOne('/api/v1/me/whatsapp-consent'));
    expect(save.request.method).toBe('PUT');
    expect(save.request.body).toEqual({
      purpose: 'Service',
      granted: true,
      textVersion: '2026-10-27',
    });
    save.flush({
      linked: true,
      phoneMasked: '******1234',
      service: { granted: true, at: '2026-10-27T10:00:00Z' },
      marketing: { granted: false, at: null },
      textVersion: '2026-10-27',
      serviceText: 'x',
      marketingText: 'y',
    });
  });
});
