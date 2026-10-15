import { httpResource } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  signal,
  untracked,
} from '@angular/core';
import {
  FormsModule,
  NonNullableFormBuilder,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { LucideCrosshair } from '@lucide/angular';
import { InputText } from 'primeng/inputtext';
import { Select } from 'primeng/select';
import { Step, StepList, Stepper } from 'primeng/stepper';
import type { components } from '../../../core/api/api.generated';
import { problemMessage } from '../../../core/auth/auth.models';
import { AuthService } from '../../../core/auth/auth.service';
import { CatalogService } from '../../../core/catalog/catalog.service';
import { GeolocationFailure, GeolocationService } from '../../../core/geo/geolocation.service';
import {
  EstablishmentInput,
  KycView,
  OpeningHourInput,
  PartnerEstablishmentDetail,
  PartnerService,
  STATUS_LABELS,
} from '../../../core/partner/partner.service';
import { LocationPicker, PickedPosition } from '../../../shared/location-picker/location-picker';
import { GalleryManager } from '../gallery/gallery-manager';
import { HoursEditor } from './hours-editor';
import { IdentityForm } from './identity-form';

type GeoItem = components['schemas']['GeoItemDto'];
type CountryItem = components['schemas']['CountryDto'];

const MAX_CATEGORIES = 3;
/** Plateau, Abidjan: where the pin starts until the owner moves it or uses their position. */
const START: PickedPosition = { lat: 5.3235, lng: -4.0197 };
const MISSING_STEP: Readonly<Record<string, number>> = {
  category: 1,
  hours: 3,
  photo: 4,
  identity: 5,
  identity_documents: 5,
};

const STEPS = [
  { id: 1, label: 'Informations' },
  { id: 2, label: 'Localisation' },
  { id: 3, label: 'Horaires' },
  { id: 4, label: 'Photos' },
  { id: 5, label: 'Identité' },
  { id: 6, label: 'Récapitulatif' },
] as const;

/** Onboarding wizard (PRD ONB-01). Steps 1 and 2 create the draft; every later step saves on its own, so the owner can resume. */
@Component({
  selector: 'app-wizard',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    FormsModule,
    ReactiveFormsModule,
    RouterLink,
    InputText,
    Select,
    Stepper,
    StepList,
    Step,
    LucideCrosshair,
    LocationPicker,
    HoursEditor,
    GalleryManager,
    IdentityForm,
  ],
  styles: `
    .page {
      display: grid;
      gap: 1.25rem;
      padding-block: clamp(1.5rem, 4vw, 3rem);
    }
    .panel {
      display: grid;
      gap: 1.25rem;
      padding: clamp(1.25rem, 3vw, 2rem);
      border: 1px solid var(--kb-border);
      border-radius: var(--radius-lg);
      background: #fff;
      box-shadow: var(--shadow-soft);
    }
    .panel > form {
      display: grid;
      gap: 1rem;
    }
    .grid {
      display: grid;
      gap: 1rem;

      @media (min-width: 640px) {
        grid-template-columns: 1fr 1fr;
      }
    }
    .grid-3 {
      display: grid;
      gap: 1rem;

      @media (min-width: 640px) {
        grid-template-columns: repeat(3, 1fr);
      }
    }
    .field {
      display: grid;
      gap: 0.375rem;

      label,
      legend {
        font-size: 0.875rem;
        font-weight: 600;
      }
      input.p-inputtext {
        width: 100%;
        min-height: 3rem;
      }
    }
    fieldset.field {
      padding: 0;
      border: 0;
      margin: 0;
    }
    textarea {
      width: 100%;
      min-height: 7rem;
      padding: 0.75rem;
      border: 1px solid var(--kb-border);
      border-radius: var(--radius-sm);
      font: inherit;
      color: inherit;
    }
    .chips {
      display: flex;
      flex-wrap: wrap;
      gap: 0.5rem;
    }
    .chip {
      min-height: 2.75rem;
      padding: 0.5rem 1rem;
      border: 1.5px solid var(--kb-border);
      border-radius: var(--radius-pill);
      background: #fff;
      font: inherit;
      font-weight: 600;
      cursor: pointer;
      transition:
        border-color 200ms ease,
        background-color 200ms ease;

      &[aria-pressed='true'] {
        border-color: var(--kb-primary-600);
        background: var(--kb-primary-50);
        color: var(--kb-primary-800);
      }
      &:disabled {
        opacity: 0.5;
        cursor: not-allowed;
      }
    }
    .actions {
      display: flex;
      flex-wrap: wrap;
      gap: 0.75rem;
    }
    .recap {
      display: grid;
      gap: 0.5rem;
      padding: 0;
    }
    .recap li {
      display: flex;
      flex-wrap: wrap;
      gap: 0.5rem 1rem;
      align-items: center;
      justify-content: space-between;
      padding: 0.625rem 0.875rem;
      border: 1px solid var(--kb-border);
      border-radius: var(--radius-md);
    }
    .banner {
      padding: 0.875rem 1rem;
      border: 1px solid var(--kb-primary-200);
      border-radius: var(--radius-md);
      background: var(--kb-primary-50);
      font-weight: 500;
    }
    .coords {
      font-size: 0.875rem;
      color: var(--kb-ink-muted);
    }
    .stepper {
      overflow-x: auto;
    }
  `,
  template: `
    <div class="container page">
      <a class="link" routerLink="/pro">← Mes établissements</a>
      <div>
        <p class="eyebrow">Inscription d'un établissement</p>
        <h1>{{ detail()?.name ?? 'Nouvel établissement' }}</h1>
      </div>

      @if (loadError()) {
        <p class="form-error" role="alert">{{ loadError() }}</p>
      } @else {
        @if (detail(); as d) {
          @if (d.status !== 'Draft') {
            <p class="banner" role="status">
              Statut : <strong>{{ statusLabel() }}</strong>
              @if (d.status === 'PendingReview') {
                — votre dossier est en cours de validation, vous serez prévenu par e-mail.
              }
              @if (d.status === 'Rejected') {
                — {{ d.latestReview?.comment }}
              }
            </p>
          } @else if (d.latestReview?.status === 'ChangesRequested') {
            <p class="form-error" role="status">
              Corrections demandées : {{ d.latestReview?.comment }}
            </p>
          }
        }

        <div class="stepper">
          <p-stepper [value]="step()" (valueChange)="goTo($event ?? 1)">
            <p-step-list>
              @for (s of steps; track s.id) {
                <p-step [value]="s.id" [disabled]="s.id > 2 && !detail()">{{ s.label }}</p-step>
              }
            </p-step-list>
          </p-stepper>
        </div>

        <section class="panel" [attr.aria-label]="'Étape ' + step()">
          @switch (step()) {
            @case (1) {
              <form (ngSubmit)="next(1)" [formGroup]="basics" novalidate>
                <div class="field">
                  <label for="w-name">Nom de l'établissement</label>
                  <input pInputText id="w-name" formControlName="name" maxlength="150" />
                  @if (invalid('name')) {
                    <p class="field-error" role="alert">
                      Le nom est requis (2 caractères minimum).
                    </p>
                  }
                </div>

                <fieldset class="field">
                  <legend>Catégories (jusqu'à {{ maxCategories }})</legend>
                  <div class="chips">
                    @for (category of categoryList(); track category.id) {
                      <button
                        type="button"
                        class="chip"
                        [attr.aria-pressed]="selectedCategories().includes(category.id)"
                        [disabled]="
                          !selectedCategories().includes(category.id) &&
                          selectedCategories().length >= maxCategories
                        "
                        (click)="toggleCategory(category.id)"
                      >
                        {{ category.name }}
                      </button>
                    }
                  </div>
                  @if (showErrors() && selectedCategories().length === 0) {
                    <p class="field-error" role="alert">Choisissez au moins une catégorie.</p>
                  }
                </fieldset>

                <div class="field">
                  <label for="w-description">Description</label>
                  <textarea
                    id="w-description"
                    formControlName="description"
                    maxlength="4000"
                  ></textarea>
                  @if (invalid('description')) {
                    <p class="field-error" role="alert">
                      Décrivez votre établissement (10 caractères minimum).
                    </p>
                  }
                </div>

                <div class="grid">
                  <div class="field">
                    <label for="w-phone">Téléphone du service client</label>
                    <input
                      pInputText
                      id="w-phone"
                      type="tel"
                      formControlName="phone"
                      autocomplete="tel"
                      inputmode="tel"
                      placeholder="+225 07 00 00 00 00"
                    />
                    @if (invalid('phone')) {
                      <p class="field-error" role="alert">
                        Saisissez un numéro valide (8 à 15 chiffres).
                      </p>
                    }
                  </div>
                  <div class="field">
                    <label for="w-email">E-mail de contact (facultatif)</label>
                    <input
                      pInputText
                      id="w-email"
                      type="email"
                      formControlName="email"
                      autocomplete="email"
                    />
                    @if (invalid('email')) {
                      <p class="field-error" role="alert">Adresse e-mail invalide.</p>
                    }
                  </div>
                </div>

                <div class="actions">
                  <button class="btn btn-primary" type="submit">Continuer</button>
                </div>
              </form>
            }

            @case (2) {
              <form (ngSubmit)="saveBasics()" [formGroup]="basics" novalidate>
                <div class="grid-3">
                  <div class="field">
                    <label for="w-region">Région</label>
                    <p-select
                      inputId="w-region"
                      name="region"
                      [options]="regions.hasValue() ? regions.value() : []"
                      optionLabel="name"
                      optionValue="id"
                      placeholder="Choisir"
                      [ngModel]="regionId()"
                      [ngModelOptions]="{ standalone: true }"
                      (ngModelChange)="selectRegion($event)"
                      [fluid]="true"
                    />
                  </div>
                  <div class="field">
                    <label for="w-city">Ville</label>
                    <p-select
                      inputId="w-city"
                      name="city"
                      [options]="cities.hasValue() ? cities.value() : []"
                      optionLabel="name"
                      optionValue="id"
                      placeholder="Choisir"
                      [disabled]="!regionId()"
                      [ngModel]="cityId()"
                      [ngModelOptions]="{ standalone: true }"
                      (ngModelChange)="selectCity($event)"
                      [fluid]="true"
                    />
                  </div>
                  <div class="field">
                    <label for="w-commune">Commune</label>
                    <p-select
                      inputId="w-commune"
                      name="commune"
                      [options]="communes.hasValue() ? communes.value() : []"
                      optionLabel="name"
                      optionValue="id"
                      placeholder="Choisir"
                      [disabled]="!cityId()"
                      [ngModel]="communeId()"
                      [ngModelOptions]="{ standalone: true }"
                      (ngModelChange)="communeId.set($event ?? '')"
                      [fluid]="true"
                    />
                    @if (showErrors() && !communeId()) {
                      <p class="field-error" role="alert">Choisissez la commune.</p>
                    }
                  </div>
                </div>

                <div class="field">
                  <label for="w-address">Adresse</label>
                  <input
                    pInputText
                    id="w-address"
                    formControlName="address"
                    autocomplete="street-address"
                    maxlength="250"
                  />
                  @if (invalid('address')) {
                    <p class="field-error" role="alert">
                      Saisissez l'adresse (3 caractères minimum).
                    </p>
                  }
                </div>

                <div class="field">
                  <span id="w-map-label" class="visually-hidden">Position exacte</span>
                  <p class="hint">
                    Cliquez sur la carte ou déplacez le repère pour indiquer la position exacte.
                  </p>
                  <app-location-picker
                    [latitude]="position().lat"
                    [longitude]="position().lng"
                    (moved)="position.set($event)"
                  />
                  <p class="coords">
                    Latitude {{ position().lat }} · Longitude {{ position().lng }}
                  </p>
                  <div class="actions">
                    <button
                      type="button"
                      class="btn btn-ghost btn-sm"
                      [disabled]="locating()"
                      (click)="useMyPosition()"
                    >
                      <svg lucideCrosshair aria-hidden="true"></svg>
                      {{ locating() ? 'Localisation…' : 'Utiliser ma position' }}
                    </button>
                  </div>
                  @if (locationError()) {
                    <p class="form-error" role="alert">{{ locationError() }}</p>
                  }
                </div>

                @if (error()) {
                  <p class="form-error" role="alert">{{ error() }}</p>
                }
                <div class="actions">
                  <button class="btn btn-ghost" type="button" (click)="goTo(1)">Retour</button>
                  <button class="btn btn-primary" type="submit" [disabled]="busy() || !editable()">
                    {{ busy() ? 'Enregistrement…' : 'Enregistrer et continuer' }}
                  </button>
                </div>
              </form>
            }

            @case (3) {
              <h2>Horaires d'ouverture</h2>
              <app-hours-editor
                [hours]="hoursInput()"
                [disabled]="!editable()"
                (saved)="saveHours($event)"
              />
              @if (error()) {
                <p class="form-error" role="alert">{{ error() }}</p>
              }
              <div class="actions">
                <button class="btn btn-ghost" type="button" (click)="goTo(2)">Retour</button>
                <button class="btn btn-ghost" type="button" (click)="goTo(4)">
                  Passer à l'étape suivante
                </button>
              </div>
            }

            @case (4) {
              <h2>Photo de profil et devanture</h2>
              <p class="hint">
                Au moins une photo est obligatoire. La première est utilisée comme couverture.
              </p>
              @if (detail(); as d) {
                <app-gallery-manager
                  [establishmentId]="d.id"
                  [media]="d.media"
                  [videos]="d.videos ?? []"
                  [videosAllowed]="d.videosAllowed"
                  [maxVideos]="d.maxVideos"
                  [maxPhotos]="d.maxPhotos"
                  [editable]="d.isEditable"
                  (changed)="refresh()"
                />
              }
              <div class="actions">
                <button class="btn btn-ghost" type="button" (click)="goTo(3)">Retour</button>
                <button class="btn btn-primary" type="button" (click)="goTo(5)">Continuer</button>
              </div>
            }

            @case (5) {
              <h2>Pièce d'identité du gérant</h2>
              <app-identity-form
                [kyc]="kyc()"
                [accountName]="accountName()"
                (changed)="onKyc($event)"
              />
              <div class="actions">
                <button class="btn btn-ghost" type="button" (click)="goTo(4)">Retour</button>
                <button class="btn btn-primary" type="button" (click)="goTo(6)">Continuer</button>
              </div>
            }

            @case (6) {
              <h2>Récapitulatif</h2>
              @if (detail(); as d) {
                <ul class="recap">
                  <li>
                    <span>Nom</span><strong>{{ d.name }}</strong>
                  </li>
                  <li>
                    <span>Adresse</span><strong>{{ d.addressLine }}</strong>
                  </li>
                  <li>
                    <span>Téléphone</span><strong>{{ d.customerServicePhone }}</strong>
                  </li>
                  <li>
                    <span>Horaires</span><strong>{{ d.openingHours.length }} plage(s)</strong>
                  </li>
                  <li>
                    <span>Photos</span><strong>{{ d.media.length }}</strong>
                  </li>
                  <li>
                    <span>Pièce d'identité</span><strong>{{ kycLabel() }}</strong>
                  </li>
                </ul>

                @if (d.missing.length > 0 && d.status === 'Draft') {
                  <div class="form-error" role="alert">
                    <p>Il reste à compléter avant de soumettre :</p>
                    <ul>
                      @for (item of d.missing; track item.code) {
                        <li>
                          {{ item.label }}
                          <button type="button" class="link" (click)="goTo(stepFor(item.code))">
                            Y aller
                          </button>
                        </li>
                      }
                    </ul>
                  </div>
                }

                @if (submitted()) {
                  <p class="form-success" role="status">
                    Dossier envoyé ! L'équipe Keke Beauty l'examine ; vous serez prévenu par e-mail
                    de la décision.
                  </p>
                }
                @if (error()) {
                  <p class="form-error" role="alert">{{ error() }}</p>
                }

                <div class="actions">
                  <button class="btn btn-ghost" type="button" (click)="goTo(5)">Retour</button>
                  @if (d.status === 'Draft') {
                    <button
                      class="btn btn-primary"
                      type="button"
                      [disabled]="busy() || d.missing.length > 0"
                      (click)="submit()"
                    >
                      {{ busy() ? 'Envoi…' : 'Soumettre mon dossier' }}
                    </button>
                  } @else {
                    <a class="btn btn-primary" routerLink="/pro">Retour à mes établissements</a>
                  }
                </div>
              }
            }
          }
        </section>
      }
    </div>
  `,
})
export class Wizard {
  /** Bound from the `:id` route parameter: a UUID, or `nouveau` for a brand-new establishment. */
  readonly id = input<string>('nouveau');

  private readonly partner = inject(PartnerService);
  private readonly router = inject(Router);
  private readonly geolocation = inject(GeolocationService);
  private readonly auth = inject(AuthService);
  private readonly categories = inject(CatalogService).categories;

  protected readonly steps = STEPS;
  protected readonly maxCategories = MAX_CATEGORIES;
  protected readonly step = signal(1);
  protected readonly detail = signal<PartnerEstablishmentDetail | null>(null);
  protected readonly kyc = signal<KycView | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly loadError = signal('');
  protected readonly submitted = signal(false);
  protected readonly showErrors = signal(false);
  protected readonly locating = signal(false);
  protected readonly locationError = signal('');

  protected readonly selectedCategories = signal<readonly string[]>([]);
  protected readonly regionId = signal('');
  protected readonly cityId = signal('');
  protected readonly communeId = signal('');
  protected readonly position = signal<PickedPosition>(START);

  protected readonly basics = inject(NonNullableFormBuilder).group({
    name: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(150)]],
    description: ['', [Validators.required, Validators.minLength(10), Validators.maxLength(4000)]],
    phone: ['', [Validators.required, Validators.pattern(/^\+?[0-9][0-9 .-]{6,18}[0-9]$/)]],
    email: ['', [Validators.email, Validators.maxLength(254)]],
    address: ['', [Validators.required, Validators.minLength(3), Validators.maxLength(250)]],
  });

  protected readonly establishmentId = computed(() =>
    this.id() && this.id() !== 'nouveau' ? this.id() : null,
  );
  protected readonly editable = computed(() => this.detail()?.isEditable ?? true);
  protected readonly categoryList = computed(() =>
    this.categories.hasValue() ? this.categories.value() : [],
  );
  protected readonly accountName = computed(() => {
    const user = this.auth.user();
    return user ? `${user.firstName} ${user.lastName}`.trim() : '';
  });
  protected readonly statusLabel = computed(() => STATUS_LABELS[this.detail()?.status ?? ''] ?? '');
  protected readonly hoursInput = computed<readonly OpeningHourInput[]>(() =>
    (this.detail()?.openingHours ?? []).map((h) => ({
      dayOfWeek: h.dayOfWeek,
      opensAt: h.opensAt,
      closesAt: h.closesAt,
    })),
  );
  protected readonly kycLabel = computed(() => {
    const kyc = this.kyc();
    if (!kyc) {
      return 'Non renseignée';
    }

    const complete = kyc.hasFront && kyc.hasBack;
    switch (kyc.status) {
      case 'Approved':
        return 'Validée';
      case 'PendingReview':
        return 'En cours de vérification';
      default:
        return complete ? 'Complète, prête à être soumise' : 'Incomplète : recto et verso requis';
    }
  });

  private readonly countries = httpResource<CountryItem[]>(() => '/api/v1/geo/countries');
  private readonly countryId = computed(() =>
    this.countries.hasValue() ? this.countries.value()[0]?.id : undefined,
  );
  protected readonly regions = httpResource<GeoItem[]>(() =>
    this.countryId() ? `/api/v1/geo/countries/${this.countryId()}/regions` : undefined,
  );
  protected readonly cities = httpResource<GeoItem[]>(() =>
    this.regionId() ? `/api/v1/geo/regions/${this.regionId()}/cities` : undefined,
  );
  protected readonly communes = httpResource<GeoItem[]>(() =>
    this.cityId() ? `/api/v1/geo/cities/${this.cityId()}/communes` : undefined,
  );

  private loadedFor: string | null = null;

  constructor() {
    effect(() => {
      const id = this.establishmentId();
      untracked(() => void this.load(id));
    });
  }

  protected invalid(name: keyof typeof this.basics.controls): boolean {
    const control = this.basics.controls[name];
    return control.invalid && (control.touched || control.dirty || this.showErrors());
  }

  protected toggleCategory(id: string): void {
    this.selectedCategories.update((current) =>
      current.includes(id) ? current.filter((c) => c !== id) : [...current, id],
    );
  }

  protected selectRegion(id: string | null): void {
    this.regionId.set(id ?? '');
    this.cityId.set('');
    this.communeId.set('');
  }

  protected selectCity(id: string | null): void {
    this.cityId.set(id ?? '');
    this.communeId.set('');
  }

  protected goTo(step: number): void {
    if (step > 2 && !this.detail()) {
      return;
    }

    this.error.set('');
    this.step.set(step);
  }

  protected stepFor(code: string): number {
    return MISSING_STEP[code] ?? 6;
  }

  /** Step 1 only validates locally: nothing is saved before the location is known. */
  protected next(from: number): void {
    this.showErrors.set(true);
    const { name, description, phone, email } = this.basics.controls;
    const valid =
      [name, description, phone, email].every((c) => c.valid) &&
      this.selectedCategories().length > 0;
    if (!valid) {
      [name, description, phone, email].forEach((c) => c.markAsTouched());
      return;
    }

    this.showErrors.set(false);
    this.goTo(from + 1);
  }

  protected async saveBasics(): Promise<void> {
    this.showErrors.set(true);
    this.basics.markAllAsTouched();
    if (this.basics.invalid || !this.communeId() || this.selectedCategories().length === 0) {
      return;
    }

    const value = this.basics.getRawValue();
    const input: EstablishmentInput = {
      name: value.name.trim(),
      description: value.description.trim(),
      customerServicePhone: value.phone.trim(),
      email: value.email.trim() || null,
      categoryIds: [...this.selectedCategories()],
      communeId: this.communeId(),
      addressLine: value.address.trim(),
      latitude: this.position().lat,
      longitude: this.position().lng,
    };

    this.busy.set(true);
    this.error.set('');
    try {
      const existing = this.establishmentId();
      if (existing) {
        await this.partner.update(existing, input);
        await this.refresh();
        this.goTo(3);
      } else {
        const created = await this.partner.create(input);
        this.showErrors.set(false);
        await this.router.navigate(['/pro/etablissements', created], { replaceUrl: true });
      }
    } catch (error) {
      this.error.set(problemMessage(error));
    } finally {
      this.busy.set(false);
    }
  }

  protected async saveHours(hours: OpeningHourInput[]): Promise<void> {
    const id = this.detail()?.id;
    if (!id) {
      return;
    }

    this.error.set('');
    try {
      await this.partner.setOpeningHours(id, hours);
      await this.refresh();
      this.goTo(4);
    } catch (error) {
      this.error.set(problemMessage(error));
    }
  }

  protected onKyc(view: KycView): void {
    this.kyc.set(view);
    void this.refresh();
  }

  protected async submit(): Promise<void> {
    const id = this.detail()?.id;
    if (!id) {
      return;
    }

    this.busy.set(true);
    this.error.set('');
    try {
      await this.partner.submit(id);
      this.submitted.set(true);
      await this.refresh();
    } catch (error) {
      this.error.set(problemMessage(error));
    } finally {
      this.busy.set(false);
    }
  }

  protected async useMyPosition(): Promise<void> {
    this.locating.set(true);
    this.locationError.set('');
    try {
      const here = await this.geolocation.locate();
      this.position.set({ lat: here.lat, lng: here.lng });
    } catch (error) {
      this.locationError.set(
        error instanceof GeolocationFailure ? error.userMessage : 'Position indisponible.',
      );
    } finally {
      this.locating.set(false);
    }
  }

  /** Reloads the server state without touching the form the owner may be editing. */
  protected async refresh(): Promise<void> {
    const id = this.detail()?.id ?? this.establishmentId();
    if (!id) {
      return;
    }

    try {
      this.detail.set(await this.partner.get(id));
      this.kyc.set(await this.partner.getKyc());
    } catch (error) {
      this.loadError.set(problemMessage(error, "Impossible de charger l'établissement."));
    }
  }

  private async load(id: string | null): Promise<void> {
    if (id === this.loadedFor) {
      return;
    }

    this.loadedFor = id;
    this.loadError.set('');
    if (!id) {
      this.detail.set(null);
      this.step.set(1);
      return;
    }

    try {
      const [detail, kyc] = await Promise.all([this.partner.get(id), this.partner.getKyc()]);
      this.detail.set(detail);
      this.kyc.set(kyc);
      this.fillForm(detail);
      this.step.set(this.firstStep(detail));
    } catch (error) {
      this.loadError.set(problemMessage(error, "Impossible de charger l'établissement."));
    }
  }

  private fillForm(detail: PartnerEstablishmentDetail): void {
    this.basics.setValue({
      name: detail.name,
      description: detail.description,
      phone: detail.customerServicePhone,
      email: detail.email ?? '',
      address: detail.addressLine,
    });
    this.selectedCategories.set(detail.categoryIds);
    this.regionId.set(detail.regionId);
    this.cityId.set(detail.cityId);
    this.communeId.set(detail.communeId);
    this.position.set({ lat: detail.latitude, lng: detail.longitude });
  }

  /** Resume where the owner left off: the first missing item, or the summary once complete or submitted. */
  private firstStep(detail: PartnerEstablishmentDetail): number {
    if (detail.status !== 'Draft' || detail.missing.length === 0) {
      return 6;
    }

    return Math.min(...detail.missing.map((m) => this.stepFor(m.code)));
  }
}
