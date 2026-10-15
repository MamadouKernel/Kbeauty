import { TestBed } from '@angular/core/testing';
import {
  DEFAULT_YANGO_URL_TEMPLATE,
  DIRECTIONS_CONFIG,
  DirectionsLinkService,
} from './directions.service';

function service(yangoUrlTemplate = ''): DirectionsLinkService {
  TestBed.configureTestingModule({
    providers: [{ provide: DIRECTIONS_CONFIG, useValue: { yangoUrlTemplate } }],
  });
  return TestBed.inject(DirectionsLinkService);
}

describe('DirectionsLinkService', () => {
  it('always offers Google Maps with the destination pre-filled', () => {
    const [google] = service().options(5.3235, -4.0197, false);

    expect(google).toEqual({
      id: 'google',
      label: 'Google Maps',
      url: 'https://www.google.com/maps/dir/?api=1&destination=5.3235,-4.0197',
    });
  });

  it('offers Apple Maps on Apple platforms only', () => {
    expect(
      service()
        .options(5.3, -4.0, false)
        .map((o) => o.id),
    ).toEqual(['google']);
  });

  it('adds Apple Plans when running on an Apple device', () => {
    const options = service().options(5.3, -4.0, true);

    expect(options.map((o) => o.id)).toEqual(['google', 'apple']);
    expect(options[1].url).toBe('https://maps.apple.com/?daddr=5.3,-4');
  });

  it('hides Yango until a template is configured', () => {
    expect(
      service('')
        .options(5.3, -4.0, false)
        .some((o) => o.id === 'yango'),
    ).toBe(false);
  });

  it('fills the configured Yango template', () => {
    const yango = service('https://yango.example/route?to={lat},{lng}')
      .options(5.3235, -4.0197, false)
      .find((o) => o.id === 'yango');

    expect(yango?.url).toBe('https://yango.example/route?to=5.3235,-4.0197');
  });

  it.each(['javascript:alert({lat})', 'data:text/html,{lng}', 'not a url {lat}'])(
    'ignores an unsafe or malformed template (%s)',
    (template) => {
      expect(
        service(template)
          .options(5.3, -4.0, false)
          .some((o) => o.id === 'yango'),
      ).toBe(false);
    },
  );

  it('opens the Yango app through the official go.link with the destination, and falls back to the web order page', () => {
    const yango = service(DEFAULT_YANGO_URL_TEMPLATE)
      .options(5.3235, -4.0197, false)
      .find((o) => o.id === 'yango');

    const url = new URL(yango!.url);
    expect(url.origin + url.pathname).toBe('https://yango.go.link/route');
    expect(url.searchParams.get('end-lat')).toBe('5.3235');
    expect(url.searchParams.get('end-lon')).toBe('-4.0197');
    expect(url.searchParams.get('ref')).toBe('kekebeauty');
    // The fallback lists longitude first, as Yango's documentation does.
    expect(new URL(url.searchParams.get('adj_fallback')!).searchParams.get('gto')).toBe(
      '-4.0197,5.3235',
    );
  });
});
