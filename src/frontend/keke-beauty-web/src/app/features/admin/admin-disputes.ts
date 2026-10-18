import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, resource, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { problemMessage } from '../../core/auth/auth.models';
import {
  DISPUTE_REASONS,
  DISPUTE_RESOLUTIONS,
  DisputeService,
  type Dispute,
} from '../../core/disputes/dispute.service';

/** `/admin/litiges` — Keke Protect: claims waiting for a decision. An upheld claim refunds the client's online payment. */
@Component({
  selector: 'app-admin-disputes',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, FormsModule],
  template: `
    <div class="container admin-page">
      <div>
        <p class="eyebrow">Administration</p>
        <h1>Litiges</h1>
      </div>

      @if (message(); as text) {
        <p role="status" class="badge" data-tone="ok">{{ text }}</p>
      }
      @if (error(); as text) {
        <p class="form-error" role="alert">{{ text }}</p>
      }

      <div class="admin-filters">
        <label>
          Statut
          <select class="admin-input" [ngModel]="status()" (ngModelChange)="status.set($event)">
            <option value="Open">À traiter</option>
            <option value="Resolved">Clos</option>
            <option value="">Tous</option>
          </select>
        </label>
      </div>

      @if (disputes.hasValue()) {
        @for (d of disputes.value(); track d.id) {
          <section class="admin-card" [attr.aria-label]="d.establishmentName">
            <h2>{{ d.establishmentName }} · {{ reasonLabel(d.reason) }}</h2>
            <p>
              {{ d.clientName }} · rendez-vous du
              {{ d.appointmentStartsAt | date: 'dd/MM/yyyy HH:mm' }} · ouvert le
              {{ d.createdAt | date: 'dd/MM/yyyy' }}
            </p>
            <p><strong>Cliente :</strong> {{ d.description }}</p>
            @if (d.partnerResponse) {
              <p><strong>Établissement :</strong> {{ d.partnerResponse }}</p>
            } @else if (d.status === 'Open') {
              <p class="hint">L'établissement n'a pas encore répondu.</p>
            }
            @if (d.status === 'Open') {
              <div class="admin-filters">
                <label>
                  Décision
                  <select
                    class="admin-input"
                    [ngModel]="decision(d)"
                    (ngModelChange)="setDecision(d, $event)"
                  >
                    @for (r of resolutions; track r.value) {
                      <option [value]="r.value">{{ r.label }}</option>
                    }
                  </select>
                </label>
                <label>
                  Message aux deux parties
                  <input
                    class="admin-input"
                    maxlength="500"
                    [ngModel]="note(d)"
                    (ngModelChange)="setNote(d, $event)"
                  />
                </label>
                <button
                  class="btn btn-primary btn-sm"
                  type="button"
                  [disabled]="busy() || note(d).trim().length < 3"
                  (click)="resolve(d)"
                >
                  Trancher
                </button>
              </div>
            } @else {
              <p>
                <strong>{{ resolutionLabel(d) }}</strong> · {{ d.adminNote }}
                @if (d.refundStatus) {
                  · paiement : {{ d.refundStatus }}
                }
              </p>
            }
          </section>
        } @empty {
          <p class="hint">Aucun litige dans cette liste.</p>
        }
      } @else if (disputes.error()) {
        <p class="form-error" role="alert">Impossible de charger les litiges.</p>
      } @else {
        <p role="status">Chargement…</p>
      }
    </div>
  `,
})
export class AdminDisputes {
  private readonly service = inject(DisputeService);

  protected readonly status = signal('Open');
  protected readonly busy = signal(false);
  protected readonly message = signal('');
  protected readonly error = signal('');
  protected readonly resolutions = Object.entries(DISPUTE_RESOLUTIONS).map(([value, label]) => ({
    value,
    label,
  }));
  private readonly decisions = signal<Readonly<Record<string, string>>>({});
  private readonly notes = signal<Readonly<Record<string, string>>>({});

  protected readonly disputes = resource({
    params: () => this.status(),
    loader: ({ params }) => this.service.adminList(params || undefined),
  });

  protected reasonLabel(reason: string): string {
    return DISPUTE_REASONS[reason] ?? reason;
  }

  protected resolutionLabel(d: Dispute): string {
    return DISPUTE_RESOLUTIONS[d.resolution ?? ''] ?? 'Clos';
  }

  protected decision(d: Dispute): string {
    return this.decisions()[d.id] ?? 'RejectedClaim';
  }

  protected note(d: Dispute): string {
    return this.notes()[d.id] ?? '';
  }

  protected setDecision(d: Dispute, value: string): void {
    this.decisions.update((all) => ({ ...all, [d.id]: value }));
  }

  protected setNote(d: Dispute, value: string): void {
    this.notes.update((all) => ({ ...all, [d.id]: value }));
  }

  protected async resolve(d: Dispute): Promise<void> {
    const decision = this.decision(d);
    const refund = decision === 'RefundedClient';
    if (refund && !confirm('Rembourser la cliente de son paiement en ligne ?')) {
      return;
    }

    this.busy.set(true);
    this.error.set('');
    this.message.set('');
    try {
      await this.service.resolve(d.id, decision, this.note(d).trim());
      this.message.set(refund ? 'Litige tranché : la cliente est remboursée.' : 'Litige rejeté.');
    } catch (error) {
      this.error.set(problemMessage(error));
    } finally {
      this.busy.set(false);
      this.disputes.reload();
    }
  }
}
