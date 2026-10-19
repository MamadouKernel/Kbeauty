import { ChangeDetectionStrategy, Component, inject, input, output, signal } from '@angular/core';
import {
  LucideArrowLeft,
  LucideArrowRight,
  LucideImagePlus,
  LucideStar,
  LucideTrash2,
  LucideVideo,
} from '@lucide/angular';
import { problemMessage } from '../../../core/auth/auth.models';
import { PartnerMediaItem, PartnerService } from '../../../core/partner/partner.service';

const ACCEPTED = 'image/jpeg,image/png,image/webp';
const ACCEPTED_VIDEOS = 'video/mp4,video/webm';
// Mirrors PartnerMediaService.MaxVideos and the Media:MaxVideoMegabytes default on the API.
const MAX_VIDEO_MEGABYTES = 50;
const POSTER_WIDTH = 960;

interface VideoPreview {
  readonly poster: Blob | null;
  readonly durationSeconds: number | null;
}

/** Reads the length and a first-frame poster in the browser; a clip the browser cannot decode is still uploaded without them. */
function readVideoPreview(file: File): Promise<VideoPreview | null> {
  return new Promise((resolve) => {
    const url = URL.createObjectURL(file);
    const video = document.createElement('video');
    const finish = (result: VideoPreview | null): void => {
      URL.revokeObjectURL(url);
      resolve(result);
    };
    video.preload = 'metadata';
    video.muted = true;
    video.playsInline = true;
    video.onerror = () => finish(null);
    video.onloadedmetadata = () => {
      video.currentTime = Math.min(0.5, (video.duration || 1) / 2);
    };
    video.onseeked = () => {
      const duration = Number.isFinite(video.duration) ? video.duration : null;
      const scale = Math.min(1, POSTER_WIDTH / (video.videoWidth || POSTER_WIDTH));
      const canvas = document.createElement('canvas');
      canvas.width = Math.max(1, Math.round((video.videoWidth || POSTER_WIDTH) * scale));
      canvas.height = Math.max(1, Math.round((video.videoHeight || 540) * scale));
      canvas.getContext('2d')?.drawImage(video, 0, 0, canvas.width, canvas.height);
      canvas.toBlob((poster) => finish({ poster, durationSeconds: duration }), 'image/jpeg', 0.85);
    };
    video.src = url;
  });
}

/** Photo gallery editor (PRD SVC-03): upload, cover, order and delete. The parent reloads on `changed`. */
@Component({
  selector: 'app-gallery-manager',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    LucideArrowLeft,
    LucideArrowRight,
    LucideImagePlus,
    LucideStar,
    LucideTrash2,
    LucideVideo,
  ],
  styles: `
    :host {
      display: grid;
      gap: 1rem;
    }
    .head {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: space-between;
      gap: 0.75rem;
    }
    .counter {
      font-weight: 600;
      color: var(--kb-ink-muted);
    }
    .grid {
      display: grid;
      gap: 1rem;
      grid-template-columns: repeat(auto-fill, minmax(min(100%, 10.5rem), 1fr));
    }
    figure {
      display: grid;
      margin: 0;
      overflow: hidden;
      border: 1px solid var(--kb-border);
      border-radius: var(--radius-md);
      background: var(--kb-card);
      box-shadow: var(--shadow-soft);
    }
    figure.cover {
      border-color: var(--kb-primary-500);
    }
    img {
      width: 100%;
      aspect-ratio: 4 / 3;
      object-fit: cover;
      background: var(--kb-primary-50);
    }
    figcaption {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 0.25rem;
      padding: 0.5rem;
    }
    .badge {
      padding: 0.125rem 0.625rem;
      border-radius: var(--radius-pill);
      background: var(--kb-plum-600);
      color: #fff;
      font-size: var(--fs-2xs);
      font-weight: 600;
    }
    .tools {
      display: flex;
      margin-left: auto;
    }
    .icon-btn {
      display: inline-grid;
      place-items: center;
      width: 2.75rem;
      height: 2.75rem;
      border: 0;
      border-radius: var(--radius-pill);
      background: transparent;
      color: var(--kb-ink-muted);
      cursor: pointer;

      &:hover:not(:disabled) {
        background: var(--kb-primary-50);
        color: var(--kb-primary-700);
      }
      &:disabled {
        opacity: 0.35;
        cursor: not-allowed;
      }
    }
    .icon-btn svg {
      width: 1.125rem;
      height: 1.125rem;
    }
    .empty {
      padding: 2rem 1rem;
      border: 1.5px dashed var(--kb-border);
      border-radius: var(--radius-md);
      text-align: center;
      color: var(--kb-ink-muted);
    }
    input[type='file'] {
      position: absolute;
      width: 1px;
      height: 1px;
      opacity: 0;
    }
    .upload:has(input:focus-visible) {
      outline: 3px solid var(--kb-primary-400);
      outline-offset: 3px;
    }
    .videos {
      display: grid;
      gap: 1rem;
      padding-top: 1rem;
      border-top: 1px solid var(--kb-border);
    }
    video {
      width: 100%;
      aspect-ratio: 16 / 9;
      object-fit: cover;
      background: #000;
    }
  `,
  template: `
    <div class="head">
      <p class="counter" aria-live="polite">{{ media().length }} / {{ maxPhotos() }} photos</p>
      @if (editable()) {
        <label class="btn btn-primary upload" [attr.aria-disabled]="!canAdd() || busy()">
          <svg lucideImagePlus aria-hidden="true" width="18" height="18"></svg>
          {{ busy() ? 'Envoi…' : 'Ajouter des photos' }}
          <input
            type="file"
            multiple
            [accept]="accepted"
            [disabled]="!canAdd() || busy()"
            (change)="upload($any($event.target))"
          />
        </label>
      }
    </div>
    <p class="hint">JPEG, PNG ou WebP, 10 Mo maximum par photo.</p>

    @if (error()) {
      <p class="form-error" role="alert">{{ error() }}</p>
    }

    @if (media().length === 0) {
      <p class="empty">Aucune photo pour l'instant. Ajoutez au moins une photo de la devanture.</p>
    } @else {
      <ul class="grid">
        @for (item of media(); track item.id; let first = $first; let last = $last) {
          <li>
            <figure [class.cover]="item.isCover">
              <img [src]="item.thumbUrl ?? item.url" alt="" loading="lazy" />
              <figcaption>
                @if (item.isCover) {
                  <span class="badge">Couverture</span>
                }
                @if (editable()) {
                  <span class="tools">
                    <button
                      type="button"
                      class="icon-btn"
                      [disabled]="item.isCover || busy()"
                      [attr.aria-label]="'Choisir la photo ' + ($index + 1) + ' comme couverture'"
                      (click)="makeCover(item)"
                    >
                      <svg lucideStar aria-hidden="true"></svg>
                    </button>
                    <button
                      type="button"
                      class="icon-btn"
                      [disabled]="first || busy()"
                      [attr.aria-label]="'Déplacer la photo ' + ($index + 1) + ' vers la gauche'"
                      (click)="move($index, -1)"
                    >
                      <svg lucideArrowLeft aria-hidden="true"></svg>
                    </button>
                    <button
                      type="button"
                      class="icon-btn"
                      [disabled]="last || busy()"
                      [attr.aria-label]="'Déplacer la photo ' + ($index + 1) + ' vers la droite'"
                      (click)="move($index, 1)"
                    >
                      <svg lucideArrowRight aria-hidden="true"></svg>
                    </button>
                    <button
                      type="button"
                      class="icon-btn"
                      [disabled]="busy()"
                      [attr.aria-label]="'Supprimer la photo ' + ($index + 1)"
                      (click)="remove(item)"
                    >
                      <svg lucideTrash2 aria-hidden="true"></svg>
                    </button>
                  </span>
                }
              </figcaption>
            </figure>
          </li>
        }
      </ul>
    }

    <section class="videos" aria-labelledby="videos-title">
      <div class="head">
        <h2 id="videos-title" class="counter">
          Vidéos ({{ videos().length }} / {{ maxVideos() }})
        </h2>
        @if (editable() && videosAllowed()) {
          <label class="btn btn-secondary upload" [attr.aria-disabled]="!canAddVideo() || busy()">
            <svg lucideVideo aria-hidden="true" width="18" height="18"></svg>
            {{ busy() ? 'Envoi…' : 'Ajouter une vidéo' }}
            <input
              type="file"
              [accept]="acceptedVideos"
              [disabled]="!canAddVideo() || busy()"
              (change)="uploadVideo($any($event.target))"
            />
          </label>
        }
      </div>
      @if (!videosAllowed()) {
        <p class="hint">Les vidéos sont réservées à la formule Premium.</p>
      } @else {
        <p class="hint">MP4 ou WebM, {{ maxVideoMegabytes }} Mo maximum par vidéo.</p>
      }
      @if (videos().length > 0) {
        <ul class="grid">
          @for (item of videos(); track item.id) {
            <li>
              <figure>
                <video
                  controls
                  preload="metadata"
                  playsinline
                  [poster]="item.posterUrl ?? ''"
                  [src]="item.url"
                  [attr.aria-label]="'Vidéo ' + ($index + 1)"
                ></video>
                <figcaption>
                  @if (editable()) {
                    <span class="tools">
                      <button
                        type="button"
                        class="icon-btn"
                        [disabled]="busy()"
                        [attr.aria-label]="'Supprimer la vidéo ' + ($index + 1)"
                        (click)="remove(item)"
                      >
                        <svg lucideTrash2 aria-hidden="true"></svg>
                      </button>
                    </span>
                  }
                </figcaption>
              </figure>
            </li>
          }
        </ul>
      }
    </section>
  `,
})
export class GalleryManager {
  readonly establishmentId = input.required<string>();
  readonly media = input.required<readonly PartnerMediaItem[]>();
  readonly maxPhotos = input.required<number>();
  readonly videos = input<readonly PartnerMediaItem[]>([]);
  readonly videosAllowed = input(false);
  readonly maxVideos = input(3);
  readonly editable = input(true);
  readonly changed = output<void>();

  private readonly partner = inject(PartnerService);
  protected readonly accepted = ACCEPTED;
  protected readonly acceptedVideos = ACCEPTED_VIDEOS;
  protected readonly maxVideoMegabytes = MAX_VIDEO_MEGABYTES;
  protected readonly busy = signal(false);
  protected readonly error = signal('');

  protected canAdd(): boolean {
    return this.media().length < this.maxPhotos();
  }

  protected async upload(input: HTMLInputElement): Promise<void> {
    const files = [...(input.files ?? [])];
    input.value = '';
    await this.run(async () => {
      for (const file of files.slice(0, this.maxPhotos() - this.media().length)) {
        await this.partner.uploadPhoto(this.establishmentId(), file);
      }
    });
  }

  protected canAddVideo(): boolean {
    return this.videos().length < this.maxVideos();
  }

  protected async uploadVideo(input: HTMLInputElement): Promise<void> {
    const file = input.files?.[0];
    input.value = '';
    if (!file) {
      return;
    }
    if (file.size > MAX_VIDEO_MEGABYTES * 1024 * 1024) {
      this.error.set(`La vidéo dépasse ${MAX_VIDEO_MEGABYTES} Mo.`);
      return;
    }
    await this.run(async () => {
      const preview = await readVideoPreview(file);
      await this.partner.uploadVideo(
        this.establishmentId(),
        file,
        preview?.poster,
        preview?.durationSeconds,
      );
    });
  }

  protected makeCover(item: PartnerMediaItem): Promise<void> {
    return this.run(() => this.partner.setCover(this.establishmentId(), item.id));
  }

  protected remove(item: PartnerMediaItem): Promise<void> {
    return this.run(() => this.partner.deletePhoto(this.establishmentId(), item.id));
  }

  protected move(index: number, offset: -1 | 1): Promise<void> {
    const ids = this.media().map((m) => m.id);
    const target = index + offset;
    [ids[index], ids[target]] = [ids[target], ids[index]];
    return this.run(() => this.partner.reorderPhotos(this.establishmentId(), ids));
  }

  private async run(action: () => Promise<unknown>): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    try {
      await action();
    } catch (error) {
      this.error.set(problemMessage(error));
    } finally {
      this.busy.set(false);
      this.changed.emit();
    }
  }
}
