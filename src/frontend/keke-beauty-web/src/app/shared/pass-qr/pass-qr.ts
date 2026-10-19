import { ChangeDetectionStrategy, Component, inject, input, resource } from '@angular/core';
import { BookingService } from '../../core/booking/booking.service';

/** The client's pass for one appointment: a QR code the establishment scans at the counter. */
@Component({
  selector: 'app-pass-qr',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    :host {
      display: grid;
      justify-items: center;
      gap: 0.5rem;
      padding: 1rem;
      border: 1px dashed var(--kb-primary-300);
      border-radius: var(--radius-md);
      background: var(--kb-card);
    }
    img {
      width: min(100%, 14rem);
      height: auto;
      image-rendering: pixelated;
    }
    p {
      margin: 0;
      color: var(--kb-ink-muted);
      font-size: var(--fs-xs);
      text-align: center;
    }
  `,
  template: `
    @if (qr.hasValue()) {
      <img [src]="qr.value()" width="224" height="224" alt="QR code de votre rendez-vous" />
      <p>Montrez ce code à l'établissement à votre arrivée.</p>
    } @else if (qr.error()) {
      <p role="alert">Le pass n'est pas disponible pour ce rendez-vous.</p>
    } @else {
      <p role="status">Préparation du pass…</p>
    }
  `,
})
export class PassQr {
  private readonly booking = inject(BookingService);

  readonly appointmentId = input.required<string>();

  protected readonly qr = resource({
    params: () => this.appointmentId(),
    loader: async ({ params }) => {
      const pass = await this.booking.pass(params);
      const { default: QRCode } = await import('qrcode');
      return QRCode.toDataURL(pass.token, {
        margin: 1,
        width: 224,
        color: { dark: '#36174f', light: '#ffffff' },
      });
    },
  });
}
