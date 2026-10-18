import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { ReviewService } from './review.service';

describe('ReviewService photos', () => {
  let service: ReviewService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(ReviewService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('sends a photo with its kind as a multipart form', async () => {
    const file = new File([new Uint8Array([1, 2, 3])], 'avant.png', { type: 'image/png' });
    const pending = service.uploadPhoto('r1', file, 'Before');

    const request = http.expectOne('/api/v1/reviews/r1/photos');
    expect(request.request.method).toBe('POST');
    const form = request.request.body as FormData;
    expect(form.get('kind')).toBe('Before');
    expect((form.get('file') as File).name).toBe('avant.png');
    request.flush({
      id: 'p1',
      kind: 'Before',
      url: '/api/v1/files/uploads/reviews/r1/p1.webp',
      thumbUrl: null,
    });

    expect((await pending).kind).toBe('Before');
  });

  it('removes a photo of a review', async () => {
    const pending = service.deletePhoto('r1', 'p1');

    const request = http.expectOne('/api/v1/reviews/r1/photos/p1');
    expect(request.request.method).toBe('DELETE');
    request.flush(null);

    await pending;
  });

  it('keeps the comment optional when writing a review', async () => {
    const pending = service.create('a1', 5, '   ');

    const request = http.expectOne('/api/v1/appointments/a1/review');
    expect(request.request.body).toEqual({ rating: 5, comment: null });
    request.flush({ id: 'r1', rating: 5 });

    expect((await pending).id).toBe('r1');
  });
});
