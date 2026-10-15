import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, firstValueFrom } from 'rxjs';
import type { components } from '../api/api.generated';

type Schemas = components['schemas'];

export type PartnerEstablishmentSummary = Schemas['PartnerEstablishmentSummary'];
export type PartnerEstablishmentDetail = Schemas['PartnerEstablishmentDetail'];
export type PartnerServiceItem = Schemas['PartnerServiceItem'];
export type PartnerGroupItem = Schemas['PartnerGroupItem'];
export type PartnerMediaItem = Schemas['PartnerMediaItem'];
export type EstablishmentInput = Schemas['EstablishmentInput'];
export type OpeningHourInput = Schemas['OpeningHourInput'];
export type ServiceInput = Schemas['ServiceInput'];
export type KycInput = Schemas['KycInput'];
export type KycView = Schemas['KycView'];
export type MissingItem = Schemas['MissingItem'];
export type ReviewInfo = Schemas['ReviewInfo'];

export type DocumentSide = 'front' | 'back' | 'selfie';

export const DOCUMENT_TYPES: readonly { readonly value: string; readonly label: string }[] = [
  { value: 'NationalId', label: "Carte nationale d'identité" },
  { value: 'Passport', label: 'Passeport' },
  { value: 'DrivingLicense', label: 'Permis de conduire' },
  { value: 'ConsularCard', label: 'Carte consulaire' },
];

export const STATUS_LABELS: Readonly<Record<string, string>> = {
  Draft: 'Brouillon',
  PendingReview: 'En cours de validation',
  Active: 'Publié',
  Rejected: 'Refusé',
  Suspended: 'Suspendu',
};

/** Typed access to the partner area of the API (`/api/v1/pro/**`). Errors are left to the callers. */
@Injectable({ providedIn: 'root' })
export class PartnerService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/pro';

  list(): Promise<PartnerEstablishmentSummary[]> {
    return firstValueFrom(
      this.http.get<PartnerEstablishmentSummary[]>(`${this.base}/establishments`),
    );
  }

  get(id: string): Promise<PartnerEstablishmentDetail> {
    return firstValueFrom(
      this.http.get<PartnerEstablishmentDetail>(`${this.base}/establishments/${id}`),
    );
  }

  async create(input: EstablishmentInput): Promise<string> {
    const created = await firstValueFrom(
      this.http.post<{ id: string }>(`${this.base}/establishments`, input),
    );
    return created.id;
  }

  update(id: string, input: EstablishmentInput): Promise<void> {
    return this.send(this.http.put<void>(`${this.base}/establishments/${id}`, input));
  }

  setOpeningHours(id: string, hours: readonly OpeningHourInput[]): Promise<void> {
    return this.send(this.http.put<void>(`${this.base}/establishments/${id}/opening-hours`, hours));
  }

  submit(id: string): Promise<ReviewInfo> {
    return firstValueFrom(
      this.http.post<ReviewInfo>(`${this.base}/establishments/${id}/submit`, {}),
    );
  }

  // ───────── Services and groups ─────────

  async addGroup(id: string, name: string): Promise<string> {
    const created = await firstValueFrom(
      this.http.post<{ id: string }>(`${this.base}/establishments/${id}/service-groups`, { name }),
    );
    return created.id;
  }

  renameGroup(id: string, groupId: string, name: string): Promise<void> {
    return this.send(
      this.http.put<void>(`${this.base}/establishments/${id}/service-groups/${groupId}`, { name }),
    );
  }

  deleteGroup(id: string, groupId: string): Promise<void> {
    return this.send(
      this.http.delete<void>(`${this.base}/establishments/${id}/service-groups/${groupId}`),
    );
  }

  async addService(id: string, input: ServiceInput): Promise<string> {
    const created = await firstValueFrom(
      this.http.post<{ id: string }>(`${this.base}/establishments/${id}/services`, input),
    );
    return created.id;
  }

  updateService(id: string, serviceId: string, input: ServiceInput): Promise<void> {
    return this.send(
      this.http.put<void>(`${this.base}/establishments/${id}/services/${serviceId}`, input),
    );
  }

  deleteService(id: string, serviceId: string): Promise<void> {
    return this.send(
      this.http.delete<void>(`${this.base}/establishments/${id}/services/${serviceId}`),
    );
  }

  reorderServices(id: string, ids: readonly string[]): Promise<void> {
    return this.send(
      this.http.put<void>(`${this.base}/establishments/${id}/services/order`, { ids }),
    );
  }

  // ───────── Gallery ─────────

  uploadPhoto(id: string, file: File): Promise<PartnerMediaItem> {
    return firstValueFrom(
      this.http.post<PartnerMediaItem>(`${this.base}/establishments/${id}/media`, formWith(file)),
    );
  }

  /** Short MP4/WebM clip, with an optional poster image and its duration (read in the browser). */
  uploadVideo(
    id: string,
    file: File,
    poster?: Blob | null,
    durationSeconds?: number | null,
  ): Promise<PartnerMediaItem> {
    const form = formWith(file);
    if (poster) {
      form.append('poster', poster, 'poster.jpg');
    }
    if (durationSeconds) {
      form.append('durationSeconds', String(Math.round(durationSeconds)));
    }
    return firstValueFrom(
      this.http.post<PartnerMediaItem>(`${this.base}/establishments/${id}/videos`, form),
    );
  }

  deletePhoto(id: string, mediaId: string): Promise<void> {
    return this.send(this.http.delete<void>(`${this.base}/establishments/${id}/media/${mediaId}`));
  }

  setCover(id: string, mediaId: string): Promise<void> {
    return this.send(
      this.http.put<void>(`${this.base}/establishments/${id}/media/${mediaId}/cover`, {}),
    );
  }

  reorderPhotos(id: string, ids: readonly string[]): Promise<void> {
    return this.send(this.http.put<void>(`${this.base}/establishments/${id}/media/order`, { ids }));
  }

  // ───────── Identity (KYC) ─────────

  /** `null` until the owner has saved identity data. */
  async getKyc(): Promise<KycView | null> {
    try {
      return await firstValueFrom(this.http.get<KycView>(`${this.base}/kyc`));
    } catch (error) {
      if (error instanceof HttpErrorResponse && error.status === 404) {
        return null;
      }

      throw error;
    }
  }

  saveKyc(input: KycInput): Promise<KycView> {
    return firstValueFrom(this.http.put<KycView>(`${this.base}/kyc`, input));
  }

  uploadDocument(side: DocumentSide, file: File): Promise<KycView> {
    return firstValueFrom(
      this.http.put<KycView>(`${this.base}/kyc/documents/${side}`, formWith(file)),
    );
  }

  private send(request: Observable<unknown>): Promise<void> {
    return firstValueFrom(request).then(() => undefined);
  }
}

function formWith(file: File): FormData {
  const form = new FormData();
  form.append('file', file, file.name);
  return form;
}
