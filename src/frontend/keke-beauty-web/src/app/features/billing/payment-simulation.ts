import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { Router } from '@angular/router';
import { BillingService } from '../../core/billing/billing.service';

/**
 * `/paiement/simulation?tx=…` — stands in for the payment provider's page while the development gateway is used.
 * In production the customer is sent to CinetPay instead, and the API refuses the simulate endpoint.
 */
@Component({
  selector: 'app-payment-simulation',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    .page {
      display: grid;
      gap: 1rem;
      max-width: 32rem;
      padding-block: clamp(1.5rem, 4vw, 3rem);
    }
    .actions {
      display: flex;
      flex-wrap: wrap;
      gap: 0.75rem;
    }
  `,
  template: `
    <div class="container page">
      <p class="eyebrow">Environnement de test</p>
      <h1>Paiement simulé</h1>
      <p>
        Cette page remplace la page de paiement Mobile Money / carte bancaire. Aucun argent n'est
        débité.
      </p>
      @if (error(); as message) {
        <p class="form-error" role="alert">{{ message }}</p>
      }
      @if (tx(); as transaction) {
        <p>Transaction : {{ transaction }}</p>
        <div class="actions">
          <button
            type="button"
            class="btn btn-primary"
            [disabled]="busy()"
            (click)="finish(transaction, 'accepted')"
          >
            Payer
          </button>
          <button
            type="button"
            class="btn btn-ghost"
            [disabled]="busy()"
            (click)="finish(transaction, 'refused')"
          >
            Refuser
          </button>
        </div>
      } @else {
        <p class="form-error" role="alert">Transaction inconnue.</p>
      }
    </div>
  `,
})
export class PaymentSimulation {
  /** Bound from the `?tx=` query parameter. */
  readonly tx = input<string>();
  /** Address to return to, set by the gateway; only same-origin addresses are followed. */
  readonly back = input<string>();

  private readonly billing = inject(BillingService);
  private readonly router = inject(Router);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  private returnPath(): string {
    try {
      const target = new URL(this.back() ?? '', window.location.origin);
      return target.origin === window.location.origin ? target.pathname + target.search : '/pro';
    } catch {
      return '/pro';
    }
  }

  protected async finish(transaction: string, status: 'accepted' | 'refused'): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await this.billing.simulate(transaction, status);
      // Like the real provider, send the customer back to where the payment started.
      await this.router.navigateByUrl(this.returnPath());
    } catch (e) {
      this.error.set(
        e instanceof HttpErrorResponse && e.status === 404
          ? 'Cette transaction est introuvable (le serveur a peut-être redémarré).'
          : 'La simulation a échoué. Réessayez.',
      );
    } finally {
      this.busy.set(false);
    }
  }
}
