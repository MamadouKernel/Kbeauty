import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  input,
  resource,
  signal,
} from '@angular/core';
import { RouterLink } from '@angular/router';
import { isoDay } from '../../../core/booking/booking.service';
import { APPOINTMENT_STATUS } from '../../../core/booking/booking.service';
import { AgendaService } from '../../../core/partner/agenda.service';

const PERIODS = [7, 30, 90];

/** Simple statistics (PRD AGD-08): page views, call and directions clicks, appointments per status. */
@Component({
  selector: 'app-stats-panel',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  styles: `
    .toolbar {
      display: flex;
      flex-wrap: wrap;
      gap: 0.5rem;
      margin-bottom: 1rem;
    }
    .cards {
      display: grid;
      gap: 1rem;
      grid-template-columns: repeat(auto-fit, minmax(min(100%, 10rem), 1fr));
    }
    .card {
      padding: 1rem 1.25rem;
      border: 1px solid var(--kb-border);
      border-radius: var(--radius-lg);
      background: #fff;
      box-shadow: var(--shadow-soft);
    }
    .card strong {
      display: block;
      font-family: var(--font-display);
      font-size: 2rem;
    }
  `,
  template: `
    <div class="toolbar" role="group" aria-label="Période">
      @for (p of periods; track p) {
        <button
          type="button"
          class="btn btn-sm"
          [class.btn-primary]="days() === p"
          [class.btn-ghost]="days() !== p"
          (click)="days.set(p)"
        >
          {{ p }} jours
        </button>
      }
    </div>
    @if (stats.hasValue()) {
      <div class="cards">
        <div class="card">
          <strong>{{ stats.value().views }}</strong> Vues de la fiche
        </div>
        <div class="card">
          <strong>{{ stats.value().callClicks }}</strong> Clics « Appeler »
        </div>
        <div class="card">
          <strong>{{ stats.value().directionsClicks }}</strong> Clics « S'y rendre »
        </div>
      </div>
      <h3>Rendez-vous par statut</h3>
      <div class="cards">
        @for (row of rows(); track row.status) {
          <div class="card">
            <strong>{{ row.count }}</strong> {{ row.label }}
          </div>
        } @empty {
          <p class="hint">Aucun rendez-vous sur la période.</p>
        }
      </div>
    } @else if (stats.error()) {
      <p class="form-error" role="alert">
        Les statistiques font partie du plan Premium (ou votre abonnement est suspendu).
        <a [routerLink]="['/pro/etablissements', establishmentId(), 'abonnement']"
          >Voir mon abonnement</a
        >
      </p>
    } @else {
      <p role="status">Chargement…</p>
    }
  `,
})
export class StatsPanel {
  readonly establishmentId = input.required<string>();

  private readonly agenda = inject(AgendaService);
  protected readonly periods = PERIODS;
  protected readonly days = signal(30);

  protected readonly stats = resource({
    params: () => ({ id: this.establishmentId(), days: this.days() }),
    loader: ({ params }) => {
      const now = new Date();
      const from = new Date(now.getFullYear(), now.getMonth(), now.getDate() - params.days);
      const to = new Date(now.getFullYear(), now.getMonth(), now.getDate() + 30);
      return this.agenda.stats(params.id, isoDay(from), isoDay(to));
    },
  });

  protected readonly rows = computed(() =>
    this.stats.hasValue()
      ? Object.entries(this.stats.value().appointments).map(([status, count]) => ({
          status,
          count,
          label: APPOINTMENT_STATUS[status]?.label ?? status,
        }))
      : [],
  );
}
