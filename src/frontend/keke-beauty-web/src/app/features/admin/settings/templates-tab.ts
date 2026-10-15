import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  resource,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AdminApiService, type Template } from '../../../core/admin/admin-api.service';
import { problemMessage } from '../../../core/auth/auth.models';

const SMS_LENGTH = 160;

/** Editable notification messages with variables such as {etablissement} (PRD NOT-02). */
@Component({
  selector: 'app-templates-tab',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule],
  styles: `
    .layout {
      display: grid;
      gap: 1rem;
      grid-template-columns: minmax(14rem, 1fr) 2fr;

      @media (max-width: 767px) {
        grid-template-columns: 1fr;
      }
    }
    ul {
      display: grid;
      gap: 0.25rem;
      margin: 0;
      padding: 0;
      list-style: none;
    }
    .pick {
      width: 100%;
      text-align: left;
      border: 0;
      background: none;
      font: inherit;
      cursor: pointer;
      padding: 0.5rem;
      border-radius: var(--radius-sm, 0.5rem);

      &[aria-pressed='true'] {
        background: var(--kb-primary-50);
        font-weight: 700;
      }
    }
    textarea {
      min-height: 7rem;
    }
    code {
      padding: 0.0625rem 0.375rem;
      border-radius: 0.25rem;
      background: var(--kb-primary-50);
    }
  `,
  template: `
    <div class="layout">
      <section class="admin-card" aria-labelledby="tpl-list">
        <h2 id="tpl-list">Messages</h2>
        <ul>
          @for (t of templates.value() ?? []; track t.code) {
            <li>
              <button
                type="button"
                class="pick"
                [attr.aria-pressed]="code() === t.code"
                (click)="choose(t)"
              >
                {{ t.label }}
                @if (t.isCustom) {
                  <span class="badge">modifié</span>
                }
              </button>
            </li>
          }
        </ul>
      </section>

      <section class="admin-card" aria-labelledby="tpl-edit">
        <h2 id="tpl-edit">Modifier le message</h2>
        @if (current(); as t) {
          @if (message(); as text) {
            <p role="status" class="badge" data-tone="ok">{{ text }}</p>
          }
          @if (error(); as text) {
            <p class="form-error" role="alert">{{ text }}</p>
          }
          <form (ngSubmit)="save(t)">
            <label>
              Titre
              <input
                class="admin-input"
                name="title"
                maxlength="120"
                [(ngModel)]="title"
                required
              />
            </label>
            <label>
              Message
              <textarea
                class="admin-input"
                name="body"
                maxlength="480"
                [(ngModel)]="body"
                required
              ></textarea>
            </label>
            <p class="auth-lead">
              {{ body.length }} caractères{{
                body.length > smsLength
                  ? ' (au-delà de ' + smsLength + ', un SMS sera découpé)'
                  : ''
              }}. Variables :
              @for (v of t.variables; track v) {
                <code>{{ '{' + v + '}' }}</code>
                {{ ' ' }}
              }
            </p>
            <div class="row-actions">
              <button type="submit" class="btn btn-primary btn-sm" [disabled]="busy()">
                Enregistrer
              </button>
              @if (t.isCustom) {
                <button
                  type="button"
                  class="btn btn-ghost btn-sm"
                  [disabled]="busy()"
                  (click)="reset(t)"
                >
                  Revenir au texte d'origine
                </button>
              }
            </div>
          </form>
        } @else {
          <p>Choisissez un message dans la liste.</p>
        }
      </section>
    </div>
  `,
})
export class TemplatesTab {
  private readonly api = inject(AdminApiService);

  protected readonly smsLength = SMS_LENGTH;
  protected title = '';
  protected body = '';
  protected readonly code = signal<string | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly message = signal<string | null>(null);

  protected readonly templates = resource({ loader: () => this.api.templates() });
  protected readonly current = computed(() =>
    this.templates.value()?.find((t) => t.code === this.code()),
  );

  protected choose(t: Template): void {
    this.code.set(t.code);
    this.title = t.title;
    this.body = t.body;
    this.error.set(null);
    this.message.set(null);
  }

  protected async save(t: Template): Promise<void> {
    await this.run('Message enregistré.', t.code, () =>
      this.api.updateTemplate(t.code, this.title, this.body),
    );
  }

  protected async reset(t: Template): Promise<void> {
    await this.run('Texte d’origine rétabli.', t.code, () => this.api.resetTemplate(t.code));
  }

  private async run(success: string, code: string, action: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    this.message.set(null);
    try {
      await action();
      this.templates.reload();
      this.message.set(success);
      const refreshed = (await this.api.templates()).find((t) => t.code === code);
      if (refreshed) {
        this.title = refreshed.title;
        this.body = refreshed.body;
      }
    } catch (e) {
      this.error.set(problemMessage(e));
    } finally {
      this.busy.set(false);
    }
  }
}
