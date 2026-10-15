import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  computed,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { InputText } from 'primeng/inputtext';
import { Select } from 'primeng/select';
import { problemMessage } from '../../../core/auth/auth.models';
import {
  DOCUMENT_TYPES,
  DocumentSide,
  KycInput,
  KycView,
  PartnerService,
} from '../../../core/partner/partner.service';

interface DocumentSlot {
  readonly side: DocumentSide;
  readonly label: string;
  readonly required: boolean;
}

const SLOTS: readonly DocumentSlot[] = [
  { side: 'front', label: 'Recto de la pièce', required: true },
  { side: 'back', label: 'Verso de la pièce', required: true },
  { side: 'selfie', label: 'Selfie avec la pièce (facultatif)', required: false },
];

const STATUS_MESSAGES: Readonly<Record<string, string>> = {
  PendingReview: "Votre pièce d'identité est en cours de vérification par l'équipe Keke Beauty.",
  Approved:
    "Votre pièce d'identité est validée : elle sera réutilisée pour vos autres établissements.",
  Rejected:
    "Votre pièce d'identité a été refusée. Corrigez-la puis soumettez à nouveau votre dossier.",
  ChangesRequested:
    'Des corrections sont demandées sur votre pièce. Modifiez-la puis soumettez à nouveau.',
};

/** Step 5 of the wizard: identity of the owner (PRD ONB-01). Documents are sent to the private bucket and never shown back. */
@Component({
  selector: 'app-identity-form',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule, InputText, Select],
  styles: `
    :host {
      display: grid;
      gap: 1rem;
    }
    form {
      display: grid;
      gap: 1rem;
    }
    .row {
      display: grid;
      gap: 1rem;

      @media (min-width: 640px) {
        grid-template-columns: 1fr 1fr;
      }
    }
    .field {
      display: grid;
      gap: 0.375rem;

      label {
        font-size: 0.875rem;
        font-weight: 600;
      }
    }
    .field input.p-inputtext {
      width: 100%;
      min-height: 3rem;
    }
    .docs {
      display: grid;
      gap: 0.75rem;
      padding: 0;
      border: 0;
      margin: 0;
    }
    legend {
      padding: 0;
      margin-bottom: 0.25rem;
      font-weight: 600;
    }
    .doc {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: space-between;
      gap: 0.75rem;
      padding: 0.75rem 1rem;
      border: 1px solid var(--kb-border);
      border-radius: var(--radius-md);
      background: #fff;
    }
    .doc__state {
      font-size: 0.875rem;
      font-weight: 600;
      color: var(--kb-ink-muted);
    }
    .doc__state.done {
      color: var(--kb-success);
    }
    .doc input[type='file'] {
      max-width: 100%;
      font: inherit;
      font-size: 0.875rem;
    }
    .privacy {
      font-size: 0.875rem;
      color: var(--kb-ink-muted);
    }
  `,
  template: `
    @if (statusMessage()) {
      <p class="form-success" role="status">{{ statusMessage() }}</p>
    }
    @if (kyc()?.reviewComment; as comment) {
      <p class="form-error" role="status">Commentaire de l'équipe : {{ comment }}</p>
    }

    <form [formGroup]="form" (ngSubmit)="save()" novalidate>
      <div class="row">
        <div class="field">
          <label for="kyc-holder">Nom complet figurant sur la pièce</label>
          <input pInputText id="kyc-holder" formControlName="holderName" autocomplete="name" />
          @if (invalid('holderName')) {
            <p class="field-error" role="alert">Saisissez le nom tel qu'il figure sur la pièce.</p>
          }
        </div>
        <div class="field">
          <label for="kyc-type">Type de pièce</label>
          <p-select
            inputId="kyc-type"
            formControlName="documentType"
            [options]="documentTypes"
            optionLabel="label"
            optionValue="value"
            [fluid]="true"
          />
        </div>
      </div>

      <div class="row">
        <div class="field">
          <label for="kyc-number">Numéro de la pièce</label>
          <input pInputText id="kyc-number" formControlName="documentNumber" autocomplete="off" />
          @if (invalid('documentNumber')) {
            <p class="field-error" role="alert">
              Saisissez le numéro de la pièce (4 caractères minimum).
            </p>
          }
        </div>
        <div class="field">
          <label for="kyc-expiry">Date d'expiration</label>
          <input pInputText id="kyc-expiry" type="date" formControlName="documentExpiry" />
        </div>
      </div>

      @if (editable()) {
        <button class="btn btn-ghost" type="submit" [disabled]="busy()">
          {{ busy() ? 'Enregistrement…' : 'Enregistrer les informations' }}
        </button>
      }
    </form>

    <fieldset class="docs">
      <legend>Photos de la pièce</legend>
      @for (slot of slots; track slot.side) {
        <div class="doc">
          <label [for]="'kyc-file-' + slot.side">{{ slot.label }}</label>
          <span class="doc__state" [class.done]="sent(slot.side)">
            {{ sent(slot.side) ? 'Envoyé ✓' : slot.required ? 'À envoyer' : 'Non envoyé' }}
          </span>
          @if (editable()) {
            <input
              type="file"
              [id]="'kyc-file-' + slot.side"
              accept="image/jpeg,image/png,image/webp"
              [disabled]="busy()"
              (change)="upload(slot.side, $any($event.target))"
            />
          }
        </div>
      }
    </fieldset>
    <p class="privacy">
      Vos documents sont stockés de façon privée et chiffrée ; seuls les administrateurs de Keke
      Beauty peuvent les consulter, et chaque consultation est journalisée.
    </p>

    @if (error()) {
      <p class="form-error" role="alert">{{ error() }}</p>
    }
  `,
})
export class IdentityForm implements OnInit {
  readonly kyc = input<KycView | null>(null);
  readonly accountName = input('');
  readonly changed = output<KycView>();

  private readonly partner = inject(PartnerService);
  protected readonly documentTypes = DOCUMENT_TYPES;
  protected readonly slots = SLOTS;
  protected readonly busy = signal(false);
  protected readonly error = signal('');

  protected readonly form = inject(NonNullableFormBuilder).group({
    holderName: ['', [Validators.required, Validators.minLength(3), Validators.maxLength(150)]],
    documentType: ['NationalId', Validators.required],
    documentNumber: ['', [Validators.required, Validators.minLength(4), Validators.maxLength(40)]],
    documentExpiry: [''],
  });

  protected readonly editable = computed(() => this.kyc()?.isEditable ?? true);
  protected readonly statusMessage = computed(
    () => STATUS_MESSAGES[this.kyc()?.status ?? ''] ?? '',
  );

  ngOnInit(): void {
    const kyc = this.kyc();
    this.form.patchValue({
      holderName: kyc?.holderName || this.accountName(),
      documentType: kyc?.documentType ?? 'NationalId',
      documentNumber: kyc?.documentNumber ?? '',
      documentExpiry: kyc?.documentExpiry ?? '',
    });
    if (!this.editable()) {
      this.form.disable();
    }
  }

  protected invalid(name: 'holderName' | 'documentNumber'): boolean {
    const control = this.form.controls[name];
    return control.invalid && (control.touched || control.dirty);
  }

  protected sent(side: DocumentSide): boolean {
    const kyc = this.kyc();
    return side === 'front' ? !!kyc?.hasFront : side === 'back' ? !!kyc?.hasBack : !!kyc?.hasSelfie;
  }

  protected async save(): Promise<void> {
    await this.run(() => this.persist());
  }

  protected async upload(side: DocumentSide, input: HTMLInputElement): Promise<void> {
    const file = input.files?.[0];
    input.value = '';
    if (!file) {
      return;
    }

    await this.run(async () => {
      // The server needs the identity data before it accepts a document.
      if (!(await this.persist())) {
        return null;
      }

      return this.partner.uploadDocument(side, file);
    });
  }

  private async persist(): Promise<KycView | null> {
    this.form.markAllAsTouched();
    if (this.form.invalid) {
      return null;
    }

    const value = this.form.getRawValue();
    const payload: KycInput = {
      holderName: value.holderName.trim(),
      documentType: value.documentType,
      documentNumber: value.documentNumber.trim(),
      documentExpiry: value.documentExpiry || null,
    };
    return this.partner.saveKyc(payload);
  }

  private async run(action: () => Promise<KycView | null>): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    try {
      const view = await action();
      if (view) {
        this.changed.emit(view);
      }
    } catch (error) {
      this.error.set(problemMessage(error));
    } finally {
      this.busy.set(false);
    }
  }
}
