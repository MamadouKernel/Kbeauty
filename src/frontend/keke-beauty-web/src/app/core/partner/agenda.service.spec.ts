import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { AgendaService } from './agenda.service';

describe('AgendaService counter and pass', () => {
  let service: AgendaService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(AgendaService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('posts a walk-in to the counter of the establishment', async () => {
    const input = {
      serviceIds: ['s1'],
      resourceId: null,
      startsAt: null,
      clientName: 'Mariam Traoré',
      phone: null,
      paidMethod: 'Cash',
      paidReference: null,
    };
    const pending = service.counter('e1', input);

    const request = http.expectOne('/api/v1/pro/establishments/e1/counter');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual(input);
    request.flush({ id: 'a1', clientName: 'Mariam Traoré' });

    expect((await pending).clientName).toBe('Mariam Traoré');
  });

  it('sends the scanned token to verify a pass', async () => {
    const pending = service.verifyPass('e1', 'token-123');

    const request = http.expectOne('/api/v1/pro/establishments/e1/pass/verify');
    expect(request.request.body).toEqual({ token: 'token-123' });
    request.flush({ appointmentId: 'a1' });

    expect((await pending).appointmentId).toBe('a1');
  });
});
