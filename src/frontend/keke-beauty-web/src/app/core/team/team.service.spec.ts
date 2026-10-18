import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { TeamService, elapsed } from './team.service';

describe('TeamService', () => {
  let service: TeamService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(TeamService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('asks for the planning of a given day', async () => {
    const pending = service.planning('2026-10-18');

    const request = http.expectOne('/api/v1/team/planning?date=2026-10-18');
    expect(request.request.method).toBe('GET');
    request.flush({ date: '2026-10-18', timezone: 'Africa/Abidjan', appointments: [] });

    expect((await pending).appointments).toEqual([]);
  });

  it('asks for today when no day is given', async () => {
    const pending = service.planning();

    http.expectOne('/api/v1/team/planning').flush({ date: 'x', timezone: 'UTC', appointments: [] });

    expect((await pending).timezone).toBe('UTC');
  });

  it('starts and finishes an appointment', async () => {
    const started = service.start('a1');
    const start = http.expectOne('/api/v1/team/appointments/a1/start');
    expect(start.request.method).toBe('POST');
    start.flush({ id: 'a1', startedAt: '2026-10-18T10:00:00Z' });
    expect((await started).startedAt).toBe('2026-10-18T10:00:00Z');

    const finished = service.finish('a1');
    http.expectOne('/api/v1/team/appointments/a1/finish').flush({ id: 'a1', finishedAt: 'x' });
    expect((await finished).id).toBe('a1');
  });

  it('saves the technical note of a client of one establishment', async () => {
    const input = { allergies: 'Latex', notes: null };
    const pending = service.saveNote('e1', 'c1', input);

    const request = http.expectOne('/api/v1/team/establishments/e1/clients/c1/note');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual(input);
    request.flush({ ...input, updatedAt: '2026-10-18T10:00:00Z' });

    expect((await pending).allergies).toBe('Latex');
  });

  it('sends the period of the commissions only when given', async () => {
    const pending = service.commissions('2026-10-01', '2026-10-18');

    http.expectOne('/api/v1/team/commissions?from=2026-10-01&to=2026-10-18').flush([]);

    expect(await pending).toEqual([]);
  });

  it('links a resource to a collaborator by e-mail', async () => {
    const input = { email: 'awa@example.com', commissionPercent: 30 };
    const pending = service.assign('e1', 'r1', input);

    const request = http.expectOne('/api/v1/pro/establishments/e1/team/r1');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual(input);
    request.flush({ resourceId: 'r1', userId: 'u1', commissionPercent: 30 });

    expect((await pending).userId).toBe('u1');
  });

  it('unlinks a collaborator', async () => {
    const pending = service.unassign('e1', 'r1');

    const request = http.expectOne('/api/v1/pro/establishments/e1/team/r1');
    expect(request.request.method).toBe('DELETE');
    request.flush(null);

    await pending;
  });
});

describe('elapsed', () => {
  const start = '2026-10-18T10:00:00Z';

  it('formats a running timer as hours, minutes and seconds', () => {
    expect(elapsed(start, Date.parse('2026-10-18T10:00:00Z'))).toBe('00:00:00');
    expect(elapsed(start, Date.parse('2026-10-18T10:12:05Z'))).toBe('00:12:05');
    expect(elapsed(start, Date.parse('2026-10-18T11:30:09Z'))).toBe('01:30:09');
  });

  it('never goes negative when the clock is slightly behind', () => {
    expect(elapsed(start, Date.parse('2026-10-18T09:59:58Z'))).toBe('00:00:00');
  });
});

describe('TeamService payouts', () => {
  let service: TeamService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(TeamService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('saves the collaborator payout account', async () => {
    const input = {
      operator: 'wave-cote-divoire',
      account: '07 00 00 99 88',
      accountName: 'Awa Koné',
    };
    const pending = service.savePayoutAccount(input);

    const request = http.expectOne('/api/v1/team/payout-account');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual(input);
    request.flush({
      configured: true,
      operator: input.operator,
      accountMasked: '•••• 9988',
      accountName: 'Awa Koné',
    });

    expect((await pending).configured).toBe(true);
  });

  it('reads what the collaborator is owed and has received', async () => {
    const pending = service.myPayouts();

    http
      .expectOne('/api/v1/team/payouts')
      .flush({ account: { configured: false }, owed: [], history: [] });

    expect((await pending).history).toEqual([]);
  });

  it('lets the owner see and pay what a collaborator is owed', async () => {
    const view = service.ownerPayouts('e1');
    http
      .expectOne('/api/v1/pro/establishments/e1/team-payouts')
      .flush({ owed: [], history: [], availableBalance: 7000 });
    expect((await view).availableBalance).toBe(7000);

    const paid = service.payCollaborator('e1', 'u1');
    const request = http.expectOne('/api/v1/pro/establishments/e1/team-payouts/u1');
    expect(request.request.method).toBe('POST');
    request.flush({ id: 'p1', amount: 3500, status: 'Succeeded' });
    expect((await paid).amount).toBe(3500);
  });
});
