import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { providePrimeNG } from 'primeng/config';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { AdminUser, AdminUserDetail } from '../../core/admin/admin-api.service';
import { UsersPage } from './users-page';

const USER: AdminUser = {
  id: 'u1',
  email: 'awa@example.com',
  firstName: 'Awa',
  lastName: 'Koné',
  roles: ['Client'],
  status: 'Active',
  emailConfirmed: true,
  totpEnabled: true,
  createdAt: '2026-01-10T10:00:00Z',
};

const DETAIL: AdminUserDetail = { user: USER, establishments: [], history: [] };

describe('UsersPage', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ path: '', component: UsersPage }]),
        provideHttpClient(),
        provideHttpClientTesting(),
        providePrimeNG(),
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  async function open() {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/', UsersPage);
    (await vi.waitFor(() => http.expectOne((r) => r.url === '/api/v1/admin/users'))).flush({
      items: [USER],
      total: 1,
      page: 1,
      pageSize: 20,
    });
    await vi.waitFor(() => {
      harness.detectChanges();
      expect(root(harness).textContent).toContain('awa@example.com');
    });
    return harness;
  }

  const root = (harness: RouterTestingHarness) => harness.routeNativeElement as HTMLElement;

  const button = (harness: RouterTestingHarness, text: string) =>
    Array.from(root(harness).querySelectorAll('button')).find((b) => b.textContent?.includes(text));

  it('lists the accounts with their roles and status', async () => {
    const harness = await open();

    const text = root(harness).textContent ?? '';
    expect(text).toContain('Awa Koné');
    expect(text).toContain('Client');
    expect(text).toContain('Actif');
  });

  it('refuses to suspend without a reason and sends it once given', async () => {
    const harness = await open();
    button(harness, 'Détails')?.click();
    (await vi.waitFor(() => http.expectOne('/api/v1/admin/users/u1'))).flush(DETAIL);
    await vi.waitFor(() => {
      harness.detectChanges();
      expect(button(harness, 'Suspendre…')).toBeTruthy();
    });

    button(harness, 'Suspendre…')?.click();
    harness.detectChanges();
    const confirm = button(harness, 'Suspendre le compte');
    confirm?.click();
    harness.detectChanges();
    expect(root(harness).querySelector('[role="alert"]')?.textContent).toContain('motif');
    http.expectNone('/api/v1/admin/users/u1/suspend');

    const textarea = root(harness).querySelector('textarea') as HTMLTextAreaElement;
    textarea.value = 'Fausses réservations';
    textarea.dispatchEvent(new Event('input'));
    harness.detectChanges();
    await harness.fixture.whenStable();
    button(harness, 'Suspendre le compte')?.click();

    const request = await vi.waitFor(() => http.expectOne('/api/v1/admin/users/u1/suspend'));
    expect(request.request.body).toEqual({ reason: 'Fausses réservations' });
    request.flush(null, { status: 204, statusText: 'No Content' });
  });

  it('shows the API message when the action is refused', async () => {
    const harness = await open();
    button(harness, 'Détails')?.click();
    (await vi.waitFor(() => http.expectOne('/api/v1/admin/users/u1'))).flush(DETAIL);
    await vi.waitFor(() => {
      harness.detectChanges();
      expect(button(harness, 'Suspendre…')).toBeTruthy();
    });
    button(harness, 'Suspendre…')?.click();
    harness.detectChanges();

    const textarea = root(harness).querySelector('textarea') as HTMLTextAreaElement;
    textarea.value = 'Motif valable';
    textarea.dispatchEvent(new Event('input'));
    harness.detectChanges();
    await harness.fixture.whenStable();
    button(harness, 'Suspendre le compte')?.click();
    (await vi.waitFor(() => http.expectOne('/api/v1/admin/users/u1/suspend'))).flush(
      { title: 'Vous ne pouvez pas appliquer cette action à votre propre compte.' },
      { status: 409, statusText: 'Conflict' },
    );

    await vi.waitFor(() => {
      harness.detectChanges();
      expect(root(harness).textContent).toContain('propre compte');
    });
  });
});
