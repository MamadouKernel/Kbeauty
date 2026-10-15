import { DatePipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  inject,
  input,
  resource,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { problemMessage } from '../../core/auth/auth.models';
import {
  Decision,
  DocumentSide,
  ReviewDetail,
  ReviewService,
} from '../../core/admin/review.service';
import { DOCUMENT_TYPES } from '../../core/partner/partner.service';

const DECISIONS: readonly {
  readonly value: Decision;
  readonly label: string;
  readonly hint: string;
}[] = [
  { value: 'Approve', label: 'Approuver', hint: "L'établissement est publié dans l'annuaire." },
  {
    value: 'RequestChanges',
    label: 'Demander des corrections',
    hint: 'Le partenaire corrige puis soumet à nouveau (commentaire obligatoire).',
  },
  { value: 'Reject', label: 'Rejeter', hint: 'Refus définitif de ce dossier (motif obligatoire).' },
];

const SIDES: readonly { readonly side: DocumentSide; readonly label: string }[] = [
  { side: 'front', label: 'Recto' },
  { side: 'back', label: 'Verso' },
  { side: 'selfie', label: 'Selfie' },
];

const CHECK_ICONS: Readonly<Record<string, string>> = { ok: '✓', warning: '!', error: '✕' };

/** `/admin/kyc/:id` — one submission: establishment, identity, indicative checks, document viewer and decision. */
@Component({
  selector: 'app-kyc-review',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, RouterLink, DatePipe],
  styles: `
    .page {
      display: grid;
      gap: 1.25rem;
      padding-block: clamp(1.5rem, 4vw, 3rem);
    }
    .layout {
      display: grid;
      gap: 1.25rem;

      @media (min-width: 960px) {
        grid-template-columns: 1fr 1fr;
        align-items: start;
      }
    }
    section {
      display: grid;
      gap: 0.75rem;
      padding: clamp(1rem, 3vw, 1.5rem);
      border: 1px solid var(--kb-border);
      border-radius: var(--radius-lg);
      background: #fff;
      box-shadow: var(--shadow-soft);
    }
    dl {
      display: grid;
      grid-template-columns: max-content 1fr;
      gap: 0.375rem 1rem;
      margin: 0;
    }
    dt {
      color: var(--kb-ink-muted);
    }
    dd {
      margin: 0;
      font-weight: 500;
      overflow-wrap: anywhere;
    }
    .checks {
      display: grid;
      gap: 0.5rem;
    }
    .check {
      display: flex;
      gap: 0.625rem;
      align-items: start;
    }
    .check__icon {
      display: inline-grid;
      flex: none;
      place-items: center;
      width: 1.5rem;
      height: 1.5rem;
      border-radius: 50%;
      font-size: 0.8125rem;
      font-weight: 800;
      color: #fff;
    }
    .check[data-level='ok'] .check__icon {
      background: var(--kb-success);
    }
    .check[data-level='warning'] .check__icon {
      background: var(--kb-warning);
    }
    .check[data-level='error'] .check__icon {
      background: var(--kb-danger);
    }
    .photos {
      display: grid;
      gap: 0.5rem;
      grid-template-columns: repeat(auto-fill, minmax(min(100%, 7rem), 1fr));
    }
    .photos img {
      width: 100%;
      aspect-ratio: 4 / 3;
      border-radius: var(--radius-sm);
      object-fit: cover;
    }
    .viewer img {
      width: 100%;
      max-height: 32rem;
      border: 1px solid var(--kb-border);
      border-radius: var(--radius-md);
      object-fit: contain;
      background: var(--kb-primary-50);
    }
    .stack {
      display: grid;
      gap: 1.25rem;
    }
    .options {
      display: grid;
      gap: 0.5rem;
      padding: 0;
      border: 0;
      margin: 0;
    }
    .buttons {
      display: flex;
      flex-wrap: wrap;
      gap: 0.5rem;
    }
    .option {
      display: grid;
      grid-template-columns: auto 1fr;
      gap: 0.125rem 0.625rem;
      padding: 0.75rem 1rem;
      border: 1.5px solid var(--kb-border);
      border-radius: var(--radius-md);
      cursor: pointer;

      &:has(input:checked) {
        border-color: var(--kb-primary-600);
        background: var(--kb-primary-50);
      }
      input {
        grid-row: span 2;
        align-self: center;
        accent-color: var(--kb-primary-600);
      }
      small {
        grid-column: 2;
        color: var(--kb-ink-muted);
      }
    }
    textarea {
      width: 100%;
      min-height: 6rem;
      padding: 0.75rem;
      border: 1px solid var(--kb-border);
      border-radius: var(--radius-sm);
      font: inherit;
      color: inherit;
    }
  `,
  template: `
    <div class="container page">
      <a class="link" routerLink="/admin/kyc">← File des dossiers</a>

      @if (review.hasValue()) {
        @let r = review.value();
        <div>
          <p class="eyebrow">Dossier soumis le {{ r.submittedAt | date: 'dd/MM/yyyy à HH:mm' }}</p>
          <h1>{{ r.establishmentName }}</h1>
        </div>

        @if (done()) {
          <p class="form-success" role="status">{{ done() }}</p>
        }

        <div class="layout">
          <div class="stack">
            <section aria-labelledby="est-title">
              <h2 id="est-title">Établissement</h2>
              <dl>
                <dt>Catégories</dt>
                <dd>{{ r.categories.join(', ') }}</dd>
                <dt>Adresse</dt>
                <dd>{{ r.addressLine }}, {{ r.communeName }}, {{ r.cityName }}</dd>
                <dt>Téléphone</dt>
                <dd>{{ r.customerServicePhone }}</dd>
                <dt>E-mail</dt>
                <dd>{{ r.email ?? '—' }}</dd>
                <dt>Position</dt>
                <dd>
                  <a class="link" target="_blank" rel="noopener noreferrer" [href]="mapUrl(r)"
                    >{{ r.latitude }}, {{ r.longitude }} (OpenStreetMap)</a
                  >
                </dd>
              </dl>
              <p>{{ r.description }}</p>
              <div class="photos">
                @for (url of r.photoUrls; track url) {
                  <img [src]="url" alt="Photo de l'établissement" loading="lazy" />
                }
              </div>
            </section>

            <section aria-labelledby="owner-title">
              <h2 id="owner-title">Gérant</h2>
              <dl>
                <dt>Compte</dt>
                <dd>{{ r.ownerName }} · {{ r.ownerEmail }}</dd>
              </dl>
            </section>
          </div>

          <div class="stack">
            <section aria-labelledby="id-title">
              <h2 id="id-title">Pièce d'identité</h2>
              @if (r.identity; as id) {
                <dl>
                  <dt>Nom sur la pièce</dt>
                  <dd>{{ id.holderName }}</dd>
                  <dt>Type</dt>
                  <dd>{{ typeLabel(id.documentType) }}</dd>
                  <dt>Numéro</dt>
                  <dd>{{ id.documentNumber }}</dd>
                  <dt>Expiration</dt>
                  <dd>{{ id.documentExpiry ? (id.documentExpiry | date: 'dd/MM/yyyy') : '—' }}</dd>
                  <dt>Statut</dt>
                  <dd>{{ id.status }}</dd>
                </dl>

                <h3>Contrôles indicatifs</h3>
                <ul class="checks">
                  @for (check of r.checks; track check.code) {
                    <li class="check" [attr.data-level]="check.level">
                      <span class="check__icon" aria-hidden="true">{{ icon(check.level) }}</span>
                      <span>{{ check.message }}</span>
                    </li>
                  }
                </ul>

                <h3>Documents</h3>
                <div class="buttons">
                  @for (slot of sides; track slot.side) {
                    <button
                      type="button"
                      class="btn btn-ghost btn-sm"
                      [disabled]="!available(r, slot.side) || loadingDoc()"
                      (click)="open(r.reviewId, slot.side)"
                    >
                      {{ slot.label }}{{ available(r, slot.side) ? '' : ' (absent)' }}
                    </button>
                  }
                </div>
                @if (docError()) {
                  <p class="form-error" role="alert">{{ docError() }}</p>
                }
                @if (documentUrl(); as url) {
                  <div class="viewer">
                    <img [src]="url" [alt]="'Document : ' + viewing()" />
                    <button type="button" class="btn btn-ghost btn-sm" (click)="closeDocument()">
                      Fermer le document
                    </button>
                  </div>
                }
              } @else {
                <p class="form-error" role="status">
                  Aucune pièce d'identité n'est associée à ce dossier.
                </p>
              }
            </section>

            @if (r.status === 'PendingReview' && !done()) {
              <section aria-labelledby="decision-title">
                <h2 id="decision-title">Décision</h2>
                <form (ngSubmit)="decide(r)" novalidate>
                  <fieldset class="options">
                    <legend class="visually-hidden">Décision</legend>
                    @for (option of decisions; track option.value) {
                      <label class="option">
                        <input
                          type="radio"
                          name="decision"
                          [value]="option.value"
                          [ngModel]="decision()"
                          (ngModelChange)="decision.set($event)"
                        />
                        <strong>{{ option.label }}</strong>
                        <small>{{ option.hint }}</small>
                      </label>
                    }
                  </fieldset>

                  <label for="decision-comment"
                    >Commentaire{{
                      decision() === 'Approve' ? ' (facultatif)' : ' (obligatoire)'
                    }}</label
                  >
                  <textarea
                    id="decision-comment"
                    name="comment"
                    maxlength="1000"
                    [ngModel]="comment()"
                    (ngModelChange)="comment.set($event)"
                  ></textarea>

                  @if (error()) {
                    <p class="form-error" role="alert">{{ error() }}</p>
                  }
                  <button class="btn btn-primary" type="submit" [disabled]="busy()">
                    {{ busy() ? 'Envoi…' : 'Enregistrer la décision' }}
                  </button>
                </form>
              </section>
            } @else if (!done()) {
              <section>
                <h2>Décision</h2>
                <p>
                  Dossier déjà traité : <strong>{{ r.status }}</strong
                  >{{ r.comment ? ' — ' + r.comment : '' }}
                </p>
              </section>
            }
          </div>
        </div>
      } @else if (review.error()) {
        <p class="form-error" role="alert">Impossible de charger ce dossier.</p>
      } @else {
        <p role="status">Chargement…</p>
      }
    </div>
  `,
})
export class KycReview {
  /** Bound from the `:id` route parameter (the review id). */
  readonly id = input.required<string>();

  private readonly reviews = inject(ReviewService);

  protected readonly decisions = DECISIONS;
  protected readonly sides = SIDES;
  protected readonly decision = signal<Decision>('Approve');
  protected readonly comment = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly done = signal('');
  protected readonly documentUrl = signal<string | null>(null);
  protected readonly viewing = signal('');
  protected readonly loadingDoc = signal(false);
  protected readonly docError = signal('');

  protected readonly review = resource({
    params: () => this.id(),
    loader: ({ params }) => this.reviews.get(params),
  });

  constructor() {
    inject(DestroyRef).onDestroy(() => this.closeDocument());
  }

  protected icon(level: string): string {
    return CHECK_ICONS[level] ?? '•';
  }

  protected typeLabel(type: string): string {
    return DOCUMENT_TYPES.find((t) => t.value === type)?.label ?? type;
  }

  protected mapUrl(review: ReviewDetail): string {
    return `https://www.openstreetmap.org/?mlat=${review.latitude}&mlon=${review.longitude}#map=17/${review.latitude}/${review.longitude}`;
  }

  protected available(review: ReviewDetail, side: DocumentSide): boolean {
    const identity = review.identity;
    return side === 'front'
      ? !!identity?.hasFront
      : side === 'back'
        ? !!identity?.hasBack
        : !!identity?.hasSelfie;
  }

  protected async open(reviewId: string, side: DocumentSide): Promise<void> {
    this.loadingDoc.set(true);
    this.docError.set('');
    try {
      const url = await this.reviews.openDocument(reviewId, side);
      this.closeDocument();
      this.documentUrl.set(url);
      this.viewing.set(SIDES.find((s) => s.side === side)?.label ?? side);
    } catch (error) {
      this.docError.set(problemMessage(error, 'Impossible de charger le document.'));
    } finally {
      this.loadingDoc.set(false);
    }
  }

  protected closeDocument(): void {
    const url = this.documentUrl();
    if (url) {
      URL.revokeObjectURL(url);
      this.documentUrl.set(null);
    }
  }

  protected async decide(review: ReviewDetail): Promise<void> {
    if (this.decision() !== 'Approve' && !this.comment().trim()) {
      this.error.set('Un commentaire est obligatoire pour cette décision.');
      return;
    }

    this.busy.set(true);
    this.error.set('');
    try {
      await this.reviews.decide(review.reviewId, this.decision(), this.comment());
      this.closeDocument();
      this.done.set(
        this.decision() === 'Approve'
          ? "Dossier approuvé : l'établissement est publié et le partenaire a été prévenu."
          : 'Décision enregistrée : le partenaire a été prévenu.',
      );
      this.review.reload();
    } catch (error) {
      this.error.set(problemMessage(error));
    } finally {
      this.busy.set(false);
    }
  }
}
