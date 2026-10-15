import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Router } from '@angular/router';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import { AuthService } from '../auth/auth.service';
import { FavoritesService } from './favorites.service';

describe('FavoritesService', () => {
  const user = signal<{ id: string } | null>(null);
  const navigate = vi.fn(() => Promise.resolve(true));
  let http: HttpTestingController;

  function create(): FavoritesService {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: { user } },
        { provide: Router, useValue: { navigate, url: '/etablissements/salon-un' } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    return TestBed.inject(FavoritesService);
  }

  beforeEach(() => {
    user.set(null);
    navigate.mockClear();
  });

  it('sends a visitor who is not signed in to the sign-in page, then back to where they were', async () => {
    const service = create();

    await service.toggle('e1');

    expect(navigate).toHaveBeenCalledWith(['/connexion'], {
      queryParams: { retour: '/etablissements/salon-un' },
    });
    expect(service.isFavorite('e1')).toBe(false);
    http.expectNone('/api/v1/me/favorites/e1');
  });

  it('loads the favorites after sign-in and saves a new one at once, before the server answers', async () => {
    const service = create();
    user.set({ id: 'u1' });
    TestBed.tick();
    http.expectOne('/api/v1/me/favorites/ids').flush(['e2']);
    await vi.waitFor(() => expect(service.isFavorite('e2')).toBe(true));

    const pending = service.toggle('e1');
    expect(service.isFavorite('e1')).toBe(true); // optimistic
    const request = http.expectOne('/api/v1/me/favorites/e1');
    expect(request.request.method).toBe('PUT');
    request.flush(null, { status: 204, statusText: 'No Content' });
    await pending;

    expect(service.isFavorite('e1')).toBe(true);
    expect(service.count()).toBe(2);
  });

  it('takes the heart back when the server refuses', async () => {
    const service = create();
    user.set({ id: 'u1' });
    TestBed.tick();
    http.expectOne('/api/v1/me/favorites/ids').flush([]);

    const pending = service.toggle('e1');
    http
      .expectOne('/api/v1/me/favorites/e1')
      .flush({ title: 'limite' }, { status: 409, statusText: 'Conflict' });
    await pending;

    expect(service.isFavorite('e1')).toBe(false);
  });

  it('removes a favorite with DELETE', async () => {
    const service = create();
    user.set({ id: 'u1' });
    TestBed.tick();
    http.expectOne('/api/v1/me/favorites/ids').flush(['e1']);
    await vi.waitFor(() => expect(service.isFavorite('e1')).toBe(true));

    const pending = service.toggle('e1');
    const request = http.expectOne('/api/v1/me/favorites/e1');
    expect(request.request.method).toBe('DELETE');
    request.flush(null, { status: 204, statusText: 'No Content' });
    await pending;

    expect(service.isFavorite('e1')).toBe(false);
  });
});
