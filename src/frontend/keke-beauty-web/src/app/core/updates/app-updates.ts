import { DOCUMENT } from '@angular/common';
import { Injectable, inject } from '@angular/core';
import { SwUpdate, type VersionEvent } from '@angular/service-worker';

/** A page opened this recently has nothing to lose from a reload: nobody has filled a form yet. */
const FRESH_PAGE_MS = 120_000;

/**
 * The service worker keeps serving the version it cached until the page is reloaded twice, so after a release a returning
 * visitor would keep running the old code (and its old settings, e.g. an old map configuration). When a new version is ready
 * shortly after the page was opened, switch to it at once with a single reload. Later on the user is never interrupted: the
 * new version simply takes over at the next visit.
 */
@Injectable({ providedIn: 'root' })
export class AppUpdates {
  private readonly updates = inject(SwUpdate);
  private readonly document = inject(DOCUMENT);
  private readonly openedAt = Date.now();

  constructor() {
    if (this.updates.isEnabled) {
      this.updates.versionUpdates.subscribe((event) => void this.onVersion(event));
    }
  }

  private async onVersion(event: VersionEvent): Promise<void> {
    if (event.type !== 'VERSION_READY' || Date.now() - this.openedAt > FRESH_PAGE_MS) {
      return;
    }

    await this.updates.activateUpdate();
    this.document.location.reload();
  }
}
