import { ThemeToggle } from './shared/theme-toggle/theme-toggle';
import { DOCUMENT } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  computed,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import {
  LucideBriefcaseBusiness,
  LucideCalendarDays,
  LucideChevronDown,
  LucideCircleUserRound,
  LucideLayoutDashboard,
  LucideLayoutGrid,
  LucideLogIn,
  LucideLogOut,
  LucideMap,
  LucideMenu,
  LucideShieldCheck,
  LucideSparkles,
  LucideStore,
  LucideUserPlus,
  LucideX,
} from '@lucide/angular';
import { filter } from 'rxjs';
import { AuthService } from './core/auth/auth.service';
import { IdleLogout } from './core/auth/idle-logout';
import { HealthService } from './core/health/health.service';
import { NotificationBell } from './shared/notification-bell/notification-bell';
import { ApiStatusBadge } from './shared/api-status-badge/api-status-badge';

type Menu = 'account' | 'mobile';

@Component({
  selector: 'app-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ThemeToggle,
    RouterOutlet,
    RouterLink,
    RouterLinkActive,
    ApiStatusBadge,
    NotificationBell,
    LucideBriefcaseBusiness,
    LucideCalendarDays,
    LucideChevronDown,
    LucideCircleUserRound,
    LucideLayoutDashboard,
    LucideLayoutGrid,
    LucideLogIn,
    LucideLogOut,
    LucideMap,
    LucideMenu,
    LucideShieldCheck,
    LucideSparkles,
    LucideStore,
    LucideUserPlus,
    LucideX,
  ],
  styleUrl: './app.scss',
  templateUrl: './app.html',
  host: {
    '(document:click)': 'closeOnOutsideClick($event)',
    '(document:keydown.escape)': 'closeAndRestoreFocus()',
  },
})
export class App {
  protected readonly auth = inject(AuthService);
  protected readonly idle = inject(IdleLogout);
  private readonly router = inject(Router);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly document = inject(DOCUMENT);

  protected readonly year = new Date().getFullYear();
  protected readonly openMenu = signal<Menu | null>(null);

  protected readonly isAdmin = computed(
    () =>
      this.auth
        .user()
        ?.roles.some(
          (role) =>
            role === 'SuperAdmin' ||
            role === 'Admin' ||
            role === 'AdminKyc' ||
            role === 'AdminSupport' ||
            role === 'AdminAccountant',
        ) ?? false,
  );

  protected readonly isPartner = computed(
    () => this.auth.user()?.roles.includes('PartnerOwner') ?? false,
  );

  protected readonly isStaff = computed(
    () => this.auth.user()?.roles.includes('PartnerStaff') ?? false,
  );

  protected readonly initial = computed(
    () => this.auth.user()?.firstName.charAt(0).toUpperCase() ?? '',
  );

  constructor() {
    void inject(HealthService).check();
    this.router.events
      .pipe(
        filter((event) => event instanceof NavigationEnd),
        takeUntilDestroyed(inject(DestroyRef)),
      )
      .subscribe(() => this.openMenu.set(null));
  }

  protected toggle(menu: Menu): void {
    const opening = this.openMenu() !== menu;
    this.openMenu.set(opening ? menu : null);
    if (opening && menu === 'account') {
      // The panel is rendered by the change detection that follows this click: focus its first item then.
      setTimeout(() => {
        const panel = this.host.nativeElement.querySelector<HTMLElement>(
          '[data-menu-panel="account"]',
        );
        if (panel) {
          this.items(panel)[0]?.focus();
        }
      });
    }
  }

  protected close(): void {
    this.openMenu.set(null);
  }

  protected async logout(): Promise<void> {
    this.close();
    await this.auth.logout();
    await this.router.navigateByUrl('/');
  }

  /** Arrow keys, Home and End move between the menu items, as expected from a menu. */
  protected navigate(event: KeyboardEvent): void {
    const items = this.items(event.currentTarget as HTMLElement);
    const index = items.indexOf(this.document.activeElement as HTMLElement);
    const target =
      event.key === 'ArrowDown'
        ? (index + 1) % items.length
        : event.key === 'ArrowUp'
          ? (index - 1 + items.length) % items.length
          : event.key === 'Home'
            ? 0
            : event.key === 'End'
              ? items.length - 1
              : -1;
    if (target >= 0) {
      event.preventDefault();
      items[target]?.focus();
    }
  }

  protected closeOnOutsideClick(event: Event): void {
    const header = this.host.nativeElement.querySelector('.site-header');
    if (this.openMenu() && !header?.contains(event.target as Node)) {
      this.close();
    }
  }

  protected closeAndRestoreFocus(): void {
    const menu = this.openMenu();
    if (!menu) {
      return;
    }

    this.close();
    this.host.nativeElement.querySelector<HTMLElement>(`[data-menu-trigger="${menu}"]`)?.focus();
  }

  private items(panel: HTMLElement): HTMLElement[] {
    return [...panel.querySelectorAll<HTMLElement>('[role="menuitem"]')];
  }
}
