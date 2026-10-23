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

  async function open(email: boolean) {
    const fixture = TestBed.createComponent(NotificationSettings);
    fixture.detectChanges();
    (await vi.waitFor(() => http.expectOne('/api/v1/me/notification-preferences'))).flush({
      push: true,
      sms: true,
      whatsApp: true,
      reminders: true,
      email,
    });
    (await vi.waitFor(() => http.expectOne('/api/v1/me/whatsapp'))).flush({ available: false });
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(fixture.nativeElement.querySelector('input[name="email"]')).toBeTruthy();
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
});
