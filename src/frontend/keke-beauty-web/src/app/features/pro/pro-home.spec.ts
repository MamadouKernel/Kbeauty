import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { providePrimeNG } from 'primeng/config';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { PartnerEstablishmentSummary } from '../../core/partner/partner.service';
import { ProHome } from './pro-home';

const DRAFT: PartnerEstablishmentSummary = {
  id: 'e1',
  slug: 'salon-un',
  name: 'Salon Un',
  status: 'Draft',
  reviewComment: 'Photo de la devanture floue.',
  coverUrl: null,
  missing: [{ code: 'hours', label: "Renseigner les horaires d'ouverture." }],
};

const ACTIVE: PartnerEstablishmentSummary = {
  id: 'e2',
  slug: 'salon-deux',
  name: 'Salon Deux',
  status: 'Active',
  reviewComment: null,
  coverUrl: '/api/v1/files/uploads/e2/a_thumb.webp',
  missing: [],
};

describe('ProHome', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ path: '', component: ProHome }]),
        provideHttpClient(),
        provideHttpClientTesting(),
        providePrimeNG(),
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  async function open() {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/', ProHome);
    return harness;
  }

  const root = (harness: RouterTestingHarness) => harness.routeNativeElement as HTMLElement;

  it('invites the partner to register a first establishment', async () => {
    const harness = await open();
    (await vi.waitFor(() => http.expectOne('/api/v1/pro/establishments'))).flush([]);

    await vi.waitFor(() => {
      harness.detectChanges();
      expect(root(harness).textContent).toContain('Votre premier établissement');
    });
    expect(root(harness).querySelector('a[href="/pro/etablissements/nouveau"]')).toBeTruthy();
  });

  it('lists establishments with their status, what is missing and the next action', async () => {
    const harness = await open();
    (await vi.waitFor(() => http.expectOne('/api/v1/pro/establishments'))).flush([DRAFT, ACTIVE]);

    await vi.waitFor(() => {
      harness.detectChanges();
      expect(root(harness).textContent).toContain('Salon Un');
    });
    const text = root(harness).textContent ?? '';
    expect(text).toContain('Brouillon');
    expect(text).toContain("Renseigner les horaires d'ouverture.");
    expect(text).toContain('Photo de la devanture floue.');
    expect(text).toContain("Continuer l'inscription");
    expect(text).toContain('Publié');
    expect(root(harness).querySelector('a[href="/etablissements/salon-deux"]')).toBeTruthy();
    expect(
      root(harness).querySelector('a[href="/pro/etablissements/e2/prestations"]'),
    ).toBeTruthy();
    // A draft that is not published has no public page to open.
    expect(root(harness).querySelector('a[href="/etablissements/salon-un"]')).toBeNull();
  });

  it('shows an error message when the list cannot be loaded', async () => {
    const harness = await open();
    (await vi.waitFor(() => http.expectOne('/api/v1/pro/establishments'))).flush(
      {},
      { status: 500, statusText: 'Server Error' },
    );

    await vi.waitFor(() => {
      harness.detectChanges();
      expect(root(harness).querySelector('[role="alert"]')?.textContent).toContain(
        'Impossible de charger',
      );
    });
  });
});
