import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import type { components } from '../api/api.generated';

type Schemas = components['schemas'];

export type Review = Schemas['ReviewDto'];
export type ReviewsPage = Schemas['ReviewsPage'];
export type MyReview = Schemas['MyReviewDto'];
export type ReviewPhoto = Schemas['ReviewPhotoDto'];
export type ReviewPhotoKind = 'Before' | 'After' | 'Other';

/** Reviews: a client writes one after a completed visit, the establishment answers it. */
@Injectable({ providedIn: 'root' })
export class ReviewService {
  private readonly http = inject(HttpClient);

  /** The reviews the signed-in client already wrote (to tell which visits still wait for one). */
  mine(): Promise<MyReview[]> {
    return firstValueFrom(this.http.get<MyReview[]>('/api/v1/me/reviews'));
  }

  create(appointmentId: string, rating: number, comment: string): Promise<Review> {
    return firstValueFrom(
      this.http.post<Review>(`/api/v1/appointments/${appointmentId}/review`, {
        rating,
        comment: comment.trim() || null,
      }),
    );
  }

  /** Attaches a before / after photo to one of my reviews (at most four per review). */
  uploadPhoto(reviewId: string, file: File, kind: ReviewPhotoKind): Promise<ReviewPhoto> {
    const form = new FormData();
    form.append('file', file);
    form.append('kind', kind);
    return firstValueFrom(this.http.post<ReviewPhoto>(`/api/v1/reviews/${reviewId}/photos`, form));
  }

  deletePhoto(reviewId: string, photoId: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`/api/v1/reviews/${reviewId}/photos/${photoId}`));
  }

  /** Every review of one of the owner's establishments, hidden ones included. */
  ofEstablishment(establishmentId: string, page: number): Promise<ReviewsPage> {
    return firstValueFrom(
      this.http.get<ReviewsPage>(
        `/api/v1/pro/establishments/${establishmentId}/reviews?page=${page}&pageSize=10`,
      ),
    );
  }

  reply(reviewId: string, text: string): Promise<Review> {
    return firstValueFrom(
      this.http.post<Review>(`/api/v1/pro/reviews/${reviewId}/reply`, { text }),
    );
  }
}
