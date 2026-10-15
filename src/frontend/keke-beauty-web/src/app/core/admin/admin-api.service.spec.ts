import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import { AdminApiService } from './admin-api.service';

describe('AdminApiService', () => {
  let service: AdminApiService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(AdminApiService);
    http = TestBed.inject(HttpTestingController);
  });

  it('sends only the filters that are filled in', async () => {
    const promise = service.users({ q: 'awa', role: '', status: 'Active', page: 2 });

    const request = http.expectOne((r) => r.url === '/api/v1/admin/users');
    expect(request.request.params.get('q')).toBe('awa');
    expect(request.request.params.get('status')).toBe('Active');
    expect(request.request.params.get('page')).toBe('2');
    expect(request.request.params.has('role')).toBe(false);
    request.flush({ items: [], total: 0, page: 2, pageSize: 20 });

    expect((await promise).page).toBe(2);
  });

  it('posts the reason of a suspension', async () => {
    const promise = service.suspendUser('u1', 'Fausses réservations');

    const request = http.expectOne('/api/v1/admin/users/u1/suspend');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ reason: 'Fausses réservations' });
    request.flush(null, { status: 204, statusText: 'No Content' });
    await promise;
  });

  it('encodes template codes that contain dots and dashes', async () => {
    const promise = service.updateTemplate('billing.reminder.-3', 'Titre', 'Corps');

    const request = http.expectOne('/api/v1/admin/templates/billing.reminder.-3');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ title: 'Titre', body: 'Corps' });
    request.flush(null, { status: 204, statusText: 'No Content' });
    await promise;
  });

  it('asks the geography endpoint for the children of a parent', async () => {
    const promise = service.geo('regions', 'c1');

    const request = http.expectOne((r) => r.url === '/api/v1/admin/geo/regions');
    expect(request.request.params.get('parentId')).toBe('c1');
    request.flush([]);
    expect(await promise).toEqual([]);
  });
});
