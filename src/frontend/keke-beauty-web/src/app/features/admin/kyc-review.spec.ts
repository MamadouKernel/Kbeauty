import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { providePrimeNG } from 'primeng/config';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { ReviewDetail } from '../../core/admin/review.service';
import { KycReview } from './kyc-review';

const REVIEW: ReviewDetail = {
  reviewId: 'r1',
  status: 'PendingReview',
  comment: null,
  submittedAt: '2026-10-11T10:00:00Z',
  establishmentId: 'e1',
  establishmentName: 'Salon Test',
  description: 'Un salon de test.',
  customerServicePhone: '+22507000000',
  email: null,
  addressLine: 'Rue 1',
  communeName: 'Cocody',
  cityName: 'Abidjan',
  latitude: 5.36,
  longitude: -3.99,
  categories: ['Salon de coiffure'],
  photoUrls: ['/api/v1/files/uploads/e1/a.webp'],
  ownerName: 'Awa Koné',
  ownerEmail: 'awa@example.com',
  identity: {
    status: 'PendingReview',
    holderName: 'Awa Koné',
    documentType: 'NationalId',
    documentNumber: 'CI001234567',
    documentExpiry: '2032-05-01',
    hasFront: true,
    hasBack: true,
    hasSelfie: false,
  },
  checks: [
    { code: 'name_match', level: 'ok', message: 'Le nom saisi correspond au nom du compte.' },
    {
      code: 'duplicate_document',
      level: 'error',
      message: 'Ce numéro de pièce est déjà utilisé par un autre compte.',
    },
  ],
};

describe('KycReview', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        providePrimeNG(),
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => vi.unstubAllGlobals());

  async function open() {
    const fixture = TestBed.createComponent(KycReview);
    fixture.componentRef.setInput('id', 'r1');
    fixture.detectChanges();
    (await vi.waitFor(() => http.expectOne('/api/v1/admin/kyc/r1'))).flush(REVIEW);
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(fixture.nativeElement.querySelector('h1')?.textContent).toContain('Salon Test');
    });
    return fixture;
  }

  const root = (fixture: { nativeElement: unknown }) => fixture.nativeElement as HTMLElement;

  it('shows the establishment, the identity and the indicative checks', async () => {
    const fixture = await open();

    const text = root(fixture).textContent ?? '';
    expect(text).toContain('CI001234567');
    expect(text).toContain("Carte nationale d'identité");
    expect(text).toContain('Le nom saisi correspond au nom du compte.');
    expect(root(fixture).querySelector('.check[data-level="error"]')?.textContent).toContain(
      'déjà utilisé',
    );
    expect(root(fixture).querySelector('img[alt="Photo de l\'établissement"]')).toBeTruthy();
    // The absent selfie cannot be opened.
    const selfie = [...root(fixture).querySelectorAll('button')].find((b) =>
      b.textContent?.includes('Selfie'),
    );
    expect(selfie?.disabled).toBe(true);
  });

  it('opens a document through an authorised blob request and revokes it when closed', async () => {
    const create = vi.fn(() => 'blob:front');
    const revoke = vi.fn();
    vi.stubGlobal('URL', Object.assign(URL, { createObjectURL: create, revokeObjectURL: revoke }));
    const fixture = await open();

    [...root(fixture).querySelectorAll('button')]
      .find((b) => b.textContent?.includes('Recto'))
      ?.click();
    const request = await vi.waitFor(() => http.expectOne('/api/v1/admin/kyc/r1/documents/front'));
    expect(request.request.responseType).toBe('blob');
    request.flush(new Blob(['x'], { type: 'image/webp' }));

    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(root(fixture).querySelector('.viewer img')?.getAttribute('src')).toBe('blob:front');
    });

    [...root(fixture).querySelectorAll('button')]
      .find((b) => b.textContent?.includes('Fermer le document'))
      ?.click();
    expect(revoke).toHaveBeenCalledWith('blob:front');
  });

  it('requires a comment to request corrections, then sends the decision', async () => {
    const fixture = await open();
    // Radio order: Approve, RequestChanges, Reject (the value is consumed by the forms directive, not the DOM).
    const radio = root(fixture).querySelectorAll<HTMLInputElement>('input[type="radio"]')[1];
    radio.click();
    fixture.detectChanges();

    const form = root(fixture).querySelector('form')!;
    form.dispatchEvent(new Event('submit'));
    fixture.detectChanges();
    expect(root(fixture).querySelector('[role="alert"]')?.textContent).toContain(
      'commentaire est obligatoire',
    );
    http.expectNone('/api/v1/admin/kyc/r1/decision');

    const textarea = root(fixture).querySelector<HTMLTextAreaElement>('#decision-comment')!;
    textarea.value = 'Photo floue.';
    textarea.dispatchEvent(new Event('input'));
    form.dispatchEvent(new Event('submit'));

    const decision = await vi.waitFor(() => http.expectOne('/api/v1/admin/kyc/r1/decision'));
    expect(decision.request.body).toEqual({ decision: 'RequestChanges', comment: 'Photo floue.' });
    decision.flush({
      status: 'ChangesRequested',
      comment: 'Photo floue.',
      submittedAt: REVIEW.submittedAt,
      reviewedAt: REVIEW.submittedAt,
    });

    (await vi.waitFor(() => http.expectOne('/api/v1/admin/kyc/r1'))).flush({
      ...REVIEW,
      status: 'ChangesRequested',
    });
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(root(fixture).querySelector('[role="status"]')?.textContent).toContain(
        'Décision enregistrée',
      );
    });
  });

  it('does not offer a decision for a file that was already processed', async () => {
    const fixture = TestBed.createComponent(KycReview);
    fixture.componentRef.setInput('id', 'r1');
    fixture.detectChanges();
    (await vi.waitFor(() => http.expectOne('/api/v1/admin/kyc/r1'))).flush({
      ...REVIEW,
      status: 'Approved',
    });

    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(root(fixture).textContent).toContain('Dossier déjà traité');
    });
    expect(root(fixture).querySelector('form')).toBeNull();
  });
});
