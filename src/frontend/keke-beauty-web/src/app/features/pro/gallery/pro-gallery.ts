import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  resource,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { PartnerService } from '../../../core/partner/partner.service';
import { GalleryManager } from './gallery-manager';

/** `/pro/etablissements/:id/galerie` — manage the photos of an existing establishment. */
@Component({
  selector: 'app-pro-gallery',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, GalleryManager],
  styles: `
    .page {
      display: grid;
      gap: 1.25rem;
      padding-block: clamp(1.5rem, 4vw, 3rem);
    }
  `,
  template: `
    <div class="container page">
      <a class="link" routerLink="/pro">← Mes établissements</a>
      @if (detail.hasValue()) {
        @let e = detail.value();
        <p class="eyebrow">{{ e.name }}</p>
        <h1>Galerie <em>photos</em></h1>
        @if (!e.isEditable) {
          <p class="form-error" role="status">
            Ce dossier est verrouillé ({{
              e.status === 'PendingReview' ? 'en cours de validation' : 'non modifiable'
            }}).
          </p>
        }
        <app-gallery-manager
          [establishmentId]="id()"
          [media]="e.media"
          [videos]="e.videos ?? []"
          [videosAllowed]="e.videosAllowed"
          [maxVideos]="e.maxVideos"
          [maxPhotos]="e.maxPhotos"
          [editable]="e.isEditable"
          (changed)="detail.reload()"
        />
      } @else if (detail.error()) {
        <p class="form-error" role="alert">Impossible de charger cet établissement.</p>
      } @else {
        <p role="status">Chargement…</p>
      }
    </div>
  `,
})
export class ProGallery {
  /** Bound from the `:id` route parameter. */
  readonly id = input.required<string>();

  private readonly partner = inject(PartnerService);
  protected readonly request = computed(() => this.id());
  protected readonly detail = resource({
    params: () => this.request(),
    loader: ({ params }) => this.partner.get(params),
  });
}
