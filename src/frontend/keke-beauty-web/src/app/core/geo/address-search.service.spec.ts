import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { AddressSearchService } from './address-search.service';

describe('AddressSearchService', () => {
  let service: AddressSearchService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(AddressSearchService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('asks our own API, never a third party, and encodes the query', async () => {
    const pending = service.search('rue des Jardins & co');

    const request = http.expectOne((r) => r.url === '/api/v1/geo/search');
    expect(request.request.params.get('q')).toBe('rue des Jardins & co');
    expect(request.request.urlWithParams).toContain('q=rue%20des%20Jardins%20%26%20co');
    request.flush([{ label: 'Rue des Jardins, Cocody', latitude: 5.36, longitude: -3.99 }]);

    await expect(pending).resolves.toEqual([
      { label: 'Rue des Jardins, Cocody', latitude: 5.36, longitude: -3.99 },
    ]);
  });

  it('returns the first address at a point, or null when there is none', async () => {
    const found = service.reverse(5.36, -3.99);
    const request = http.expectOne((r) => r.url === '/api/v1/geo/reverse');
    expect(request.request.params.get('lat')).toBe('5.36');
    expect(request.request.params.get('lon')).toBe('-3.99');
    request.flush([{ label: 'Boulevard Latrille', latitude: 5.36, longitude: -3.99 }]);
    await expect(found).resolves.toMatchObject({ label: 'Boulevard Latrille' });

    const none = service.reverse(0, 0);
    http.expectOne((r) => r.url === '/api/v1/geo/reverse').flush([]);
    await expect(none).resolves.toBeNull();
  });
});
