import { DEFAULT_MAP_CONFIG, mapConfigFrom } from './map-config';

describe('mapConfigFrom', () => {
  it('keeps OpenStreetMap when nothing is configured', () => {
    expect(mapConfigFrom('', '')).toBe(DEFAULT_MAP_CONFIG);
    expect(mapConfigFrom(undefined, undefined)).toBe(DEFAULT_MAP_CONFIG);
  });

  it('uses a configured https tile server and escapes its attribution', () => {
    const config = mapConfigFrom(
      'https://api.maptiler.com/maps/streets-v2/{z}/{x}/{y}.png?key=abc',
      '© MapTiler <script>alert(1)</script>',
    );

    expect(config.tilesUrl).toBe(
      'https://api.maptiler.com/maps/streets-v2/{z}/{x}/{y}.png?key=abc',
    );
    expect(config.attributionHtml).toBe('© MapTiler &lt;script&gt;alert(1)&lt;/script&gt;');
  });

  it('falls back to OpenStreetMap attribution when only the URL is configured', () => {
    const config = mapConfigFrom('https://{s}.tiles.example/{z}/{x}/{y}.png', '');

    expect(config.attributionHtml).toContain('openstreetmap.org/copyright');
  });

  it('accepts the site own tile proxy and nothing else on the same origin', () => {
    expect(mapConfigFrom('/tiles/{z}/{x}/{y}.png', '').tilesUrl).toBe('/tiles/{z}/{x}/{y}.png');
    expect(mapConfigFrom('/other/{z}/{x}/{y}.png', '')).toBe(DEFAULT_MAP_CONFIG);
    expect(mapConfigFrom('//evil.example/tiles/{z}/{x}/{y}.png', '')).toBe(DEFAULT_MAP_CONFIG);
  });

  it.each([
    'http://tiles.example/{z}/{x}/{y}.png',
    'javascript:alert(1)',
    'https://tiles.example/no-placeholders.png',
    'https://tiles.example/{z}/{x}.png',
    42,
  ])('rejects an unsafe or malformed tile URL (%s)', (url) => {
    expect(mapConfigFrom(url, 'x')).toBe(DEFAULT_MAP_CONFIG);
  });
});
