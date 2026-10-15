import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, resource, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AdminApiService, LegalDocumentAdmin } from '../../../core/admin/admin-api.service';
import { problemMessage } from '../../../core/auth/auth.models';

/** Editor of the legal texts (terms of use, privacy policy, legal notice) with the version history. */
@Component({
  selector: 'app-legal-tab',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, DatePipe],
  styles: `
    .picker {
      display: flex;
      flex-wrap: wrap;
      gap: 0.5rem;
    }
    form {
      display: grid;
      gap: 0.875rem;
    }
    label {
      display: grid;
      gap: 0.25rem;
      font-size: 0.875rem;
      font-weight: 600;
    }
    textarea {
      min-height: 22rem;
      font-family: var(--font-mono, ui-monospace, monospace);
      line-height: 1.5;
      resize: vertical;
    }
    .hint {
      margin: 0;
      font-size: 0.875rem;
      color: var(--kb-ink-muted);
    }
    label.check {
      display: flex;
      align-items: center;
      gap: 0.625rem;
      min-height: 2.75rem;
    }
    label.check input {
      width: 1.25rem;
      height: 1.25rem;
    }
  `,
  template: `
    <section class="admin-card" aria-labelledby="legal-title">
      <h2 id="legal-title">Textes légaux</h2>
      @if (documents.hasValue()) {
        <div class="picker" role="group" aria-label="Document">
          @for (d of documents.value(); track d.slug) {
            <button
              type="button"
              class="btn btn-sm"
              [class.btn-primary]="slug() === d.slug"
              [class.btn-ghost]="slug() !== d.slug"
              (click)="open(d.slug)"
            >
              {{ d.title }}
              @if (!d.isPublished) {
                <span class="badge" data-tone="warn">Non publié</span>
              }
            </button>
          }
        </div>
      } @else if (documents.error()) {
        <p class="form-error" role="alert">Impossible de charger les textes.</p>
      }

      @if (current(); as doc) {
        <form (ngSubmit)="save()">
          <label>
            Titre
            <input class="admin-input" name="title" maxlength="120" [(ngModel)]="title" />
          </label>
          <label>
            Texte
            <textarea class="admin-input" name="body" rows="20" [(ngModel)]="body"></textarea>
          </label>
          <p class="hint">
            Mise en forme : <code>## Titre de section</code>, <code>- élément de liste</code>,
            paragraphes séparés par une ligne vide. Remplacez chaque mention « [à compléter] » avant
            de publier.
          </p>
          <label class="check">
            <input type="checkbox" name="published" [(ngModel)]="published" />
            Publié (visible par les visiteurs)
          </label>
          <div class="admin-filters">
            <button type="submit" class="btn btn-primary" [disabled]="busy()">
              Enregistrer (version {{ doc.version + 1 }})
            </button>
            <span class="hint">
              Version {{ doc.version }}, modifiée le {{ doc.updatedAt | date: 'dd/MM/yyyy HH:mm' }}
            </span>
          </div>
        </form>

        @if (versions.hasValue() && versions.value().length > 0) {
          <h3>Historique</h3>
          <div class="table-scroll">
            <table class="admin-table">
              <thead>
                <tr>
                  <th scope="col">Version</th>
                  <th scope="col">Titre</th>
                  <th scope="col">Publiée</th>
                  <th scope="col">Enregistrée le</th>
                </tr>
              </thead>
              <tbody>
                @for (v of versions.value(); track v.version) {
                  <tr>
                    <td>{{ v.version }}</td>
                    <td>{{ v.title }}</td>
                    <td>{{ v.wasPublished ? 'Oui' : 'Non' }}</td>
                    <td>{{ v.savedAt | date: 'dd/MM/yyyy HH:mm' }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      }

      @if (message(); as text) {
        <p role="status" class="badge" data-tone="ok">{{ text }}</p>
      }
      @if (error(); as text) {
        <p class="form-error" role="alert">{{ text }}</p>
      }
    </section>
  `,
})
export class LegalTab {
  private readonly api = inject(AdminApiService);

  protected title = '';
  protected body = '';
  protected published = false;
  protected readonly slug = signal<string | null>(null);
  protected readonly current = signal<LegalDocumentAdmin | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly message = signal<string | null>(null);
  protected readonly documents = resource({ loader: () => this.api.legalDocuments() });
  protected readonly versions = resource({
    params: () => this.slug(),
    loader: ({ params }) => (params ? this.api.legalVersions(params) : Promise.resolve([])),
  });

  protected async open(slug: string): Promise<void> {
    this.error.set(null);
    this.message.set(null);
    try {
      this.show(await this.api.legalDocument(slug));
    } catch (e) {
      this.error.set(problemMessage(e));
    }
  }

  protected async save(): Promise<void> {
    const slug = this.slug();
    if (!slug) {
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    this.message.set(null);
    try {
      this.show(
        await this.api.saveLegalDocument(slug, {
          title: this.title,
          body: this.body,
          published: this.published,
        }),
      );
      this.message.set('Texte enregistré.');
      this.documents.reload();
      this.versions.reload();
    } catch (e) {
      this.error.set(problemMessage(e));
    } finally {
      this.busy.set(false);
    }
  }

  private show(doc: LegalDocumentAdmin): void {
    this.slug.set(doc.slug);
    this.current.set(doc);
    this.title = doc.title;
    this.body = doc.body;
    this.published = doc.isPublished;
  }
}
