import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { providePrimeNG } from 'primeng/config';
import { vi } from 'vitest';
import { Category } from '../../../core/catalog/catalog.service';
import { Home } from './home';

const CATEGORIES: Category[] = [
  { id: '1', name: 'Salon de coiffure', slug: 'salon-de-coiffure', icon: 'hair' },
  { id: '2', name: 'Espace spa', slug: 'espace-spa', icon: 'spa' },
  { id: '3', name: 'Onglerie', slug: 'onglerie', icon: 'nails' },
];

describe('Home', () => {
  let fixture: ComponentFixture<Home>;
  let http: HttpTestingController;
  let el: HTMLElement;

  /** Re-runs change detection until the assertion holds (zoneless + async resource). */
  const eventually = (assertion: () => void) =>
    vi.waitFor(() => {
      fixture.detectChanges();
      assertion();
    });

  const respondWithCategories = async (): Promise<void> => {
    const request = await vi.waitFor(() => http.expectOne('/api/v1/categories'));
    request.flush(CATEGORIES);
    await eventually(() =>
      expect(el.querySelectorAll('button.cat-card').length).toBe(CATEGORIES.length),
    );
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [Home],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        providePrimeNG(),
      ],
    }).compileComponents();

    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(Home);
    el = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();
  });

  it('renders exactly one h1 and the search form', () => {
    expect(el.querySelectorAll('h1').length).toBe(1);
    expect(el.querySelector('form.search')).toBeTruthy();
  });

  it('shows skeletons while categories load', async () => {
    await eventually(() =>
      expect(el.querySelectorAll('.cat-card--skeleton').length).toBeGreaterThan(0),
    );
  });

  it('renders one button per category returned by the API', async () => {
    await respondWithCategories();

    const names = Array.from(el.querySelectorAll('button.cat-card')).map((b) =>
      b.textContent?.trim(),
    );
    expect(names).toEqual(['Salon de coiffure', 'Espace spa', 'Onglerie']);
  });

  it('toggles the pressed state when a category is picked twice', async () => {
    await respondWithCategories();
    const spa = el.querySelectorAll<HTMLButtonElement>('button.cat-card')[1];

    spa.click();
    await eventually(() => expect(spa.getAttribute('aria-pressed')).toBe('true'));

    spa.click();
    await eventually(() => expect(spa.getAttribute('aria-pressed')).toBe('false'));
  });

  it('offers a retry when categories fail to load', async () => {
    const request = await vi.waitFor(() => http.expectOne('/api/v1/categories'));
    request.flush('boom', { status: 500, statusText: 'Server Error' });

    await eventually(() =>
      expect(el.querySelector('[role="alert"]')?.textContent).toContain('Réessayer'),
    );
  });

  it('opens the directory with the chosen category and keyword on submit', async () => {
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    await respondWithCategories();
    el.querySelectorAll<HTMLButtonElement>('button.cat-card')[0].click();
    fixture.detectChanges();
    const input = el.querySelector<HTMLInputElement>('#search-text')!;
    input.value = ' tresses ';
    input.dispatchEvent(new Event('input'));

    el.querySelector<HTMLButtonElement>('form.search button[type="submit"]')!.click();

    await vi.waitFor(() =>
      expect(navigate).toHaveBeenCalledWith(['/etablissements'], {
        queryParams: { categorie: 'salon-de-coiffure', q: 'tresses' },
      }),
    );
  });
});
