import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  resource,
  signal,
  untracked,
} from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { LucideArrowDown, LucideArrowUp, LucidePencil, LucideTrash2 } from '@lucide/angular';
import { InputText } from 'primeng/inputtext';
import { Select } from 'primeng/select';
import { problemMessage } from '../../../core/auth/auth.models';
import { CatalogService } from '../../../core/catalog/catalog.service';
import { formatDuration, formatMoney } from '../../../core/format/format';
import {
  PartnerEstablishmentDetail,
  PartnerService,
  PartnerServiceItem,
  ServiceInput,
} from '../../../core/partner/partner.service';

const PRICE_TYPES = [
  { value: 'Fixed', label: 'Prix fixe' },
  { value: 'From', label: 'À partir de' },
];

/** `/pro/etablissements/:id/prestations` — service groups and services with their price grid (PRD SVC-01/02). */
@Component({
  selector: 'app-catalog-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    InputText,
    Select,
    LucideArrowDown,
    LucideArrowUp,
    LucidePencil,
    LucideTrash2,
  ],
  styles: `
    .page {
      display: grid;
      gap: 1.5rem;
      padding-block: clamp(1.5rem, 4vw, 3rem);
    }
    section {
      display: grid;
      gap: 1rem;
      padding: clamp(1rem, 3vw, 1.75rem);
      border: 1px solid var(--kb-border);
      border-radius: var(--radius-lg);
      background: #fff;
      box-shadow: var(--shadow-soft);
    }
    .inline {
      display: flex;
      flex-wrap: wrap;
      gap: 0.5rem;

      input.p-inputtext {
        flex: 1 1 14rem;
        min-height: 3rem;
      }
    }
    .rows {
      display: grid;
      gap: 0.5rem;
    }
    .row {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 0.5rem 1rem;
      padding: 0.625rem 0.875rem;
      border: 1px solid var(--kb-border);
      border-radius: var(--radius-md);
    }
    .row__main {
      flex: 1 1 14rem;
      display: grid;
    }
    .row__meta {
      font-size: var(--fs-sm);
      color: var(--kb-ink-muted);
    }
    .tools {
      display: flex;
    }
    .icon-btn {
      display: inline-grid;
      place-items: center;
      width: 2.75rem;
      height: 2.75rem;
      border: 0;
      border-radius: var(--radius-pill);
      background: transparent;
      color: var(--kb-ink-muted);
      cursor: pointer;

      &:hover:not(:disabled) {
        background: var(--kb-primary-50);
        color: var(--kb-primary-700);
      }
      &:disabled {
        opacity: 0.35;
        cursor: not-allowed;
      }
    }
    .icon-btn svg {
      width: 1.125rem;
      height: 1.125rem;
    }
    form {
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
    .field {
      display: grid;
      gap: 0.375rem;

      label {
        font-size: var(--fs-sm);
        font-weight: 600;
      }
      input.p-inputtext {
        width: 100%;
        min-height: 3rem;
      }
    }
    .checks {
      display: flex;
      flex-wrap: wrap;
      gap: 1.25rem;
    }
    .checks label {
      display: flex;
      align-items: center;
      gap: 0.5rem;
      min-height: 2.75rem;
    }
    .checks input {
      width: 1.25rem;
      height: 1.25rem;
      accent-color: var(--kb-primary-600);
    }
    .tag {
      padding: 0.0625rem 0.5rem;
      border-radius: var(--radius-pill);
      background: var(--kb-primary-50);
      font-size: var(--fs-2xs);
      font-weight: 600;
      color: var(--kb-primary-700);
    }
    textarea {
      width: 100%;
      min-height: 5rem;
      padding: 0.75rem;
      border: 1px solid var(--kb-border);
      border-radius: var(--radius-sm);
      font: inherit;
      color: inherit;
    }
  `,
  template: `
    <div class="container page">
      <a class="link" routerLink="/pro">← Mes établissements</a>
      @if (detail.hasValue()) {
        @let e = detail.value();
        <div>
          <p class="eyebrow">{{ e.name }}</p>
          <h1>Prestations et <em>tarifs</em></h1>
        </div>

        @if (!e.isEditable) {
          <p class="form-error" role="status">
            Ce dossier est verrouillé : les prestations ne sont pas modifiables.
          </p>
        }
        @if (error()) {
          <p class="form-error" role="alert">{{ error() }}</p>
        }

        <section aria-labelledby="groups-title">
          <h2 id="groups-title">Rubriques</h2>
          <p class="hint">
            Regroupez vos prestations (ex. « Tresses », « Soins du visage »). Facultatif.
          </p>
          @if (e.groups.length > 0) {
            <ul class="rows">
              @for (group of e.groups; track group.id) {
                <li class="row">
                  <span class="row__main">
                    <strong>{{ group.name }}</strong>
                  </span>
                  @if (e.isEditable) {
                    <span class="tools">
                      <button
                        type="button"
                        class="icon-btn"
                        [attr.aria-label]="'Renommer la rubrique ' + group.name"
                        (click)="renameGroup(e, group.id, group.name)"
                      >
                        <svg lucidePencil aria-hidden="true"></svg>
                      </button>
                      <button
                        type="button"
                        class="icon-btn"
                        [attr.aria-label]="'Supprimer la rubrique ' + group.name"
                        (click)="removeGroup(e, group.id)"
                      >
                        <svg lucideTrash2 aria-hidden="true"></svg>
                      </button>
                    </span>
                  }
                </li>
              }
            </ul>
          }
          @if (e.isEditable) {
            <form class="inline" (submit)="addGroup($event, e, groupName)">
              <label class="visually-hidden" for="group-name">Nom de la nouvelle rubrique</label>
              <input
                pInputText
                id="group-name"
                #groupName
                placeholder="Nouvelle rubrique"
                maxlength="120"
              />
              <button class="btn btn-ghost" type="submit" [disabled]="busy()">
                Ajouter la rubrique
              </button>
            </form>
          }
        </section>

        <section aria-labelledby="services-title">
          <h2 id="services-title">Prestations ({{ e.services.length }})</h2>
          @if (e.services.length === 0) {
            <p class="hint">Aucune prestation. Ajoutez vos soins avec leur prix et leur durée.</p>
          } @else {
            <ul class="rows">
              @for (service of e.services; track service.id; let first = $first; let last = $last) {
                <li class="row">
                  <span class="row__main">
                    <strong>{{ service.name }}</strong>
                    <span class="row__meta">
                      {{ priceLabel(service) }} · {{ duration(service.durationMinutes) }}
                      @if (groupLabel(e, service.groupId); as group) {
                        · {{ group }}
                      }
                    </span>
                  </span>
                  @if (!service.isVisible) {
                    <span class="tag">Masquée</span>
                  }
                  @if (!service.isBookable) {
                    <span class="tag">Non réservable</span>
                  }
                  @if (e.isEditable) {
                    <span class="tools">
                      <button
                        type="button"
                        class="icon-btn"
                        [disabled]="first || busy()"
                        [attr.aria-label]="'Monter ' + service.name"
                        (click)="move(e, $index, -1)"
                      >
                        <svg lucideArrowUp aria-hidden="true"></svg>
                      </button>
                      <button
                        type="button"
                        class="icon-btn"
                        [disabled]="last || busy()"
                        [attr.aria-label]="'Descendre ' + service.name"
                        (click)="move(e, $index, 1)"
                      >
                        <svg lucideArrowDown aria-hidden="true"></svg>
                      </button>
                      <button
                        type="button"
                        class="icon-btn"
                        [attr.aria-label]="'Modifier ' + service.name"
                        (click)="edit(service)"
                      >
                        <svg lucidePencil aria-hidden="true"></svg>
                      </button>
                      <button
                        type="button"
                        class="icon-btn"
                        [attr.aria-label]="'Supprimer ' + service.name"
                        (click)="removeService(e, service)"
                      >
                        <svg lucideTrash2 aria-hidden="true"></svg>
                      </button>
                    </span>
                  }
                </li>
              }
            </ul>
          }
        </section>

        @if (e.isEditable) {
          <section aria-labelledby="form-title">
            <h2 id="form-title">
              {{ editingId() ? 'Modifier la prestation' : 'Ajouter une prestation' }}
            </h2>
            <form [formGroup]="form" (ngSubmit)="saveService(e)" novalidate>
              <div class="grid">
                <div class="field">
                  <label for="svc-name">Nom</label>
                  <input pInputText id="svc-name" formControlName="name" maxlength="150" />
                  @if (invalid('name')) {
                    <p class="field-error" role="alert">
                      Le nom est requis (2 caractères minimum).
                    </p>
                  }
                </div>
                <div class="field">
                  <label for="svc-category">Catégorie</label>
                  <p-select
                    inputId="svc-category"
                    formControlName="categoryId"
                    [options]="categoryOptions(e)"
                    optionLabel="name"
                    optionValue="id"
                    [fluid]="true"
                  />
                </div>
                <div class="field">
                  <label for="svc-price">Prix (FCFA)</label>
                  <input
                    pInputText
                    id="svc-price"
                    type="number"
                    min="0"
                    step="500"
                    formControlName="price"
                    inputmode="numeric"
                  />
                  @if (invalid('price')) {
                    <p class="field-error" role="alert">Saisissez un prix valide.</p>
                  }
                </div>
                <div class="field">
                  <label for="svc-type">Type de prix</label>
                  <p-select
                    inputId="svc-type"
                    formControlName="priceType"
                    [options]="priceTypes"
                    optionLabel="label"
                    optionValue="value"
                    [fluid]="true"
                  />
                </div>
                <div class="field">
                  <label for="svc-duration">Durée (minutes)</label>
                  <input
                    pInputText
                    id="svc-duration"
                    type="number"
                    min="5"
                    max="720"
                    step="5"
                    formControlName="durationMinutes"
                    inputmode="numeric"
                  />
                  @if (invalid('durationMinutes')) {
                    <p class="field-error" role="alert">
                      La durée doit être comprise entre 5 et 720 minutes.
                    </p>
                  }
                </div>
                <div class="field">
                  <label for="svc-group">Rubrique</label>
                  <p-select
                    inputId="svc-group"
                    formControlName="groupId"
                    [options]="groupOptions(e)"
                    optionLabel="name"
                    optionValue="id"
                    placeholder="Aucune"
                    [showClear]="true"
                    [fluid]="true"
                  />
                </div>
              </div>
              <div class="field">
                <label for="svc-description">Description (facultatif)</label>
                <textarea
                  id="svc-description"
                  formControlName="description"
                  maxlength="1000"
                ></textarea>
              </div>
              <div class="checks">
                <label
                  ><input type="checkbox" formControlName="isVisible" /> Visible sur la fiche</label
                >
                <label
                  ><input type="checkbox" formControlName="isBookable" /> Réservable en ligne</label
                >
              </div>
              <div class="inline">
                <button class="btn btn-primary" type="submit" [disabled]="busy()">
                  {{
                    busy()
                      ? 'Enregistrement…'
                      : editingId()
                        ? 'Enregistrer'
                        : 'Ajouter la prestation'
                  }}
                </button>
                @if (editingId()) {
                  <button class="btn btn-ghost" type="button" (click)="cancelEdit(e)">
                    Annuler
                  </button>
                }
              </div>
            </form>
          </section>
        }
      } @else if (detail.error()) {
        <p class="form-error" role="alert">Impossible de charger cet établissement.</p>
      } @else {
        <p role="status">Chargement…</p>
      }
    </div>
  `,
})
export class CatalogPage {
  /** Bound from the `:id` route parameter. */
  readonly id = input.required<string>();

  private readonly partner = inject(PartnerService);
  private readonly categories = inject(CatalogService).categories;

  protected readonly priceTypes = PRICE_TYPES;
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly editingId = signal<string | null>(null);

  protected readonly detail = resource({
    params: () => this.id(),
    loader: ({ params }) => this.partner.get(params),
  });

  protected readonly form = inject(NonNullableFormBuilder).group({
    name: ['', [Validators.required, Validators.minLength(2), Validators.maxLength(150)]],
    description: [''],
    price: [0, [Validators.required, Validators.min(0), Validators.max(10_000_000)]],
    priceType: ['Fixed', Validators.required],
    durationMinutes: [60, [Validators.required, Validators.min(5), Validators.max(720)]],
    categoryId: ['', Validators.required],
    groupId: [null as string | null],
    isBookable: [true],
    isVisible: [true],
  });

  private readonly categoryNames = computed(() =>
    this.categories.hasValue() ? this.categories.value() : [],
  );

  constructor() {
    // Preselect the establishment's first category once it is known, so a new service is valid by default.
    effect(() => {
      const loaded = this.detail.hasValue() ? this.detail.value() : null;
      untracked(() => {
        if (loaded && !this.form.controls.categoryId.value) {
          this.form.controls.categoryId.setValue(loaded.categoryIds[0] ?? '');
        }
      });
    });
  }

  protected duration(minutes: number): string {
    return formatDuration(minutes);
  }

  protected priceLabel(service: PartnerServiceItem): string {
    const price = formatMoney(service.price, service.currencyCode);
    return service.priceType === 'From' ? `À partir de ${price}` : price;
  }

  protected groupLabel(detail: PartnerEstablishmentDetail, groupId: string | null): string | null {
    return detail.groups.find((g) => g.id === groupId)?.name ?? null;
  }

  protected categoryOptions(detail: PartnerEstablishmentDetail) {
    return this.categoryNames().filter((c) => detail.categoryIds.includes(c.id));
  }

  protected groupOptions(detail: PartnerEstablishmentDetail) {
    return detail.groups;
  }

  protected invalid(name: 'name' | 'price' | 'durationMinutes'): boolean {
    const control = this.form.controls[name];
    return control.invalid && (control.touched || control.dirty);
  }

  protected edit(service: PartnerServiceItem): void {
    this.editingId.set(service.id);
    this.form.reset({
      name: service.name,
      description: service.description ?? '',
      price: service.price,
      priceType: service.priceType,
      durationMinutes: service.durationMinutes,
      categoryId: service.categoryId,
      groupId: service.groupId,
      isBookable: service.isBookable,
      isVisible: service.isVisible,
    });
  }

  protected cancelEdit(detail: PartnerEstablishmentDetail): void {
    this.editingId.set(null);
    this.resetForm(detail);
  }

  protected async saveService(detail: PartnerEstablishmentDetail): Promise<void> {
    this.form.markAllAsTouched();
    if (this.form.invalid) {
      return;
    }

    const value = this.form.getRawValue();
    const input: ServiceInput = {
      name: value.name.trim(),
      description: value.description.trim() || null,
      price: Number(value.price),
      priceType: value.priceType,
      durationMinutes: Number(value.durationMinutes),
      categoryId: value.categoryId,
      groupId: value.groupId,
      isBookable: value.isBookable,
      isVisible: value.isVisible,
    };
    const editing = this.editingId();
    await this.run(async () => {
      if (editing) {
        await this.partner.updateService(this.id(), editing, input);
      } else {
        await this.partner.addService(this.id(), input);
      }

      this.editingId.set(null);
      this.resetForm(detail);
    });
  }

  protected removeService(
    detail: PartnerEstablishmentDetail,
    service: PartnerServiceItem,
  ): Promise<void> {
    if (!confirm(`Supprimer la prestation « ${service.name} » ?`)) {
      return Promise.resolve();
    }

    return this.run(() => this.partner.deleteService(detail.id, service.id));
  }

  protected move(detail: PartnerEstablishmentDetail, index: number, offset: -1 | 1): Promise<void> {
    const ids = detail.services.map((s) => s.id);
    const target = index + offset;
    [ids[index], ids[target]] = [ids[target], ids[index]];
    return this.run(() => this.partner.reorderServices(detail.id, ids));
  }

  protected async addGroup(
    event: Event,
    detail: PartnerEstablishmentDetail,
    field: HTMLInputElement,
  ): Promise<void> {
    event.preventDefault();
    const name = field.value.trim();
    if (!name) {
      return;
    }

    await this.run(async () => {
      await this.partner.addGroup(detail.id, name);
      field.value = '';
    });
  }

  protected renameGroup(
    detail: PartnerEstablishmentDetail,
    groupId: string,
    current: string,
  ): Promise<void> {
    const name = prompt('Nouveau nom de la rubrique', current)?.trim();
    return name && name !== current
      ? this.run(() => this.partner.renameGroup(detail.id, groupId, name))
      : Promise.resolve();
  }

  protected removeGroup(detail: PartnerEstablishmentDetail, groupId: string): Promise<void> {
    if (!confirm('Supprimer cette rubrique ? Ses prestations sont conservées.')) {
      return Promise.resolve();
    }

    return this.run(() => this.partner.deleteGroup(detail.id, groupId));
  }

  private resetForm(detail: PartnerEstablishmentDetail): void {
    this.form.reset({
      name: '',
      description: '',
      price: 0,
      priceType: 'Fixed',
      durationMinutes: 60,
      categoryId: detail.categoryIds[0] ?? '',
      groupId: null,
      isBookable: true,
      isVisible: true,
    });
  }

  private async run(action: () => Promise<unknown>): Promise<void> {
    this.busy.set(true);
    this.error.set('');
    try {
      await action();
      this.detail.reload();
    } catch (error) {
      this.error.set(problemMessage(error));
    } finally {
      this.busy.set(false);
    }
  }
}
