import {
  ChangeDetectionStrategy,
  Component,
  inject,
  linkedSignal,
  resource,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AdminApiService, type CategoryAdmin } from '../../../core/admin/admin-api.service';
import { problemMessage } from '../../../core/auth/auth.models';

/** Beauty categories: add, rename, icon, order, activation (PRD ADM-05). */
@Component({
  selector: 'app-categories-tab',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  template: `
    <section class="admin-card" aria-labelledby="cat-title">
      <h2 id="cat-title">Catégories</h2>
      @if (message(); as text) {
        <p role="status" class="badge" data-tone="ok">{{ text }}</p>
      }
      @if (error(); as text) {
        <p class="form-error" role="alert">{{ text }}</p>
      }

      <form class="admin-filters" (ngSubmit)="create()">
        <label>
          Nouvelle catégorie
          <input class="admin-input" name="name" [(ngModel)]="name" placeholder="Nom" required />
        </label>
        <label>
          Icône
          <input
            class="admin-input"
            name="icon"
            [(ngModel)]="icon"
            placeholder="ex. hair"
            required
          />
        </label>
        <label>
          Ordre
          <input
            class="admin-input"
            type="number"
            name="order"
            min="0"
            max="1000"
            [(ngModel)]="order"
          />
        </label>
        <button type="submit" class="btn btn-primary" [disabled]="busy()">Ajouter</button>
      </form>

      <div class="table-scroll">
        <table class="admin-table">
          <caption class="visually-hidden">
            Catégories
          </caption>
          <thead>
            <tr>
              <th scope="col">Nom</th>
              <th scope="col">Icône</th>
              <th scope="col">Ordre</th>
              <th scope="col">Actif</th>
              <th scope="col">Fiches</th>
              <th scope="col"><span class="visually-hidden">Actions</span></th>
            </tr>
          </thead>
          <tbody>
            @for (c of drafts(); track c.id) {
              <tr>
                <td>
                  <input
                    class="admin-input"
                    [name]="'n' + c.id"
                    [(ngModel)]="c.name"
                    [attr.aria-label]="'Nom de ' + c.slug"
                  />
                </td>
                <td>
                  <input
                    class="admin-input"
                    [name]="'i' + c.id"
                    [(ngModel)]="c.icon"
                    [attr.aria-label]="'Icône de ' + c.slug"
                  />
                </td>
                <td>
                  <input
                    class="admin-input"
                    type="number"
                    [name]="'o' + c.id"
                    [(ngModel)]="c.sortOrder"
                    [attr.aria-label]="'Ordre de ' + c.slug"
                  />
                </td>
                <td>
                  <input
                    type="checkbox"
                    [name]="'a' + c.id"
                    [(ngModel)]="c.isActive"
                    [attr.aria-label]="'Actif : ' + c.slug"
                  />
                </td>
                <td>{{ c.establishments }}</td>
                <td>
                  <button
                    type="button"
                    class="btn btn-ghost btn-sm"
                    [disabled]="busy()"
                    (click)="save(c)"
                  >
                    Enregistrer
                  </button>
                </td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    </section>
  `,
})
export class CategoriesTab {
  private readonly api = inject(AdminApiService);

  protected name = '';
  protected icon = '';
  protected order = 100;
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly message = signal<string | null>(null);

  protected readonly categories = resource({ loader: () => this.api.categories() });
  /** Editable copies: typing never touches the loaded values until saved. */
  protected readonly drafts = linkedSignal<CategoryAdmin[] | undefined, CategoryAdmin[]>({
    source: () => this.categories.value(),
    computation: (loaded) => (loaded ?? []).map((c) => ({ ...c })),
  });

  protected async create(): Promise<void> {
    await this.run('Catégorie ajoutée.', async () => {
      await this.api.createCategory({
        name: this.name,
        icon: this.icon,
        sortOrder: Number(this.order),
        isActive: true,
      });
      this.name = '';
      this.icon = '';
    });
  }

  protected async save(c: CategoryAdmin): Promise<void> {
    await this.run('Catégorie enregistrée.', () =>
      this.api.updateCategory(c.id, {
        name: c.name,
        icon: c.icon,
        sortOrder: Number(c.sortOrder),
        isActive: c.isActive,
      }),
    );
  }

  private async run(success: string, action: () => Promise<unknown>): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    this.message.set(null);
    try {
      await action();
      this.message.set(success);
      this.categories.reload();
    } catch (e) {
      this.error.set(problemMessage(e));
    } finally {
      this.busy.set(false);
    }
  }
}
