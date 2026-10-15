import { ChangeDetectionStrategy, Component, inject, resource, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AdminApiService } from '../../core/admin/admin-api.service';

const ENTITY_TYPES = [
  { value: '', label: 'Tous les objets' },
  { value: 'User', label: 'Comptes' },
  { value: 'Establishment', label: 'Établissements' },
  { value: 'EstablishmentReview', label: 'Dossiers KYC' },
  { value: 'Subscription', label: 'Abonnements' },
  { value: 'Plan', label: 'Formules' },
  { value: 'Category', label: 'Catégories' },
  { value: 'Geo', label: 'Zones' },
  { value: 'Template', label: 'Messages' },
  { value: 'Settings', label: 'Paramètres' },
];

/** `/admin/journal` — who did what, when and from where; document views are included (PRD ADM-06). */
@Component({
  selector: 'app-audit-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, DatePipe],
  styles: `
    code {
      font-size: 0.8125rem;
      word-break: break-word;
    }
  `,
  template: `
    <div class="container admin-page">
      <div>
        <p class="eyebrow">Administration</p>
        <h1>Journal d'audit</h1>
      </div>

      <form class="admin-filters" (ngSubmit)="search()">
        <label>
          Action (début)
          <input class="admin-input" name="action" [(ngModel)]="action" placeholder="ex. user." />
        </label>
        <label>
          Objet
          <select class="admin-input" name="type" [(ngModel)]="entityType">
            @for (t of entityTypes; track t.value) {
              <option [value]="t.value">{{ t.label }}</option>
            }
          </select>
        </label>
        <label>
          Du
          <input class="admin-input" type="date" name="from" [(ngModel)]="from" />
        </label>
        <label>
          Au
          <input class="admin-input" type="date" name="to" [(ngModel)]="to" />
        </label>
        <button type="submit" class="btn btn-primary">Filtrer</button>
      </form>

      @if (entries.hasValue()) {
        @let result = entries.value();
        <div class="table-scroll">
          <table class="admin-table">
            <caption class="visually-hidden">
              Journal d'audit
            </caption>
            <thead>
              <tr>
                <th scope="col">Date</th>
                <th scope="col">Qui</th>
                <th scope="col">Action</th>
                <th scope="col">Objet</th>
                <th scope="col">Détail</th>
                <th scope="col">IP</th>
              </tr>
            </thead>
            <tbody>
              @for (e of result.items; track e.id) {
                <tr>
                  <td>{{ e.at | date: 'dd/MM/yyyy HH:mm:ss' }}</td>
                  <td>{{ e.actorEmail ?? e.actorUserId }}</td>
                  <td>{{ e.action }}</td>
                  <td>{{ e.entityType }}</td>
                  <td>
                    @if (e.data) {
                      <code>{{ e.data }}</code>
                    }
                  </td>
                  <td>{{ e.ip ?? '—' }}</td>
                </tr>
              } @empty {
                <tr>
                  <td colspan="6">Aucune entrée.</td>
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
          <span>Page {{ result.page }} · {{ result.total }} entrée(s)</span>
          <button
            type="button"
            class="btn btn-ghost btn-sm"
            [disabled]="result.page * result.pageSize >= result.total"
            (click)="go(1)"
          >
            Suivant
          </button>
        </div>
      } @else if (entries.error()) {
        <p class="form-error" role="alert">Impossible de charger le journal.</p>
      } @else {
        <p role="status">Chargement…</p>
      }
    </div>
  `,
})
export class AuditPage {
  private readonly api = inject(AdminApiService);

  protected readonly entityTypes = ENTITY_TYPES;
  protected action = '';
  protected entityType = '';
  protected from = '';
  protected to = '';

  protected readonly page = signal(1);
  protected readonly applied = signal({ action: '', entityType: '', from: '', to: '' });

  protected readonly entries = resource({
    params: () => ({ ...this.applied(), page: this.page() }),
    loader: ({ params }) =>
      this.api.audit({
        action: params.action,
        entityType: params.entityType,
        from: params.from ? new Date(params.from).toISOString() : undefined,
        // The "to" day is included: the API bound is exclusive.
        to: params.to
          ? new Date(new Date(params.to).getTime() + 86_400_000).toISOString()
          : undefined,
        page: params.page,
      }),
  });

  protected search(): void {
    this.page.set(1);
    this.applied.set({
      action: this.action.trim(),
      entityType: this.entityType,
      from: this.from,
      to: this.to,
    });
  }

  protected go(step: number): void {
    this.page.update((p) => Math.max(1, p + step));
  }
}
