import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  resource,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { problemMessage } from '../../../core/auth/auth.models';
import { formatMoney } from '../../../core/format/format';
import { MOBILE_MONEY_LABELS } from '../../../core/format/payment-methods';
import { AgendaService, type PartnerAppointment } from '../../../core/partner/agenda.service';
import { PartnerService } from '../../../core/partner/partner.service';

/**
 * `/pro/etablissements/:id/comptoir` — a client who walks in without an account: the salon records the appointment itself.
 * It is confirmed at once, takes the first free person, and can be marked paid right away.
 */
@Component({
  selector: 'app-pro-counter',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, RouterLink],
  styles: `
    .page {
      display: grid;
      gap: 1.25rem;
      max-width: 44rem;
      padding-block: clamp(1.5rem, 4vw, 3rem);
    }
    form,
    .card {
      display: grid;
      gap: 1rem;
      padding: 1.25rem;
      border: 1px solid var(--kb-border);
      border-radius: var(--radius-lg);
      background: #fff;
      box-shadow: var(--shadow-soft);
    }
    fieldset {
      display: grid;
      gap: 0.5rem;
      margin: 0;
      padding: 0;
      border: 0;
    }
    legend {
      margin-bottom: 0.5rem;
      font-size: var(--fs-sm);
      font-weight: 600;
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
      accent-color: var(--kb-primary-600);
    }
    .price {
      margin-left: auto;
      color: var(--kb-ink-muted);
      font-size: var(--fs-sm);
    }
    .two {
      display: grid;
      gap: 1rem;
      grid-template-columns: repeat(auto-fit, minmax(14rem, 1fr));
    }
    label.stack {
      display: grid;
      gap: 0.375rem;
      font-size: var(--fs-sm);
      font-weight: 600;
    }
    .total {
      display: flex;
      justify-content: space-between;
      font-weight: 700;
    }
    .done {
      border-color: color-mix(in srgb, var(--kb-success) 35%, white);
      background: color-mix(in srgb, var(--kb-success) 6%, white);
    }
  `,
  template: `
    <div class="container page">
      <a class="link" routerLink="/pro">← Mes établissements</a>
      <div>
        <p class="eyebrow">Comptoir</p>
        <h1>Enregistrer une cliente <em>présente</em></h1>
        <p class="hint">
          Pour une cliente qui se présente sans rendez-vous et sans compte. Le rendez-vous est
          confirmé immédiatement.
        </p>
      </div>

      @if (done(); as booked) {
        <section class="card done" role="status">
          <h2>C'est enregistré</h2>
          <p>
            {{ booked.clientName }} — {{ time(booked) }} —
            {{ money(booked.totalPrice, booked.currencyCode) }}
            @if (booked.servicePayment) {
              · payé
            }
          </p>
          <div class="actions">
            <button class="btn btn-primary btn-sm" type="button" (click)="another()">
              Enregistrer une autre cliente
            </button>
            <a class="btn btn-ghost btn-sm" [routerLink]="['/pro/etablissements', id(), 'agenda']"
              >Voir l'agenda</a
            >
          </div>
        </section>
      } @else if (detail.hasValue()) {
        <form (ngSubmit)="submit()" novalidate>
          <fieldset>
            <legend>Prestations</legend>
            @for (s of services(); track s.id) {
              <label class="check">
                <input type="checkbox" [checked]="selected().has(s.id)" (change)="toggle(s.id)" />
                <span>{{ s.name }}</span>
                <span class="price"
                  >{{ s.durationMinutes }} min · {{ money(s.price, s.currencyCode) }}</span
                >
              </label>
            } @empty {
              <p class="hint">
                Ajoutez d'abord des prestations réservables dans
                <a class="link" [routerLink]="['/pro/etablissements', id(), 'prestations']"
                  >Prestations</a
                >.
              </p>
            }
          </fieldset>

          <div class="two">
            <label class="stack">
              Nom de la cliente
              <input
                class="admin-input"
                name="name"
                [(ngModel)]="clientName"
                required
                minlength="2"
                maxlength="80"
                autocomplete="off"
              />
            </label>
            <label class="stack">
              Téléphone (facultatif)
              <input
                class="admin-input"
                name="phone"
                [(ngModel)]="phone"
                inputmode="tel"
                autocomplete="off"
                placeholder="07 00 00 00 00"
              />
            </label>
          </div>

          <label class="stack">
            Paiement reçu
            <select class="admin-input" name="paid" [(ngModel)]="paidMethod">
              <option value="">Pas encore payé</option>
              @if (payments.value()?.cash) {
                <option value="Cash">Espèces</option>
              }
              @for (m of payments.value()?.mobileMoney ?? []; track m.operator) {
                <option [value]="m.operator">{{ operatorLabel(m.operator) }}</option>
              }
            </select>
          </label>
          @if (paidMethod() && paidMethod() !== 'Cash') {
            <label class="stack">
              Référence de la transaction
              <input class="admin-input" name="ref" [(ngModel)]="reference" maxlength="60" />
            </label>
          }

          <p class="total">
            <span>Total</span><span>{{ money(total(), currency()) }}</span>
          </p>

          @if (error()) {
            <p class="form-error" role="alert">{{ error() }}</p>
          }
          <button
            class="btn btn-primary"
            type="submit"
            [disabled]="busy() || selected().size === 0 || clientName().trim().length < 2"
          >
            {{ busy() ? 'Enregistrement…' : 'Enregistrer le rendez-vous' }}
          </button>
        </form>
      } @else if (detail.error()) {
        <p class="form-error" role="alert">Impossible de charger l'établissement.</p>
      } @else {
        <p class="hint" role="status">Chargement…</p>
      }
    </div>
  `,
})
export class ProCounter {
  private readonly agenda = inject(AgendaService);
  private readonly partner = inject(PartnerService);

  readonly id = input.required<string>();

  protected readonly detail = resource({
    params: () => this.id(),
    loader: ({ params }) => this.partner.get(params),
  });
  protected readonly payments = resource({
    params: () => this.id(),
    loader: ({ params }) => this.agenda.acceptedPayments(params),
  });

  protected readonly services = computed(() =>
    (this.detail.value()?.services ?? []).filter((s) => s.isBookable && s.isVisible),
  );
  protected readonly selected = signal<ReadonlySet<string>>(new Set());
  protected readonly clientName = signal('');
  protected readonly phone = signal('');
  protected readonly paidMethod = signal('');
  protected readonly reference = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly done = signal<PartnerAppointment | null>(null);

  protected readonly total = computed(() =>
    this.services()
      .filter((s) => this.selected().has(s.id))
      .reduce((sum, s) => sum + s.price, 0),
  );
  protected readonly currency = computed(() => this.services()[0]?.currencyCode ?? 'XOF');

  protected money(amount: number, currency: string): string {
    return formatMoney(amount, currency);
  }

  protected operatorLabel(operator: string): string {
    return MOBILE_MONEY_LABELS[operator] ?? operator;
  }

  protected time(a: PartnerAppointment): string {
    return new Date(a.startsAt).toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit' });
  }

  protected toggle(serviceId: string): void {
    const next = new Set(this.selected());
    if (!next.delete(serviceId)) {
      next.add(serviceId);
    }

    this.selected.set(next);
  }

  protected another(): void {
    this.done.set(null);
    this.clientName.set('');
    this.phone.set('');
    this.paidMethod.set('');
    this.reference.set('');
    this.selected.set(new Set());
  }

  protected async submit(): Promise<void> {
    this.error.set(null);
    this.busy.set(true);
    try {
      this.done.set(
        await this.agenda.counter(this.id(), {
          serviceIds: [...this.selected()],
          resourceId: null,
          startsAt: null,
          clientName: this.clientName(),
          phone: this.phone().trim() || null,
          paidMethod: this.paidMethod() || null,
          paidReference: this.reference().trim() || null,
        }),
      );
    } catch (e) {
      this.error.set(problemMessage(e));
    } finally {
      this.busy.set(false);
    }
  }
}
