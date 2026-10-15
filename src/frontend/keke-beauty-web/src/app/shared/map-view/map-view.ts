import {
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  afterNextRender,
  effect,
  inject,
  input,
  output,
  untracked,
  viewChild,
} from '@angular/core';
import type { LeafletKeyboardEvent, Map as LeafletMap, LayerGroup, Marker } from 'leaflet';
import { MAP_CONFIG } from '../../core/config/map-config';

/** Minimal shape a point needs: lets the search results and the single establishment page share the map. */
export interface MapPoint {
  readonly slug: string;
  readonly name: string;
  readonly latitude: number;
  readonly longitude: number;
  readonly communeName: string;
  /** Shown in the popup when known. The address line is where the partner writes the quartier and the street. */
  readonly phone?: string;
  readonly addressLine?: string;
  readonly serviceCount?: number;
}

const ABIDJAN: [number, number] = [5.345, -4.025];

/**
 * OpenStreetMap map (Leaflet, loaded on demand). Pins are keyboard-focusable; popups are built with DOM nodes and
 * textContent only, so establishment data is never interpreted as HTML.
 */
@Component({
  selector: 'app-map-view',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    :host {
      display: block;
    }
    .map {
      height: var(--map-height, 32rem);
      border: 1px solid var(--kb-border);
      border-radius: var(--radius-lg);
      overflow: hidden;
      background: var(--kb-primary-50);
      z-index: 0;
    }
  `,
  template: `<div #host class="map" role="region" [attr.aria-label]="label()"></div>`,
})
export class MapView {
  readonly points = input.required<readonly MapPoint[]>();
  readonly label = input('Carte des établissements');
  readonly selected = output<string>();

  /** The slug of the point to emphasise (and bring into view), e.g. the card the visitor is looking at. */
  readonly highlight = input<string | null>(null);
  /** False when the page shows its own cards: a click on a pin then only reports it (`picked`) instead of opening a popup. */
  readonly popups = input(true);
  readonly picked = output<string>();

  private readonly mapConfig = inject(MAP_CONFIG);
  private readonly markerBySlug = new Map<string, Marker>();
  private readonly host = viewChild.required<ElementRef<HTMLElement>>('host');
  private map: LeafletMap | null = null;
  private markers: LayerGroup | null = null;
  private leaflet: typeof import('leaflet') | null = null;

  constructor() {
    afterNextRender(() => void this.initialise());
    effect(() => {
      this.points();
      this.render();
    });
    effect(() => {
      const slug = this.highlight();
      untracked(() => this.applyHighlight(slug));
    });
    inject(DestroyRef).onDestroy(() => this.map?.remove());
  }

  private applyHighlight(slug: string | null): void {
    for (const [key, marker] of this.markerBySlug) {
      const active = key === slug;
      marker.getElement()?.classList.toggle('kb-pin--active', active);
      marker.setZIndexOffset(active ? 1000 : 0);
    }

    const marker = slug ? this.markerBySlug.get(slug) : undefined;
    if (marker && this.map) {
      this.map.panTo(marker.getLatLng(), { animate: true });
    }
  }

  private async initialise(): Promise<void> {
    // Leaflet is CommonJS: the bundler exposes its API on `default`.
    const imported = (await import('leaflet')) as typeof import('leaflet') & {
      default?: typeof import('leaflet');
    };
    const leaflet = imported.default ?? imported;
    this.leaflet = leaflet;
    this.map = leaflet.map(this.host().nativeElement, {
      center: ABIDJAN,
      zoom: 12,
      zoomControl: true,
    });
    leaflet
      .tileLayer(this.mapConfig.tilesUrl, {
        maxZoom: 19,
        attribution: this.mapConfig.attributionHtml,
      })
      .addTo(this.map);
    this.markers = leaflet.layerGroup().addTo(this.map);
    this.render();
  }

  private render(): void {
    const { leaflet, map, markers } = this;
    if (!leaflet || !map || !markers) {
      return;
    }

    markers.clearLayers();
    this.markerBySlug.clear();
    const points = this.points();
    for (const point of points) {
      const marker = leaflet.marker([point.latitude, point.longitude], {
        title: point.name,
        alt: point.name,
        keyboard: true,
        icon: leaflet.divIcon({
          className: 'kb-pin',
          html: '<span></span>',
          iconSize: [28, 36],
          iconAnchor: [14, 34],
        }),
      });
      if (this.popups()) {
        // Room above the popup so that its title is never cut by the edge of the map.
        marker.bindPopup(this.popup(point), { maxWidth: 260, autoPanPaddingTopLeft: [16, 56] });
      }

      marker.on('click', () => this.picked.emit(point.slug));
      // From the keyboard Leaflet fires "keypress", not "click": Enter or Space picks the pin as a click would.
      marker.on('keypress', (event) => {
        const key = (event as LeafletKeyboardEvent).originalEvent.key;
        if (key === 'Enter' || key === ' ') {
          this.picked.emit(point.slug);
        }
      });
      marker.addTo(markers);
      this.markerBySlug.set(point.slug, marker);
    }

    this.applyHighlight(this.highlight());

    if (points.length === 1) {
      map.setView([points[0].latitude, points[0].longitude], 15);
    } else if (points.length > 1) {
      map.fitBounds(
        leaflet.latLngBounds(points.map((p) => [p.latitude, p.longitude] as [number, number])),
        { padding: [40, 40], maxZoom: 15 },
      );
    }

    map.invalidateSize();
  }

  private popup(point: MapPoint): HTMLElement {
    const root = document.createElement('div');
    root.className = 'kb-popup';
    const title = document.createElement('strong');
    title.textContent = point.name;
    root.append(title);

    const phone = point.phone?.trim();
    if (phone) {
      // Only digits, "+" and separators reach the tel: link, whatever the data holds.
      const link = document.createElement('a');
      link.href = `tel:${phone.replace(/[^\d+]/g, '')}`;
      link.textContent = phone;
      root.append(this.line('Téléphone', link));
    }

    root.append(this.line('Commune', point.communeName));
    const quartier = point.addressLine?.trim();
    if (quartier) {
      root.append(this.line('Quartier', quartier));
    }

    if (point.serviceCount !== undefined) {
      root.append(this.line('Nombre de prestations', String(point.serviceCount)));
    }

    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'kb-popup-link';
    button.textContent = 'Voir la fiche';
    button.addEventListener('click', () => this.selected.emit(point.slug));
    root.append(button);
    return root;
  }

  /** One "Label : value" row; the value is plain text or an element built here, never markup from the data. */
  private line(label: string, value: string | HTMLElement): HTMLElement {
    const row = document.createElement('p');
    row.style.margin = '0.125rem 0';
    const name = document.createElement('span');
    name.textContent = `${label} : `;
    name.style.color = 'var(--kb-ink-muted)';
    row.append(name, value);
    return row;
  }
}
