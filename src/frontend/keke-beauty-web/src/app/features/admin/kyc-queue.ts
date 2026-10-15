import { ChangeDetectionStrategy, Component, inject, resource, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { ReviewService } from '../../core/admin/review.service';

const TABS = [
  { status: 'PendingReview', label: 'À examiner' },
  { status: 'ChangesRequested', label: 'Corrections demandées' },
  { status: 'Approved', label: 'Approuvés' },
  { status: 'Rejected', label: 'Refusés' },
] as const;

/** `/admin/kyc` — review queue, oldest first (PRD ADM-03). */
@Component({
  selector: 'app-kyc-queue',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, DatePipe],
  styles: `
    .page {
      display: grid;
      gap: 1.25rem;
      padding-block: clamp(1.5rem, 4vw, 3rem);
    }
    .tabs {
      display: flex;
      flex-wrap: wrap;
      gap: 0.5rem;
    }
    .tab {
      min-height: 2.75rem;
      padding: 0.5rem 1.125rem;
      border: 1.5px solid var(--kb-border);
      border-radius: var(--radius-pill);
      background: #fff;
      font: inherit;
      font-weight: 600;
      cursor: pointer;

      &[aria-pressed='true'] {
        border-color: var(--kb-primary-600);
        background: var(--kb-primary-50);
        color: var(--kb-primary-800);
      }
    }
    table {
      width: 100%;
      border-collapse: collapse;
      background: #fff;
      border: 1px solid var(--kb-border);
      border-radius: var(--radius-md);
      overflow: hidden;
    }
    th,
    td {
      padding: 0.75rem 1rem;
      text-align: left;
      border-bottom: 1px solid var(--kb-border);
    }
    th {
      font-size: var(--fs-xs);
      text-transform: uppercase;
      letter-spacing: 0.04em;
      color: var(--kb-ink-muted);
    }
    .scroll {
      position: relative;
      overflow-x: auto;
    }
    .pager {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 0.75rem;
    }
  `,
  template: `
    <div class="container page">
      <a class="link" routerLink="/admin">← Administration</a>
      <div>
        <p class="eyebrow">Validation des partenaires</p>
        <h1>Dossiers KYC</h1>
      </div>

      <div class="tabs" role="group" aria-label="Filtrer par statut">
        @for (tab of tabs; track tab.status) {
          <button
            type="button"
            class="tab"
            [attr.aria-pressed]="status() === tab.status"
            (click)="select(tab.status)"
          >
            {{ tab.label }}
          </button>
        }
      </div>

      @if (queue.hasValue()) {
        @let q = queue.value();
        @if (q.items.length === 0) {
          <p class="empty" role="status">Aucun dossier dans cette catégorie.</p>
        } @else {
          <div class="scroll">
            <table>
              <caption class="visually-hidden">
                Dossiers, du plus ancien au plus récent
              </caption>
              <thead>
                <tr>
                  <th scope="col">Établissement</th>
                  <th scope="col">Gérant</th>
                  <th scope="col">Commune</th>
                  <th scope="col">Soumis le</th>
                  <th scope="col"><span class="visually-hidden">Action</span></th>
                </tr>
              </thead>
              <tbody>
                @for (item of q.items; track item.reviewId) {
                  <tr>
                    <th scope="row">{{ item.establishmentName }}</th>
                    <td>
                      {{ item.ownerName }}<br /><small>{{ item.ownerEmail }}</small>
                    </td>
                    <td>{{ item.communeName }}</td>
                    <td>{{ item.submittedAt | date: 'dd/MM/yyyy HH:mm' }}</td>
                    <td>
                      <a class="btn btn-primary btn-sm" [routerLink]="['/admin/kyc', item.reviewId]"
                        >Examiner</a
                      >
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
          <div class="pager">
            <button
              type="button"
              class="btn btn-ghost btn-sm"
              [disabled]="page() === 1"
              (click)="go(page() - 1)"
            >
              Précédent
            </button>
            <span>Page {{ q.page }} · {{ q.total }} dossier(s)</span>
            <button
              type="button"
              class="btn btn-ghost btn-sm"
              [disabled]="q.page * q.pageSize >= q.total"
              (click)="go(page() + 1)"
            >
              Suivant
            </button>
          </div>
        }
      } @else if (queue.error()) {
        <p class="form-error" role="alert">Impossible de charger la file. Réessayez.</p>
      } @else {
        <p role="status">Chargement…</p>
      }
    </div>
  `,
})
export class KycQueue {
  private readonly reviews = inject(ReviewService);

  protected readonly tabs = TABS;
  protected readonly status = signal<string>('PendingReview');
  protected readonly page = signal(1);

  protected readonly queue = resource({
    params: () => ({ status: this.status(), page: this.page() }),
    loader: ({ params }) => this.reviews.queue(params.status, params.page),
  });

  protected select(status: string): void {
    this.status.set(status);
    this.page.set(1);
  }

  protected go(page: number): void {
    this.page.set(page);
  }
}
