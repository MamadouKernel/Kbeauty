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
  viewChild,
} from '@angular/core';
import type { Map as LeafletMap, Marker } from 'leaflet';
import { MAP_CONFIG } from '../../core/config/map-config';

export interface PickedPosition {
  readonly lat: number;
  readonly lng: number;
}

const ZOOM = 16;
const PRECISION = 1_000_000;

/**
 * OpenStreetMap map with one draggable pin (onboarding "exact position", PRD ONB-01). Click the map or drag the pin;
 * the position is reported through `moved`. The wizard also offers numeric fields as a keyboard-friendly alternative.
 */
@Component({
  selector: 'app-location-picker',
  changeDetection: ChangeDetectionStrategy.OnPush,
  styles: `
    :host {
      display: block;
    }
    .map {
      height: var(--map-height, 20rem);
      border: 1px solid var(--kb-border);
      border-radius: var(--radius-lg);
      overflow: hidden;
      background: var(--kb-primary-50);
      z-index: 0;
    }
  `,
  template: `<div #host class="map" role="application" [attr.aria-label]="label()"></div>`,
})
export class LocationPicker {
  readonly latitude = input.required<number>();
  readonly longitude = input.required<number>();
  readonly label = input("Carte : cliquez ou déplacez le repère pour placer l'établissement");
  readonly moved = output<PickedPosition>();

  private readonly mapConfig = inject(MAP_CONFIG);
  private readonly host = viewChild.required<ElementRef<HTMLElement>>('host');
  private map: LeafletMap | null = null;
  private marker: Marker | null = null;

  constructor() {
    afterNextRender(() => void this.initialise());
    effect(() => this.syncMarker(this.latitude(), this.longitude()));
    inject(DestroyRef).onDestroy(() => this.map?.remove());
  }

  private async initialise(): Promise<void> {
    // Leaflet is CommonJS: the bundler exposes its API on `default`.
    const imported = (await import('leaflet')) as typeof import('leaflet') & {
      default?: typeof import('leaflet');
    };
    const leaflet = imported.default ?? imported;
    const start: [number, number] = [this.latitude(), this.longitude()];

    this.map = leaflet.map(this.host().nativeElement, { center: start, zoom: ZOOM });
    leaflet
      .tileLayer(this.mapConfig.tilesUrl, {
        maxZoom: 19,
        attribution: this.mapConfig.attributionHtml,
      })
      .addTo(this.map);
    this.marker = leaflet
      .marker(start, {
        draggable: true,
        keyboard: true,
        title: "Position de l'établissement",
        icon: leaflet.divIcon({
          className: 'kb-pin',
          html: '<span></span>',
          iconSize: [28, 36],
          iconAnchor: [14, 34],
        }),
      })
      .addTo(this.map);

    this.marker.on('dragend', () => this.report(this.marker?.getLatLng()));
    this.map.on('click', (event) => {
      this.marker?.setLatLng(event.latlng);
      this.report(event.latlng);
    });
  }

  private syncMarker(lat: number, lng: number): void {
    const marker = this.marker;
    if (!marker || !this.map) {
      return;
    }

    const current = marker.getLatLng();
    if (round(current.lat) !== round(lat) || round(current.lng) !== round(lng)) {
      marker.setLatLng([lat, lng]);
      this.map.setView([lat, lng], this.map.getZoom());
    }
  }

  private report(latlng: { lat: number; lng: number } | undefined): void {
    if (latlng) {
      this.moved.emit({ lat: round(latlng.lat), lng: round(latlng.lng) });
    }
  }
}

function round(value: number): number {
  return Math.round(value * PRECISION) / PRECISION;
}
