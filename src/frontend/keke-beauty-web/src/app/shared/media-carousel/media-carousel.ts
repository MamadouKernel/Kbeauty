import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  computed,
  input,
  signal,
  viewChild,
} from '@angular/core';
import { LucideChevronLeft, LucideChevronRight } from '@lucide/angular';
import type { MediaItem } from '../../core/establishments/establishment.service';

/**
 * Photo and short-video carousel (PRD EST-01). Native scroll-snap (swipe on touch, scroll on desktop) plus
 * previous/next buttons, thumbnails and arrow keys. Only the visible video keeps playing.
 */
@Component({
  selector: 'app-media-carousel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [LucideChevronLeft, LucideChevronRight],
  styles: `
    :host {
      display: block;
    }
    .viewport {
      position: relative;
    }
    .track {
      display: flex;
      overflow-x: auto;
      scroll-snap-type: x mandatory;
      scrollbar-width: none;
      border-radius: var(--radius-lg);
      background: var(--kb-primary-50);
    }
    .track::-webkit-scrollbar {
      display: none;
    }
    .slide {
      flex: 0 0 100%;
      aspect-ratio: 3 / 2;
      scroll-snap-align: center;
    }
    .slide img,
    .slide video {
      width: 100%;
      height: 100%;
      object-fit: cover;
    }
    .nav {
      position: absolute;
      top: 50%;
      display: grid;
      place-items: center;
      width: 2.75rem;
      height: 2.75rem;
      border: 0;
      border-radius: 50%;
      background: rgb(255 255 255 / 92%);
      box-shadow: var(--shadow-soft);
      color: var(--kb-primary-800);
      cursor: pointer;
      transform: translateY(-50%);
      transition: background-color 200ms ease;
    }
    .nav:hover {
      background: #fff;
    }
    .nav svg {
      width: 1.5rem;
      height: 1.5rem;
    }
    .prev {
      left: 0.75rem;
    }
    .next {
      right: 0.75rem;
    }
    .counter {
      position: absolute;
      right: 0.75rem;
      bottom: 0.75rem;
      padding: 0.25rem 0.625rem;
      border-radius: var(--radius-pill);
      background: rgb(54 23 79 / 72%);
      color: #fff;
      font-size: var(--fs-xs);
      font-weight: 600;
    }
    .thumbs {
      display: flex;
      gap: 0.5rem;
      margin-top: 0.75rem;
      overflow-x: auto;
    }
    .thumb {
      flex: none;
      width: 4.5rem;
      height: 3.25rem;
      padding: 0;
      overflow: hidden;
      border: 2px solid transparent;
      border-radius: var(--radius-sm);
      background: var(--kb-primary-50);
      cursor: pointer;
      opacity: 0.7;
      transition:
        opacity 200ms ease,
        border-color 200ms ease;
    }
    .thumb[aria-current='true'] {
      border-color: var(--kb-primary-600);
      opacity: 1;
    }
    .thumb img {
      width: 100%;
      height: 100%;
      object-fit: cover;
    }
    .empty {
      display: grid;
      place-items: center;
      aspect-ratio: 3 / 2;
      border-radius: var(--radius-lg);
      background: linear-gradient(135deg, var(--kb-primary-300), var(--kb-primary-700));
      color: #fff;
    }
  `,
  template: `
    @if (media().length === 0) {
      <div class="empty" role="img" aria-label="Aucun visuel disponible"></div>
    } @else {
      <div
        class="viewport"
        role="group"
        aria-roledescription="carrousel"
        aria-label="Photos et vidéos de l'établissement"
      >
        <!-- The scrollable strip is the keyboard stop: arrow keys move between the slides (and it scrolls natively too). -->
        <div
          #track
          class="track"
          role="group"
          aria-label="Diapositives"
          tabindex="0"
          (keydown.arrowLeft)="go(index() - 1)"
          (keydown.arrowRight)="go(index() + 1)"
          (scroll)="onScroll()"
        >
          @for (item of media(); track item.url; let i = $index) {
            <figure
              class="slide"
              role="group"
              aria-roledescription="diapositive"
              [attr.aria-label]="i + 1 + ' sur ' + media().length"
              style="margin: 0"
            >
              @if (item.kind === 'Video') {
                <video
                  controls
                  preload="metadata"
                  playsinline
                  [poster]="item.posterUrl ?? ''"
                  [attr.aria-label]="item.alt ?? 'Vidéo'"
                >
                  <source
                    [src]="item.url"
                    [type]="item.url.endsWith('.webm') ? 'video/webm' : 'video/mp4'"
                  />
                </video>
              } @else {
                <img
                  [src]="item.url"
                  [alt]="item.alt ?? ''"
                  width="1200"
                  height="800"
                  [attr.loading]="i === 0 ? 'eager' : 'lazy'"
                />
              }
            </figure>
          }
        </div>

        @if (media().length > 1) {
          <button
            type="button"
            class="nav prev"
            aria-label="Visuel précédent"
            [disabled]="index() === 0"
            (click)="go(index() - 1)"
          >
            <svg lucideChevronLeft aria-hidden="true"></svg>
          </button>
          <button
            type="button"
            class="nav next"
            aria-label="Visuel suivant"
            [disabled]="index() === media().length - 1"
            (click)="go(index() + 1)"
          >
            <svg lucideChevronRight aria-hidden="true"></svg>
          </button>
        }
        <span class="counter" aria-live="polite">{{ index() + 1 }} / {{ media().length }}</span>
      </div>

      @if (media().length > 1) {
        <div class="thumbs">
          @for (item of media(); track item.url; let i = $index) {
            <button
              type="button"
              class="thumb"
              [attr.aria-current]="i === index()"
              [attr.aria-label]="'Afficher le visuel ' + (i + 1)"
              (click)="go(i)"
            >
              <img
                [src]="item.kind === 'Video' ? (item.posterUrl ?? '') : item.url"
                alt=""
                width="72"
                height="52"
                loading="lazy"
              />
            </button>
          }
        </div>
      }
    }
  `,
})
export class MediaCarousel {
  readonly media = input.required<readonly MediaItem[]>();

  private readonly track = viewChild<ElementRef<HTMLElement>>('track');
  protected readonly index = signal(0);
  private readonly reducedMotion = computed(
    () =>
      typeof matchMedia !== 'undefined' && matchMedia('(prefers-reduced-motion: reduce)').matches,
  );

  protected go(target: number): void {
    const element = this.track()?.nativeElement;
    const last = this.media().length - 1;
    if (!element || last < 0) {
      return;
    }

    const next = Math.min(Math.max(target, 0), last);
    element.scrollTo({
      left: next * element.clientWidth,
      behavior: this.reducedMotion() ? 'auto' : 'smooth',
    });
    this.index.set(next);
  }

  protected onScroll(): void {
    const element = this.track()?.nativeElement;
    if (!element || element.clientWidth === 0) {
      return;
    }

    const current = Math.round(element.scrollLeft / element.clientWidth);
    if (current !== this.index()) {
      this.index.set(current);
      element.querySelectorAll('video').forEach((video) => video.pause());
    }
  }
}
