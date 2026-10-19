import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { LucideCalendarCheck, LucideCircleCheck, LucideMapPin, LucideStar } from '@lucide/angular';

/**
 * Frame of the sign-in, sign-up and account-recovery pages: an illustrated plum panel (the promise of the service) next to
 * the form. On a phone the panel shrinks to a short banner above the form, so the form stays first.
 */
@Component({
  selector: 'app-auth-layout',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, LucideCalendarCheck, LucideCircleCheck, LucideMapPin, LucideStar],
  styles: `
    :host {
      display: block;
    }
    .auth-shell {
      display: grid;
      width: min(100% - 2rem, 72rem);
      margin-inline: auto;
      overflow: hidden;
      border: 1px solid var(--kb-border);
      border-radius: var(--radius-xl);
      background: var(--kb-card);
      box-shadow: var(--shadow-luxe);

      @media (min-width: 900px) {
        grid-template-columns: minmax(0, 0.95fr) minmax(0, 1.05fr);
        min-height: 40rem;
      }
    }

    /* ───── Illustrated panel ───── */
    .aside {
      position: relative;
      display: grid;
      align-content: space-between;
      gap: 1.5rem;
      padding: clamp(1.5rem, 4vw, 2.75rem);
      overflow: hidden;
      color: #fff;
      background:
        radial-gradient(80% 60% at 85% 10%, rgb(201 163 106 / 35%), transparent 60%),
        radial-gradient(70% 60% at 0% 100%, rgb(165 107 191 / 55%), transparent 65%),
        linear-gradient(150deg, #7b358e 0%, #4b0d61 55%, #2b0739 100%);
    }
    .logo {
      display: inline-flex;
      align-items: center;
      gap: 0.6rem;
      color: #fff;
      font-size: var(--fs-lg);
      font-weight: 700;
      text-decoration: none;
      justify-self: start;

      .mark {
        display: inline-grid;
        place-items: center;
        width: 2.25rem;
        height: 2.25rem;
        border-radius: 0.8rem;
        background: rgb(255 255 255 / 16%);
        box-shadow: inset 0 1px 0 rgb(255 255 255 / 30%);
      }
      svg {
        width: 1.15rem;
        height: 1.15rem;
      }
      em {
        font-style: normal;
        color: #f4ead9;
      }
    }
    .scene {
      position: relative;
      justify-self: center;
      width: min(100%, 22rem);
      aspect-ratio: 1;
    }
    .scene > svg {
      width: 100%;
      height: 100%;
    }
    .chip {
      position: absolute;
      display: inline-flex;
      align-items: center;
      gap: 0.4rem;
      padding: 0.45rem 0.8rem;
      border-radius: var(--radius-pill);
      background: rgb(255 255 255 / 94%);
      color: #3f0f52;
      font-size: var(--fs-xs);
      font-weight: 700;
      white-space: nowrap;
      box-shadow: 0 12px 30px -12px rgb(20 0 30 / 55%);

      svg {
        width: 0.95rem;
        height: 0.95rem;
        color: #6a2a85;
      }
    }
    .chip--rating {
      top: 12%;
      right: -2%;
    }
    .chip--booked {
      bottom: 10%;
      left: -4%;
    }
    .pitch h2 {
      margin-bottom: 0.75rem;
      color: #fff;
      font-size: var(--fs-h2);
    }
    .pitch h2 em {
      color: #f4ead9;
    }
    .points {
      display: grid;
      gap: 0.55rem;
      margin: 0;
      padding: 0;
      list-style: none;
      color: rgb(255 255 255 / 88%);

      li {
        display: flex;
        align-items: center;
        gap: 0.55rem;
      }
      svg {
        flex-shrink: 0;
        width: 1.1rem;
        height: 1.1rem;
        color: #dcbb85;
      }
    }

    /* On a phone: a short banner, no illustration, the form right below. */
    @media (max-width: 899px) {
      .aside {
        gap: 0.75rem;
      }
      .scene,
      .points {
        display: none;
      }
      .pitch h2 {
        margin: 0;
        font-size: var(--fs-h3);
      }
    }
  `,
  template: `
    <section class="auth-page" [attr.aria-labelledby]="labelledBy()">
      <div class="auth-shell">
        <div class="aside">
          <a class="logo" routerLink="/" aria-label="Keke Beauty — accueil">
            <span class="mark"><svg lucideMapPin aria-hidden="true"></svg></span>
            <span>Keke <em>Beauty</em></span>
          </a>

          <div class="scene" aria-hidden="true">
            <svg viewBox="0 0 360 360" role="presentation">
              <defs>
                <linearGradient id="kb-mirror" x1="0" y1="0" x2="1" y2="1">
                  <stop offset="0" stop-color="#f7eefa" stop-opacity="0.95" />
                  <stop offset="1" stop-color="#c39ad5" stop-opacity="0.75" />
                </linearGradient>
                <linearGradient id="kb-skin" x1="0" y1="0" x2="1" y2="1">
                  <stop offset="0" stop-color="#a86b4c" />
                  <stop offset="1" stop-color="#6e3f2a" />
                </linearGradient>
                <linearGradient id="kb-gold" x1="0" y1="0" x2="0" y2="1">
                  <stop offset="0" stop-color="#f4ead9" />
                  <stop offset="1" stop-color="#c9a36a" />
                </linearGradient>
              </defs>
              <!-- halo -->
              <circle cx="180" cy="180" r="160" fill="#fff" fill-opacity="0.06" />
              <circle cx="180" cy="180" r="118" fill="#fff" fill-opacity="0.06" />
              <!-- arched mirror -->
              <path d="M96 318V160a84 84 0 0 1 168 0v158z" fill="url(#kb-gold)" />
              <path d="M108 318V162a72 72 0 0 1 144 0v156z" fill="url(#kb-mirror)" />
              <!-- woman: hair bun, profile, shoulders -->
              <circle cx="186" cy="132" r="30" fill="#2b0739" />
              <circle cx="188" cy="178" r="40" fill="#2b0739" />
              <path
                d="M170 170c0-17 12-28 26-28 13 0 22 11 22 24 0 6-2 10-1 14l7 9c2 2 0 5-3 5h-5c-1 6-3 14-12 15l-8 1 1 14h-25z"
                fill="url(#kb-skin)"
              />
              <path d="M132 318c4-42 24-70 56-70s52 28 56 70z" fill="#6a2a85" />
              <path d="M170 250c6 10 30 10 36 0v-6h-36z" fill="url(#kb-skin)" />
              <circle cx="219" cy="190" r="3.5" fill="#c9a36a" />
              <!-- vanity: nail polish and comb -->
              <rect
                x="64"
                y="318"
                width="232"
                height="10"
                rx="5"
                fill="#f4ead9"
                fill-opacity="0.9"
              />
              <rect x="252" y="286" width="22" height="32" rx="6" fill="#e05a9a" />
              <rect x="257" y="272" width="12" height="16" rx="3" fill="#2b0739" />
              <path d="M78 300h44v18H78z" fill="#f4ead9" fill-opacity="0.85" />
              <path
                d="M82 300v-10M88 300v-10M94 300v-10M100 300v-10M106 300v-10M112 300v-10M118 300v-10"
                stroke="#f4ead9"
                stroke-opacity="0.85"
                stroke-width="3"
                stroke-linecap="round"
              />
              <!-- sparkles -->
              <path d="M292 92l5 13 13 5-13 5-5 13-5-13-13-5 13-5z" fill="#f4ead9" />
              <path d="M70 120l3 8 8 3-8 3-3 8-3-8-8-3 8-3z" fill="#f4ead9" fill-opacity="0.8" />
              <path
                d="M300 220l2.5 6 6 2.5-6 2.5-2.5 6-2.5-6-6-2.5 6-2.5z"
                fill="#fff"
                fill-opacity="0.7"
              />
            </svg>
            <span class="chip chip--rating"
              ><svg lucideStar aria-hidden="true"></svg> 4,9 · salons vérifiés</span
            >
            <span class="chip chip--booked"
              ><svg lucideCalendarCheck aria-hidden="true"></svg> Rendez-vous confirmé</span
            >
          </div>

          <div class="pitch">
            <h2>
              {{ title() }} <em>{{ accent() }}</em>
            </h2>
            <ul class="points">
              @for (point of points(); track point) {
                <li><svg lucideCircleCheck aria-hidden="true"></svg>{{ point }}</li>
              }
            </ul>
          </div>
        </div>

        <div class="auth-card">
          <ng-content />
        </div>
      </div>
    </section>
  `,
})
export class AuthLayout {
  /** Id of the page heading, which names the page region. */
  readonly labelledBy = input.required<string>();
  readonly title = input('Votre beauté,');
  readonly accent = input('à deux pas.');
  readonly points = input<readonly string[]>([
    'Des salons, spas et ongleries vérifiés à Abidjan',
    'Réservation en ligne, confirmation immédiate',
    'Rappels, fidélité et cartes cadeaux',
  ]);
}
