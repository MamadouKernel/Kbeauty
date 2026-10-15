import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { providePrimeNG } from 'primeng/config';
import { vi } from 'vitest';
import type {
  EstablishmentPage,
  EstablishmentSummary,
} from '../../../core/establishments/establishment.service';
import { GeolocationFailure, GeolocationService } from '../../../core/geo/geolocation.service';
import { Search } from './search';

function summary(overrides: Partial<EstablishmentSummary> = {}): EstablishmentSummary {
  return {
    id: crypto.randomUUID(),
    slug: 'maison-eclat-coiffure',
    name: 'Maison Éclat Coiffure',
    coverUrl: '/media/demo/hair-1.svg',
    categories: [{ name: 'Salon de coiffure', slug: 'salon-de-coiffure' }],
    communeName: 'Cocody',
    cityName: 'Abidjan',
    distanceKm: null,
    latitude: 5.36,
    longitude: -3.99,
    startingPrice: 5000,
    currencyCode: 'XOF',
    isFeatured: true,
    bookingAvailable: true,
    phone: '+225 07 00 00 00 01',
    addressLine: 'Rue des Jardins, Deux-Plateaux',
    serviceCount: 6,
    reviewCount: 0,
    ...overrides,
  };
}

function page(items: EstablishmentSummary[], total = items.length): EstablishmentPage {
  return { items, total, page: 1, pageSize: 12 };
}

describe('Search page', () => {
  let http: HttpTestingController;
  let router: Router;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ path: 'etablissements', component: Search }]),
        provideHttpClient(),
        provideHttpClientTesting(),
        providePrimeNG(),
      ],
    });
    http = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
  });

  /** Answers every request matching the URL prefix until the page settles (geo + categories + search). */
  function answer(
    pattern: RegExp,
    body: object | string | null,
    init?: { status: number; statusText: string },
  ) {
    return vi.waitFor(() => {
      const request = http.expectOne((r) => pattern.test(r.urlWithParams));
      request.flush(body, init);
      return request;
    });
  }

  async function open(url = '/etablissements') {
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(url, Search);
    return harness;
  }

  const noise = () => {
    http.match((r) => /\/api\/v1\/(geo|categories)/.test(r.url)).forEach((r) => r.flush([]));
  };

  it('lists the establishments returned by the API', async () => {
    const harness = await open();
    noise();
    await answer(
      /\/api\/v1\/establishments\?/,
      page([summary(), summary({ slug: 'autre', name: 'Autre Salon', isFeatured: false })]),
    );

    await vi.waitFor(() => {
      harness.detectChanges();
      const cards = (harness.routeNativeElement as HTMLElement).querySelectorAll(
        'app-establishment-card',
      );
      expect(cards.length).toBe(2);
    });
    const text = (harness.routeNativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Maison Éclat Coiffure');
    expect(text).toContain('2 établissements');
    expect(text).toContain('À partir de 5 000 F CFA');
  });

  it('turns the URL into API filters (category, text, bookable)', async () => {
    await open('/etablissements?categorie=onglerie&q=manucure&reservation=1');
    noise();

    const request = await answer(/\/api\/v1\/establishments\?/, page([]));

    expect(request.request.params.get('category')).toBe('onglerie');
    expect(request.request.params.get('q')).toBe('manucure');
    expect(request.request.params.get('bookable')).toBe('true');
    expect(request.request.params.get('pageSize')).toBe('12');
  });

  it('sends the position and radius for a nearby search and asks for 50 results on the map view', async () => {
    await open('/etablissements?lat=5.3235&lng=-4.0197&rayon=3&vue=carte');
    noise();

    const request = await answer(/\/api\/v1\/establishments\?/, page([]));

    expect(request.request.params.get('lat')).toBe('5.3235');
    expect(request.request.params.get('radiusKm')).toBe('3');
    expect(request.request.params.get('pageSize')).toBe('50');
  });

  it('shows a helpful empty state and can reset the filters', async () => {
    const harness = await open('/etablissements?q=zzz');
    noise();
    await answer(/\/api\/v1\/establishments\?/, page([]));

    await vi.waitFor(() => {
      harness.detectChanges();
      expect((harness.routeNativeElement as HTMLElement).textContent).toContain(
        'Aucun établissement trouvé',
      );
    });

    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    [...(harness.routeNativeElement as HTMLElement).querySelectorAll('button')]
      .find((b) => b.textContent?.includes('Réinitialiser les filtres'))!
      .click();
    expect(navigate).toHaveBeenCalledWith([], expect.objectContaining({ queryParams: {} }));
  });

  it('offers a retry when the search fails', async () => {
    const harness = await open();
    noise();
    await answer(/\/api\/v1\/establishments\?/, 'boom', {
      status: 500,
      statusText: 'Server Error',
    });

    await vi.waitFor(() => {
      harness.detectChanges();
      expect(
        (harness.routeNativeElement as HTMLElement).querySelector('[role="alert"]')?.textContent,
      ).toContain('Réessayer');
    });
  });

  it('explains when the browser refuses geolocation but keeps the search usable', async () => {
    const harness = await open();
    noise();
    await answer(/\/api\/v1\/establishments\?/, page([summary()]));
    vi.spyOn(TestBed.inject(GeolocationService), 'locate').mockRejectedValue(
      new GeolocationFailure('denied'),
    );

    await vi.waitFor(() => harness.detectChanges());
    const button = [...(harness.routeNativeElement as HTMLElement).querySelectorAll('button')].find(
      (b) => b.textContent?.includes('Autour de moi'),
    )!;
    button.click();

    await vi.waitFor(() => {
      harness.detectChanges();
      expect((harness.routeNativeElement as HTMLElement).textContent).toContain('refusé');
    });
    expect(
      (harness.routeNativeElement as HTMLElement).querySelectorAll('app-establishment-card').length,
    ).toBe(1);
  });
});
