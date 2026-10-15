import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import type { components } from '../api/api.generated';

type Schemas = components['schemas'];

export type QueuePage = Schemas['QueuePage'];
export type QueueItem = Schemas['QueueItem'];
export type ReviewDetail = Schemas['ReviewDetail'];
export type KycCheck = Schemas['KycCheck'];
export type ReviewInfo = Schemas['ReviewInfo'];

export type Decision = 'Approve' | 'Reject' | 'RequestChanges';
export type DocumentSide = 'front' | 'back' | 'selfie';

/** Admin review queue (`/api/v1/admin/kyc`). Identity documents are fetched as blobs: never as public URLs. */
@Injectable({ providedIn: 'root' })
export class ReviewService {
  private readonly http = inject(HttpClient);
  private readonly base = '/api/v1/admin/kyc';

  queue(status: string, page: number, pageSize = 20): Promise<QueuePage> {
    return firstValueFrom(
      this.http.get<QueuePage>(`${this.base}/queue`, {
        params: { status, page, pageSize },
      }),
    );
  }

  get(reviewId: string): Promise<ReviewDetail> {
    return firstValueFrom(this.http.get<ReviewDetail>(`${this.base}/${reviewId}`));
  }

  decide(reviewId: string, decision: Decision, comment: string): Promise<ReviewInfo> {
    return firstValueFrom(
      this.http.post<ReviewInfo>(`${this.base}/${reviewId}/decision`, {
        decision,
        comment: comment.trim() || null,
      }),
    );
  }

  /** Every call is recorded in the audit log server-side. The caller revokes the object URL when done. */
  async openDocument(reviewId: string, side: DocumentSide): Promise<string> {
    const blob = await firstValueFrom(
      this.http.get(`${this.base}/${reviewId}/documents/${side}`, { responseType: 'blob' }),
    );
    return URL.createObjectURL(blob);
  }
}
