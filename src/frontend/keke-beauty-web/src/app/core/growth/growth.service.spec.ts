import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { GrowthService, progressToReward, rewardsFor } from './growth.service';

describe('GrowthService', () => {
  let service: GrowthService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(GrowthService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('joins the waiting list of a day', async () => {
    const pending = service.joinWaitlist('e1', '2026-11-03', 's1');

    const request = http.expectOne('/api/v1/waitlist');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({
      establishmentId: 'e1',
      date: '2026-11-03',
      serviceId: 's1',
    });
    request.flush({ id: 'w1', status: 'Waiting' });

    expect((await pending).status).toBe('Waiting');
  });

  it('leaves the waiting list', async () => {
    const pending = service.leaveWaitlist('w1');
    const request = http.expectOne('/api/v1/me/waitlist/w1');
    expect(request.request.method).toBe('DELETE');
    request.flush(null, { status: 204, statusText: 'No Content' });
    await pending;
  });

  it('reads no public loyalty card as null', async () => {
    const pending = service.publicLoyalty('salon');
    http
      .expectOne('/api/v1/establishments/salon/loyalty')
      .flush(null, { status: 404, statusText: 'Not Found' });
    expect(await pending).toBeNull();
  });

  it('saves the loyalty card of an establishment', async () => {
    const program = {
      isActive: true,
      pointsPer1000: 10,
      rewardThreshold: 500,
      rewardDescription: 'Un soin offert',
    };
    const pending = service.saveLoyaltyProgram('e1', program);

    const request = http.expectOne('/api/v1/pro/establishments/e1/loyalty');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual(program);
    request.flush({ ...program, activeSince: '2026-10-18T00:00:00Z' });

    expect((await pending).activeSince).not.toBeNull();
  });

  it('grants a reward to a client', async () => {
    const pending = service.redeemLoyalty('e1', 'u1');
    const request = http.expectOne('/api/v1/pro/establishments/e1/loyalty/redeem');
    expect(request.request.body).toEqual({ clientUserId: 'u1' });
    request.flush({ clientUserId: 'u1', points: 20 });
    expect((await pending).points).toBe(20);
  });

  it('looks a gift card up by its code and debits it', async () => {
    const found = service.lookupGiftCard('e1', 'kb 7hq4 m2xp');
    http
      .expectOne('/api/v1/pro/establishments/e1/gift-cards/lookup?code=kb%207hq4%20m2xp')
      .flush({ id: 'g1', balance: 20000 });
    expect((await found).id).toBe('g1');

    const debited = service.redeemGiftCard('g1', 5000);
    const request = http.expectOne('/api/v1/pro/gift-cards/g1/redeem');
    expect(request.request.body).toEqual({ amount: 5000, appointmentId: null });
    request.flush({ id: 'g1', balance: 15000 });
    expect((await debited).balance).toBe(15000);
  });

  it('adds a received gift card to the account', async () => {
    const pending = service.claimGiftCard('KB-7HQ4-M2XP');
    const request = http.expectOne('/api/v1/me/gift-cards');
    expect(request.request.body).toEqual({ code: 'KB-7HQ4-M2XP' });
    request.flush({ id: 'g1' });
    expect((await pending).id).toBe('g1');
  });

  it('sends a group request and the establishment answer', async () => {
    const asked = service.requestGroup({
      establishmentId: 'e1',
      eventType: 'Wedding',
      desiredStartsAt: '2026-12-12T10:00:00.000Z',
      groupSize: 8,
      services: 'Coiffure et maquillage',
      message: null,
      phone: '0700000000',
    });
    const create = http.expectOne('/api/v1/group-bookings');
    expect(create.request.body.groupSize).toBe(8);
    create.flush({ id: 'r1', status: 'Pending' });
    expect((await asked).status).toBe('Pending');

    const answered = service.answerGroupBooking('r1', true, 240000, 'Avec plaisir');
    const request = http.expectOne('/api/v1/pro/group-bookings/r1/answer');
    expect(request.request.body).toEqual({
      accept: true,
      quotedPrice: 240000,
      message: 'Avec plaisir',
    });
    request.flush({ id: 'r1', status: 'Accepted' });
    expect((await answered).status).toBe('Accepted');
  });

  it('asks for recommendations with or without a position', async () => {
    const anywhere = service.recommendations(null, 4);
    http.expectOne('/api/v1/recommendations?count=4').flush([]);
    expect(await anywhere).toEqual([]);

    const near = service.recommendations({ lat: 5.36, lng: -3.99 });
    http.expectOne('/api/v1/recommendations?count=8&lat=5.36&lng=-3.99').flush([]);
    expect(await near).toEqual([]);
  });
});

describe('loyalty helpers', () => {
  it('counts the rewards a balance gives', () => {
    expect(rewardsFor(0, 100)).toBe(0);
    expect(rewardsFor(99, 100)).toBe(0);
    expect(rewardsFor(250, 100)).toBe(2);
    expect(rewardsFor(50, 0)).toBe(0);
  });

  it('shows the way to the next reward', () => {
    expect(progressToReward(0, 100)).toBe(0);
    expect(progressToReward(30, 100)).toBe(30);
    expect(progressToReward(130, 100)).toBe(30);
  });
});
