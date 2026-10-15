import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { PartnerService } from './partner.service';

describe('PartnerService', () => {
  let service: PartnerService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(PartnerService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('creates an establishment and resolves with its id', async () => {
    const pending = service.create({
      name: 'Salon',
      description: 'Une description suffisante.',
      customerServicePhone: '+22507000000',
      email: null,
      categoryIds: ['c1'],
      communeId: 'k1',
      addressLine: 'Rue 1',
      latitude: 5.3,
      longitude: -4,
    });

    const request = http.expectOne('/api/v1/pro/establishments');
    expect(request.request.method).toBe('POST');
    expect(request.request.body.categoryIds).toEqual(['c1']);
    request.flush({ id: 'new-id' }, { status: 201, statusText: 'Created' });

    await expect(pending).resolves.toBe('new-id');
  });

  it('replaces the opening hours with a PUT of the whole list', async () => {
    const hours = [{ dayOfWeek: 1, opensAt: '09:00', closesAt: '18:00' }];
    const pending = service.setOpeningHours('e1', hours);

    const request = http.expectOne('/api/v1/pro/establishments/e1/opening-hours');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual(hours);
    request.flush(null, { status: 204, statusText: 'No Content' });

    await expect(pending).resolves.toBeUndefined();
  });

  it('uploads a photo as multipart form data with a "file" part', async () => {
    const file = new File([new Uint8Array([1, 2, 3])], 'devanture.png', { type: 'image/png' });
    const pending = service.uploadPhoto('e1', file);

    const request = http.expectOne('/api/v1/pro/establishments/e1/media');
    expect(request.request.method).toBe('POST');
    const body = request.request.body as FormData;
    expect(body).toBeInstanceOf(FormData);
    expect((body.get('file') as File).name).toBe('devanture.png');
    request.flush({
      id: 'm1',
      url: '/api/v1/files/uploads/x.webp',
      thumbUrl: null,
      isCover: true,
      sortOrder: 0,
    });

    await expect(pending).resolves.toMatchObject({ id: 'm1', isCover: true });
  });

  it('uploads a video with its poster and duration to the videos endpoint', async () => {
    const file = new File([new Uint8Array([1, 2, 3])], 'salon.mp4', { type: 'video/mp4' });
    const poster = new Blob(['x'], { type: 'image/jpeg' });
    const pending = service.uploadVideo('e1', file, poster, 41.6);

    const request = http.expectOne('/api/v1/pro/establishments/e1/videos');
    const body = request.request.body as FormData;
    expect((body.get('file') as File).name).toBe('salon.mp4');
    expect((body.get('poster') as File).name).toBe('poster.jpg');
    expect(body.get('durationSeconds')).toBe('42');
    request.flush({ id: 'v1', kind: 'Video' });

    await expect(pending).resolves.toMatchObject({ id: 'v1' });
  });

  it('sends the identity documents to the side-specific endpoint', async () => {
    const file = new File(['x'], 'recto.png', { type: 'image/png' });
    const pending = service.uploadDocument('front', file);

    const request = http.expectOne('/api/v1/pro/kyc/documents/front');
    expect(request.request.method).toBe('PUT');
    request.flush({ status: 'Draft', isEditable: true, hasFront: true });

    await expect(pending).resolves.toMatchObject({ hasFront: true });
  });

  it('treats a missing identity file as null but still surfaces other errors', async () => {
    const missing = service.getKyc();
    http
      .expectOne('/api/v1/pro/kyc')
      .flush({ code: 'kyc.not_found' }, { status: 404, statusText: 'Not Found' });
    await expect(missing).resolves.toBeNull();

    const broken = service.getKyc();
    http.expectOne('/api/v1/pro/kyc').flush({}, { status: 500, statusText: 'Server Error' });
    await expect(broken).rejects.toMatchObject({ status: 500 });
  });

  it('propagates API errors untouched so callers can show the message', async () => {
    const pending = service.submit('e1');
    http
      .expectOne('/api/v1/pro/establishments/e1/submit')
      .flush(
        { title: 'Le dossier est incomplet', code: 'establishment.incomplete' },
        { status: 409, statusText: 'Conflict' },
      );

    await expect(pending).rejects.toMatchObject({ status: 409 });
  });

  it('reorders services and photos with the ordered ids', async () => {
    const services = service.reorderServices('e1', ['b', 'a']);
    const first = http.expectOne('/api/v1/pro/establishments/e1/services/order');
    expect(first.request.body).toEqual({ ids: ['b', 'a'] });
    first.flush(null, { status: 204, statusText: 'No Content' });
    await services;

    const photos = service.reorderPhotos('e1', ['y', 'x']);
    const second = http.expectOne('/api/v1/pro/establishments/e1/media/order');
    expect(second.request.body).toEqual({ ids: ['y', 'x'] });
    second.flush(null, { status: 204, statusText: 'No Content' });
    await photos;
  });
});
