import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import {
  LucideBrush,
  LucideDroplet,
  LucideFlower2,
  LucideHandHeart,
  LucidePalette,
  LucideScissors,
  LucideSmile,
  LucideSparkles,
} from '@lucide/angular';

/** Maps the semantic icon key sent by the API to an SVG icon (no emoji as icons, per design system). */
@Component({
  selector: 'app-category-icon',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    LucideBrush,
    LucideDroplet,
    LucideFlower2,
    LucideHandHeart,
    LucidePalette,
    LucideScissors,
    LucideSmile,
    LucideSparkles,
  ],
  template: `
    @switch (name()) {
      @case ('hair') {
        <svg lucideScissors aria-hidden="true"></svg>
      }
      @case ('spa') {
        <svg lucideFlower2 aria-hidden="true"></svg>
      }
      @case ('massage') {
        <svg lucideHandHeart aria-hidden="true"></svg>
      }
      @case ('nails') {
        <svg lucideSparkles aria-hidden="true"></svg>
      }
      @case ('barber') {
        <svg lucideSmile aria-hidden="true"></svg>
      }
      @case ('skincare') {
        <svg lucideDroplet aria-hidden="true"></svg>
      }
      @case ('makeup') {
        <svg lucidePalette aria-hidden="true"></svg>
      }
      @default {
        <svg lucideBrush aria-hidden="true"></svg>
      }
    }
  `,
  styles: `
    :host {
      display: inline-flex;
      width: 1.5rem;
      height: 1.5rem;
    }
    svg {
      width: 100%;
      height: 100%;
    }
  `,
})
export class CategoryIcon {
  readonly name = input.required<string>();
}
