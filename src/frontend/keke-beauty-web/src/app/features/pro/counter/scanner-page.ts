import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  inject,
  input,
  signal,
  viewChild,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { problemMessage } from '../../../core/auth/auth.models';
import { formatMoney } from '../../../core/format/format';
import { AgendaService, type PassCheck } from '../../../core/partner/agenda.service';

interface Detector {
  detect(source: CanvasImageSource): Promise<{ rawValue: string }[]>;
}

/**
 * `/pro/etablissements/:id/scanner` — checks a client's pass. The camera reads the QR code where the browser can
 * (BarcodeDetector); pasting the code or link always works.
 */
@Component({
  selector: 'app-pro-scanner',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, RouterLink],
  styles: `
    .page {
      display: grid;
      gap: 1.25rem;
      max-width: 40rem;
      padding-block: clamp(1.5rem, 4vw, 3rem);
    }
    .card {
      display: grid;
      gap: 0.75rem;
      padding: 1.25rem;
      border: 1px solid var(--kb-border);
      border-radius: var(--radius-lg);
      background: #fff;
      box-shadow: var(--shadow-soft);
    }
    video {
      width: 100%;
      max-height: 22rem;
      border-radius: var(--radius-md);
      background: #000;
      object-fit: cover;
    }
    .row {
      display: flex;
      flex-wrap: wrap;
      gap: 0.5rem;
    }
    .row input {
      flex: 1 1 14rem;
    }
    .ok {
      border-color: color-mix(in srgb, var(--kb-success) 35%, white);
      background: color-mix(in srgb, var(--kb-success) 6%, white);
    }
    dl {
      display: grid;
      grid-template-columns: auto 1fr;
      gap: 0.25rem 1rem;
      margin: 0;
    }
    dt {
      color: var(--kb-ink-muted);
    }
    dd {
      margin: 0;
      font-weight: 600;
    }
  `,
  template: `
    <div class="container page">
      <a class="link" routerLink="/pro">← Mes établissements</a>
      <div>
        <p class="eyebrow">Pass client</p>
        <h1>Scanner un <em>pass</em></h1>
        <p class="hint">La cliente montre le QR code de son rendez-vous depuis son compte.</p>
      </div>

      <section class="card" aria-label="Scanner avec la caméra">
        @if (scanning()) {
          <video #video playsinline muted aria-label="Aperçu de la caméra"></video>
          <button class="btn btn-ghost btn-sm" type="button" (click)="stop()">
            Arrêter la caméra
          </button>
        } @else {
          <button class="btn btn-primary" type="button" (click)="start()">
            Scanner avec la caméra
          </button>
        }
        @if (cameraNote()) {
          <p class="hint" role="status">{{ cameraNote() }}</p>
        }
      </section>

      <form class="card" (ngSubmit)="verify(manual())">
        <label for="code">Ou collez le code / le lien du pass</label>
        <div class="row">
          <input
            id="code"
            class="admin-input"
            name="code"
            [(ngModel)]="manual"
            autocomplete="off"
            spellcheck="false"
          />
          <button class="btn btn-primary" type="submit" [disabled]="!manual().trim() || busy()">
            Vérifier
          </button>
        </div>
      </form>

      @if (error()) {
        <p class="form-error" role="alert">{{ error() }}</p>
      }
      @if (result(); as r) {
        <section class="card ok" role="status" aria-label="Pass valide">
          <h2>Pass valide</h2>
          <dl>
            <dt>Cliente</dt>
            <dd>{{ r.clientName }}</dd>
            <dt>Quand</dt>
            <dd>
              {{ day(r.startsAt) }}
              @if (!r.isToday) {
                <span class="hint"> (pas aujourd'hui)</span>
              }
            </dd>
            <dt>Prestations</dt>
            <dd>
              @for (l of r.lines; track $index) {
                <span
                  >{{ l.name }}
                  @if (!$last) {
                    ,
                  }
                </span>
              }
            </dd>
            <dt>Total</dt>
            <dd>{{ money(r.totalPrice, r.currencyCode) }}{{ r.isPaid ? ' · payé' : '' }}</dd>
            <dt>Statut</dt>
            <dd>{{ r.status }}</dd>
          </dl>
        </section>
      }
    </div>
  `,
})
export class ProScanner {
  private readonly agenda = inject(AgendaService);

  readonly id = input.required<string>();

  protected readonly manual = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly result = signal<PassCheck | null>(null);
  protected readonly scanning = signal(false);
  protected readonly cameraNote = signal<string | null>(null);

  private readonly video = viewChild<ElementRef<HTMLVideoElement>>('video');
  private stream: MediaStream | null = null;
  private frame = 0;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.release());
  }

  protected money(amount: number, currency: string): string {
    return formatMoney(amount, currency);
  }

  protected day(iso: string): string {
    return new Date(iso).toLocaleString('fr-FR', {
      weekday: 'long',
      day: 'numeric',
      month: 'long',
      hour: '2-digit',
      minute: '2-digit',
    });
  }

  protected async start(): Promise<void> {
    this.cameraNote.set(null);
    const detectorCtor = (
      globalThis as unknown as { BarcodeDetector?: new (o: { formats: string[] }) => Detector }
    ).BarcodeDetector;
    if (!detectorCtor || !navigator.mediaDevices?.getUserMedia) {
      this.cameraNote.set(
        'Ce navigateur ne sait pas lire un QR code avec la caméra : collez le code ci-dessous.',
      );
      return;
    }

    try {
      this.stream = await navigator.mediaDevices.getUserMedia({
        video: { facingMode: 'environment' },
      });
    } catch {
      this.cameraNote.set(
        'Autorisez la caméra pour scanner le pass, ou collez le code ci-dessous.',
      );
      return;
    }

    this.scanning.set(true);
    // The <video> exists after the next render.
    await new Promise((resolve) => setTimeout(resolve));
    const element = this.video()?.nativeElement;
    if (!element) {
      this.release();
      return;
    }

    element.srcObject = this.stream;
    await element.play();
    const detector = new detectorCtor({ formats: ['qr_code'] });
    const tick = async (): Promise<void> => {
      if (!this.scanning()) {
        return;
      }

      try {
        const [code] = await detector.detect(element);
        if (code) {
          this.stop();
          await this.verify(code.rawValue);
          return;
        }
      } catch {
        // A frame that cannot be read is simply skipped.
      }

      this.frame = requestAnimationFrame(() => void tick());
    };
    void tick();
  }

  protected stop(): void {
    this.release();
  }

  protected async verify(token: string): Promise<void> {
    if (!token.trim()) {
      return;
    }

    this.error.set(null);
    this.result.set(null);
    this.busy.set(true);
    try {
      this.result.set(await this.agenda.verifyPass(this.id(), token));
    } catch (e) {
      this.error.set(problemMessage(e));
    } finally {
      this.busy.set(false);
    }
  }

  private release(): void {
    cancelAnimationFrame(this.frame);
    this.stream?.getTracks().forEach((t) => t.stop());
    this.stream = null;
    this.scanning.set(false);
  }
}
