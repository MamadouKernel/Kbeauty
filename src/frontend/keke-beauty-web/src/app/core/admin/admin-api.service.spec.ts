import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import { AdminApiService } from './admin-api.service';

describe('AdminApiService', () => {
  let service: AdminApiService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(AdminApiService);
    http = TestBed.inject(HttpTestingController);
  });

  it('sends only the filters that are filled in', async () => {
    const promise = service.users({ q: 'awa', role: '', status: 'Active', page: 2 });

    const request = http.expectOne((r) => r.url === '/api/v1/admin/users');
    expect(request.request.params.get('q')).toBe('awa');
    expect(request.request.params.get('status')).toBe('Active');
    expect(request.request.params.get('page')).toBe('2');
    expect(request.request.params.has('role')).toBe(false);
    request.flush({ items: [], total: 0, page: 2, pageSize: 20 });

    expect((await promise).page).toBe(2);
  });

  it('posts the reason of a suspension', async () => {
    const promise = service.suspendUser('u1', 'Fausses réservations');

    const request = http.expectOne('/api/v1/admin/users/u1/suspend');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ reason: 'Fausses réservations' });
    request.flush(null, { status: 204, statusText: 'No Content' });
    await promise;
  });

  it('encodes template codes that contain dots and dashes', async () => {
    const promise = service.updateTemplate('billing.reminder.-3', 'Titre', 'Corps');

    const request = http.expectOne('/api/v1/admin/templates/billing.reminder.-3');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ title: 'Titre', body: 'Corps' });
    request.flush(null, { status: 204, statusText: 'No Content' });
    await promise;
  });

  it('asks the geography endpoint for the children of a parent', async () => {
    const promise = service.geo('regions', 'c1');

    const request = http.expectOne((r) => r.url === '/api/v1/admin/geo/regions');
    expect(request.request.params.get('parentId')).toBe('c1');
    request.flush([]);
    expect(await promise).toEqual([]);
  });

  it('fetches the analytical dashboard', async () => {
    const promise = service.dashboard();

    const request = http.expectOne('/api/v1/admin/dashboard');
    expect(request.request.method).toBe('GET');
    request.flush({
      activeEstablishments: 4,
      pendingReviewEstablishments: 1,
      draftEstablishments: 0,
      rejectedEstablishments: 0,
      suspendedEstablishments: 0,
      pendingAppointments: 2,
      confirmedAppointments: 3,
      completedAppointments: 8,
      noShowAppointments: 1,
      cancelledOrRejectedAppointments: 1,
      completedVolume: 50000,
      monthlyRecurringRevenue: 15000,
      activeSubscriptions: 3,
      trialingSubscriptions: 1,
      pastDueSubscriptions: 0,
      openDisputes: 0,
      totalUsers: 12,
      totalPartners: 4,
      totalStaff: 6,
      publishedReviews: 10,
      averageRating: 4.8,
      currencyCode: 'XOF',
      plans: [],
    });

    expect((await promise).completedVolume).toBe(50000);
  });

  it('creates and deletes a subscription plan with maxServices and perks', async () => {
    const createPromise = service.createPlan({
      name: 'Starter',
      monthlyPrice: 5000,
      annualPrice: 50000,
      features: {
        booking: true,
        videos: false,
        maxMedia: 5,
        featured: false,
        stats: true,
        maxVideos: 0,
        maxResources: 2,
        maxAppointmentsPerMonth: 100,
        maxServices: 8,
        perks: ['Badge Starter', 'Support WhatsApp'],
      },
      isActive: true,
      commitmentMonths: 6,
      trialDays: 14,
      promoPercent: 0,
      promoPeriods: 0,
      promoEndsAt: null,
    });

    const postReq = http.expectOne('/api/v1/admin/plans');
    expect(postReq.request.method).toBe('POST');
    expect(postReq.request.body.features.maxServices).toBe(8);
    expect(postReq.request.body.features.perks).toEqual(['Badge Starter', 'Support WhatsApp']);
    postReq.flush({ id: 'p-starter', code: 'starter', name: 'Starter' });
    await createPromise;

    const deletePromise = service.deletePlan('p-starter');
    const delReq = http.expectOne('/api/v1/admin/plans/p-starter');
    expect(delReq.request.method).toBe('DELETE');
    delReq.flush(null, { status: 204, statusText: 'No Content' });
    await deletePromise;
  });

  it('grants and updates fine-grained administrator roles', async () => {
    const grantPromise = service.grantAdmin('kyc@keke.ci', 'AdminKyc');
    const grantReq = http.expectOne('/api/v1/admin/admins');
    expect(grantReq.request.method).toBe('POST');
    expect(grantReq.request.body).toEqual({ email: 'kyc@keke.ci', role: 'AdminKyc' });
    grantReq.flush({ id: 'u-kyc', email: 'kyc@keke.ci', roles: ['AdminKyc'] });
    await grantPromise;

    const updatePromise = service.updateAdminRole('u-kyc', 'AdminAccountant');
    const updateReq = http.expectOne('/api/v1/admin/admins/u-kyc');
    expect(updateReq.request.method).toBe('PUT');
    expect(updateReq.request.body).toEqual({ role: 'AdminAccountant' });
    updateReq.flush({ id: 'u-kyc', email: 'kyc@keke.ci', roles: ['AdminAccountant'] });
    await updatePromise;
  });
});
