import { ChangeDetectionStrategy, Component, inject, resource, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AdminApiService } from '../../core/admin/admin-api.service';
import { problemMessage } from '../../core/auth/auth.models';
import { ReasonForm } from './shared/reason-form';

const STATUSES = [
  { value: '', label: 'Tous les statuts' },
  { value: 'Active', label: 'Publiés' },
  { value: 'Suspended', label: 'Suspendus' },
  { value: 'PendingReview', label: 'En validation' },
  { value: 'Draft', label: 'Brouillons' },
  { value: 'Rejected', label: 'Rejetés' },
];

const STATUS_LABELS: Record<string, string> = Object.fromEntries(
  STATUSES.filter((s) => s.value).map((s) => [s.value, s.label]),
);

type Pending = { kind: 'status'; suspend: boolean } | { kind: 'media'; mediaId: string } | null;

/** `/admin/etablissements` — moderation: suspend or reactivate a listing, remove a photo, history (PRD ADM-02). */
@Component({
  selector: 'app-admin-establishments-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, DatePipe, ReasonForm],
  styles: `
    .gallery {
      display: flex;
      flex-wrap: wrap;
      gap: 0.75rem;
    }
    figure {
      display: grid;
      gap: 0.375rem;
      margin: 0;
    }
    img {
      width: 8rem;
      height: 6rem;
      border-radius: var(--radius-md);
      object-fit: cover;
    }
  `,
  template: `
    <div class="container admin-page">
      <div>
        <p class="eyebrow">Administration</p>
        <h1>Établissements</h1>
      </div>

      <form class="admin-filters" (ngSubmit)="search()">
        <label>
          Recherche
          <input class="admin-input" name="q" [(ngModel)]="q" placeholder="Nom ou adresse web" />
        </label>
        <label>
          Statut
          <select class="admin-input" name="status" [(ngModel)]="status">
            @for (s of statuses; track s.value) {
              <option [value]="s.value">{{ s.label }}</option>
            }
          </select>
        </label>
        <button type="submit" class="btn btn-primary">Rechercher</button>
      </form>

      @if (message(); as text) {
        <p role="status" class="badge" data-tone="ok">{{ text }}</p>
      }
      @if (error(); as text) {
        <p class="form-error" role="alert">{{ text }}</p>
      }

      @if (list.hasValue()) {
        @let result = list.value();
        <div class="table-scroll">
          <table class="admin-table">
            <caption class="visually-hidden">
              Établissements
            </caption>
            <thead>
              <tr>
                <th scope="col">Nom</th>
                <th scope="col">Propriétaire</th>
                <th scope="col">Formule</th>
                <th scope="col">Statut</th>
                <th scope="col">Création</th>
                <th scope="col"><span class="visually-hidden">Actions</span></th>
              </tr>
            </thead>
            <tbody>
              @for (e of result.items; track e.id) {
                <tr>
                  <td>{{ e.name }}</td>
                  <td>{{ e.ownerEmail ?? '—' }}</td>
                  <td>{{ e.planCode }}</td>
                  <td>
                    <span class="badge" [attr.data-tone]="tone(e.status)">{{
                      statusLabel(e.status)
                    }}</span>
                  </td>
                  <td>{{ e.createdAt | date: 'dd/MM/yyyy' }}</td>
                  <td>
                    <button type="button" class="btn btn-ghost btn-sm" (click)="open(e.id)">
                      Modérer
                    </button>
                  </td>
                </tr>
              } @empty {
                <tr>
                  <td colspan="6">Aucun établissement ne correspond.</td>
                </tr>
              }
            </tbody>
          </table>
        </div>
        <div class="pager">
          <button
            type="button"
            class="btn btn-ghost btn-sm"
            [disabled]="page() <= 1"
            (click)="go(-1)"
          >
            Précédent
          </button>
          <span>Page {{ result.page }} · {{ result.total }} résultat(s)</span>
          <button
            type="button"
            class="btn btn-ghost btn-sm"
            [disabled]="result.page * result.pageSize >= result.total"
            (click)="go(1)"
          >
            Suivant
          </button>
        </div>
      } @else if (list.error()) {
        <p class="form-error" role="alert">Impossible de charger les établissements.</p>
      } @else {
        <p role="status">Chargement…</p>
      }

      @if (detail.hasValue() && selected()) {
        @let d = detail.value();
        <section class="admin-card" aria-labelledby="detail-title">
          <h2 id="detail-title">{{ d.establishment.name }}</h2>
          <p>
            {{ statusLabel(d.establishment.status) }} · formule {{ d.establishment.planCode }} ·
            {{ d.establishment.ownerEmail ?? 'sans propriétaire' }}
          </p>

          @if (d.media.length > 0) {
            <h3>Photos</h3>
            <div class="gallery">
              @for (m of d.media; track m.id) {
                <figure>
                  <img [src]="m.url" [alt]="m.altText ?? 'Photo de l’établissement'" />
                  <button
                    type="button"
                    class="btn btn-ghost btn-sm"
                    (click)="pending.set({ kind: 'media', mediaId: m.id })"
                  >
                    Retirer…
                  </button>
                </figure>
              }
            </div>
          }

          @if (d.history.length > 0) {
            <h3>Historique de modération</h3>
            <ul>
              @for (h of d.history; track h.id) {
                <li>
                  {{ h.at | date: 'dd/MM/yyyy HH:mm' }} — {{ h.action }} par {{ h.actorEmail }}
                </li>
              }
            </ul>
          }

          @if (pending(); as action) {
            <app-reason-form
              [confirmLabel]="confirmLabel(action)"
              [busy]="busy()"
              (confirmed)="apply(d.establishment.id, action, $event)"
              (cancelled)="pending.set(null)"
            />
          } @else {
            <div class="row-actions">
              @if (d.establishment.status === 'Active') {
                <button
                  type="button"
                  class="btn btn-primary btn-sm"
                  (click)="pending.set({ kind: 'status', suspend: true })"
                >
                  Suspendre la fiche…
                </button>
              } @else if (d.establishment.status === 'Suspended') {
                <button
                  type="button"
                  class="btn btn-primary btn-sm"
                  (click)="pending.set({ kind: 'status', suspend: false })"
                >
                  Rétablir la fiche…
                </button>
              }
              <button type="button" class="btn btn-ghost btn-sm" (click)="selected.set(null)">
                Fermer
              </button>
            </div>
          }
        </section>
      }
    </div>
  `,
})
export class EstablishmentsPage {
  private readonly api = inject(AdminApiService);

  protected readonly statuses = STATUSES;
  protected q = '';
  protected status = '';

  protected readonly page = signal(1);
  protected readonly applied = signal({ q: '', status: '' });
  protected readonly selected = signal<string | null>(null);
  protected readonly pending = signal<Pending>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly message = signal<string | null>(null);

  protected readonly list = resource({
    params: () => ({ ...this.applied(), page: this.page() }),
    loader: ({ params }) => this.api.establishments(params),
  });
  protected readonly detail = resource({
    params: () => this.selected(),
    loader: ({ params }) =>
      params ? this.api.establishment(params) : Promise.reject(new Error('none')),
  });

  protected statusLabel(status: string): string {
    return STATUS_LABELS[status] ?? status;
  }

  protected tone(status: string): string {
    return status === 'Active'
      ? 'ok'
      : status === 'Suspended' || status === 'Rejected'
        ? 'bad'
        : '';
  }

  protected confirmLabel(action: NonNullable<Pending>): string {
    return action.kind === 'media' ? 'Retirer la photo' : action.suspend ? 'Suspendre' : 'Rétablir';
  }

  protected search(): void {
    this.page.set(1);
    this.applied.set({ q: this.q.trim(), status: this.status });
  }

  protected go(step: number): void {
    this.page.update((p) => Math.max(1, p + step));
  }

  protected open(id: string): void {
    this.pending.set(null);
    this.error.set(null);
    this.message.set(null);
    this.selected.set(id);
  }

  protected async apply(id: string, action: NonNullable<Pending>, reason: string): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      if (action.kind === 'media') {
        await this.api.removeMedia(id, action.mediaId, reason);
        this.message.set('Photo retirée.');
      } else if (action.suspend) {
        await this.api.suspendEstablishment(id, reason);
        this.message.set('Fiche suspendue.');
      } else {
        await this.api.reactivateEstablishment(id, reason);
        this.message.set('Fiche rétablie.');
      }

      this.pending.set(null);
      this.detail.reload();
      this.list.reload();
    } catch (e) {
      this.error.set(problemMessage(e));
    } finally {
      this.busy.set(false);
    }
  }
}
