import { HttpClient } from '@angular/common/http';
import { DatePipe } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  resource,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import type { components } from '../../../core/api/api.generated';
import { parseLegalText } from './legal-markdown';

type LegalDocument = components['schemas']['LegalDocumentDto'];

export type LegalSlug = 'cgu' | 'confidentialite' | 'mentions-legales';

const OTHERS: Record<LegalSlug, readonly { slug: LegalSlug; label: string }[]> = {
  cgu: [
    { slug: 'confidentialite', label: 'Politique de confidentialité' },
    { slug: 'mentions-legales', label: 'Mentions légales' },
  ],
  confidentialite: [
    { slug: 'cgu', label: 'Conditions d’utilisation' },
    { slug: 'mentions-legales', label: 'Mentions légales' },
  ],
  'mentions-legales': [
    { slug: 'cgu', label: 'Conditions d’utilisation' },
    { slug: 'confidentialite', label: 'Politique de confidentialité' },
  ],
};

/**
 * Terms of use, privacy policy and legal notice. The texts are written and published by the admins in the back-office
 * (Paramètres › Textes légaux) and read from the API; see docs/VALIDATION-JURIDIQUE.md for the review they need before launch.
 */
@Component({
  selector: 'app-legal',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, DatePipe],
  styles: `
    .page {
      display: grid;
      gap: 1rem;
      max-width: 48rem;
      padding-block: clamp(1.5rem, 4vw, 3rem);
    }
    ul.items {
      padding-left: 1.25rem;
      list-style: disc;
    }
    .updated {
      margin: 0;
      font-size: 0.875rem;
      color: var(--kb-ink-muted);
    }
    nav ul {
      display: flex;
      flex-wrap: wrap;
      gap: 1rem;
      margin: 0;
      padding: 0;
      list-style: none;
    }
  `,
  template: `
    <article class="container page">
      <p class="eyebrow">Informations légales</p>
      @if (document.hasValue()) {
        @let d = document.value();
        <h1>{{ d.title }}</h1>
        @for (block of blocks(); track $index) {
          @switch (block.kind) {
            @case ('heading') {
              <h2>{{ block.text }}</h2>
            }
            @case ('list') {
              <ul class="items">
                @for (item of block.items; track $index) {
                  <li>{{ item }}</li>
                }
              </ul>
            }
            @default {
              <p>{{ block.text }}</p>
            }
          }
        }
        <p class="updated">
          Version {{ d.version }} · mise à jour le {{ d.updatedAt | date: 'dd/MM/yyyy' }}
        </p>
      } @else if (document.error()) {
        <h1>Document indisponible</h1>
        <p>Ce texte n'est pas encore publié. Merci de revenir plus tard ou de nous contacter.</p>
      } @else {
        <p role="status">Chargement…</p>
      }
      <nav aria-label="Autres textes légaux">
        <ul>
          @for (other of others(); track other.slug) {
            <li>
              <a class="link" [routerLink]="'/' + other.slug">{{ other.label }}</a>
            </li>
          }
        </ul>
      </nav>
    </article>
  `,
})
export class Legal {
  /** Bound from the route data. */
  readonly doc = input<LegalSlug>('cgu');

  private readonly http = inject(HttpClient);

  protected readonly document = resource({
    params: () => this.doc(),
    loader: ({ params }) => firstValueFrom(this.http.get<LegalDocument>(`/api/v1/legal/${params}`)),
  });
  protected readonly blocks = computed(() =>
    this.document.hasValue() ? parseLegalText(this.document.value().body) : [],
  );
  protected readonly others = computed(() => OTHERS[this.doc()]);
}
