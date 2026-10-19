import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { vi } from 'vitest';
import { App } from './app';
import { AuthService } from './core/auth/auth.service';

describe('App shell', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
  });

  it('renders the brand, landmarks and a skip link', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;

    expect(el.querySelector('.brand')?.textContent).toContain('Keke');
    expect(el.querySelector('nav[aria-label="Navigation principale"]')).toBeTruthy();
    expect(el.querySelector('main#contenu')).toBeTruthy();
    expect(el.querySelector('a.skip-link')?.getAttribute('href')).toBe('#contenu');
  });

  it('checks API readiness at startup and announces it', async () => {
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();

    http.expectOne('/health/ready').flush('Healthy');

    await vi.waitFor(() => {
      fixture.detectChanges();
      expect((fixture.nativeElement as HTMLElement).textContent).toContain('Service en ligne');
    });
  });
});

describe('Header menus', () => {
  const PARTNER = {
    id: 'u1',
    email: 'awa@example.com',
    firstName: 'Awa',
    lastName: 'Koné',
    roles: ['PartnerOwner'],
    twoFactorEnabled: true,
  };

  function create(user: typeof PARTNER | null) {
    const logout = vi.fn(() => Promise.resolve());
    TestBed.configureTestingModule({
      imports: [App],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: AuthService,
          useValue: { user: signal(user), idleMinutes: signal(null), logout },
        },
      ],
    });
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const trigger = () => root.querySelector<HTMLButtonElement>('[data-menu-trigger="account"]')!;
    const items = () => [...root.querySelectorAll<HTMLElement>('[role="menuitem"]')];
    return { fixture, root, trigger, items, logout };
  }

  it('offers sign-in and sign-up in a dropdown for visitors', () => {
    const { fixture, root, trigger, items } = create(null);
    expect(root.querySelector('[role="menu"]')).toBeNull();
    expect(trigger().getAttribute('aria-expanded')).toBe('false');

    trigger().click();
    fixture.detectChanges();

    expect(trigger().getAttribute('aria-expanded')).toBe('true');
    expect(items().map((i) => i.textContent?.replace(/\s+/g, ' ').trim())).toEqual([
      "Se connecter J'ai déjà un compte",
      'Créer un compte Réservez en quelques clics',
      'Inscrire mon établissement Salon, spa, onglerie…',
    ]);
    expect(items()[0].getAttribute('href')).toBe('/connexion');
    expect(items()[2].getAttribute('href')).toBe('/inscription?type=partenaire');
  });

  it('closes on Escape and when clicking elsewhere', () => {
    const { fixture, root, trigger } = create(null);

    trigger().click();
    fixture.detectChanges();
    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    fixture.detectChanges();
    expect(root.querySelector('[role="menu"]')).toBeNull();

    trigger().click();
    fixture.detectChanges();
    document.body.click();
    fixture.detectChanges();
    expect(root.querySelector('[role="menu"]')).toBeNull();
  });

  it('shows the account menu with role-specific entries and signs out', async () => {
    const { fixture, root, trigger, items, logout } = create(PARTNER);

    trigger().click();
    fixture.detectChanges();

    expect(root.querySelector('.menu__head')?.textContent).toContain('awa@example.com');
    const labels = items().map((i) => i.textContent?.trim());
    expect(labels).toEqual(['Mon compte', 'Mon espace pro', 'Se déconnecter']);

    items()[2].click();
    await vi.waitFor(() => expect(logout).toHaveBeenCalled());
  });

  it('opens the mobile sheet with the navigation links', () => {
    const { fixture, root } = create(null);

    root.querySelector<HTMLButtonElement>('.burger')!.click();
    fixture.detectChanges();

    const sheet = root.querySelector('#mobile-nav');
    expect(sheet?.textContent).toContain('Établissements');
    expect(sheet?.querySelectorAll('svg').length).toBeGreaterThanOrEqual(4);
  });

  it('puts an icon in every primary navigation link', () => {
    const { root } = create(null);

    const links = root.querySelectorAll('nav[aria-label="Navigation principale"] a');
    expect(links.length).toBe(4);
    links.forEach((link) => expect(link.querySelector('svg')).toBeTruthy());
  });

  it('shows the Administration link for fine-grained admin roles such as AdminKyc', () => {
    const { fixture, trigger, items } = create({
      ...PARTNER,
      roles: ['AdminKyc'],
    });

    trigger().click();
    fixture.detectChanges();

    const labels = items().map((i) => i.textContent?.trim());
    expect(labels).toContain('Administration');
  });

  it('displays the pre-logout inactivity warning banner and allows staying signed in', () => {
    const { fixture, root } = create(PARTNER);
    const app = fixture.componentInstance as unknown as {
      idle: { warningSecondsLeft: { set(v: number | null): void }; staySignedIn(): void };
    };
    const staySpy = vi.spyOn(app.idle, 'staySignedIn').mockImplementation(() => {
      app.idle.warningSecondsLeft.set(null);
    });

    app.idle.warningSecondsLeft.set(45);
    fixture.detectChanges();

    const banner = root.querySelector('.idle-banner');
    expect(banner?.textContent).toContain('45 s');
    banner?.querySelector<HTMLButtonElement>('button')?.click();
    fixture.detectChanges();

    expect(staySpy).toHaveBeenCalled();
    expect(root.querySelector('.idle-banner')).toBeNull();
  });
});
