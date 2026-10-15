import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { HealthService } from './health.service';

describe('HealthService', () => {
  let service: HealthService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(HealthService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('starts in the checking state', () => {
    expect(service.status()).toBe('checking');
  });

  it('is online when /health/ready answers 200', async () => {
    const pending = service.check();
    http.expectOne('/health/ready').flush('Healthy');
    await pending;

    expect(service.status()).toBe('online');
  });

  it('is offline when /health/ready answers 503', async () => {
    const pending = service.check();
    http
      .expectOne('/health/ready')
      .flush('Unhealthy', { status: 503, statusText: 'Service Unavailable' });
    await pending;

    expect(service.status()).toBe('offline');
  });

  it('goes back to checking while a new probe is in flight', async () => {
    const first = service.check();
    http.expectOne('/health/ready').flush('Healthy');
    await first;

    const second = service.check();
    expect(service.status()).toBe('checking');
    http.expectOne('/health/ready').flush('Healthy');
    await second;
  });
});
