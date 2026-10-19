import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, input, resource, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { problemMessage } from '../../../core/auth/auth.models';
import {
  DISPUTE_REASONS,
  DISPUTE_RESOLUTIONS,
  DisputeService,
  type Dispute,
} from '../../../core/disputes/dispute.service';

/**
 * `/pro/etablissements/:id/litiges` — claims filed by clients about this establishment (Keke Protect).
 * The establishment answers once; the payout for the appointment is held until an administrator decides.
 */
@Component({
  selector: 'app-pro-disputes',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, FormsModule, RouterLink],
  styles: `
    .page {
      display: grid;
      gap: 1.25rem;
      max-width: 46rem;
      padding-block: clamp(1.5rem, 4vw, 3rem);
    }
    .card {
      display: grid;
      gap: 0.6rem;
      padding: 1.1rem 1.25rem;
      border: 1px solid var(--kb-border);
      border-radius: var(--radius-lg);
      background: var(--kb-card);
      box-shadow: var(--shadow-soft);
    }
    h2,
    p {
      margin: 0;
    }
    .top {
      display: flex;
      flex-wrap: wrap;
      justify-content: space-between;
      gap: 0.5rem;
    }
    .meta {
      color: var(--kb-ink-muted);
    }
    .quote {
      padding: 0.6rem 0.8rem;
      border-left: 3px solid var(--kb-border);
      background: var(--kb-primary-50);
      border-radius: 0 var(--radius-md) var(--radius-md) 0;
    }
    textarea {
      width: 100%;
      min-height: 5rem;
      padding: 0.75rem;
      border: 1px solid var(--kb-border);
      border-radius: 0.75rem;
      font: inherit;
      resize: vertical;
    }
    .actions {
      display: flex;
      gap: 0.5rem;
    }
  `,
  template: `
    <div class="container page">
      <a class="link" routerLink="/pro">← Mes établissements</a>
      <div>
        <p class="eyebrow">Keke Protect</p>
        <h1>Litiges <em>clients</em></h1>
        <p class="hint">
          Quand une cliente signale un problème, le versement du rendez-vous est suspendu. Répondez
          avec des faits : notre équipe tranche ensuite.
        </p>
      </div>

      @if (error()) {
        <p class="form-error" role="alert">{{ error() }}</p>
      }

      @if (disputes.hasValue()) {
        @for (d of disputes.value(); track d.id) {
          <article class="card">
            <div class="top">
              <h2>{{ reasonLabel(d.reason) }}</h2>
              <span class="badge" [attr.data-tone]="d.status === 'Open' ? 'wait' : 'ok'">{{
                statusLabel(d)
              }}</span>
            </div>
            <p class="meta">
              {{ d.clientName }} · rendez-vous du
              {{ d.appointmentStartsAt | date: 'dd/MM/yyyy HH:mm' }}
            </p>
            <p class="quote">{{ d.description }}</p>
            @if (d.partnerResponse) {
              <p><strong>Votre réponse :</strong> {{ d.partnerResponse }}</p>
            }
            @if (d.adminNote) {
              <p class="meta"><strong>Décision de l'équipe :</strong> {{ d.adminNote }}</p>
            }
            @if (d.status === 'Open') {
              <label class="visually-hidden" [for]="'answer-' + d.id">Votre réponse</label>
              <textarea
                [id]="'answer-' + d.id"
                maxlength="1000"
                placeholder="Votre version des faits"
                [ngModel]="draft(d)"
                (ngModelChange)="setDraft(d, $event)"
              ></textarea>
              <div class="actions">
                <button
                  class="btn btn-primary btn-sm"
                  type="button"
                  [disabled]="busy() || draft(d).trim().length < 3"
                  (click)="respond(d)"
                >
                  {{ d.partnerResponse ? 'Mettre à jour ma réponse' : 'Répondre' }}
                </button>
              </div>
            }
          </article>
        } @empty {
          <p class="hint">Aucun litige : tout se passe bien.</p>
        }
      } @else if (disputes.error()) {
        <p class="form-error" role="alert">Impossible de charger les litiges. Réessayez.</p>
      } @else {
        <p role="status">Chargement…</p>
      }
    </div>
  `,
})
export class ProDisputes {
  private readonly service = inject(DisputeService);

  readonly id = input.required<string>();

  protected readonly busy = signal(false);
  protected readonly error = signal('');
  private readonly drafts = signal<Readonly<Record<string, string>>>({});

  protected readonly disputes = resource({
    params: () => this.id(),
    loader: ({ params }) => this.service.forEstablishment(params),
  });

  protected reasonLabel(reason: string): string {
    return DISPUTE_REASONS[reason] ?? reason;
  }

  protected statusLabel(d: Dispute): string {
    return d.status === 'Open'
      ? 'En cours d’examen'
      : (DISPUTE_RESOLUTIONS[d.resolution ?? ''] ?? 'Clos');
  }

  protected draft(d: Dispute): string {
    return this.drafts()[d.id] ?? d.partnerResponse ?? '';
  }

  protected setDraft(d: Dispute, value: string): void {
    this.drafts.update((all) => ({ ...all, [d.id]: value }));
  }

  protected async respond(d: Dispute): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    try {
      await this.service.respond(d.id, this.draft(d).trim());
    } catch (error) {
      this.error.set(problemMessage(error));
    } finally {
      this.busy.set(false);
      this.disputes.reload();
    }
  }
}
