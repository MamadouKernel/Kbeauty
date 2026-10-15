import { httpResource } from '@angular/common/http';
import { Injectable } from '@angular/core';
import type { components } from '../api/api.generated';

/**
 * Category as defined by the API contract (generated from /openapi/v1.json, see `npm run api:types`).
 * `icon` is a semantic key (hair, spa, massage, nails, barber, skincare, makeup) mapped by CategoryIcon.
 */
export type Category = components['schemas']['CategoryDto'];

@Injectable({ providedIn: 'root' })
export class CatalogService {
  readonly categories = httpResource<Category[]>(() => '/api/v1/categories');
}
