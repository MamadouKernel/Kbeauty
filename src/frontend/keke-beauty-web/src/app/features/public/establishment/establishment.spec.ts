import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { providePrimeNG } from 'primeng/config';
import { vi } from 'vitest';
import { DIRECTIONS_CONFIG } from '../../../core/directions/directions.service';
import type { EstablishmentDetails } from '../../../core/establishments/establishment.service';
import { formatDuration, formatMoney } from '../../../core/format/format';
import { Establishment } from './establishment';

const DETAILS: EstablishmentDetails = {
  id: '11111111-1111-1111-1111-111111111111',
  slug: 'maison-eclat-coiffure',
  name: 'Maison Éclat Coiffure',
  description: 'Salon de coiffure afro et mixte.',
  categories: [{ name: 'Salon de coiffure', slug: 'salon-de-coiffure' }],
  phone: '+2250700000001',
  email: null,
  addressLine: 'Rue des Jardins',
  communeName: 'Cocody',
  cityName: 'Abidjan',
  regionName: "District autonome d'Abidjan",
  latitude: 5.3599,
  longitude: -3.9877,
  timezone: 'Africa/Abidjan',
  bookingAvailable: true,
  isFeatured: true,
  media: [
    {
      kind: 'Photo',
      url: '/media/demo/hair-1.svg',
      posterUrl: null,
      alt: 'Visuel 1',
      durationSeconds: null,
    },
    {
      kind: 'Photo',
      url: '/media/demo/hair-2.svg',
      posterUrl: null,
      alt: 'Visuel 2',
      durationSeconds: null,
    },
  ],
  serviceGroups: [
    {
      name: 'Tresses',
      services: [
        {
          id: 's1',
          name: 'Box braids',
          description: null,
          price: 25000,
          priceType: 'From',
          currencyCode: 'XOF',
          durationMinutes: 240,
          promoPercent: 0,
          effectivePrice: 25000,
        },
        {
          id: 's2',
          name: 'Cornrows',
          description: null,
          price: 12000,
          priceType: 'Fixed',
          currencyCode: 'XOF',
          durationMinutes: 90,
          promoPercent: 0,
          effectivePrice: 12000,
        },
      ],
    },
  ],
  reviewCount: 0,
  acceptedPayments: {
    cash: true,
    mobileMoney: [
      { operator: 'wave-cote-divoire', account: '+2250700001234', holderName: 'Awa Koné' },
    ],
  },
  openingHours: [
    { dayOfWeek: 1, opensAt: '08:30', closesAt: '12:30' },
    { dayOfWeek: 1, opensAt: '14:00', closesAt: '19:00' },
    { dayOfWeek: 6, opensAt: '08:30', closesAt: '18:00' },
  ],
};

describe('Establishment page', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ path: 'etablissements/:slug', component: Establishment }]),
        provideHttpClient(),
        provideHttpClientTesting(),
        providePrimeNG(),
        { provide: DIRECTIONS_CONFIG, useValue: { yangoUrlTemplate: '' } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  async function open(slug = 'maison-eclat-coiffure') {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(`/etablissements/${slug}`, Establishment);
    return harness;
  }

  const root = (harness: RouterTestingHarness) => harness.routeNativeElement as HTMLElement;

  it('renders services, hours and the contact actions', async () => {
    const harness = await open();
    (await vi.waitFor(() => http.expectOne('/api/v1/establishments/maison-eclat-coiffure'))).flush(
      DETAILS,
    );

    await vi.waitFor(() => {
      harness.detectChanges();
      expect(root(harness).querySelector('h1')?.textContent).toContain('Maison Éclat Coiffure');
    });

    // The services come first (price, duration and a "Réserver" button on each line)...
    const services = root(harness).textContent ?? '';
    expect(services).toContain('Box braids');
    expect(services).toContain('À partir de 25 000 F CFA');
    expect(services).toContain('4 h');
    expect(root(harness).querySelectorAll('a[href*="reserver"]').length).toBeGreaterThanOrEqual(3);

    // ...and the practical information is under "À propos".
    root(harness).querySelector<HTMLButtonElement>('button[role="tab"]#tab-apropos')?.click();
    harness.detectChanges();
    const text = root(harness).textContent ?? '';
    expect(text).toContain('08:30 – 12:30 · 14:00 – 19:00');
    expect(text).toContain('Fermé'); // days without opening hours
    expect(
      root(harness).querySelector<HTMLAnchorElement>('a[href^="tel:"]')?.getAttribute('href'),
    ).toBe('tel:+2250700000001');
    expect(text).toContain('+225 07 00 00 00 01');
  });

  it('only offers online booking when the establishment allows it', async () => {
    const harness = await open();
    (await vi.waitFor(() => http.expectOne('/api/v1/establishments/maison-eclat-coiffure'))).flush({
      ...DETAILS,
      bookingAvailable: false,
    });

    await vi.waitFor(() => {
      harness.detectChanges();
      expect(root(harness).querySelector('h1')).toBeTruthy();
    });
    expect(
      [...root(harness).querySelectorAll('a, button')].some((b) =>
        b.textContent?.includes('Réserver'),
      ),
    ).toBe(false);
  });

  it('counts the visit and the click on Appeler', async () => {
    const harness = await open();
    (await vi.waitFor(() => http.expectOne('/api/v1/establishments/maison-eclat-coiffure'))).flush(
      DETAILS,
    );

    const view = await vi.waitFor(() =>
      http.expectOne((r) => r.url.endsWith('/events') && r.body?.type === 'view'),
    );
    view.flush(null, { status: 204, statusText: 'No Content' });

    await vi.waitFor(() => harness.detectChanges());
    const call = root(harness).querySelector<HTMLAnchorElement>('a[href^="tel:"]')!;
    call.addEventListener('click', (event) => event.preventDefault());
    call.click();

    const click = await vi.waitFor(() =>
      http.expectOne((r) => r.url.endsWith('/events') && r.body?.type === 'call'),
    );
    expect(click.request.url).toBe(`/api/v1/establishments/${DETAILS.id}/events`);
    click.flush(null, { status: 204, statusText: 'No Content' });
  });

  it('shows a clear "not found" page for unknown establishments', async () => {
    const harness = await open('inconnu');
    (await vi.waitFor(() => http.expectOne('/api/v1/establishments/inconnu'))).flush(
      { title: 'Not Found' },
      { status: 404, statusText: 'Not Found' },
    );

    await vi.waitFor(() => {
      harness.detectChanges();
      expect(root(harness).textContent).toContain('Établissement introuvable');
    });
  });
});

describe('format helpers', () => {
  it.each([
    [30, '30 min'],
    [60, '1 h'],
    [90, '1 h 30'],
    [240, '4 h'],
  ])('formats %i minutes as %s', (minutes, expected) => {
    expect(formatDuration(minutes)).toBe(expected);
  });

  it('formats XOF amounts with thin separators normalised', () => {
    expect(formatMoney(25000)).toBe('25 000 F CFA');
  });
});

describe('Establishment page with Yango enabled', () => {
  it('sends "S\'y rendre" and the address straight to Yango with the shop as destination', async () => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ path: 'etablissements/:slug', component: Establishment }]),
        provideHttpClient(),
        provideHttpClientTesting(),
        providePrimeNG(),
        {
          provide: DIRECTIONS_CONFIG,
          useValue: { yangoUrlTemplate: 'https://yango.example/route?to={lat},{lng}' },
        },
      ],
    });
    const http = TestBed.inject(HttpTestingController);
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/etablissements/maison-eclat-coiffure', Establishment);
    (await vi.waitFor(() => http.expectOne('/api/v1/establishments/maison-eclat-coiffure'))).flush(
      DETAILS,
    );

    await vi.waitFor(() => {
      harness.detectChanges();
      expect(
        (harness.routeNativeElement as HTMLElement).querySelectorAll('a[href^="https://yango"]')
          .length,
      ).toBe(2);
    });
    const links = Array.from(
      (harness.routeNativeElement as HTMLElement).querySelectorAll<HTMLAnchorElement>(
        'a[href^="https://yango"]',
      ),
    );
    const expected = `https://yango.example/route?to=${DETAILS.latitude},${DETAILS.longitude}`;
    expect(links.map((a) => a.getAttribute('href'))).toEqual([expected, expected]);
    expect(links.every((a) => a.target === '_blank')).toBe(true);
  });
});
