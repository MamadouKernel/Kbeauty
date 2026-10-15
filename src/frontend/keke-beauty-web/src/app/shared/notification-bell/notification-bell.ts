import { DatePipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { LucideBell, LucideCheckCheck } from '@lucide/angular';
import { NotificationService } from '../../core/notifications/notification.service';

const TOAST_MS = 6000;

/** Bell with an unread counter, a dropdown of the latest notifications, and a toast for a message that just arrived. */
@Component({
  selector: 'app-notification-bell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, DatePipe, LucideBell, LucideCheckCheck],
  host: {
    '(document:click)': 'closeOnOutsideClick($event)',
    '(document:keydown.escape)': 'open.set(false)',
  },
  styles: `
    :host {
      position: relative;
      display: inline-block;
    }
    .bell {
      position: relative;
      display: inline-grid;
      place-items: center;
      width: 2.75rem;
      height: 2.75rem;
      border: 1px solid var(--kb-primary-200);
      border-radius: 50%;
      background: rgb(255 255 255 / 70%);
      color: var(--kb-primary-700);
      cursor: pointer;
      transition:
        background-color 200ms ease,
        transform 200ms var(--ease-out);

      &:hover {
        background: var(--kb-primary-50);
        transform: translateY(-1px);
      }
      svg {
        width: 1.25rem;
        height: 1.25rem;
      }
    }
    .count {
      position: absolute;
      top: -0.25rem;
      right: -0.25rem;
      min-width: 1.25rem;
      height: 1.25rem;
      padding-inline: 0.25rem;
      border-radius: var(--radius-pill);
      background: var(--kb-danger);
      color: #fff;
      font-size: 0.6875rem;
      font-weight: 800;
      line-height: 1.25rem;
      text-align: center;
      box-shadow: 0 0 0 2px #fff;
    }
    .panel {
      position: absolute;
      top: calc(100% + 0.875rem);
      right: 0;
      z-index: 60;
      display: grid;
      width: min(24rem, calc(100vw - 2rem));
      max-height: min(30rem, 70vh);
      overflow: auto;
      padding: 0.5rem;
      border: 1px solid rgb(139 69 194 / 18%);
      border-radius: 1.375rem;
      background: rgb(255 255 255 / 96%);
      backdrop-filter: blur(22px);
      box-shadow: 0 24px 60px -18px rgb(54 23 79 / 45%);
      animation: panel-in 200ms var(--ease-out);
    }
    @keyframes panel-in {
      from {
        opacity: 0;
        transform: translateY(-8px) scale(0.96);
      }
    }
    .head {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 0.5rem 0.75rem;
      font-weight: 700;
    }
    .mark-all {
      display: inline-flex;
      align-items: center;
      gap: 0.375rem;
      min-height: 2.75rem;
      padding-inline: 0.5rem;
      border: 0;
      background: transparent;
      font: inherit;
      font-size: var(--fs-xs);
      font-weight: 600;
      color: var(--kb-primary-600);
      cursor: pointer;

      svg {
        width: 1rem;
        height: 1rem;
      }
    }
    .item {
      display: grid;
      gap: 0.125rem;
      width: 100%;
      padding: 0.625rem 0.75rem;
      border: 0;
      border-radius: 1rem;
      background: transparent;
      font: inherit;
      text-align: left;
      color: inherit;
      cursor: pointer;

      &:hover,
      &:focus-visible {
        background: var(--kb-primary-50);
      }
      &[data-unread='true'] {
        background: color-mix(in srgb, var(--kb-primary-100) 55%, white);
      }
      strong {
        display: flex;
        align-items: center;
        gap: 0.5rem;
      }
      &[data-unread='true'] strong::before {
        content: '';
        width: 0.5rem;
        height: 0.5rem;
        border-radius: 50%;
        background: var(--kb-primary-600);
      }
      span {
        font-size: var(--fs-sm);
        color: var(--kb-ink-muted);
      }
      time {
        font-size: var(--fs-2xs);
        color: var(--kb-ink-muted);
      }
    }
    .empty {
      padding: 1.5rem 1rem;
      text-align: center;
      color: var(--kb-ink-muted);
    }
    .foot {
      padding: 0.5rem 0.75rem;
      text-align: center;
    }
    .toast {
      position: fixed;
      right: 1rem;
      bottom: 1rem;
      z-index: 80;
      display: grid;
      gap: 0.25rem;
      max-width: min(22rem, calc(100vw - 2rem));
      padding: 0.875rem 1.125rem;
      border: 1px solid var(--kb-primary-200);
      border-radius: var(--radius-md);
      background: #fff;
      box-shadow: var(--shadow-lift);
      animation: panel-in 240ms var(--ease-out);

      span {
        color: var(--kb-ink-muted);
        font-size: var(--fs-md);
      }
    }
  `,
  template: `
    <button
      type="button"
      class="bell"
      aria-haspopup="true"
      [attr.aria-expanded]="open()"
      aria-controls="notification-panel"
      (click)="toggle()"
    >
      <svg lucideBell aria-hidden="true"></svg>
      @if (service.unread() > 0) {
        <span class="count" aria-hidden="true">{{
          service.unread() > 9 ? '9+' : service.unread()
        }}</span>
      }
      <span class="visually-hidden"
        >Notifications{{
          service.unread() > 0 ? ' : ' + service.unread() + ' non lue(s)' : ''
        }}</span
      >
    </button>

    @if (open()) {
      <div id="notification-panel" class="panel" role="region" aria-label="Notifications">
        <div class="head">
          <span>Notifications</span>
          @if (service.unread() > 0) {
            <button type="button" class="mark-all" (click)="service.markAllRead()">
              <svg lucideCheckCheck aria-hidden="true"></svg>
              Tout marquer comme lu
            </button>
          }
        </div>
        @for (n of service.items(); track n.id) {
          <button
            type="button"
            class="item"
            [attr.data-unread]="!n.isRead"
            (click)="service.markRead(n.id)"
          >
            <strong>{{ n.title }}</strong>
            <span>{{ n.body }}</span>
            <time [attr.datetime]="n.createdAt">{{ n.createdAt | date: 'dd/MM/yyyy HH:mm' }}</time>
          </button>
        } @empty {
          <p class="empty">Aucune notification pour l'instant.</p>
        }
        <p class="foot">
          <a class="link" routerLink="/compte/rendez-vous" (click)="open.set(false)"
            >Mes rendez-vous</a
          >
        </p>
      </div>
    }

    @if (toast(); as t) {
      <div class="toast" role="status" aria-live="polite">
        <strong>{{ t.title }}</strong>
        <span>{{ t.body }}</span>
      </div>
    }
  `,
})
export class NotificationBell {
  protected readonly service = inject(NotificationService);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);

  protected readonly open = signal(false);
  protected readonly toast = signal<{ title: string; body: string } | null>(null);
  private toastTimer: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    effect(() => {
      const latest = this.service.latest();
      untracked(() => {
        clearTimeout(this.toastTimer);
        this.toast.set(latest ? { title: latest.title, body: latest.body } : null);
        if (latest) {
          this.toastTimer = setTimeout(() => this.toast.set(null), TOAST_MS);
        }
      });
    });
  }

  protected toggle(): void {
    this.open.update((value) => !value);
  }

  protected closeOnOutsideClick(event: Event): void {
    if (this.open() && !this.host.nativeElement.contains(event.target as Node)) {
      this.open.set(false);
    }
  }
}
